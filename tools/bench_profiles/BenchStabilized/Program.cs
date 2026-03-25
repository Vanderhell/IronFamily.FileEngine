using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronConfig.ILog;
using IronConfig.Iupd;
using IronConfig;

class Program
{
    static void Main()
    {
        var repoRoot = FindRepoRoot();
        var results = new List<BenchResult>();
        var memoryProfiles = new List<MemoryProfile>();

        // Benchmark ILOG profiles
        BenchmarkIlogProfiles(repoRoot, results, memoryProfiles);

        // Benchmark IUPD profiles
        BenchmarkIupdProfiles(results, memoryProfiles);

        // Save to JSON
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.Combine(repoRoot, "artifacts", "bench"));
        var outputPath = Path.Combine(repoRoot, "artifacts", "bench", "profiles_bench.json");
        File.WriteAllText(outputPath, json);

        Console.WriteLine($"Benchmark results saved to {outputPath}");

        // Save memory profiles
        var memoryJson = JsonSerializer.Serialize(memoryProfiles, new JsonSerializerOptions { WriteIndented = true });
        var memoryPath = Path.Combine(repoRoot, "artifacts", "bench", "memory_profiles.json");
        File.WriteAllText(memoryPath, memoryJson);

        Console.WriteLine($"Memory profiles saved to {memoryPath}");
    }

    record BenchResult
    {
        [JsonPropertyName("engine")]
        public string Engine { get; init; }

        [JsonPropertyName("profile")]
        public string Profile { get; init; }

        [JsonPropertyName("dataset")]
        public string Dataset { get; init; }

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; init; }

        [JsonPropertyName("encode_ms_median")]
        public double EncodeMsMedian { get; init; }

        [JsonPropertyName("decode_ms_median")]
        public double DecodeMsMedian { get; init; }

        [JsonPropertyName("validate_fast_ms_median")]
        public double ValidateFastMsMedian { get; init; }

        [JsonPropertyName("validate_strict_ms_median")]
        public double ValidateStrictMsMedian { get; init; }

        [JsonPropertyName("managed_alloc_bytes_median")]
        public long ManagedAllocBytesMedian { get; init; }

        [JsonPropertyName("working_set_bytes_median")]
        public long WorkingSetBytesMedian { get; init; }
    }

    record MemoryProfile
    {
        [JsonPropertyName("scenario")]
        public string Scenario { get; init; }

        [JsonPropertyName("engine")]
        public string Engine { get; init; }

        [JsonPropertyName("allocated_bytes_delta")]
        public long AllocatedBytesDelta { get; init; }

        [JsonPropertyName("heap_delta_bytes")]
        public long HeapDeltaBytes { get; init; }

        [JsonPropertyName("working_set_delta_bytes")]
        public long WorkingSetDeltaBytes { get; init; }

        [JsonPropertyName("gc_collection_count")]
        public int GcCollectionCount { get; init; }
    }

    static void ProfileMemory(Action action, string scenario, string engine, List<MemoryProfile> profiles)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        var startAllocated = GC.GetAllocatedBytesForCurrentThread();
        var startHeap = GC.GetTotalMemory(false);
        var startWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var startGcCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        action();

        var endAllocated = GC.GetAllocatedBytesForCurrentThread();
        var endHeap = GC.GetTotalMemory(false);
        var endWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var endGcCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        profiles.Add(new MemoryProfile
        {
            Scenario = scenario,
            Engine = engine,
            AllocatedBytesDelta = endAllocated - startAllocated,
            HeapDeltaBytes = endHeap - startHeap,
            WorkingSetDeltaBytes = endWorkingSet - startWorkingSet,
            GcCollectionCount = endGcCollections - startGcCollections
        });
    }

    static void BenchmarkIlogProfiles(string repoRoot, List<BenchResult> results, List<MemoryProfile> profiles)
{
    var baseDatasets = new[] { "small", "medium" };
    var heavyMode = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IRONFAMILY_BENCH_HEAVY"));
    var datasets = heavyMode ? new[] { "small", "medium", "large", "mega" } : baseDatasets;

    foreach (var dataset in datasets)
    {
        var vectorPath = Path.Combine(repoRoot, "vectors/small", "ilog", dataset, "expected", "ilog.ilog");
        if (!File.Exists(vectorPath))
            continue;

        var fileBytes = File.ReadAllBytes(vectorPath);
        if (fileBytes.Length < 6)
            continue;

        byte flags = fileBytes[5];
        string profileName = DetermineIlogProfile(flags);

        // Profile memory for ILOG Open
        ProfileMemory(() => IlogReader.Open(fileBytes, out _), $"ILOG.Open.{dataset}", "ILOG", profiles);

        IlogReader.Open(fileBytes, out _);

        var encodeMs = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            IlogReader.Open(fileBytes, out _);
            sw.Stop();
            encodeMs.Add(sw.Elapsed.TotalMilliseconds);
        }

        IlogReader.Open(fileBytes, out var fastView);

        // Profile memory for ILOG ValidateFast
        ProfileMemory(() => IlogReader.ValidateFast(fastView), $"ILOG.ValidateFast.{dataset}", "ILOG", profiles);

        var fastMs = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            IlogReader.ValidateFast(fastView);
            sw.Stop();
            fastMs.Add(sw.Elapsed.TotalMilliseconds);
        }

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
            SizeBytes = fileBytes.Length,
            EncodeMsMedian = encodeMs.OrderBy(x => x).Skip(2).First(),
            DecodeMsMedian = 0,
            ValidateFastMsMedian = fastMs.OrderBy(x => x).Skip(2).First(),
            ValidateStrictMsMedian = strictMs.OrderBy(x => x).Skip(2).First(),
            ManagedAllocBytesMedian = 0,
            WorkingSetBytesMedian = 0
        });
    }
}

    static void BenchmarkIupdProfiles(List<BenchResult> results, List<MemoryProfile> memoryProfiles)
{
    var iupdProfiles = new[] { IupdProfile.MINIMAL, IupdProfile.FAST, IupdProfile.SECURE, IupdProfile.OPTIMIZED };
    var heavyMode = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IRONFAMILY_BENCH_HEAVY"));
    var baseTestSizes = new[] { ("small", 1024), ("medium", 10 * 1024) };
    var heavyTestSizes = new[] { ("small", 1024), ("medium", 10 * 1024), ("large", 100 * 1024), ("mega", 1000 * 1024) };
    var testSizes = heavyMode ? heavyTestSizes : baseTestSizes;

    foreach (var (sizeLabel, size) in testSizes)
    {
        var testData = GenerateTestData(size);
        foreach (var profile in iupdProfiles)
        {
            try
            {
                var encodeMs = new List<double>();
                long encodedSize = 0;

                // Profile memory for IUPD Encode
                byte[] encoded2Temp = null;
                ProfileMemory(() =>
                {
                    var writer = new IupdWriter();
                    writer.SetProfile(profile);
                    writer.AddChunk(0, testData);
                    writer.SetApplyOrder(0);
                    encoded2Temp = writer.Build();
                }, $"IUPD.Encode.{sizeLabel}", "IUPD", memoryProfiles);

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
                    encodedSize = encoded.Length;
                }

                var writer2 = new IupdWriter();
                writer2.SetProfile(profile);
                writer2.AddChunk(0, testData);
                writer2.SetApplyOrder(0);
                var encoded2 = writer2.Build();

                // Profile memory for IUPD Decode
                ProfileMemory(() =>
                {
                    var reader = IupdReader.Open(encoded2, out _);
                    var applier = reader.BeginApply();
                    while (applier.TryNext(out var chunk))
                        _ = chunk.Payload.ToArray();
                }, $"IUPD.Decode.{sizeLabel}", "IUPD", memoryProfiles);

                var decodeMs = new List<double>();
                for (int i = 0; i < 5; i++)
                {
                    var reader = IupdReader.Open(encoded2, out _);
                    var sw = Stopwatch.StartNew();
                    var applier = reader.BeginApply();
                    while (applier.TryNext(out var chunk))
                        _ = chunk.Payload.ToArray();
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

    static string DetermineIlogProfile(byte flags) => flags switch
    {
        0x01 => "MINIMAL",
        0x03 => "INTEGRITY",
        0x09 => "SEARCHABLE",
        0x11 => "ARCHIVED",
        0x07 => "AUDITED",
        _ => $"UNKNOWN(0x{flags:X2})"
    };

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
