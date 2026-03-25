using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IronConfig.Iupd;
using IronFamily.MegaBench.Datasets.IUpd;

namespace IronFamily.MegaBench.Bench;

public static class IupdMiniBenchRunner
{
    private const int WarmupCount = 3;
    private const long DefaultTargetTotalUs = 10000;
    private const int DefaultSampleCount = 11;

    public static int Run(string[] args)
    {
        string dataset = "10KB";
        string[] profiles = ["MINIMAL", "FAST", "SECURE", "OPTIMIZED", "INCREMENTAL"];
        string label = "default";
        int sampleCount = DefaultSampleCount;
        long targetTotalUs = DefaultTargetTotalUs;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--dataset" && i + 1 < args.Length)
            {
                dataset = args[++i];
            }
            else if (args[i] == "--profiles" && i + 1 < args.Length)
            {
                profiles = args[++i]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (args[i] == "--label" && i + 1 < args.Length)
            {
                label = args[++i];
            }
            else if (args[i] == "--samples" && i + 1 < args.Length)
            {
                sampleCount = int.Parse(args[++i]);
            }
            else if (args[i] == "--target-us" && i + 1 < args.Length)
            {
                targetTotalUs = long.Parse(args[++i]);
            }
        }

        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        Environment.SetEnvironmentVariable("IRONFAMILY_BENCH_ALLOW_ALL_IUPD_PROFILES", "1");

        Console.WriteLine("=== IUPD Mini Bench ===");
        Console.WriteLine($"Dataset: {dataset}");
        Console.WriteLine($"Profiles: {string.Join(", ", profiles)}");
        Console.WriteLine($"Label: {label}");
        Console.WriteLine($"Samples: {sampleCount}");
        Console.WriteLine($"TargetUs: {targetTotalUs}");

        var results = new List<IupdMiniBenchResult>();
        foreach (var profileName in profiles)
        {
            var profile = Enum.Parse<IupdProfile>(profileName, ignoreCase: true);
            byte[] data = IUpdDatasetGenerator.GenerateDataset(dataset, profile);
            PreWarmProfile(data, profile);

            var openSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int j = 0; j < batchN; j++)
                    _ = IupdReader.OpenStreaming(data.AsMemory(), out _);
                return batchN;
            }, sampleCount, targetTotalUs);

            var fastSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int j = 0; j < batchN; j++)
                {
                    var reader = OpenReader(data, profile);
                    var err = reader.ValidateFast();
                    if (!err.IsOk)
                        throw new InvalidOperationException($"ValidateFast failed for {profile}: {err.Code}");
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            var strictSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int j = 0; j < batchN; j++)
                {
                    var reader = OpenReader(data, profile);
                    var err = reader.ValidateStrict();
                    if (!err.IsOk)
                        throw new InvalidOperationException($"ValidateStrict failed for {profile}: {err.Code}");
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            var applySamples = MeasurePerOpUsSamples(batchN =>
            {
                long totalBytes = 0;
                for (int j = 0; j < batchN; j++)
                {
                    var reader = OpenReader(data, profile);
                    var err = reader.ValidateStrict();
                    if (!err.IsOk)
                        throw new InvalidOperationException($"ValidateStrict failed for {profile}: {err.Code}");

                    var applier = reader.BeginApply();
                    while (applier.TryNext(out var chunk))
                        totalBytes += chunk.Payload.Length;
                }

                GC.KeepAlive(totalBytes);
                return batchN;
            }, sampleCount, targetTotalUs);

            var result = new IupdMiniBenchResult
            {
                Profile = profile.ToString(),
                Bytes = data.Length,
                Open = Stats.Compute(openSamples),
                Fast = Stats.Compute(fastSamples),
                Strict = Stats.Compute(strictSamples),
                Apply = Stats.Compute(applySamples)
            };

            results.Add(result);
            Console.WriteLine(
                $"{result.Profile}: open={result.Open.Median:F2} us fast={result.Fast.Median:F2} us strict={result.Strict.Median:F2} us apply={result.Apply.Median:F2} us p95(strict)={result.Strict.P95:F2} us cv(strict)={result.Strict.Cv:F3}");
        }

        string outDir = Path.Combine(
            GetRepoRoot(),
            "artifacts", "bench", "megabench_metrics", "iupd_mini");
        Directory.CreateDirectory(outDir);

        string outPath = Path.Combine(outDir, $"iupd_mini_{dataset}_{label}.json");
        File.WriteAllText(outPath, JsonSerializer.Serialize(new
        {
            dataset,
            label,
            profiles = results
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"artifact: {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static IupdReader OpenReader(byte[] data, IupdProfile profile)
    {
        var reader = IupdReader.OpenStreaming(data.AsMemory(), out var err);
        if (reader == null || !err.IsOk)
            throw new InvalidOperationException($"Open failed for {profile}: {err.Code}");
        return reader;
    }

    private static void PreWarmProfile(byte[] data, IupdProfile profile)
    {
        var reader = OpenReader(data, profile);
        var fastErr = reader.ValidateFast();
        if (!fastErr.IsOk)
            throw new InvalidOperationException($"Prewarm ValidateFast failed for {profile}: {fastErr.Code}");

        reader = OpenReader(data, profile);
        var strictErr = reader.ValidateStrict();
        if (!strictErr.IsOk)
            throw new InvalidOperationException($"Prewarm ValidateStrict failed for {profile}: {strictErr.Code}");

        reader = OpenReader(data, profile);
        strictErr = reader.ValidateStrict();
        if (!strictErr.IsOk)
            throw new InvalidOperationException($"Prewarm apply failed for {profile}: {strictErr.Code}");

        long totalBytes = 0;
        var applier = reader.BeginApply();
        while (applier.TryNext(out var chunk))
            totalBytes += chunk.Payload.Length;
        GC.KeepAlive(totalBytes);
    }

    private static double[] MeasurePerOpUsSamples(Func<int, int> opBatch, int sampleCount, long targetTotalUs)
    {
        var samples = new List<double>();

        for (int w = 0; w < WarmupCount; w++)
            _ = opBatch(1);

        for (int s = 0; s < sampleCount; s++)
        {
            int batchN = 1;
            long elapsedUs = 0;
            while (elapsedUs < targetTotalUs)
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                int executed = opBatch(batchN);
                long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                elapsedUs = elapsedTicks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

                if (executed != batchN)
                    throw new InvalidOperationException("Batch count mismatch.");

                if (elapsedUs < targetTotalUs && elapsedUs > 0)
                {
                    double ratio = (double)targetTotalUs / elapsedUs;
                    batchN = Math.Max(batchN + 1, (int)(batchN * ratio * 1.1));
                }
            }

            samples.Add((double)elapsedUs / batchN);
        }

        return samples.ToArray();
    }

    private static string GetRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "tools", "megabench", "MegaBench.csproj")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private sealed class IupdMiniBenchResult
    {
        public string Profile { get; set; } = "";
        public int Bytes { get; set; }
        public StatsSummary Open { get; set; } = new();
        public StatsSummary Fast { get; set; } = new();
        public StatsSummary Strict { get; set; } = new();
        public StatsSummary Apply { get; set; } = new();
    }
}
