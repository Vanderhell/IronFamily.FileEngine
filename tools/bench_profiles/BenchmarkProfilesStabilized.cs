using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronConfig.ILog;
using IronConfig.Iupd;

class BenchmarkProfilesStabilized
{
    private class BenchResult
    {
        [JsonPropertyName("engine")]
        public string Engine { get; set; }

        [JsonPropertyName("profile")]
        public string Profile { get; set; }

        [JsonPropertyName("dataset")]
        public string Dataset { get; set; }

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("encode_ms_median")]
        public double EncodeMsMedian { get; set; }

        [JsonPropertyName("decode_ms_median")]
        public double DecodeMsMedian { get; set; }

        [JsonPropertyName("validate_fast_ms_median")]
        public double ValidateFastMsMedian { get; set; }

        [JsonPropertyName("validate_strict_ms_median")]
        public double ValidateStrictMsMedian { get; set; }

        [JsonPropertyName("managed_alloc_bytes_median")]
        public long ManagedAllocBytesMedian { get; set; }

        [JsonPropertyName("working_set_bytes_median")]
        public long WorkingSetBytesMedian { get; set; }
    }

    static void Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var results = new List<BenchResult>();

        // Benchmark ILOG profiles
        BenchmarkIlogProfiles(repoRoot, results);

        // Benchmark IUPD profiles
        BenchmarkIupdProfiles(results);

        // Save to JSON
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.Combine(repoRoot, "artifacts", "bench"));
        var outputPath = Path.Combine(repoRoot, "artifacts", "bench", "profiles_bench.json");
        File.WriteAllText(outputPath, json);

        Console.WriteLine($"Benchmark results saved to {outputPath}");
    }

    static void BenchmarkIlogProfiles(string repoRoot, List<BenchResult> results)
    {
        var datasets = new[] { "small", "medium" };

        foreach (var dataset in datasets)
        {
            var vectorPath = Path.Combine(repoRoot, "vectors/small", "ilog", dataset, "expected", "ilog.ilog");
            if (!File.Exists(vectorPath))
                continue;

            var fileBytes = File.ReadAllBytes(vectorPath);
            long fileSize = fileBytes.Length;

            if (fileBytes.Length < 6)
                continue;

            byte flags = fileBytes[5];
            string profileName = DetermineIlogProfile(flags);

            // Warmup
            IlogReader.Open(fileBytes, out _);

            // Encode time measurements (N/A for ILOG - using open time as proxy)
            var encodeMs = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                IlogReader.Open(fileBytes, out _);
                sw.Stop();
                encodeMs.Add(sw.Elapsed.TotalMilliseconds);
            }

            // ValidateFast measurements
            IlogReader.Open(fileBytes, out var fastView);
            var fastMs = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                IlogReader.ValidateFast(fastView);
                sw.Stop();
                fastMs.Add(sw.Elapsed.TotalMilliseconds);
            }

            // ValidateStrict measurements
            IlogReader.Open(fileBytes, out var strictView);
            var strictMs = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                IlogReader.ValidateStrict(strictView);
                sw.Stop();
                strictMs.Add(sw.Elapsed.TotalMilliseconds);
            }

            results.Add(new BenchResult
            {
                Engine = "ILOG",
                Profile = profileName,
                Dataset = dataset,
                SizeBytes = fileSize,
                EncodeMsMedian = encodeMs.OrderBy(x => x).Skip(2).First(),
                DecodeMsMedian = 0, // N/A for ILOG
                ValidateFastMsMedian = fastMs.OrderBy(x => x).Skip(2).First(),
                ValidateStrictMsMedian = strictMs.OrderBy(x => x).Skip(2).First(),
                ManagedAllocBytesMedian = 0,
                WorkingSetBytesMedian = 0
            });
        }
    }

    static void BenchmarkIupdProfiles(List<BenchResult> results)
    {
        var profiles = new[]
        {
            IupdProfile.MINIMAL,
            IupdProfile.FAST,
            IupdProfile.SECURE,
            IupdProfile.OPTIMIZED
        };

        var testSizes = new[] { ("small", 1024), ("medium", 10 * 1024) };

        foreach (var (sizeLabel, size) in testSizes)
        {
            var testData = GenerateTestData(size);

            foreach (var profile in profiles)
            {
                try
                {
                    // Warmup
                    var w = new IupdWriter();
                    w.SetProfile(profile);
                    w.AddChunk(0, testData);
                    w.SetApplyOrder(0);
                    var _ = w.Build();

                    // Encode time measurements
                    var encodeMs = new List<double>();
                    for (int i = 0; i < 5; i++)
                    {
                        var writer = new IupdWriter();
                        writer.SetProfile(profile);
                        writer.AddChunk(0, testData);
                        writer.SetApplyOrder(0);

                        var sw = Stopwatch.StartNew();
                        var encoded = writer.Build();
                        sw.Stop();
                        encodeMs.Add(sw.Elapsed.TotalMilliseconds);
                    }

                    // Get encoded size from first run
                    var writer2 = new IupdWriter();
                    writer2.SetProfile(profile);
                    writer2.AddChunk(0, testData);
                    writer2.SetApplyOrder(0);
                    var encoded2 = writer2.Build();
                    long encodedSize = encoded2.Length;

                    // Decode time measurements
                    var decodeMs = new List<double>();
                    for (int i = 0; i < 5; i++)
                    {
                        var reader = IupdReader.Open(encoded2, out _);
                        var sw = Stopwatch.StartNew();
                        var applier = reader.BeginApply();
                        while (applier.TryNext(out var chunk))
                        {
                            _ = chunk.Payload.ToArray();
                        }
                        sw.Stop();
                        decodeMs.Add(sw.Elapsed.TotalMilliseconds);
                    }

                    results.Add(new BenchResult
                    {
                        Engine = "IUPD",
                        Profile = profile.GetDisplayName(),
                        Dataset = sizeLabel,
                        SizeBytes = encodedSize,
                        EncodeMsMedian = encodeMs.OrderBy(x => x).Skip(2).First(),
                        DecodeMsMedian = decodeMs.OrderBy(x => x).Skip(2).First(),
                        ValidateFastMsMedian = 0,
                        ValidateStrictMsMedian = 0,
                        ManagedAllocBytesMedian = 0,
                        WorkingSetBytesMedian = 0
                    });
                }
                catch { }
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
