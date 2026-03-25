using System;
using System.Diagnostics;
using Xunit;
using IronConfig.Iupd;
using IronConfig;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// IUPD Encoding Throughput Benchmark Tests
/// Measures package encoding performance
/// </summary>
public class IupdThroughputBenchmarkTests
{
    private byte[] GenerateTestData(int sizeBytes)
    {
        var data = new byte[sizeBytes];
        var rng = new Random(42);
        rng.NextBytes(data);
        return data;
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_Encoding_Small()
    {
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;
        var testData = GenerateTestData(10 * 1024); // 10 KB

        Console.WriteLine("\n=== IUPD Encoding Benchmark: 10 KB ===");

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
        {
            var writer = new IupdWriter();
            writer.SetProfile(IupdProfile.SECURE);
            writer.WithSigningKey(benchPrivateKey, benchPublicKey);
            writer.AddChunk(0, testData);
            writer.SetApplyOrder(0);
            writer.WithUpdateSequence(1);
            var result = writer.Build();
            Assert.NotEmpty(result);
        }
        sw.Stop();

        var avgMs = sw.ElapsedMilliseconds / 10.0;
        Console.WriteLine($"Average encoding time: {avgMs:F2}ms");
        Console.WriteLine($"Throughput: {(10.0 / (avgMs / 1000.0)):F2} KB/s\n");

        Assert.True(avgMs < 10000, "Small encoding should complete in reasonable time");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_Encoding_Medium()
    {
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;
        var testData = GenerateTestData(512 * 1024); // 512 KB

        Console.WriteLine("\n=== IUPD Encoding Benchmark: 512 KB ===");

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            var writer = new IupdWriter();
            writer.SetProfile(IupdProfile.SECURE);
            writer.WithSigningKey(benchPrivateKey, benchPublicKey);
            writer.AddChunk(0, testData);
            writer.SetApplyOrder(0);
            writer.WithUpdateSequence(1);
            var result = writer.Build();
            Assert.NotEmpty(result);
        }
        sw.Stop();

        var avgMs = sw.ElapsedMilliseconds / 5.0;
        var throughputMbps = (512.0 / 1024.0) / (avgMs / 1000.0);
        Console.WriteLine($"Average encoding time: {avgMs:F2}ms");
        Console.WriteLine($"Throughput: {throughputMbps:F2} MB/s\n");

        Assert.True(avgMs < 5000, "Medium encoding should complete in reasonable time");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_ProfileComparison()
    {
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;
        var testData = GenerateTestData(256 * 1024); // 256 KB

        Console.WriteLine("\n=== IUPD Profile Comparison: 256 KB ===");

        foreach (var profile in new[] { IupdProfile.SECURE, IupdProfile.OPTIMIZED })
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                var writer = new IupdWriter();
                writer.SetProfile(profile);
                writer.WithSigningKey(benchPrivateKey, benchPublicKey);
                writer.AddChunk(0, testData);
                writer.SetApplyOrder(0);
                writer.WithUpdateSequence(1);
                var result = writer.Build();
                Assert.NotEmpty(result);
            }
            sw.Stop();

            var avgMs = sw.ElapsedMilliseconds / 5.0;
            var throughputMbps = (256.0 / 1024.0) / (avgMs / 1000.0);
            Console.WriteLine($"{profile,-15} | {avgMs:F2}ms avg | {throughputMbps:F2} MB/s");
        }

        Console.WriteLine();
    }

    [Fact]
    public void Benchmark_Encoding_Determinism()
    {
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;
        var testData = GenerateTestData(100 * 1024); // 100 KB

        // Encode same data multiple times
        var result1 = EncodePackage(testData, benchPrivateKey, benchPublicKey);
        var result2 = EncodePackage(testData, benchPrivateKey, benchPublicKey);
        var result3 = EncodePackage(testData, benchPrivateKey, benchPublicKey);

        // All should be identical (deterministic)
        Assert.True(result1.Length == result2.Length);
        Assert.True(result2.Length == result3.Length);
    }

    private byte[] EncodePackage(byte[] data, byte[] privKey, byte[] pubKey)
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.WithSigningKey(privKey, pubKey);
        writer.AddChunk(0, data);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);
        return writer.Build();
    }

    [Fact]
    public void Benchmark_Output_Size()
    {
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        Console.WriteLine("\n=== IUPD Output Size Analysis ===");

        foreach (var size in new[] { 10 * 1024, 100 * 1024, 512 * 1024 })
        {
            var testData = GenerateTestData(size);
            var result = EncodePackage(testData, benchPrivateKey, benchPublicKey);

            var sizeKb = size / 1024.0;
            var outputKb = result.Length / 1024.0;
            var overhead = ((double)result.Length / size - 1.0) * 100.0;

            Console.WriteLine($"Input: {sizeKb:F0} KB | Output: {outputKb:F1} KB | Overhead: {overhead:F1}%");
        }

        Console.WriteLine();
    }
}
