using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using IronConfig.ILog;

namespace IronCert.Benchmarking;

/// <summary>
/// ILOG profile benchmark runner.
/// Tests all 5 profiles (MINIMAL, INTEGRITY, SEARCHABLE, ARCHIVED, AUDITED).
/// </summary>
public class IlogBenchmarks
{
    private readonly string _datasetPath;
    private readonly int _iterations;

    public enum IlogProfile
    {
        MINIMAL,      // L0+L1 only
        INTEGRITY,    // L0+L1+L4(CRC32)
        SEARCHABLE,   // L0+L1+L2
        ARCHIVED,     // L0+L1+L3(compression)
        AUDITED       // L0+L1+L4(BLAKE3)
    }

    public IlogBenchmarks(string datasetPath, int iterations = 10)
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
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED,
            IlogProfile.AUDITED
        };

        Console.WriteLine("\n📊 ILOG Profile Benchmarking");
        Console.WriteLine("=" .PadRight(70, '='));
        Console.WriteLine($"📁 Dataset: {Path.GetFileName(_datasetPath)}");
        Console.WriteLine($"🔄 Iterations per profile: {_iterations}\n");

        var results = new List<ProfileResult>();

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
    public ProfileResult BenchmarkProfile(IlogProfile profile)
    {
        Console.Write($"🧪 {GetProfileName(profile):15} ... ");

        var encodeMetrics = new List<BenchmarkMetrics>();
        var decodeMetrics = new List<BenchmarkMetrics>();
        var verifyMetrics = new List<BenchmarkMetrics>();

        // Warm-up
        for (int i = 0; i < 1; i++)
        {
            RunEncode(profile);
        }

        // Benchmark iterations
        for (int i = 0; i < _iterations; i++)
        {
            encodeMetrics.Add(RunEncode(profile));
            decodeMetrics.Add(RunDecode(profile));
            if (profile != IlogProfile.MINIMAL)
            {
                verifyMetrics.Add(RunVerify(profile));
            }
            Console.Write(".");
        }

        Console.WriteLine(" ✓");

        return new ProfileResult
        {
            Profile = profile,
            EncodeMetrics = encodeMetrics,
            DecodeMetrics = decodeMetrics,
            VerifyMetrics = verifyMetrics
        };
    }

    private BenchmarkMetrics RunEncode(IlogProfile profile)
    {
        var sw = Stopwatch.StartNew();
        var beforeMem = GC.GetTotalMemory(true);

        var data = File.ReadAllBytes(_datasetPath);
        var encoded = EncodeWithProfile(data, profile);

        sw.Stop();
        var afterMem = GC.GetTotalMemory(false);

        return new BenchmarkMetrics
        {
            OperationType = "Encode",
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            BytesProcessed = data.LongLength,
            OutputBytes = encoded.LongLength,
            MemoryDelta = afterMem - beforeMem,
            SizeRatio = (encoded.LongLength / (double)data.LongLength) * 100
        };
    }

    private BenchmarkMetrics RunDecode(IlogProfile profile)
    {
        var data = File.ReadAllBytes(_datasetPath);
        var encoded = EncodeWithProfile(data, profile);

        var sw = Stopwatch.StartNew();
        var beforeMem = GC.GetTotalMemory(true);

        var decoded = DecodeWithProfile(encoded, profile);

        sw.Stop();
        var afterMem = GC.GetTotalMemory(false);

        return new BenchmarkMetrics
        {
            OperationType = "Decode",
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            BytesProcessed = encoded.LongLength,
            OutputBytes = decoded.LongLength,
            MemoryDelta = afterMem - beforeMem
        };
    }

    private BenchmarkMetrics RunVerify(IlogProfile profile)
    {
        var data = File.ReadAllBytes(_datasetPath);
        var encoded = EncodeWithProfile(data, profile);

        var sw = Stopwatch.StartNew();
        var beforeMem = GC.GetTotalMemory(true);

        var isValid = VerifyWithProfile(encoded, profile);

        sw.Stop();
        var afterMem = GC.GetTotalMemory(false);

        return new BenchmarkMetrics
        {
            OperationType = "Verify",
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            BytesProcessed = encoded.LongLength,
            OutputBytes = 1,
            MemoryDelta = afterMem - beforeMem
        };
    }

    private byte[] EncodeWithProfile(byte[] data, IlogProfile profile)
    {
        var encoder = new IlogEncoder();
        var ilogProfile = profile switch
        {
            IlogProfile.MINIMAL => IlogEncoder.IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY => IlogEncoder.IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE => IlogEncoder.IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED => IlogEncoder.IlogProfile.ARCHIVED,
            IlogProfile.AUDITED => IlogEncoder.IlogProfile.AUDITED,
            _ => IlogEncoder.IlogProfile.MINIMAL
        };

        return encoder.Encode(data, ilogProfile);
    }

    private byte[] DecodeWithProfile(byte[] encoded, IlogProfile profile)
    {
        var decoder = new IlogDecoder();
        return decoder.Decode(encoded);
    }

    private bool VerifyWithProfile(byte[] encoded, IlogProfile profile)
    {
        var decoder = new IlogDecoder();
        return decoder.Verify(encoded);
    }

    private byte GetProfileFlags(IlogProfile profile) => profile switch
    {
        IlogProfile.MINIMAL => 0x01,           // LittleEndian only
        IlogProfile.INTEGRITY => 0x03,         // LE | CRC32
        IlogProfile.SEARCHABLE => 0x09,        // LE | L2(indices)
        IlogProfile.ARCHIVED => 0x11,          // LE | L3(compression)
        IlogProfile.AUDITED => 0x07,           // LE | CRC32 | BLAKE3
        _ => 0x01
    };

    private string GetProfileName(IlogProfile profile) => profile switch
    {
        IlogProfile.MINIMAL => "MINIMAL",
        IlogProfile.INTEGRITY => "INTEGRITY",
        IlogProfile.SEARCHABLE => "SEARCHABLE",
        IlogProfile.ARCHIVED => "ARCHIVED",
        IlogProfile.AUDITED => "AUDITED",
        _ => "UNKNOWN"
    };

    private void PrintResults(List<ProfileResult> results)
    {
        Console.WriteLine("\n📈 ILOG Profile Results\n");

        foreach (var result in results)
        {
            PrintProfileResult(result);
        }

        PrintComparison(results);
    }

    private void PrintProfileResult(ProfileResult result)
    {
        var profile = result.Profile;
        Console.WriteLine($"\n▶ {GetProfileName(profile)}");
        Console.WriteLine(new string('─', 60));

        if (result.EncodeMetrics.Any())
        {
            PrintMetrics("Encode", result.EncodeMetrics);
        }

        if (result.DecodeMetrics.Any())
        {
            PrintMetrics("Decode", result.DecodeMetrics);
        }

        if (result.VerifyMetrics.Any())
        {
            PrintMetrics("Verify", result.VerifyMetrics);
        }
    }

    private void PrintMetrics(string operation, List<BenchmarkMetrics> metrics)
    {
        var avg = metrics.Average(m => m.ElapsedMs);
        var p50 = metrics.OrderBy(m => m.ElapsedMs).Skip(metrics.Count / 2).First().ElapsedMs;
        var p95 = metrics.OrderBy(m => m.ElapsedMs).Skip((int)(metrics.Count * 0.95)).First().ElapsedMs;

        var throughput = metrics.Count > 0 && metrics[0].BytesProcessed > 0
            ? (metrics[0].BytesProcessed / (avg / 1000.0)) / 1_000_000
            : 0;

        var avgMem = metrics.Average(m => m.MemoryDelta);
        var sizeRatio = metrics.FirstOrDefault()?.SizeRatio ?? 0;

        Console.Write($"  {operation,-10} ");
        Console.Write($"Throughput: {throughput,7:F1} MB/s  ");
        Console.Write($"Avg: {avg,7:F2}ms  ");
        Console.Write($"P50: {p50,7:F2}ms  ");
        Console.Write($"P95: {p95,7:F2}ms");

        if (sizeRatio > 0)
        {
            Console.Write($"  Size: {sizeRatio,6:F1}%");
        }

        Console.WriteLine();
    }

    private void PrintComparison(List<ProfileResult> results)
    {
        Console.WriteLine("\n📊 Profile Comparison\n");

        Console.WriteLine("| Profile     | Encode (MB/s) | Decode (MB/s) | Size Ratio | Best For |");
        Console.WriteLine("|-------------|---------------|---------------|-----------|----------|");

        foreach (var result in results)
        {
            var encodeSpeed = result.EncodeMetrics.Any() && result.EncodeMetrics[0].BytesProcessed > 0
                ? (result.EncodeMetrics[0].BytesProcessed / (result.EncodeMetrics.Average(m => m.ElapsedMs) / 1000.0)) / 1_000_000
                : 0;

            var decodeSpeed = result.DecodeMetrics.Any() && result.DecodeMetrics[0].BytesProcessed > 0
                ? (result.DecodeMetrics[0].BytesProcessed / (result.DecodeMetrics.Average(m => m.ElapsedMs) / 1000.0)) / 1_000_000
                : 0;

            var sizeRatio = result.EncodeMetrics.FirstOrDefault()?.SizeRatio ?? 0;
            var bestFor = GetBestFor(result.Profile);

            Console.WriteLine($"| {GetProfileName(result.Profile),-11} | {encodeSpeed,13:F1} | {decodeSpeed,13:F1} | {sizeRatio,9:F1}% | {bestFor,-8} |");
        }

        Console.WriteLine();
    }

    private string GetBestFor(IlogProfile profile) => profile switch
    {
        IlogProfile.MINIMAL => "Speed",
        IlogProfile.INTEGRITY => "Balance",
        IlogProfile.SEARCHABLE => "Queries",
        IlogProfile.ARCHIVED => "Storage",
        IlogProfile.AUDITED => "Security",
        _ => "?"
    };
}

public class ProfileResult
{
    public IlogBenchmarks.IlogProfile Profile { get; set; }
    public List<BenchmarkMetrics> EncodeMetrics { get; set; } = new();
    public List<BenchmarkMetrics> DecodeMetrics { get; set; } = new();
    public List<BenchmarkMetrics> VerifyMetrics { get; set; } = new();
}
