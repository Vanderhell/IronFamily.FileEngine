using System;
using System.Buffers.Binary;
using System.Linq;
using Xunit;
using IronConfig.ILog;

namespace IronConfig.ILog.Tests;

public class IlogCompressorTests
{
    [Fact]
    public void Compress_Empty_Data_Returns_Empty()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var compressed = IlogCompressor.Compress(data);

        // Assert
        Assert.NotNull(compressed);
        Assert.Empty(compressed);
    }

    [Fact]
    public void Compress_SmallData_ProducesValidOutput()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        var compressed = IlogCompressor.Compress(data);

        // Assert
        Assert.NotNull(compressed);
        Assert.NotEmpty(compressed);
        // Small data < 128 bytes might fallback to uncompressed
    }

    [Fact]
    public void Compress_RepetitiveData_SignificantlyReducesSize()
    {
        // Arrange
        var data = new byte[1024];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        // Create a highly repetitive pattern
        for (int i = 0; i < data.Length - 32; i += 32)
        {
            for (int j = 0; j < 32; j++)
            {
                data[i + j] = (byte)((i / 32) % 256);
            }
        }

        // Act
        var compressed = IlogCompressor.Compress(data);

        // Assert
        Assert.NotNull(compressed);
        Assert.NotEmpty(compressed);

        // For repetitive data, compression should be significant (< 80% of original)
        double ratio = (double)compressed.Length / data.Length;
        Assert.True(ratio < 0.80, $"Compression ratio {ratio:P} should be better than 80% for repetitive data");
    }

    [Fact]
    public void Compress_AllZeroes_HighCompressionRatio()
    {
        // Arrange
        var data = new byte[4096];
        Array.Clear(data, 0, data.Length);

        // Act
        var compressed = IlogCompressor.Compress(data);

        // Assert
        Assert.NotNull(compressed);
        Assert.NotEmpty(compressed);

        // All zeroes should compress very well (< 5% of original)
        double ratio = (double)compressed.Length / data.Length;
        Assert.True(ratio < 0.05, $"Compression ratio {ratio:P} should be excellent for all-zeroes data");
    }

    [Fact]
    public void Compress_RandomData_FallsBackToUncompressed()
    {
        // Arrange
        var data = new byte[512];
        var random = new Random(42);
        random.NextBytes(data);

        // Act
        var compressed = IlogCompressor.Compress(data);

        // Assert
        Assert.NotNull(compressed);
        // Random data likely won't compress well and may fallback
        // Compressed should be at least as large as uncompressed (with headers)
    }

    [Fact]
    public void RoundTrip_SmallData_PreservesContent()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

        // Act
        var compressed = IlogCompressor.Compress(data);
        var decompressed = DecompressPayload(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(data, decompressed);
    }

    [Fact]
    public void RoundTrip_MediumRepetitiveData_PreservesContent()
    {
        // Arrange
        var data = new byte[256];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 16); // 16-byte repeating pattern
        }

        // Act
        var compressed = IlogCompressor.Compress(data);
        var decompressed = DecompressPayload(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(data, decompressed);
    }

    [Fact]
    public void RoundTrip_LargeRepetitiveData_PreservesContent()
    {
        // Arrange
        var data = new byte[8192];
        var pattern = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = pattern[i % pattern.Length];
        }

        // Act
        var compressed = IlogCompressor.Compress(data);
        var decompressed = DecompressPayload(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(data.Length, decompressed?.Length ?? 0);
        Assert.Equal(data, decompressed);
    }

    [Fact]
    public void RoundTrip_DeterministicLogLike_100KB_PreservesContent()
    {
        var rng = new Random(42);
        var lines = new System.Text.StringBuilder(capacity: 120_000);
        int bytes = 0;
        while (bytes < 100 * 1024)
        {
            string line = $"ts=1704067200000 level=INFO src=bench node={rng.Next(1, 32):D2} msg=pressure nominal sample={rng.Next(1000, 9999)} seq={rng.Next(0, 1_000_000)}\n";
            lines.Append(line);
            bytes += System.Text.Encoding.UTF8.GetByteCount(line);
        }

        var data = System.Text.Encoding.UTF8.GetBytes(lines.ToString());

        var compressed = IlogCompressor.Compress(data);
        var decompressed = DecompressPayload(compressed);

        Assert.NotNull(decompressed);
        Assert.Equal(data.Length, decompressed!.Length);
        Assert.Equal(data, decompressed);
    }

    [Fact]
    public void Decompress_CorruptedHeader_ReturnsFalse()
    {
        // Arrange
        var corrupted = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00 };

        // Act
        var result = IlogCompressor.TryDecompress(corrupted, out var output, out var error);

        // Assert
        Assert.False(result);
        Assert.NotNull(error);
        Assert.Empty(output ?? Array.Empty<byte>());
    }

    [Fact]
    public void Decompress_TruncatedData_ReturnsFalse()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02 };

        // Act
        var result = IlogCompressor.TryDecompress(data, out var output, out var error);

        // Assert
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void Decompress_UncompressedFallback_Works()
    {
        // Arrange
        var originalData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

        // Manually create uncompressed payload
        var payload = new byte[6 + originalData.Length];
        payload[0] = 0x01; // ArchiveVersion
        payload[1] = 0x00; // CompressionType = uncompressed
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2, 4), (uint)originalData.Length);
        Buffer.BlockCopy(originalData, 0, payload, 6, originalData.Length);

        // Act
        var result = IlogCompressor.TryDecompress(payload, out var decompressed, out var error);

        // Assert
        Assert.True(result, $"Decompression failed: {error}");
        Assert.Null(error);
        Assert.Equal(originalData, decompressed);
    }

    [Fact]
    public void Compress_LargeFile_ProducesReasonableSize()
    {
        // Arrange
        var data = new byte[65536]; // 64 KB
        var pattern = "The quick brown fox jumps over the lazy dog. ";
        int patternBytes = System.Text.Encoding.UTF8.GetBytes(pattern, 0, pattern.Length,
            data, 0);

        for (int i = patternBytes; i < data.Length; i++)
        {
            data[i] = data[i % patternBytes];
        }

        // Act
        var compressed = IlogCompressor.Compress(data);

        // Assert
        Assert.NotNull(compressed);
        Assert.True(compressed.Length > 0);

        // Repetitive text should compress well
        double ratio = (double)compressed.Length / data.Length;
        Assert.True(ratio < 0.5, $"Compression ratio {ratio:P} should be good for repetitive text");
    }

    [Fact]
    public void Compress_OverlappingMatches_IsCorrect()
    {
        // Arrange - pattern with overlapping potential matches
        var data = new byte[] {
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x02, 0x03, 0x04,
        };

        // Act
        var compressed = IlogCompressor.Compress(data);
        var decompressed = DecompressPayload(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(data, decompressed);
    }

    [Fact]
    public void Compress_Various_Patterns()
    {
        // Test multiple pattern types
        var patterns = new[]
        {
            CreatePattern(256, 0x00), // All zeros
            CreatePattern(256, 0xFF), // All 0xFF
            CreateRepeatingBytePattern(512, 0xAB), // Repeating byte
            CreateRepeatingWordPattern(512, new byte[] { 0x01, 0x02 }), // 2-byte pattern
            CreateIncreasingPattern(512), // Incrementing bytes
        };

        foreach (var data in patterns)
        {
            // Act
            var compressed = IlogCompressor.Compress(data);
            var decompressed = DecompressPayload(compressed);

            // Assert
            Assert.NotNull(decompressed);
            Assert.Equal(data, decompressed);
        }
    }

    private byte[] CreatePattern(int length, byte value)
    {
        var data = new byte[length];
        Array.Fill(data, value);
        return data;
    }

    private byte[] CreateRepeatingBytePattern(int length, byte pattern)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = pattern;
        return data;
    }

    private byte[] CreateRepeatingWordPattern(int length, byte[] pattern)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = pattern[i % pattern.Length];
        return data;
    }

    private byte[] CreateIncreasingPattern(int length)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = (byte)i;
        return data;
    }

    private byte[]? DecompressPayload(byte[] payload)
    {
        if (IlogCompressor.TryDecompress(payload, out var output, out var error))
            return output;
        return null;
    }

    [Fact]
    public void CompressionRatios_MeetExpectations()
    {
        // Test 1: All zeroes - should achieve excellent compression (< 5%)
        var allZeroes = new byte[4096];
        var compressed = IlogCompressor.Compress(allZeroes);
        double ratio = (double)compressed.Length / allZeroes.Length;
        Assert.True(ratio < 0.05, $"All-zeroes compression ratio {ratio:P} should be < 5%");

        // Test 2: Repetitive text - should achieve good compression (40-65%)
        var text = new byte[8192];
        string pattern = "The quick brown fox jumps over the lazy dog. This is a test of ILOG compression. ";
        int written = System.Text.Encoding.UTF8.GetBytes(pattern, 0, pattern.Length, text, 0);
        for (int i = written; i < text.Length; i++)
        {
            text[i] = text[i % written];
        }
        compressed = IlogCompressor.Compress(text);
        ratio = (double)compressed.Length / text.Length;
        Assert.True(ratio < 0.65, $"Repetitive text compression ratio {ratio:P} should be < 65%");

        // Test 3: 1KB repetitive - should be in expected range
        var repetitive1k = new byte[1024];
        for (int i = 0; i < repetitive1k.Length; i++)
        {
            repetitive1k[i] = (byte)(i % 16);
        }
        compressed = IlogCompressor.Compress(repetitive1k);
        ratio = (double)compressed.Length / repetitive1k.Length;
        Assert.True(ratio > 0.0 && ratio < 0.65,
            $"1KB repetitive compression ratio {ratio:P} should be between 0% and 65%");
    }
}
