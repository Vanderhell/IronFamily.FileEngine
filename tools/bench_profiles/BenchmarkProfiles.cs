using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using IronConfig.ILog;
using IronConfig.Iupd;

class BenchmarkProfiles
{
    static void Main()
    {
        var repoRoot = FindRepoRoot();

        Console.WriteLine("=== Profile Benchmark Matrix ===\n");

        // Benchmark ILOG profiles
        BenchmarkIlogProfiles(repoRoot);

        Console.WriteLine("\n");

        // Benchmark IUPD profiles
        BenchmarkIupdProfiles();
    }

    static void BenchmarkIlogProfiles(string repoRoot)
    {
        Console.WriteLine("ILOG PROFILE BENCHMARKS");
        Console.WriteLine("Dataset\t\tFileSize(bytes)\tProfile\t\tOpen(ms)\tValidateFast(MB/s)");
        Console.WriteLine(new string('-', 100));

        var datasets = new[] { "small", "medium", "large" };

        foreach (var dataset in datasets)
        {
            var vectorPath = Path.Combine(repoRoot, "vectors/small", "ilog", dataset, "expected", "ilog.ilog");
            if (!File.Exists(vectorPath))
                continue;

            var fileBytes = File.ReadAllBytes(vectorPath);
            long fileSize = fileBytes.Length;

            // Determine which profile this is by reading the flags
            if (fileBytes.Length < 6)
                continue;

            byte flags = fileBytes[5];
            string profileName = DetermineIlogProfile(flags);

            // Benchmark Open
            var sw = Stopwatch.StartNew();
            int openLoops = fileSize < 1000 ? 1000 : 100;
            for (int i = 0; i < openLoops; i++)
                IlogReader.Open(fileBytes, out _);
            sw.Stop();
            double openMs = sw.Elapsed.TotalMilliseconds / openLoops;

            // Benchmark ValidateFast
            IlogReader.Open(fileBytes, out var view);
            sw = Stopwatch.StartNew();
            int fastLoops = fileSize < 1000 ? 10000 : 1000;
            for (int i = 0; i < fastLoops; i++)
                IlogReader.ValidateFast(view);
            sw.Stop();
            double fastMBps = (fileSize * fastLoops) / (sw.Elapsed.TotalMilliseconds * 1024.0 * 1024.0);

            Console.WriteLine($"{dataset}\t\t{fileSize}\t\t{profileName}\t{openMs:F3}\t\t{fastMBps:F0}");
        }
    }

    static void BenchmarkIupdProfiles()
    {
        Console.WriteLine("IUPD PROFILE BENCHMARKS");
        Console.WriteLine("Profile\t\tDataSize\tEncoded(bytes)\tSize%ZIP\tEncodeTime(ms)\tDecodeTime(ms)");
        Console.WriteLine(new string('-', 100));

        var profiles = new[]
        {
            IupdProfile.MINIMAL,
            IupdProfile.FAST,
            IupdProfile.SECURE,
            IupdProfile.OPTIMIZED,
            IupdProfile.INCREMENTAL
        };

        // Test with different data sizes
        var testSizes = new[] { 1024, 10 * 1024, 100 * 1024 };

        foreach (var size in testSizes)
        {
            // Generate deterministic test data
            var testData = GenerateTestData(size);

            foreach (var profile in profiles)
            {
                try
                {
                    // Encode
                    var writer = new IupdWriter();
                    writer.SetProfile(profile);
                    writer.AddChunk(0, testData);
                    writer.SetApplyOrder(0);

                    var sw = Stopwatch.StartNew();
                    var encoded = writer.Build();
                    sw.Stop();
                    double encodeMs = sw.Elapsed.TotalMilliseconds;

                    // Decode
                    var reader = IupdReader.Open(encoded, out var error);
                    if (reader == null)
                        continue;

                    sw = Stopwatch.StartNew();
                    var applier = reader.BeginApply();
                    while (applier.TryNext(out var chunk))
                    {
                        // Read chunk
                        _ = chunk.Payload.ToArray();
                    }
                    sw.Stop();
                    double decodeMs = sw.Elapsed.TotalMilliseconds;

                    // Calculate metrics
                    double sizePercent = (encoded.Length * 100.0) / size;

                    Console.WriteLine($"{profile.GetDisplayName()}\t\t{size}\t\t{encoded.Length}\t\t{sizePercent:F1}%\t\t{encodeMs:F3}\t\t{decodeMs:F3}");
                }
                catch
                {
                    // Skip profiles that fail (e.g., DELTA not yet implemented)
                    Console.WriteLine($"{profile.GetDisplayName()}\t\t{size}\t\t(error)\t\t-\t\t-\t\t-");
                }
            }
        }
    }

    static byte[] GenerateTestData(int size)
    {
        var data = new byte[size];
        for (int i = 0; i < size; i++)
            data[i] = (byte)(i % 256);
        return data;
    }

    static string DetermineIlogProfile(byte flags)
    {
        return flags switch
        {
            0x01 => "MINIMAL",
            0x03 => "INTEGRITY",
            0x09 => "SEARCHABLE",
            0x11 => "ARCHIVED",
            0x07 => "AUDITED",
            _ => $"UNKNOWN(0x{flags:X2})"
        };
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        for (int i = 0; i < 25; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "vectors/small")))
                return dir.FullName;
            dir = dir.Parent;
            if (dir == null) break;
        }
        throw new Exception("Could not find repo root");
    }
}
