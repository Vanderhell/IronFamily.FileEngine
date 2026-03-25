using System;
using Xunit;
using IronConfig.Iupd;
using IronConfig;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// Round-trip tests for IupdWriter -> IupdReader
/// These tests don't require external test vectors
/// </summary>
public class IupdRoundtripTests
{
    [Fact]
    public void TestRoundtrip_SimpleFile()
    {
        // Create a simple IUPD file with 2 chunks
        // SECURITY: Changed from MINIMAL (no longer allowed) to OPTIMIZED
        var payload1 = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var payload2 = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.OPTIMIZED)
            .WithSigningKey(benchPrivateKey, benchPublicKey)
            .AddChunk(0, payload1)
            .AddChunk(1, payload2)
            .WithApplyOrder(0, 1);

        var iupdData = builder.Build();

        // Read it back
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Set verification key
        reader.SetVerificationKey(benchPublicKey);

        // Verify structure
        Assert.Equal(2u, reader.ChunkCount);
        Assert.Equal(2u, reader.ApplyOrderCount);

        // Validate fast
        error = reader.ValidateFast();
        Assert.True(error.IsOk);

        // Validate strict (includes signature and BLAKE3)
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
        // Create IUPD file with dependencies (requires SECURE profile with signing keys)
        var seed32 = IupdEd25519Keys.BenchSeed32;
        var pub32 = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.SECURE)
            .WithSigningKey(seed32, pub32)
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

        // Set verification key for SECURE profile
        reader.SetVerificationKey(pub32);

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
        // SECURITY: Changed from MINIMAL (no longer allowed) to OPTIMIZED
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.OPTIMIZED)
            .WithSigningKey(benchPrivateKey, benchPublicKey)
            .AddChunk(0, Array.Empty<byte>())
            .WithApplyOrder(0);

        var iupdData = builder.Build();

        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk, $"Open failed: {error}");

        reader.SetVerificationKey(benchPublicKey);

        error = reader.ValidateFast();
        Assert.True(error.IsOk, $"ValidateFast failed: {error}");

        error = reader.ValidateStrict();
        Assert.True(error.IsOk, $"ValidateStrict failed: {error}");  // Should pass - empty payload
    }

    [Fact]
    public void TestRoundtrip_LargePayload()
    {
        // Test with larger payload (1MB)
        // SECURITY: Changed from MINIMAL (no longer allowed) to OPTIMIZED
        var largePayload = new byte[1024 * 1024];
        for (int i = 0; i < largePayload.Length; i++)
        {
            largePayload[i] = (byte)(i % 256);
        }

        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.OPTIMIZED)
            .WithSigningKey(benchPrivateKey, benchPublicKey)
            .AddChunk(0, largePayload)
            .WithApplyOrder(0);

        var iupdData = builder.Build();

        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);

        // Full validation including signature and BLAKE3
        error = reader.ValidateStrict();
        Assert.True(error.IsOk);

        // Verify size
        Assert.Equal(1u, reader.ChunkCount);
    }

    [Fact]
    public void TestRoundtrip_MultipleChunksRandomOrder()
    {
        // Test apply order different from chunk index order
        // SECURITY: Changed from MINIMAL (no longer allowed) to OPTIMIZED
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.OPTIMIZED)
            .WithSigningKey(benchPrivateKey, benchPublicKey)
            .AddChunk(0, new byte[] { 0x00 })
            .AddChunk(1, new byte[] { 0x11 })
            .AddChunk(2, new byte[] { 0x22 })
            .AddChunk(3, new byte[] { 0x33 })
            .WithApplyOrder(3, 1, 0, 2);  // Non-sequential apply order

        var iupdData = builder.Build();

        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);

        error = reader.ValidateStrict();
        Assert.True(error.IsOk);

        // Verify apply order is preserved
        var applier = reader.BeginApply();
        var order = new System.Collections.Generic.List<uint>();
        while (applier.TryNext(out var chunk))
        {
            order.Add(chunk.ChunkIndex);
        }

        Assert.Equal(new[] { 3u, 1u, 0u, 2u }, order.ToArray());
    }

    [Fact]
    public void TestRoundtrip_Blake3Verification()
    {
        // Test that BLAKE3 hashes are correctly verified
        // Use SECURE profile which requires BLAKE3
        var payload = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };
        // Use unified bench keys
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.SECURE)
            .WithSigningKey(benchPrivateKey, benchPublicKey)
            .AddChunk(0, payload)
            .WithApplyOrder(0);

        var iupdData = builder.Build();

        // ValidateStrict should compute and verify BLAKE3
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Set verification key for SECURE profile
        reader.SetVerificationKey(benchPublicKey);

        error = reader.ValidateStrict();
        Assert.True(error.IsOk, $"BLAKE3 validation failed: {error}");
    }

    [Fact]
    public void TestRoundtrip_UncompressedPayload_WithByte8Zero_IsNotMisdetectedAsWrapped()
    {
        // Regression: a raw (uncompressed) payload where byte[8] == 0x00 must not
        // be interpreted as wrapped compression metadata.
        // Old behavior could treat marker 0x00 as wrapped payload and effectively
        // drop 9 bytes, causing false CRC/BLAKE3 failures in strict validation.
        var payload = new byte[]
        {
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // plausible size field (16)
            0x00,                                           // marker-like byte in raw payload
            0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6, 0x07
        };

        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.OPTIMIZED)
            .WithSigningKey(benchPrivateKey, benchPublicKey)
            .AddChunk(0, payload)
            .WithApplyOrder(0);

        var iupdData = builder.Build();

        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk, $"Open failed: {error}");

        reader.SetVerificationKey(benchPublicKey);

        error = reader.ValidateStrict();
        Assert.True(error.IsOk, $"ValidateStrict failed on raw payload marker-like prefix: {error}");

        var applier = reader.BeginApply();
        Assert.True(applier.TryNext(out var chunk));
        Assert.Equal(payload, chunk.Payload.ToArray());
        Assert.False(applier.TryNext(out _));
    }

    [Fact]
    public void TestRoundtrip_CompressedPayload_RepeatedAccess_RemainsStable()
    {
        var payload = new byte[128 * 1024];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)('A' + (i % 3));

        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.OPTIMIZED)
            .WithSigningKey(benchPrivateKey, benchPublicKey)
            .AddChunk(0, payload)
            .WithApplyOrder(0);

        var iupdData = builder.Build();

        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk, $"Open failed: {error}");

        reader.SetVerificationKey(benchPublicKey);

        error = reader.ValidateStrict();
        Assert.True(error.IsOk, $"ValidateStrict failed: {error}");

        error = reader.GetChunkPayload(0, out var payload1);
        Assert.True(error.IsOk, $"GetChunkPayload #1 failed: {error}");

        error = reader.GetChunkPayload(0, out var payload2);
        Assert.True(error.IsOk, $"GetChunkPayload #2 failed: {error}");

        Assert.Equal(payload, payload1.ToArray());
        Assert.Equal(payload, payload2.ToArray());

        var applier = reader.BeginApply();
        Assert.True(applier.TryNext(out var chunk));
        Assert.Equal(payload, chunk.Payload.ToArray());
        Assert.False(applier.TryNext(out _));
    }

    [Fact]
    public void TestRoundtrip_CorruptedBlake3Fails()
    {
        // Create valid IUPD file with SECURE profile, then corrupt BLAKE3 hash and verify it fails
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };
        // Use unified bench keys
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var builder = new IupdBuilder()
            .WithProfile(IupdProfile.SECURE)
            .WithSigningKey(benchPrivateKey, benchPublicKey)
            .AddChunk(0, payload)
            .WithApplyOrder(0);

        var iupdData = builder.Build();

        // Corrupt BLAKE3 hash in chunk table (offset roughly around byte 60+)
        // Chunk table starts at byte 36, each entry is 56 bytes
        // BLAKE3 hash is at offset 24-56 in the entry
        if (iupdData.Length > 100)
        {
            iupdData[80] ^= 0xFF;  // Flip some bits in BLAKE3 field
        }

        // Try to validate - should fail
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);  // Open should succeed

        // Set verification key for SECURE profile
        reader.SetVerificationKey(benchPublicKey);

        error = reader.ValidateStrict();
        Assert.False(error.IsOk);  // But strict validation should fail
        Assert.Equal(IupdErrorCode.Blake3Mismatch, error.Code);
    }
}
