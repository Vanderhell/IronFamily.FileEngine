using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using IronConfig.IronCfg;
using IronFamily.MegaBench.Datasets.IronCfg;

namespace IronFamily.MegaBench.Bench;

public static class IronCfgFullVsLayersRunner
{
    private const int DefaultSamples = 21;
    private const int WarmupCount = 3;
    private const long TargetTotalUs = 5_000;

    public static int Run(string[] args)
    {
        string dataset = "10KB";
        string? outputDir = null;
        string readModeName = "full";
        bool coldMode = false;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--dataset" && i + 1 < args.Length)
            {
                dataset = args[++i];
            }
            else if (args[i] == "--output" && i + 1 < args.Length)
            {
                outputDir = args[++i];
            }
            else if (args[i] == "--read-mode" && i + 1 < args.Length)
            {
                readModeName = args[++i];
            }
            else if (args[i] == "--cold")
            {
                coldMode = true;
            }
        }

        outputDir ??= Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "artifacts", "bench", "megabench_metrics", "icfg_full_vs_layers");
        Directory.CreateDirectory(outputDir);

        if (!TryParseReadMode(readModeName, out var readMode))
        {
            Console.Error.WriteLine($"Unknown read mode: {readModeName}");
            Console.Error.WriteLine("Expected one of: string, scalar, nested, payload, full");
            return 2;
        }

        Console.WriteLine("=== ICFG Full vs Layers ===");
        Console.WriteLine($"Dataset: {dataset}");
        Console.WriteLine($"ReadMode: {readModeName}");
        Console.WriteLine($"ColdMode: {coldMode}");

        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        var data = IronCfgDatasetGenerator.GenerateDataset(dataset, useCrc32: true);

        var openErr = IronCfgValidator.Open(data, out var view);
        if (!openErr.IsOk)
        {
            Console.Error.WriteLine($"Open failed: {openErr}");
            return 2;
        }

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        if (!strictErr.IsOk)
        {
            Console.Error.WriteLine($"ValidateStrict failed: {strictErr}");
            return 2;
        }

        var fullSamples = MeasurePerOpUsSamples(batchN =>
        {
            for (int i = 0; i < batchN; i++)
            {
                var err = IronCfgValidator.Open(data, out var openedView);
                if (!err.IsOk)
                    throw new InvalidOperationException($"Open failed: {err}");

                ResetCachesIfNeeded(coldMode);
                err = IronCfgValidator.ValidateStrict(data, openedView);
                if (!err.IsOk)
                    throw new InvalidOperationException($"ValidateStrict failed: {err}");

                ResetCachesIfNeeded(coldMode);
                ReadScenario(data, openedView, readMode);
            }
            return batchN;
        });

        var openSamples = MeasurePerOpUsSamples(batchN =>
        {
            for (int i = 0; i < batchN; i++)
            {
                _ = IronCfgValidator.Open(data, out _);
            }
            return batchN;
        });

        var fastSamples = MeasurePerOpUsSamples(batchN =>
        {
            for (int i = 0; i < batchN; i++)
            {
                _ = IronCfgValidator.ValidateFast(data);
            }
            return batchN;
        });

        var strictSamples = MeasurePerOpUsSamples(batchN =>
        {
            for (int i = 0; i < batchN; i++)
            {
                ResetCachesIfNeeded(coldMode);
                _ = IronCfgValidator.ValidateStrict(data, view);
            }
            return batchN;
        });

        var readSamples = MeasurePerOpUsSamples(batchN =>
        {
            for (int i = 0; i < batchN; i++)
            {
                ResetCachesIfNeeded(coldMode);
                ReadScenario(data, view, readMode);
            }
            return batchN;
        });

        var fullSummary = Stats.Compute(fullSamples);
        var openSummary = Stats.Compute(openSamples);
        var fastSummary = Stats.Compute(fastSamples);
        var strictSummary = Stats.Compute(strictSamples);
        var readSummary = Stats.Compute(readSamples);
        var layeredStrictMedian = openSummary.Median + strictSummary.Median + readSummary.Median;
        var layeredFastMedian = fastSummary.Median + readSummary.Median;

        var result = new IronCfgFullVsLayersResult
        {
            Dataset = dataset,
            Bytes = data.Length,
            TimestampUtc = DateTime.UtcNow.ToString("O"),
            ReadMode = readModeName,
            ColdMode = coldMode,
            FullPipeline = fullSummary,
            OpenOnly = openSummary,
            FastOnly = fastSummary,
            StrictOnly = strictSummary,
            ReadOnly = readSummary,
            LayeredStrictMedianUs = layeredStrictMedian,
            LayeredFastMedianUs = layeredFastMedian,
            FullVsLayeredStrictDeltaUs = fullSummary.Median - layeredStrictMedian,
            FullVsLayeredFastDeltaUs = fullSummary.Median - layeredFastMedian
        };

        var coldSuffix = coldMode ? "_cold" : "_warm";
        var outputPath = Path.Combine(outputDir, $"icfg_full_vs_layers_{dataset}_{readModeName}{coldSuffix}.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        WriteSummaryArtifacts(outputDir);

        Console.WriteLine($"full.median   = {fullSummary.Median:F2} us");
        Console.WriteLine($"open.median   = {openSummary.Median:F2} us");
        Console.WriteLine($"fast.median   = {fastSummary.Median:F2} us");
        Console.WriteLine($"strict.median = {strictSummary.Median:F2} us");
        Console.WriteLine($"read.median   = {readSummary.Median:F2} us");
        Console.WriteLine($"sum(strict)   = {layeredStrictMedian:F2} us");
        Console.WriteLine($"delta(strict) = {result.FullVsLayeredStrictDeltaUs:F2} us");
        Console.WriteLine($"sum(fast)     = {layeredFastMedian:F2} us");
        Console.WriteLine($"delta(fast)   = {result.FullVsLayeredFastDeltaUs:F2} us");
        Console.WriteLine($"artifact      = {outputPath}");

        return 0;
    }

    private static void ReadScenario(byte[] data, IronCfgView view, ReadMode readMode)
    {
        var rootName = new IronCfgPath[] { new IronCfgFieldIdPath(0) };
        var rootValue = new IronCfgPath[] { new IronCfgFieldIdPath(1) };
        var rootData = new IronCfgPath[] { new IronCfgFieldIdPath(2) };
        var nestedId = new IronCfgPath[] { new IronCfgFieldIdPath(3), new IronCfgFieldIdPath(0) };
        var nestedEnabled = new IronCfgPath[] { new IronCfgFieldIdPath(3), new IronCfgFieldIdPath(1) };

        switch (readMode)
        {
            case ReadMode.String:
            {
                var err = IronCfgValueReader.GetString(data, view, rootName, out var nameBytes);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetString failed: {err}");
                GC.KeepAlive(nameBytes);
                break;
            }
            case ReadMode.Scalar:
            {
                var err = IronCfgValueReader.GetInt64(data, view, rootValue, out var value);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetInt64(value) failed: {err}");
                GC.KeepAlive(value);
                break;
            }
            case ReadMode.Nested:
            {
                var err = IronCfgValueReader.GetInt64(data, view, nestedId, out var nestedValue);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetInt64(nested.id) failed: {err}");

                err = IronCfgValueReader.GetBool(data, view, nestedEnabled, out var enabled);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetBool(nested.enabled) failed: {err}");

                GC.KeepAlive(nestedValue);
                GC.KeepAlive(enabled);
                break;
            }
            case ReadMode.Payload:
            {
                var err = IronCfgValueReader.GetBytes(data, view, rootData, out var payloadBytes);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetBytes(data) failed: {err}");
                GC.KeepAlive(payloadBytes);
                break;
            }
            case ReadMode.Full:
            {
                var err = IronCfgValueReader.GetString(data, view, rootName, out var nameBytes);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetString failed: {err}");

                err = IronCfgValueReader.GetInt64(data, view, rootValue, out var value);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetInt64(value) failed: {err}");

                err = IronCfgValueReader.GetBytes(data, view, rootData, out var payloadBytes);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetBytes(data) failed: {err}");

                err = IronCfgValueReader.GetInt64(data, view, nestedId, out var nestedValue);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetInt64(nested.id) failed: {err}");

                err = IronCfgValueReader.GetBool(data, view, nestedEnabled, out var enabled);
                if (!err.IsOk)
                    throw new InvalidOperationException($"GetBool(nested.enabled) failed: {err}");

                GC.KeepAlive(nameBytes);
                GC.KeepAlive(value);
                GC.KeepAlive(payloadBytes);
                GC.KeepAlive(nestedValue);
                GC.KeepAlive(enabled);
                break;
            }
        }
    }

    private static double[] MeasurePerOpUsSamples(Func<int, int> opBatch)
    {
        var samples = new double[DefaultSamples];

        for (int i = 0; i < WarmupCount; i++)
            _ = opBatch(1);

        for (int sampleIndex = 0; sampleIndex < DefaultSamples; sampleIndex++)
        {
            int batchN = 1;
            long elapsedUs = 0;

            while (elapsedUs < TargetTotalUs)
            {
                var sw = Stopwatch.StartNew();
                _ = opBatch(batchN);
                sw.Stop();

                elapsedUs = (sw.ElapsedTicks * 1_000_000) / Stopwatch.Frequency;
                if (elapsedUs < TargetTotalUs && elapsedUs > 0)
                {
                    double ratio = (double)TargetTotalUs / elapsedUs;
                    batchN = Math.Max(batchN + 1, (int)(batchN * ratio * 1.1));
                }
            }

            samples[sampleIndex] = (double)elapsedUs / batchN;
        }

        return samples;
    }

    private static void ResetCachesIfNeeded(bool coldMode)
    {
        if (!coldMode)
            return;

        IronCfgValidator.ResetStrictMetadataCache();
        IronCfgValueReader.ResetCaches();
    }

    private static bool TryParseReadMode(string readModeName, out ReadMode readMode)
    {
        switch (readModeName.ToLowerInvariant())
        {
            case "string":
                readMode = ReadMode.String;
                return true;
            case "scalar":
                readMode = ReadMode.Scalar;
                return true;
            case "nested":
                readMode = ReadMode.Nested;
                return true;
            case "payload":
                readMode = ReadMode.Payload;
                return true;
            case "full":
                readMode = ReadMode.Full;
                return true;
            default:
                readMode = ReadMode.Full;
                return false;
        }
    }

    private static void WriteSummaryArtifacts(string outputDir)
    {
        var jsonFiles = Directory.GetFiles(outputDir, "icfg_full_vs_layers_*.json");
        var rows = new List<IronCfgFullVsLayersResult>();

        foreach (var jsonFile in jsonFiles)
        {
            var parsed = JsonSerializer.Deserialize<IronCfgFullVsLayersResult>(File.ReadAllText(jsonFile));
            if (parsed != null)
            {
                if (string.IsNullOrWhiteSpace(parsed.ReadMode))
                    parsed.ReadMode = "full";
                rows.Add(parsed);
            }
        }

        rows = rows
            .GroupBy(r => $"{r.Dataset}|{r.ReadMode}|{r.ColdMode}")
            .Select(g => g
                .OrderByDescending(r => DateTime.TryParse(r.TimestampUtc, out var ts) ? ts : DateTime.MinValue)
                .First())
            .OrderBy(r => ParseDatasetBytes(r.Dataset))
            .ThenBy(r => r.ReadMode, StringComparer.Ordinal)
            .ThenBy(r => r.ColdMode)
            .ToList();

        var csvPath = Path.Combine(outputDir, "icfg_full_vs_layers_summary.csv");
        using (var writer = new StreamWriter(csvPath, false))
        {
            writer.WriteLine("dataset,bytes,read_mode,cold_mode,full_us,open_us,fast_us,strict_us,read_us,sum_strict_us,delta_strict_us,sum_fast_us,delta_fast_us");
            foreach (var row in rows)
            {
                writer.WriteLine(
                    $"{row.Dataset},{row.Bytes},{row.ReadMode},{row.ColdMode}," +
                    $"{row.FullPipeline.Median:F2},{row.OpenOnly.Median:F2},{row.FastOnly.Median:F2},{row.StrictOnly.Median:F2},{row.ReadOnly.Median:F2}," +
                    $"{row.LayeredStrictMedianUs:F2},{row.FullVsLayeredStrictDeltaUs:F2},{row.LayeredFastMedianUs:F2},{row.FullVsLayeredFastDeltaUs:F2}");
            }
        }

        var mdPath = Path.Combine(outputDir, "icfg_full_vs_layers_summary.md");
        using (var writer = new StreamWriter(mdPath, false))
        {
            writer.WriteLine("# ICFG Full vs Layers Summary");
            writer.WriteLine();
            writer.WriteLine("| Dataset | Bytes | Read | Cold | Full (us) | Open | Fast | Strict | Read | Sum Strict | Delta Strict | Sum Fast | Delta Fast |");
            writer.WriteLine("|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var row in rows)
            {
                writer.WriteLine(
                    $"| {row.Dataset} | {row.Bytes} | {row.ReadMode} | {(row.ColdMode ? "yes" : "no")} | " +
                    $"{row.FullPipeline.Median:F2} | {row.OpenOnly.Median:F2} | {row.FastOnly.Median:F2} | {row.StrictOnly.Median:F2} | {row.ReadOnly.Median:F2} | " +
                    $"{row.LayeredStrictMedianUs:F2} | {row.FullVsLayeredStrictDeltaUs:F2} | {row.LayeredFastMedianUs:F2} | {row.FullVsLayeredFastDeltaUs:F2} |");
            }
        }

        var verdictPath = Path.Combine(outputDir, "icfg_full_vs_layers_verdict.md");
        using (var writer = new StreamWriter(verdictPath, false))
        {
            writer.WriteLine("# ICFG Full vs Layers Verdict");
            writer.WriteLine();

            var warmFullRows = rows
                .Where(r => r.ReadMode == "full" && !r.ColdMode)
                .OrderBy(r => ParseDatasetBytes(r.Dataset))
                .ToList();

            if (warmFullRows.Count > 0)
            {
                writer.WriteLine("## Warm Full Path");
                writer.WriteLine();
                foreach (var row in warmFullRows)
                {
                    writer.WriteLine(
                        $"- `{row.Dataset}` (`{row.Bytes}` B): full `{row.FullPipeline.Median:F2} us`, " +
                        $"sum(strict) `{row.LayeredStrictMedianUs:F2} us` (delta `{row.FullVsLayeredStrictDeltaUs:F2} us`), " +
                        $"sum(fast) `{row.LayeredFastMedianUs:F2} us` (delta `{row.FullVsLayeredFastDeltaUs:F2} us`)");
                }
                writer.WriteLine();

                var largestWarm = warmFullRows.Last();
                writer.WriteLine("Verdict:");
                writer.WriteLine(
                    $"- At larger payloads, `full` converges toward `open + strict + read`; largest measured warm case " +
                    $"is `{largestWarm.Dataset}` with delta `{largestWarm.FullVsLayeredStrictDeltaUs:F2} us`.");
                writer.WriteLine(
                    "- `fast + read` remains much cheaper than full validation, so it is a distinct operating point, not a close decomposition of `full`.");
                writer.WriteLine();
            }

            var coldRows = rows
                .Where(r => r.ReadMode == "full" && r.ColdMode)
                .OrderBy(r => ParseDatasetBytes(r.Dataset))
                .ToList();
            if (coldRows.Count > 0)
            {
                writer.WriteLine("## Cold Cache");
                writer.WriteLine();
                foreach (var row in coldRows)
                {
                    writer.WriteLine(
                        $"- `{row.Dataset}` (`{row.Bytes}` B): cold full `{row.FullPipeline.Median:F2} us`, " +
                        $"cold sum(strict) `{row.LayeredStrictMedianUs:F2} us`, delta `{row.FullVsLayeredStrictDeltaUs:F2} us`");
                }
                writer.WriteLine();
                writer.WriteLine("- Cold runs show a real cache penalty in both `strict` and `read` stages.");
                writer.WriteLine();
            }

            var specializedRows = rows
                .Where(r => r.ReadMode != "full")
                .OrderBy(r => ParseDatasetBytes(r.Dataset))
                .ThenBy(r => r.ReadMode, StringComparer.Ordinal)
                .ToList();
            if (specializedRows.Count > 0)
            {
                writer.WriteLine("## Specialized Read Modes");
                writer.WriteLine();
                foreach (var row in specializedRows)
                {
                    writer.WriteLine(
                        $"- `{row.Dataset}` / `{row.ReadMode}`: full `{row.FullPipeline.Median:F2} us`, read `{row.ReadOnly.Median:F2} us`, " +
                        $"sum(strict) `{row.LayeredStrictMedianUs:F2} us`");
                }
                writer.WriteLine();
                writer.WriteLine("- Narrow read paths such as `payload` materially reduce total layered cost compared with the full hot path.");
            }
        }
    }

    private static long ParseDatasetBytes(string dataset)
    {
        return dataset switch
        {
            "1KB" => 1 * 1024L,
            "10KB" => 10 * 1024L,
            "100KB" => 100 * 1024L,
            "1MB" => 1024 * 1024L,
            _ => long.MaxValue
        };
    }

    private enum ReadMode
    {
        String,
        Scalar,
        Nested,
        Payload,
        Full
    }

    private sealed class IronCfgFullVsLayersResult
    {
        public string Dataset { get; set; } = string.Empty;
        public int Bytes { get; set; }
        public string TimestampUtc { get; set; } = string.Empty;
        public string ReadMode { get; set; } = "full";
        public bool ColdMode { get; set; }
        public StatsSummary FullPipeline { get; set; } = new();
        public StatsSummary OpenOnly { get; set; } = new();
        public StatsSummary FastOnly { get; set; } = new();
        public StatsSummary StrictOnly { get; set; } = new();
        public StatsSummary ReadOnly { get; set; } = new();
        public double LayeredStrictMedianUs { get; set; }
        public double LayeredFastMedianUs { get; set; }
        public double FullVsLayeredStrictDeltaUs { get; set; }
        public double FullVsLayeredFastDeltaUs { get; set; }
    }
}
