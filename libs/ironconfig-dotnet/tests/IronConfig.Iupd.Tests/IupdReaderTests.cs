using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using IronConfig;
using IronConfig.Iupd;

namespace IronConfig.Iupd.Tests;

public class IupdReaderTests
{
    private readonly string _vectorsRoot = TestVectorHelper.ResolveTestVectorsRoot();

    private (byte[] binary, JsonElement manifest) LoadGoldenVector(string dataset)
    {
        var binPath = ResolveExistingPath(dataset, "expected", "iupd.iupd");
        var manifestPath = ResolveExistingPath(dataset, "manifest.json");

        if (!File.Exists(binPath))
            throw new FileNotFoundException($"Golden vector not found: {binPath}");

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest not found: {manifestPath}");

        var binary = File.ReadAllBytes(binPath);
        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonDocument.Parse(manifestJson).RootElement;

        return (binary, manifest);
    }

    private string ResolveExistingPath(string dataset, params string[] tail)
    {
        foreach (var bucket in new[] { "small", "medium", "large" })
        {
            var parts = new[] { _vectorsRoot, bucket, "iupd", dataset }.Concat(tail).ToArray();
            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(new[] { _vectorsRoot, "small", "iupd", dataset }.Concat(tail).ToArray());
    }

    // ============================================================================
    // A) Golden Pass Tests
    // ============================================================================

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("mega")]
    public void TestGoldenValidateFast(string dataset)
    {
        var (binary, manifest) = LoadGoldenVector(dataset);

        var reader = IupdReader.Open(binary, out var error);
        Assert.True(error.IsOk, $"Open failed for {dataset}: {error}");
        Assert.NotNull(reader);

        error = reader!.ValidateFast();
        Assert.True(error.IsOk, $"ValidateFast failed for {dataset}: {error}");
    }

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("mega")]
    public void TestGoldenValidateStrict(string dataset)
    {
        var (binary, manifest) = LoadGoldenVector(dataset);

        var reader = IupdReader.Open(binary, out var error);
        Assert.True(error.IsOk, $"Open failed for {dataset}: {error}");
        Assert.NotNull(reader);

        error = reader!.ValidateStrict();
        Assert.True(error.IsOk, $"ValidateStrict failed for {dataset}: {error}");
    }

    // ============================================================================
    // B) Manifest Parity Tests
    // ============================================================================

    [Theory]
    [InlineData("small", 2)]
    [InlineData("medium", 8)]
    [InlineData("large", 64)]
    [InlineData("mega", 512)]
    public void TestChunkCountMatches(string dataset, int expectedCount)
    {
        var (binary, manifest) = LoadGoldenVector(dataset);

        var reader = IupdReader.Open(binary, out var error);
        Assert.True(error.IsOk, $"Open failed for {dataset}: {error}");
        Assert.NotNull(reader);

        Assert.Equal((uint)expectedCount, reader!.ChunkCount);
    }

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("mega")]
    public void TestManifestCrc32Matches(string dataset)
    {
        var (binary, manifest) = LoadGoldenVector(dataset);

        var reader = IupdReader.Open(binary, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Expected CRC32 from manifest
        if (manifest.TryGetProperty("expected_crc32", out var crcEl) && crcEl.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var expectedCrc32Str = crcEl.GetString();
            uint expectedCrc32 = uint.Parse(expectedCrc32Str!, System.Globalization.NumberStyles.HexNumber);

            // Validate strict should verify CRC32
            error = reader!.ValidateStrict();
            Assert.True(error.IsOk, $"CRC32 validation failed: {error}");

            Assert.Equal(expectedCrc32, reader.ManifestCrc32);
        }
    }

    // ============================================================================
    // C) Corruption Tests (using golden_small)
    // ============================================================================

    [Fact]
    public void TestFlipPayloadByteFails()
    {
        var (binary, _) = LoadGoldenVector("small");
        var corrupted = (byte[])binary.Clone();

        // Find payload section and flip a byte
        // small dataset: 2 chunks, payload starts at offset 188
        // Header (36) + Chunk table (112) + Manifest (40) = 188
        if (corrupted.Length > 200)  // Flip byte in payload section
        {
            corrupted[200] ^= 0xFF;
        }

        var reader = IupdReader.Open(corrupted, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Strict validation should fail due to CRC32 mismatch
        error = reader!.ValidateStrict();
        Assert.False(error.IsOk, "ValidateStrict should fail on corrupted payload");
        Assert.Equal(IupdErrorCode.Crc32Mismatch, error.Code);
    }

    [Fact]
    public void TestBadChunkIndexFails()
    {
        var (binary, _) = LoadGoldenVector("small");
        var corrupted = (byte[])binary.Clone();

        // Corrupt chunk index at offset 36 (start of chunk table, after 36-byte header)
        // Set it to invalid value
        corrupted[36] = 99;  // Invalid chunk index

        var reader = IupdReader.Open(corrupted, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);  // Open succeeds

        // ValidateFast should catch this
        error = reader!.ValidateFast();
        Assert.False(error.IsOk, "ValidateFast should fail on bad chunk index");
        Assert.Equal(IupdErrorCode.ChunkIndexError, error.Code);
    }

    [Fact]
    public void TestOffsetOutOfBoundsFails()
    {
        var (binary, _) = LoadGoldenVector("small");
        var corrupted = (byte[])binary.Clone();

        // Corrupt payload offset in header (byte 28-35)
        // Set to huge value beyond file
        for (int i = 0; i < 8; i++)
            corrupted[28 + i] = 0xFF;

        var reader = IupdReader.Open(corrupted, out var error);
        Assert.NotNull(reader);
        Assert.False(error.IsOk, "Open should fail on invalid payload offset");
        Assert.Equal(IupdErrorCode.OffsetOutOfBounds, error.Code);
    }

    // ============================================================================
    // D) Apply Behavior Tests
    // ============================================================================

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    public void TestApplyIteratesInOrder(string dataset)
    {
        var (binary, manifest) = LoadGoldenVector(dataset);

        var reader = IupdReader.Open(binary, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        var applier = reader!.BeginApply();
        int count = 0;
        uint lastIndex = uint.MaxValue;

        while (applier.TryNext(out var chunk))
        {
            // Chunks should be in ascending order (0, 1, 2, ...)
            Assert.True(chunk.ChunkIndex > lastIndex || lastIndex == uint.MaxValue,
                $"Chunks not in order: {lastIndex} -> {chunk.ChunkIndex}");

            lastIndex = chunk.ChunkIndex;
            count++;
        }

        // Should have iterated all chunks
        Assert.Equal((int)reader.ChunkCount, count);
    }

    [Fact]
    public void TestApplyEndOfStreamDeterministic()
    {
        var (binary, _) = LoadGoldenVector("small");

        var reader = IupdReader.Open(binary, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        var applier = reader!.BeginApply();

        // Iterate to end
        while (applier.TryNext(out _)) { }

        // Further calls should return false
        Assert.False(applier.TryNext(out _), "TryNext should return false after end");
        Assert.False(applier.TryNext(out _), "TryNext should stay false");
    }

    // ============================================================================
    // E) Determinism Tests
    // ============================================================================

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    public void TestParsingIsDeterministic(string dataset)
    {
        var (binary, _) = LoadGoldenVector(dataset);

        // Parse twice
        var reader1 = IupdReader.Open(binary, out var error1);
        var reader2 = IupdReader.Open(binary, out var error2);

        Assert.True(error1.IsOk, $"First parse failed for {dataset}: {error1}");
        Assert.True(error2.IsOk, $"Second parse failed for {dataset}: {error2}");
        Assert.NotNull(reader1);
        Assert.NotNull(reader2);

        // Should have identical parsed results
        Assert.Equal(reader1!.ChunkCount, reader2!.ChunkCount);
        Assert.Equal(reader1.ApplyOrderCount, reader2.ApplyOrderCount);
        Assert.Equal(reader1.ManifestCrc32, reader2.ManifestCrc32);

        // Validation should give same results
        var err1 = reader1.ValidateFast();
        var err2 = reader2.ValidateFast();
        Assert.Equal(err1.IsOk, err2.IsOk);
        Assert.Equal(err1.Code, err2.Code);
    }

    // ============================================================================
    // Additional Structural Tests
    // ============================================================================

    [Fact]
    public void TestInvalidMagicFails()
    {
        var data = new byte[36];
        data[0] = 0xFF;  // Invalid magic
        data[1] = 0xFF;
        data[2] = 0xFF;
        data[3] = 0xFF;

        var reader = IupdReader.Open(data, out var error);
        Assert.Null(reader);
        Assert.Equal(IupdErrorCode.InvalidMagic, error.Code);
    }

    [Fact]
    public void TestInvalidVersionFails()
    {
        var data = new byte[36];
        // Valid magic
        data[0] = 0x49; data[1] = 0x55; data[2] = 0x50; data[3] = 0x44;  // IUPD
        // Invalid version
        data[4] = 0x99;

        var reader = IupdReader.Open(data, out var error);
        Assert.Null(reader);
        Assert.Equal(IupdErrorCode.UnsupportedVersion, error.Code);
    }

    [Fact]
    public void TestFileTooSmallFails()
    {
        // Create a file with valid IUPD magic and v1 version, but too small (< 36 bytes)
        var data = new byte[20];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 0x44505549); // IUPD magic
        data[4] = 0x01; // Version 1

        var reader = IupdReader.Open(data, out var error);
        Assert.Null(reader);
        Assert.Equal(IupdErrorCode.OffsetOutOfBounds, error.Code);
    }

    // ============================================================================
    // F) Round-trip Tests (IupdWriter -> IupdReader)
    // ============================================================================

    [Fact]
    public void TestRoundtrip_SimpleFile()
    {
        // Create a simple IUPD file with 2 chunks
        var payload1 = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var payload2 = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

        var builder = new IupdBuilder()
            .AddChunk(0, payload1)
            .AddChunk(1, payload2)
            .WithApplyOrder(0, 1);

        var iupdData = builder.Build();

        // Read it back
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Verify structure
        Assert.Equal(2u, reader.ChunkCount);
        Assert.Equal(2u, reader.ApplyOrderCount);

        // Validate fast
        error = reader.ValidateFast();
        Assert.True(error.IsOk);

        // Validate strict (includes BLAKE3)
        error = reader.ValidateStrict();
        Assert.True(error.IsOk);

        // Verify apply order
        var applier = reader.BeginApply();
        int count = 0;
        while (applier.TryNext(out var chunk))
        {
            count++;
        }
        Assert.Equal(2, count);
    }

    [Fact]
    public void TestRoundtrip_WithDependencies()
    {
        // Create IUPD file with dependency: chunk 1 depends on chunk 0
        var builder = new IupdBuilder()
            .AddChunk(0, new byte[] { 0x11 })
            .AddChunk(1, new byte[] { 0x22 })
            .AddChunk(2, new byte[] { 0x33 })
            .AddDependency(0, 1)  // 1 depends on 0
            .AddDependency(1, 2)  // 2 depends on 1
            .WithApplyOrder(0, 1, 2);

        var iupdData = builder.Build();

        // Read back
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Validate should pass
        error = reader.ValidateFast();
        Assert.True(error.IsOk);

        error = reader.ValidateStrict();
        Assert.True(error.IsOk);
    }

    [Fact]
    public void TestRoundtrip_EmptyPayload()
    {
        // Test with empty payload (edge case)
        var builder = new IupdBuilder()
            .AddChunk(0, Array.Empty<byte>())
            .WithApplyOrder(0);

        var iupdData = builder.Build();

        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        error = reader.ValidateStrict();
        Assert.True(error.IsOk);  // Should pass - empty payload with zero BLAKE3 hash
    }

    [Fact]
    public void TestRoundtrip_LargePayload()
    {
        // Test with larger payload (1MB)
        var largePayload = new byte[1024 * 1024];
        for (int i = 0; i < largePayload.Length; i++)
        {
            largePayload[i] = (byte)(i % 256);
        }

        var builder = new IupdBuilder()
            .AddChunk(0, largePayload)
            .WithApplyOrder(0);

        var iupdData = builder.Build();

        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Full validation including BLAKE3
        error = reader.ValidateStrict();
        Assert.True(error.IsOk);

        // Verify size
        Assert.Equal(1u, reader.ChunkCount);
    }
}
