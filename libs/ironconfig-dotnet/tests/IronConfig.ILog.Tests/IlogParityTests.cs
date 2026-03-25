namespace IronConfig.ILog.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using IronConfig;

/// <summary>
/// ILOG parity tests - verify .NET implementation matches spec/C behavior exactly
/// </summary>
public class IlogParityTests
{
    private static string FindRepoRoot()
    {
        return TestVectorHelper.FindRepositoryRoot();
    }

    private static string GetTestVectorPath(string dataset)
    {
        var vectorsRoot = FindRepoRoot();
        return ResolveExistingPath(vectorsRoot, dataset, "expected", "ilog.ilog");
    }

    private static string GetManifestPath(string dataset)
    {
        var vectorsRoot = FindRepoRoot();
        return ResolveExistingPath(vectorsRoot, dataset, "manifest.json");
    }

    private static string ResolveExistingPath(string vectorsRoot, string dataset, params string[] tail)
    {
        foreach (var bucket in new[] { "small", "medium", "large" })
        {
            var parts = new[] { vectorsRoot, bucket, "ilog", dataset }.Concat(tail).ToArray();
            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(new[] { vectorsRoot, "small", "ilog", dataset }.Concat(tail).ToArray());
    }

    private record class Manifest(
        string engine,
        int version,
        string dataset,
        string expected_fast,
        string expected_strict,
        int expected_events,
        string expected_crc32,
        string expected_blake3
    );

    // ========== Test A: Golden strict pass ==========

    [Theory]
    [Trait("Category", "Vectors")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("mega")]
    public void GoldenVectorValidateStrictPasses(string dataset)
    {
        var filePath = GetTestVectorPath(dataset);
        Assert.True(File.Exists(filePath), $"Vector not found: {filePath}");

        var fileBytes = File.ReadAllBytes(filePath);
        var error = IlogReader.Open(fileBytes, out var view);

        Assert.Null(error);
        Assert.NotNull(view);

        // ValidateStrict must pass
        var strictError = IlogReader.ValidateStrict(view);
        Assert.Null(strictError);

        // ValidateFast must also pass
        var fastError = IlogReader.ValidateFast(view);
        Assert.Null(fastError);
    }

    // ========== Test B: Manifest parity ==========

    [Theory]
    [Trait("Category", "Vectors")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("mega")]
    public void ManifestParityEventCount(string dataset)
    {
        var vectorPath = GetTestVectorPath(dataset);
        var manifestPath = GetManifestPath(dataset);

        Assert.True(File.Exists(manifestPath), $"Manifest not found: {manifestPath}");
        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestJson);
        Assert.NotNull(manifest);

        var fileBytes = File.ReadAllBytes(vectorPath);
        var error = IlogReader.Open(fileBytes, out var view);

        Assert.Null(error);
        Assert.NotNull(view);

        // Event count must match manifest
        Assert.Equal((uint)manifest.expected_events, view.EventCount);
    }

    [Theory]
    [Trait("Category", "Vectors")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("mega")]
    public void ManifestParityCrc32(string dataset)
    {
        var vectorPath = GetTestVectorPath(dataset);
        var manifestPath = GetManifestPath(dataset);

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestJson);
        Assert.NotNull(manifest);

        var fileBytes = File.ReadAllBytes(vectorPath);
        var error = IlogReader.Open(fileBytes, out var view);

        Assert.Null(error);
        Assert.NotNull(view);

        // Compute L0_DATA payload CRC32
        uint computedCrc32 = IlogReader.GetL0PayloadCrc32(view);
        string expectedCrc32Hex = manifest.expected_crc32;
        string computedCrc32Hex = computedCrc32.ToString("x8");

        Assert.Equal(expectedCrc32Hex, computedCrc32Hex);
    }

    [Theory]
    [Trait("Category", "Vectors")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("mega")]
    public void ManifestParityBlake3(string dataset)
    {
        var vectorPath = GetTestVectorPath(dataset);
        var manifestPath = GetManifestPath(dataset);

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestJson);
        Assert.NotNull(manifest);

        var fileBytes = File.ReadAllBytes(vectorPath);
        var error = IlogReader.Open(fileBytes, out var view);

        Assert.Null(error);
        Assert.NotNull(view);

        // Compute L0_DATA payload BLAKE3
        string computedBlake3Hex = IlogReader.GetL0PayloadBlake3Hex(view);
        string expectedBlake3Hex = manifest.expected_blake3;

        Assert.Equal(expectedBlake3Hex, computedBlake3Hex);
    }

    // ========== Test C: Corruption tests ==========

    [Fact]
    [Trait("Category", "Vectors")]
    public void CorruptionPayloadByteMustFailStrict()
    {
        var vectorPath = GetTestVectorPath("small");
        var fileBytes = (byte[])File.ReadAllBytes(vectorPath).Clone();

        // Find and corrupt a payload byte in L0_DATA block
        // L0_DATA block starts at offset 16 (after file header)
        // Block header is 72 bytes, so payload starts at 16 + 72 = 88
        // Flip one byte in the payload
        int payloadCorruptionOffset = 88 + 10;
        fileBytes[payloadCorruptionOffset] ^= 0xFF;

        var error = IlogReader.Open(fileBytes, out var view);
        Assert.Null(error);
        Assert.NotNull(view);

        // ValidateStrict should fail (CRC32 mismatch)
        var strictError = IlogReader.ValidateStrict(view);
        Assert.NotNull(strictError);
        Assert.Equal(IlogErrorCode.Crc32Mismatch, strictError.Code);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void CorruptionTruncatedHeaderMustFailFast()
    {
        var vectorPath = GetTestVectorPath("small");
        var fileBytes = File.ReadAllBytes(vectorPath).AsSpan();

        // Truncate to 50 bytes (inside block header, offset 88 is the middle)
        var truncated = fileBytes[..50].ToArray();

        var error = IlogReader.Open(truncated, out var view);
        Assert.NotNull(error);
        Assert.Equal(IlogErrorCode.BlockOutOfBounds, error.Code);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void CorruptionTocOffsetOutOfBoundsMustFailOpen()
    {
        var vectorPath = GetTestVectorPath("small");
        var fileBytes = (byte[])File.ReadAllBytes(vectorPath).Clone();

        // Corrupt TocBlockOffset (at offset 0x08) to point beyond file
        var tocOffsetValue = fileBytes.Length + 1000;
        fileBytes[0x08] = (byte)(tocOffsetValue & 0xFF);
        fileBytes[0x09] = (byte)((tocOffsetValue >> 8) & 0xFF);
        fileBytes[0x0A] = (byte)((tocOffsetValue >> 16) & 0xFF);
        fileBytes[0x0B] = (byte)((tocOffsetValue >> 24) & 0xFF);

        var error = IlogReader.Open(fileBytes, out var view);
        Assert.NotNull(error);
        Assert.Equal(IlogErrorCode.CorruptedHeader, error.Code);
    }

    // ========== Test D: Determinism ==========

    [Theory]
    [Trait("Category", "Vectors")]
    [InlineData("small")]
    [InlineData("medium")]
    public void DeterministicParsing(string dataset)
    {
        var vectorPath = GetTestVectorPath(dataset);
        var fileBytes = File.ReadAllBytes(vectorPath);

        // Parse first time
        var error1 = IlogReader.Open(fileBytes, out var view1);
        Assert.Null(error1);
        Assert.NotNull(view1);

        uint crc1 = IlogReader.GetL0PayloadCrc32(view1);
        string blake31 = IlogReader.GetL0PayloadBlake3Hex(view1);
        uint events1 = view1.EventCount;

        // Parse second time
        var error2 = IlogReader.Open(fileBytes, out var view2);
        Assert.Null(error2);
        Assert.NotNull(view2);

        uint crc2 = IlogReader.GetL0PayloadCrc32(view2);
        string blake32 = IlogReader.GetL0PayloadBlake3Hex(view2);
        uint events2 = view2.EventCount;

        // Must be identical
        Assert.Equal(crc1, crc2);
        Assert.Equal(blake31, blake32);
        Assert.Equal(events1, events2);
    }

    // ========== Test: Fast validation gate ==========

    [Theory]
    [Trait("Category", "Vectors")]
    [InlineData("small")]
    [InlineData("medium")]
    public void FastValidationGatePasses(string dataset)
    {
        var vectorPath = GetTestVectorPath(dataset);
        var fileBytes = File.ReadAllBytes(vectorPath);

        var error = IlogReader.Open(fileBytes, out var view);
        Assert.Null(error);
        Assert.NotNull(view);

        // ValidateFast must pass
        var fastError = IlogReader.ValidateFast(view);
        Assert.Null(fastError);
    }

    // ========== Test: Invalid magic must fail ==========

    [Fact]
    public void InvalidMagicFailsOpen()
    {
        var badBytes = new byte[16]
        {
            0x00, 0x00, 0x00, 0x00,  // Bad magic
            0x01,                      // Version
            0x00,                      // Flags
            0x00, 0x00,                // Reserved
            0x10, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };

        var error = IlogReader.Open(badBytes, out _);
        Assert.NotNull(error);
        Assert.Equal(IlogErrorCode.InvalidMagic, error.Code);
        Assert.Equal(0ul, error.ByteOffset);
    }

    // ========== Test: Invalid version must fail ==========

    [Fact]
    public void InvalidVersionFailsOpen()
    {
        var badBytes = new byte[16]
        {
            0x49, 0x4C, 0x4F, 0x47,  // ILOG
            0xFF,                      // Bad version
            0x00,                      // Flags
            0x00, 0x00,                // Reserved
            0x10, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };

        var error = IlogReader.Open(badBytes, out _);
        Assert.NotNull(error);
        Assert.Equal(IlogErrorCode.UnsupportedVersion, error.Code);
        Assert.Equal(4ul, error.ByteOffset);
    }
}
