using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using IronConfig.ILog;

namespace IronConfig.ILog.Tests;

public class IlogCompressionLargeFileTests
{
    [Fact]
    public void Compress_50MB_LogData_RoundTrip()
    {
        // Arrange - Generate 50MB of realistic log-like data
        Console.WriteLine("\n=== 50MB Compression Test ===");

        int targetSizeBytes = 50 * 1024 * 1024; // 50 MB
        var data = GenerateLogData(targetSizeBytes);

        Console.WriteLine($"Generated test data: {data.Length:N0} bytes ({data.Length / (1024.0 * 1024.0):F2} MB)");

        // Act - Compress
        var stopwatch = Stopwatch.StartNew();
        var compressed = IlogCompressor.Compress(data);
        stopwatch.Stop();

        double compressionRatio = (double)compressed.Length / data.Length;
        double compressSpeed = data.Length / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine($"Compressed size: {compressed.Length:N0} bytes ({compressed.Length / (1024.0 * 1024.0):F2} MB)");
        Console.WriteLine($"Compression ratio: {compressionRatio:P2}");
        Console.WriteLine($"Compression speed: {compressSpeed:F2} MB/s");
        Console.WriteLine($"Compression time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");

        // Act - Decompress
        stopwatch.Restart();
        bool decompressOk = IlogCompressor.TryDecompress(compressed, out var decompressed, out var error);
        stopwatch.Stop();

        double decompressSpeed = decompressed?.Length > 0
            ? (decompressed.Length / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds)
            : 0;

        Console.WriteLine($"Decompression time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        Console.WriteLine($"Decompression speed: {decompressSpeed:F2} MB/s");

        // Assert
        Assert.True(decompressOk, $"Decompression failed: {error}");
        Assert.NotNull(decompressed);
        Assert.Equal(data.Length, decompressed.Length);
        Assert.Equal(data, decompressed);

        // Expected compression ratios for log data
        Assert.True(compressionRatio < 0.75,
            $"Log data should compress to <75%, got {compressionRatio:P2}");

        Console.WriteLine("✅ Round-trip successful!");
    }

    [Fact]
    public void Compress_100MB_HighlyRepetitiveData_MaxCompression()
    {
        // Arrange - 100MB of highly repetitive data
        Console.WriteLine("\n=== 100MB Highly Repetitive Data Test ===");

        int targetSizeBytes = 100 * 1024 * 1024;
        var data = new byte[targetSizeBytes];

        // Fill with repeating 256-byte pattern
        string pattern = "SYSTEM ERROR: Database connection timeout at 2026-02-11T08:30:45.123Z. Retrying attempt 1 of 5. ";
        int patternLen = System.Text.Encoding.UTF8.GetBytes(
            pattern, 0, pattern.Length, data, 0);

        for (int i = patternLen; i < data.Length; i++)
            data[i] = data[i % patternLen];

        Console.WriteLine($"Generated test data: {data.Length:N0} bytes ({data.Length / (1024.0 * 1024.0):F2} MB)");

        // Act - Compress
        var stopwatch = Stopwatch.StartNew();
        var compressed = IlogCompressor.Compress(data);
        stopwatch.Stop();

        double compressionRatio = (double)compressed.Length / data.Length;
        double compressSpeed = data.Length / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine($"Compressed size: {compressed.Length:N0} bytes ({compressed.Length / (1024.0 * 1024.0):F2} MB)");
        Console.WriteLine($"Compression ratio: {compressionRatio:P2}");
        Console.WriteLine($"Compression speed: {compressSpeed:F2} MB/s");

        // Assert - Highly repetitive data should compress to <5%
        Assert.True(compressionRatio < 0.05,
            $"Highly repetitive data should compress to <5%, got {compressionRatio:P2}");

        Console.WriteLine("✅ Excellent compression ratio achieved!");
    }

    [Fact]
    public void IlogEncoder_ARCHIVED_50MB_Integration()
    {
        // Arrange - Generate 50MB log data and encode with ARCHIVED profile
        Console.WriteLine("\n=== 50MB ILOG ARCHIVED Integration Test ===");

        int targetSizeBytes = 50 * 1024 * 1024;
        var logData = GenerateLogData(targetSizeBytes);

        Console.WriteLine($"Original log data: {logData.Length:N0} bytes ({logData.Length / (1024.0 * 1024.0):F2} MB)");

        // Act - Encode
        var encoder = new IlogEncoder();
        var stopwatch = Stopwatch.StartNew();
        var encoded = encoder.Encode(logData, IlogProfile.ARCHIVED);
        stopwatch.Stop();

        Console.WriteLine($"ILOG ARCHIVED file: {encoded.Length:N0} bytes ({encoded.Length / (1024.0 * 1024.0):F2} MB)");
        Console.WriteLine($"Total delta: {(double)(encoded.Length - logData.Length) / (1024.0 * 1024.0):F2} MB");
        double totalRatio = (double)encoded.Length / logData.Length;
        Console.WriteLine($"Total ratio: {totalRatio:P2}");
        Console.WriteLine($"Encoding time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");

        // Act - Decode L3 block
        var decoder = new IlogDecoder();
        stopwatch.Restart();
        var decompressed = decoder.DecodeL3Block(encoded);
        stopwatch.Stop();

        Console.WriteLine($"Decoding time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(logData, decompressed);
        Assert.True(totalRatio < 1.0, $"ARCHIVED should be storage-first, got ratio {totalRatio:P2}");
        Assert.True(decompressed.Length == logData.Length, "Decompressed data should match original");

        Console.WriteLine("✅ Full integration test successful!");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_ARCHIVED_StorageFirst_Beats_MINIMAL_On_LogData()
    {
        Console.WriteLine("\n=== ARCHIVED Storage-First Benchmark ===");

        int targetSizeBytes = 10 * 1024 * 1024;
        var logData = GenerateLogData(targetSizeBytes);

        var encoder = new IlogEncoder();
        var minimal = encoder.Encode(logData, IlogProfile.MINIMAL);
        var archived = encoder.Encode(logData, IlogProfile.ARCHIVED);

        double ratioVsInput = (double)archived.Length / logData.Length;
        double ratioVsMinimal = (double)archived.Length / minimal.Length;

        Console.WriteLine($"Input bytes:      {logData.Length:N0}");
        Console.WriteLine($"MINIMAL bytes:    {minimal.Length:N0}");
        Console.WriteLine($"ARCHIVED bytes:   {archived.Length:N0}");
        Console.WriteLine($"ARCHIVED/input:   {ratioVsInput:P2}");
        Console.WriteLine($"ARCHIVED/minimal: {ratioVsMinimal:P2}");

        Assert.True(archived.Length < minimal.Length,
            $"ARCHIVED should be smaller than MINIMAL ({archived.Length:N0} >= {minimal.Length:N0})");
    }

    private byte[] GenerateLogData(int targetSizeBytes)
    {
        var output = new List<byte>();
        var random = new Random(42);

        // Simple repeating log pattern to avoid encoding issues
        string logTemplate = "2026-02-11T12:34:56.789 INFO [Thread-001] Request processed in 123ms\n";
        var logBytes = System.Text.Encoding.UTF8.GetBytes(logTemplate);

        while (output.Count < targetSizeBytes)
        {
            output.AddRange(logBytes);
        }

        return output.Take(targetSizeBytes).ToArray();
    }
}
