using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace IronCert.Benchmarking;

/// <summary>
/// IUPD profile benchmark runner.
/// Tests all 5 profiles (MINIMAL, SIMPLE, SEQUENTIAL, STREAMING, VERIFIED).
/// </summary>
public class IupdBenchmarks
{
    private readonly string _datasetPath;
    private readonly int _iterations;

    public enum IupdProfile
    {
        MINIMAL,      // Chunks only, no integrity, no deps
        SIMPLE,       // Chunks + CRC32, no deps
        SEQUENTIAL,   // Chunks + CRC32 + Dependencies
        STREAMING,    // All + Streaming apply capability
        VERIFIED      // All + BLAKE3 hashing
    }

    public IupdBenchmarks(string datasetPath, int iterations = 10)
    {
        _datasetPath = datasetPath;
        _iterations = iterations;
    }

    /// <summary>
    /// Run benchmarks for all 5 profiles.
    /// </summary>
    public void RunAllProfiles()
    {
        var profiles = new[]
        {
            IupdProfile.MINIMAL,
            IupdProfile.SIMPLE,
            IupdProfile.SEQUENTIAL,
            IupdProfile.STREAMING,
            IupdProfile.VERIFIED
        };

        Console.WriteLine("\n📦 IUPD Profile Benchmarking");
        Console.WriteLine("=".PadRight(70, '='));
        Console.WriteLine($"📁 Dataset: {Path.GetFileName(_datasetPath)}");
        Console.WriteLine($"🔄 Iterations per profile: {_iterations}\n");

        var results = new List<IupdProfileResult>();

        foreach (var profile in profiles)
        {
            var result = BenchmarkProfile(profile);
            results.Add(result);
        }

        PrintResults(results);
    }

    /// <summary>
    /// Benchmark a single profile.
    /// </summary>
    public IupdProfileResult BenchmarkProfile(IupdProfile profile)
    {
        Console.Write($"🧪 {GetProfileName(profile):15} ... ");

        var encodeMetrics = new List<BenchmarkMetrics>();
        var applyMetrics = new List<BenchmarkMetrics>();
        var verifyMetrics = new List<BenchmarkMetrics>();

        // Warm-up
        RunEncode(profile);

        // Benchmark iterations
        for (int i = 0; i < _iterations; i++)
        {
            encodeMetrics.Add(RunEncode(profile));
            applyMetrics.Add(RunApply(profile));

            if (NeedsVerification(profile))
            {
                verifyMetrics.Add(RunVerify(profile));
            }

            Console.Write(".");
        }

        Console.WriteLine(" ✓");

        return new IupdProfileResult
        {
            Profile = profile,
            EncodeMetrics = encodeMetrics,
            ApplyMetrics = applyMetrics,
            VerifyMetrics = verifyMetrics
        };
    }

    private BenchmarkMetrics RunEncode(IupdProfile profile)
    {
        var sw = Stopwatch.StartNew();
        var beforeMem = GC.GetTotalMemory(true);

        var data = File.ReadAllBytes(_datasetPath);
        var (manifest, payload) = EncodeWithProfile(data, profile);

        sw.Stop();
        var afterMem = GC.GetTotalMemory(false);

        var totalSize = manifest.LongLength + payload.LongLength;

        return new BenchmarkMetrics
        {
            OperationType = "Encode",
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            BytesProcessed = data.LongLength,
            OutputBytes = totalSize,
            MemoryDelta = afterMem - beforeMem,
            SizeRatio = (totalSize / (double)data.LongLength) * 100
        };
    }

    private BenchmarkMetrics RunApply(IupdProfile profile)
    {
        var data = File.ReadAllBytes(_datasetPath);
        var (manifest, payload) = EncodeWithProfile(data, profile);

        var sw = Stopwatch.StartNew();
        var beforeMem = GC.GetTotalMemory(true);

        var result = ApplyWithProfile(manifest, payload, profile);

        sw.Stop();
        var afterMem = GC.GetTotalMemory(false);

        return new BenchmarkMetrics
        {
            OperationType = "Apply",
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            BytesProcessed = payload.LongLength,
            OutputBytes = result.LongLength,
            MemoryDelta = afterMem - beforeMem
        };
    }

    private BenchmarkMetrics RunVerify(IupdProfile profile)
    {
        var data = File.ReadAllBytes(_datasetPath);
        var (manifest, payload) = EncodeWithProfile(data, profile);

        var sw = Stopwatch.StartNew();
        var beforeMem = GC.GetTotalMemory(true);

        var isValid = VerifyWithProfile(manifest, payload, profile);

        sw.Stop();
        var afterMem = GC.GetTotalMemory(false);

        return new BenchmarkMetrics
        {
            OperationType = "Verify",
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            BytesProcessed = payload.LongLength,
            OutputBytes = 1,
            MemoryDelta = afterMem - beforeMem
        };
    }

    private (byte[], byte[]) EncodeWithProfile(byte[] data, IupdProfile profile)
    {
        // TODO: Integrate with actual IUPD encoder
        // For now, simulate profile-specific encoding

        var manifest = GenerateManifest(data, profile);
        var payload = data;

        return (manifest, payload);
    }

    private byte[] GenerateManifest(byte[] data, IupdProfile profile)
    {
        var chunkCount = Math.Max(1, data.Length / 1024);
        var manifestSize = profile switch
        {
            IupdProfile.MINIMAL => 100 + (chunkCount * 20),
            IupdProfile.SIMPLE => 100 + (chunkCount * 24),       // +4 bytes for CRC32
            IupdProfile.SEQUENTIAL => 100 + (chunkCount * 32),   // +8 bytes for deps
            IupdProfile.STREAMING => 100 + (chunkCount * 40),    // +16 bytes for streaming metadata
            IupdProfile.VERIFIED => 100 + (chunkCount * 56),     // +32 bytes for BLAKE3
            _ => 100 + (chunkCount * 20)
        };

        var manifest = new byte[manifestSize];
        return manifest;
    }

    private byte[] ApplyWithProfile(byte[] manifest, byte[] payload, IupdProfile profile)
    {
        // TODO: Integrate with actual IUPD applier
        // For now, return payload
        return payload;
    }

    private bool VerifyWithProfile(byte[] manifest, byte[] payload, IupdProfile profile)
    {
        // TODO: Integrate with actual IUPD verifier
        return manifest.Length > 0 && payload.Length > 0;
    }

    private bool NeedsVerification(IupdProfile profile) => profile switch
    {
        IupdProfile.MINIMAL => false,
        IupdProfile.SIMPLE => true,           // CRC32
        IupdProfile.SEQUENTIAL => true,       // CRC32
        IupdProfile.STREAMING => true,        // CRC32
        IupdProfile.VERIFIED => true,         // BLAKE3
        _ => false
    };

    private string GetProfileName(IupdProfile profile) => profile switch
    {
        IupdProfile.MINIMAL => "MINIMAL",
        IupdProfile.SIMPLE => "SIMPLE",
        IupdProfile.SEQUENTIAL => "SEQUENTIAL",
        IupdProfile.STREAMING => "STREAMING",
        IupdProfile.VERIFIED => "VERIFIED",
        _ => "UNKNOWN"
    };

    private void PrintResults(List<IupdProfileResult> results)
    {
        Console.WriteLine("\n📈 IUPD Profile Results\n");

        foreach (var result in results)
        {
            PrintProfileResult(result);
        }

        PrintComparison(results);
    }

    private void PrintProfileResult(IupdProfileResult result)
    {
        var profile = result.Profile;
        Console.WriteLine($"\n▶ {GetProfileName(profile)}");
        Console.WriteLine(new string('─', 70));

        if (result.EncodeMetrics.Any())
        {
            PrintMetrics("Encode", result.EncodeMetrics);
        }

        if (result.ApplyMetrics.Any())
        {
            PrintMetrics("Apply", result.ApplyMetrics);
        }

        if (result.VerifyMetrics.Any())
        {
            PrintMetrics("Verify", result.VerifyMetrics);
        }
    }

    private void PrintMetrics(string operation, List<BenchmarkMetrics> metrics)
    {
        var avg = metrics.Average(m => m.ElapsedMs);
        var min = metrics.Min(m => m.ElapsedMs);
        var max = metrics.Max(m => m.ElapsedMs);
        var p50 = metrics.OrderBy(m => m.ElapsedMs).Skip(metrics.Count / 2).First().ElapsedMs;
        var p95 = metrics.OrderBy(m => m.ElapsedMs).Skip((int)(metrics.Count * 0.95)).First().ElapsedMs;

        var throughput = metrics.Count > 0 && metrics[0].BytesProcessed > 0
            ? (metrics[0].BytesProcessed / (avg / 1000.0)) / 1_000_000
            : 0;

        var avgMem = metrics.Average(m => m.MemoryDelta);
        var sizeRatio = metrics.FirstOrDefault()?.SizeRatio ?? 0;

        Console.WriteLine($"  {operation,-10}");
        Console.WriteLine($"    Throughput:  {throughput,7:F1} MB/s");
        Console.WriteLine($"    Avg:         {avg,7:F2} ms");
        Console.WriteLine($"    Min:         {min,7:F2} ms");
        Console.WriteLine($"    P50:         {p50,7:F2} ms");
        Console.WriteLine($"    P95:         {p95,7:F2} ms");
        Console.WriteLine($"    Max:         {max,7:F2} ms");
        Console.WriteLine($"    Memory Δ:    {(avgMem / 1024.0),7:F1} KB");

        if (sizeRatio > 0)
        {
            Console.WriteLine($"    Size Ratio:  {sizeRatio,7:F1}%");
        }
    }

    private void PrintComparison(List<IupdProfileResult> results)
    {
        Console.WriteLine("\n📊 Profile Comparison\n");

        Console.WriteLine("| Profile     | Encode (MB/s) | Apply (MB/s) | Size Ratio | Manifest | Best For |");
        Console.WriteLine("|-------------|---------------|--------------|-----------|----------|----------|");

        foreach (var result in results)
        {
            var encodeSpeed = result.EncodeMetrics.Any() && result.EncodeMetrics[0].BytesProcessed > 0
                ? (result.EncodeMetrics[0].BytesProcessed / (result.EncodeMetrics.Average(m => m.ElapsedMs) / 1000.0)) / 1_000_000
                : 0;

            var applySpeed = result.ApplyMetrics.Any() && result.ApplyMetrics[0].BytesProcessed > 0
                ? (result.ApplyMetrics[0].BytesProcessed / (result.ApplyMetrics.Average(m => m.ElapsedMs) / 1000.0)) / 1_000_000
                : 0;

            var sizeRatio = result.EncodeMetrics.FirstOrDefault()?.SizeRatio ?? 0;
            var manifestSize = GetManifestSizeEstimate(result.Profile);
            var bestFor = GetBestFor(result.Profile);

            Console.WriteLine($"| {GetProfileName(result.Profile),-11} | {encodeSpeed,13:F1} | {applySpeed,12:F1} | {sizeRatio,9:F1}% | {manifestSize,-8} | {bestFor,-8} |");
        }

        Console.WriteLine();
    }

    private string GetManifestSizeEstimate(IupdProfile profile) => profile switch
    {
        IupdProfile.MINIMAL => "Minimal",
        IupdProfile.SIMPLE => "Small",
        IupdProfile.SEQUENTIAL => "Medium",
        IupdProfile.STREAMING => "Medium",
        IupdProfile.VERIFIED => "Large",
        _ => "?"
    };

    private string GetBestFor(IupdProfile profile) => profile switch
    {
        IupdProfile.MINIMAL => "Speed",
        IupdProfile.SIMPLE => "Reliable",
        IupdProfile.SEQUENTIAL => "Ordered",
        IupdProfile.STREAMING => "Memory",
        IupdProfile.VERIFIED => "Security",
        _ => "?"
    };
}

public class IupdProfileResult
{
    public IupdBenchmarks.IupdProfile Profile { get; set; }
    public List<BenchmarkMetrics> EncodeMetrics { get; set; } = new();
    public List<BenchmarkMetrics> ApplyMetrics { get; set; } = new();
    public List<BenchmarkMetrics> VerifyMetrics { get; set; } = new();
}
