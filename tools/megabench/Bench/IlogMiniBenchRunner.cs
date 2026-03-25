using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IronConfig.ILog;
using IronFamily.MegaBench.Datasets.ILog;

namespace IronFamily.MegaBench.Bench;

public static class IlogMiniBenchRunner
{
    private const int WarmupCount = 3;
    private const long DefaultTargetTotalUs = 10000;
    private const int DefaultSampleCount = 11;

    public static int Run(string[] args)
    {
        string dataset = "10KB";
        string[] profiles = ["MINIMAL", "INTEGRITY"];
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

        Console.WriteLine("=== ILOG Mini Bench ===");
        Console.WriteLine($"Dataset: {dataset}");
        Console.WriteLine($"Profiles: {string.Join(", ", profiles)}");
        Console.WriteLine($"Label: {label}");
        Console.WriteLine($"Samples: {sampleCount}");
        Console.WriteLine($"TargetUs: {targetTotalUs}");

        var results = new List<IlogMiniBenchResult>();
        foreach (var profileName in profiles)
        {
            var profile = Enum.Parse<IlogProfile>(profileName, ignoreCase: true);
            byte[] data = ILogDatasetGenerator.GenerateDataset(dataset, profile);

            var openSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int j = 0; j < batchN; j++)
                    _ = IlogReader.Open(data, out _);
                return batchN;
            }, sampleCount, targetTotalUs);

            var fastSamples = MeasurePerOpUsSamples(batchN =>
            {
                var openErr = IlogReader.Open(data, out var view);
                if (openErr != null || view == null)
                    throw new InvalidOperationException($"Open failed for {profile}: {openErr?.Code}");

                for (int j = 0; j < batchN; j++)
                {
                    IlogReader.ResetFastValidation(view);
                    _ = IlogReader.ValidateFast(view);
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            var strictSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int j = 0; j < batchN; j++)
                {
                    var openErr = IlogReader.Open(data, out var view);
                    if (openErr != null || view == null)
                        throw new InvalidOperationException($"Open failed for {profile}: {openErr?.Code}");

                    _ = IlogReader.ValidateStrict(view);
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            var verifySamples = MeasurePerOpUsSamples(batchN =>
            {
                var decoder = new IlogDecoder();
                for (int j = 0; j < batchN; j++)
                {
                    if (!decoder.Verify(data))
                        throw new InvalidOperationException($"Verify failed for {profile}");
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            var result = new IlogMiniBenchResult
            {
                Profile = profile.ToString(),
                Bytes = data.Length,
                Open = Stats.Compute(openSamples),
                Fast = Stats.Compute(fastSamples),
                Strict = Stats.Compute(strictSamples),
                Verify = Stats.Compute(verifySamples)
            };

            results.Add(result);
            Console.WriteLine(
                $"{result.Profile}: open={result.Open.Median:F2} us fast={result.Fast.Median:F2} us strict={result.Strict.Median:F2} us verify={result.Verify.Median:F2} us p95(strict)={result.Strict.P95:F2} us cv(strict)={result.Strict.Cv:F3}");
        }

        string outDir = Path.Combine(
            GetRepoRoot(),
            "artifacts", "bench", "megabench_metrics", "ilog_mini");
        Directory.CreateDirectory(outDir);

        string outPath = Path.Combine(outDir, $"ilog_mini_{dataset}_{label}.json");
        File.WriteAllText(outPath, JsonSerializer.Serialize(new
        {
            dataset,
            label,
            profiles = results
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"artifact: {Path.GetFullPath(outPath)}");
        return 0;
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

    private sealed class IlogMiniBenchResult
    {
        public string Profile { get; set; } = "";
        public int Bytes { get; set; }
        public StatsSummary Open { get; set; } = new();
        public StatsSummary Fast { get; set; } = new();
        public StatsSummary Strict { get; set; } = new();
        public StatsSummary Verify { get; set; } = new();
    }
}
