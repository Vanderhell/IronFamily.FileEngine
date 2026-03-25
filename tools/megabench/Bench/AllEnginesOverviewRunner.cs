using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IronConfig.IronCfg;
using IronConfig.ILog;
using IronConfig.Iupd;
using IronFamily.MegaBench.Datasets.IronCfg;
using IronFamily.MegaBench.Datasets.ILog;
using IronFamily.MegaBench.Datasets.IUpd;

namespace IronFamily.MegaBench.Bench;

public static class AllEnginesOverviewRunner
{
    private const int WarmupCount = 3;
    private const int IronCfgHotWarmupIters = 48;
    private const long IronCfgMinTargetTotalUs = 50000;
    private const int DefaultSampleCount = 11;
    private const long DefaultTargetTotalUs = 10000;

    public static int Run(string[] args)
    {
        string[] datasets = ["100KB", "1MB"];
        int sampleCount = DefaultSampleCount;
        long targetTotalUs = DefaultTargetTotalUs;
        string label = "overview";
        bool cleanupOld = false;
        bool icfgCold = false;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--datasets" && i + 1 < args.Length)
            {
                datasets = args[++i]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (args[i] == "--samples" && i + 1 < args.Length)
            {
                sampleCount = int.Parse(args[++i]);
            }
            else if (args[i] == "--target-us" && i + 1 < args.Length)
            {
                targetTotalUs = long.Parse(args[++i]);
            }
            else if (args[i] == "--label" && i + 1 < args.Length)
            {
                label = args[++i];
            }
            else if (args[i] == "--cleanup-old")
            {
                cleanupOld = true;
            }
            else if (args[i] == "--icfg-cold")
            {
                icfgCold = true;
            }
        }

        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        Environment.SetEnvironmentVariable("IRONFAMILY_BENCH_ALLOW_ALL_IUPD_PROFILES", "1");

        var report = new OverviewReport
        {
            TimestampUtc = DateTime.UtcNow.ToString("O"),
            Label = label,
            SampleCount = sampleCount,
            TargetUs = targetTotalUs
        };

        Console.WriteLine("=== All Engines Overview ===");
        Console.WriteLine($"Datasets: {string.Join(", ", datasets)}");
        Console.WriteLine($"Samples: {sampleCount}");
        Console.WriteLine($"TargetUs: {targetTotalUs}");
        Console.WriteLine($"IcfgCold: {icfgCold}");

        foreach (var dataset in datasets)
        {
            string datasetLabel = NormalizeDatasetLabel(dataset);
            report.Entries.AddRange(BenchIronCfg(datasetLabel, sampleCount, targetTotalUs, icfgCold));
            report.Entries.AddRange(BenchIlog(datasetLabel, sampleCount, targetTotalUs));
            report.Entries.AddRange(BenchIupd(datasetLabel, sampleCount, targetTotalUs));
        }

        string outDir = Path.Combine(GetRepoRoot(), "artifacts", "bench", "megabench_metrics", "overview");
        Directory.CreateDirectory(outDir);

        string jsonPath = Path.Combine(outDir, $"overview_{label}.json");
        string mdPath = Path.Combine(outDir, $"overview_{label}.md");
        string csvPath = Path.Combine(outDir, $"overview_{label}.csv");
        string rankingMdPath = Path.Combine(outDir, $"overview_{label}_ranking.md");
        string rankingCsvPath = Path.Combine(outDir, $"overview_{label}_ranking.csv");

        string latestJsonPath = Path.Combine(outDir, "overview_latest.json");
        string latestMdPath = Path.Combine(outDir, "overview_latest.md");
        string latestCsvPath = Path.Combine(outDir, "overview_latest.csv");
        string latestRankingMdPath = Path.Combine(outDir, "overview_latest_ranking.md");
        string latestRankingCsvPath = Path.Combine(outDir, "overview_latest_ranking.csv");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(mdPath, RenderMarkdown(report));
        File.WriteAllText(csvPath, RenderCsv(report));
        File.WriteAllText(rankingMdPath, RenderRankingMarkdown(report));
        File.WriteAllText(rankingCsvPath, RenderRankingCsv(report));

        File.WriteAllText(latestJsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(latestMdPath, RenderMarkdown(report));
        File.WriteAllText(latestCsvPath, RenderCsv(report));
        File.WriteAllText(latestRankingMdPath, RenderRankingMarkdown(report));
        File.WriteAllText(latestRankingCsvPath, RenderRankingCsv(report));

        if (cleanupOld)
            CleanupOldArtifacts(outDir, label);

        Console.WriteLine($"json: {jsonPath}");
        Console.WriteLine($"md:   {mdPath}");
        Console.WriteLine($"csv:  {csvPath}");
        Console.WriteLine($"rank-md:  {rankingMdPath}");
        Console.WriteLine($"rank-csv: {rankingCsvPath}");
        Console.WriteLine($"latest: {latestJsonPath}");
        return 0;
    }

    private static IEnumerable<OverviewEntry> BenchIronCfg(string dataset, int sampleCount, long targetTotalUs, bool coldMode)
    {
        Console.WriteLine($"[ICFG] {dataset}");

        byte[] encoded = IronCfgDatasetGenerator.GenerateDataset(dataset, useCrc32: true);
        PreWarmIronCfg(encoded);
        var openErr = IronCfgValidator.Open(encoded, out var stableView);
        if (!openErr.IsOk)
            throw new InvalidOperationException($"ICFG open failed: {openErr.Code}");
        WarmupIronCfgHotPaths(encoded, stableView);
        long icfgTargetUs = Math.Max(targetTotalUs, IronCfgMinTargetTotalUs);

        ForceGc();
        var strictSamples = MeasurePerOpUsSamples(batchN =>
        {
            for (int i = 0; i < batchN; i++)
            {
                if (coldMode)
                {
                    ResetIronCfgCaches();
                    openErr = IronCfgValidator.Open(encoded, out stableView);
                    if (!openErr.IsOk)
                        throw new InvalidOperationException($"ICFG open failed: {openErr.Code}");
                }

                var strictErr = IronCfgValidator.ValidateStrict(encoded, stableView);
                if (!strictErr.IsOk)
                    throw new InvalidOperationException($"ICFG strict failed: {strictErr.Code}");
            }
            return batchN;
        }, sampleCount, icfgTargetUs);

        ForceGc();
        var readSamples = MeasurePerOpUsSamples(batchN =>
        {
            for (int i = 0; i < batchN; i++)
            {
                if (coldMode)
                {
                    ResetIronCfgCaches();
                    openErr = IronCfgValidator.Open(encoded, out stableView);
                    if (!openErr.IsOk)
                        throw new InvalidOperationException($"ICFG open failed: {openErr.Code}");
                }

                ReadIronCfgScenario(encoded, stableView);
            }
            return batchN;
        }, sampleCount, icfgTargetUs);

        ForceGc();
        var encodeSamples = MeasurePerOpUsSamples(batchN =>
        {
            for (int i = 0; i < batchN; i++)
                _ = IronCfgDatasetGenerator.GenerateDataset(dataset, useCrc32: true);
            return batchN;
        }, sampleCount, targetTotalUs);

        yield return new OverviewEntry
        {
            Engine = "ICFG",
            Dataset = dataset,
            Profile = "default",
            EncodedBytes = encoded.Length,
            Encode = Stats.Compute(encodeSamples),
            Strict = Stats.Compute(strictSamples),
            Consume = Stats.Compute(readSamples),
            ConsumeLabel = "read"
        };
    }

    private static IEnumerable<OverviewEntry> BenchIlog(string dataset, int sampleCount, long targetTotalUs)
    {
        foreach (var profile in Enum.GetValues<IlogProfile>())
        {
            Console.WriteLine($"[ILOG] {dataset} {profile}");
            byte[] encoded = ILogDatasetGenerator.GenerateDataset(dataset, profile);

            var encodeSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int i = 0; i < batchN; i++)
                    _ = ILogDatasetGenerator.GenerateDataset(dataset, profile);
                return batchN;
            }, sampleCount, targetTotalUs);

            var strictSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int i = 0; i < batchN; i++)
                {
                    var openErr = IlogReader.Open(encoded, out var view);
                    if (openErr != null || view == null)
                        throw new InvalidOperationException($"ILOG open failed: {openErr?.Code}");

                    var strictErr = IlogReader.ValidateStrict(view);
                    if (strictErr != null)
                        throw new InvalidOperationException($"ILOG strict failed: {strictErr.Code}");
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            var decodeMetric = TryMeasureMetric(() =>
                MeasurePerOpUsSamples(batchN =>
                {
                    var decoder = new IlogDecoder();
                    for (int i = 0; i < batchN; i++)
                        _ = decoder.Decode(encoded);
                    return batchN;
                }, sampleCount, targetTotalUs));

            var verifyMetric = TryMeasureMetric(() =>
                MeasurePerOpUsSamples(batchN =>
                {
                    var decoder = new IlogDecoder();
                    for (int i = 0; i < batchN; i++)
                    {
                        if (!decoder.Verify(encoded))
                            throw new InvalidOperationException($"ILOG verify failed: {profile}");
                    }
                    return batchN;
                }, sampleCount, targetTotalUs));

            yield return new OverviewEntry
            {
                Engine = "ILOG",
                Dataset = dataset,
                Profile = profile.ToString(),
                EncodedBytes = encoded.Length,
                Encode = Stats.Compute(encodeSamples),
                Strict = Stats.Compute(strictSamples),
                Consume = decodeMetric.Summary,
                ConsumeLabel = "decode",
                ConsumeError = decodeMetric.Error,
                Verify = verifyMetric.Summary,
                VerifyError = verifyMetric.Error
            };
        }
    }

    private static IEnumerable<OverviewEntry> BenchIupd(string dataset, int sampleCount, long targetTotalUs)
    {
        foreach (var profile in Enum.GetValues<IupdProfile>())
        {
            Console.WriteLine($"[IUPD] {dataset} {profile}");
            byte[] encoded = IUpdDatasetGenerator.GenerateDataset(dataset, profile);
            PreWarmIupdProfile(encoded, profile);

            var encodeSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int i = 0; i < batchN; i++)
                    _ = IUpdDatasetGenerator.GenerateDataset(dataset, profile);
                return batchN;
            }, sampleCount, targetTotalUs);

            var strictSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int i = 0; i < batchN; i++)
                {
                    var reader = OpenIupdReader(encoded, profile);
                    var strictErr = reader.ValidateStrict();
                    if (!strictErr.IsOk)
                        throw new InvalidOperationException($"IUPD strict failed: {strictErr.Code}");
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            var applySamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int i = 0; i < batchN; i++)
                {
                    var reader = OpenIupdReader(encoded, profile);
                    var strictErr = reader.ValidateStrict();
                    if (!strictErr.IsOk)
                        throw new InvalidOperationException($"IUPD strict failed: {strictErr.Code}");

                    long totalBytes = 0;
                    var applier = reader.BeginApply();
                    while (applier.TryNext(out var chunk))
                        totalBytes += chunk.Payload.Length;
                    GC.KeepAlive(totalBytes);
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            var fastSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int i = 0; i < batchN; i++)
                {
                    var reader = OpenIupdReader(encoded, profile);
                    var fastErr = reader.ValidateFast();
                    if (!fastErr.IsOk)
                        throw new InvalidOperationException($"IUPD fast failed: {fastErr.Code}");
                }
                return batchN;
            }, sampleCount, targetTotalUs);

            yield return new OverviewEntry
            {
                Engine = "IUPD",
                Dataset = dataset,
                Profile = profile.ToString(),
                EncodedBytes = encoded.Length,
                Encode = Stats.Compute(encodeSamples),
                Fast = Stats.Compute(fastSamples),
                Strict = Stats.Compute(strictSamples),
                Consume = Stats.Compute(applySamples),
                ConsumeLabel = "apply_after_strict"
            };
        }
    }

    private static IupdReader OpenIupdReader(byte[] data, IupdProfile profile)
    {
        var reader = IupdReader.OpenStreaming(data.AsMemory(), out var err);
        if (reader == null || !err.IsOk)
            throw new InvalidOperationException($"IUPD open failed for {profile}: {err.Code}");
        return reader;
    }

    private static void ResetIronCfgCaches()
    {
        IronCfgValidator.ResetStrictMetadataCache();
        IronCfgValueReader.ResetCaches();
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void PreWarmIronCfg(byte[] encoded)
    {
        ResetIronCfgCaches();
        var openErr = IronCfgValidator.Open(encoded, out var view);
        if (!openErr.IsOk)
            throw new InvalidOperationException($"ICFG prewarm open failed: {openErr.Code}");

        var strictErr = IronCfgValidator.ValidateStrict(encoded, view);
        if (!strictErr.IsOk)
            throw new InvalidOperationException($"ICFG prewarm strict failed: {strictErr.Code}");

        ReadIronCfgScenario(encoded, view);
    }

    private static void WarmupIronCfgHotPaths(byte[] data, IronCfgView view)
    {
        for (int i = 0; i < IronCfgHotWarmupIters; i++)
        {
            var strictErr = IronCfgValidator.ValidateStrict(data, view);
            if (!strictErr.IsOk)
                throw new InvalidOperationException($"ICFG warmup strict failed: {strictErr.Code}");
            ReadIronCfgScenario(data, view);
        }
    }

    private static void PreWarmIupdProfile(byte[] encoded, IupdProfile profile)
    {
        var reader = OpenIupdReader(encoded, profile);
        var fastErr = reader.ValidateFast();
        if (!fastErr.IsOk)
            throw new InvalidOperationException($"IUPD prewarm fast failed: {fastErr.Code}");

        reader = OpenIupdReader(encoded, profile);
        var strictErr = reader.ValidateStrict();
        if (!strictErr.IsOk)
            throw new InvalidOperationException($"IUPD prewarm strict failed: {strictErr.Code}");

        reader = OpenIupdReader(encoded, profile);
        strictErr = reader.ValidateStrict();
        if (!strictErr.IsOk)
            throw new InvalidOperationException($"IUPD prewarm apply failed: {strictErr.Code}");

        long totalBytes = 0;
        var applier = reader.BeginApply();
        while (applier.TryNext(out var chunk))
            totalBytes += chunk.Payload.Length;
        GC.KeepAlive(totalBytes);
    }

    private static void ReadIronCfgScenario(byte[] data, IronCfgView view)
    {
        var rootName = new IronCfgPath[] { new IronCfgFieldIdPath(0) };
        var rootValue = new IronCfgPath[] { new IronCfgFieldIdPath(1) };
        var rootData = new IronCfgPath[] { new IronCfgFieldIdPath(2) };
        var nestedId = new IronCfgPath[] { new IronCfgFieldIdPath(3), new IronCfgFieldIdPath(0) };
        var nestedEnabled = new IronCfgPath[] { new IronCfgFieldIdPath(3), new IronCfgFieldIdPath(1) };

        var err = IronCfgValueReader.GetString(data, view, rootName, out var nameBytes);
        if (!err.IsOk)
            throw new InvalidOperationException($"GetString failed: {err.Code}");

        err = IronCfgValueReader.GetInt64(data, view, rootValue, out var value);
        if (!err.IsOk)
            throw new InvalidOperationException($"GetInt64 failed: {err.Code}");

        err = IronCfgValueReader.GetBytes(data, view, rootData, out var payloadBytes);
        if (!err.IsOk)
            throw new InvalidOperationException($"GetBytes failed: {err.Code}");
        ulong payloadChecksum = ComputePayloadChecksum(payloadBytes.Span);

        err = IronCfgValueReader.GetInt64(data, view, nestedId, out var nestedValue);
        if (!err.IsOk)
            throw new InvalidOperationException($"GetInt64 nested failed: {err.Code}");

        err = IronCfgValueReader.GetBool(data, view, nestedEnabled, out var enabled);
        if (!err.IsOk)
            throw new InvalidOperationException($"GetBool failed: {err.Code}");

        GC.KeepAlive(nameBytes);
        GC.KeepAlive(value);
        GC.KeepAlive(payloadBytes);
        GC.KeepAlive(payloadChecksum);
        GC.KeepAlive(nestedValue);
        GC.KeepAlive(enabled);
    }

    private static ulong ComputePayloadChecksum(ReadOnlySpan<byte> payload)
    {
        ulong acc = 1469598103934665603UL; // FNV offset basis
        for (int i = 0; i < payload.Length; i++)
            acc = (acc ^ payload[i]) * 1099511628211UL;
        return acc;
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

    private static (StatsSummary? Summary, string? Error) TryMeasureMetric(Func<double[]> measure)
    {
        try
        {
            return (Stats.Compute(measure()), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static string NormalizeDatasetLabel(string dataset)
    {
        return dataset.Trim().ToUpperInvariant() switch
        {
            "1024" => "1KB",
            "10240" => "10KB",
            "102400" => "100KB",
            "1048576" => "1MB",
            _ => dataset.Trim().ToUpperInvariant()
        };
    }

    private static string GetRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "tools", "megabench", "MegaBench.csproj")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string RenderCsv(OverviewReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("engine,dataset,profile,encoded_bytes,encode_us,fast_us,strict_us,consume_label,consume_us,consume_error,verify_us,verify_error");
        foreach (var e in report.Entries.OrderBy(x => x.Engine).ThenBy(x => x.Dataset).ThenBy(x => x.Profile))
        {
            sb.AppendLine(
                $"{e.Engine},{e.Dataset},{e.Profile},{e.EncodedBytes},{FormatNumber(e.Encode.Median)},{Format(e.Fast)},{FormatNumber(e.Strict.Median)},{e.ConsumeLabel},{Format(e.Consume)},{EscapeCsv(e.ConsumeError)},{Format(e.Verify)},{EscapeCsv(e.VerifyError)}");
        }
        return sb.ToString();
    }

    private static string RenderMarkdown(OverviewReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# All Engines Overview");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{report.TimestampUtc}`");
        sb.AppendLine();
        sb.AppendLine($"Samples: `{report.SampleCount}`");
        sb.AppendLine();
        sb.AppendLine($"TargetUs: `{report.TargetUs}`");
        sb.AppendLine();

        foreach (var dataset in report.Entries.Select(x => x.Dataset).Distinct().OrderBy(x => x))
        {
            sb.AppendLine($"## {dataset}");
            sb.AppendLine();
            sb.AppendLine("| Engine | Profile | Encoded Bytes | Encode | Fast | Strict | Consume | Verify |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---|---:|");

            foreach (var e in report.Entries.Where(x => x.Dataset == dataset).OrderBy(x => x.Engine).ThenBy(x => x.Profile))
            {
                string consume = e.Consume != null
                    ? $"{e.ConsumeLabel} {FormatNumber(e.Consume.Median, 2)} us"
                    : $"{e.ConsumeLabel} ERR: {e.ConsumeError}";
                sb.AppendLine($"| {e.Engine} | {e.Profile} | {e.EncodedBytes} | {FormatNumber(e.Encode.Median, 2)} us | {Format(e.Fast)} | {FormatNumber(e.Strict.Median, 2)} us | {consume} | {FormatWithError(e.Verify, e.VerifyError)} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string RenderRankingMarkdown(OverviewReport report)
    {
        var rows = BuildRankingRows(report);
        var sb = new StringBuilder();
        sb.AppendLine("# All Engines Ranking");
        sb.AppendLine();
        sb.AppendLine("Metrics: encode, strict, decode, verify, apply_after_strict, encoded bytes.");
        sb.AppendLine();

        foreach (var dataset in rows.Select(r => r.Dataset).Distinct().OrderBy(ParseDatasetBytes))
        {
            sb.AppendLine($"## {dataset}");
            sb.AppendLine();
            sb.AppendLine("| Rank | Engine | Profile | Score | encode (us) | strict (us) | decode (us) | verify (us) | apply_after_strict (us) | bytes |");
            sb.AppendLine("|---:|---|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (var row in rows.Where(r => r.Dataset == dataset).OrderBy(r => r.Rank))
            {
                sb.AppendLine($"| {row.Rank} | {row.Engine} | {row.Profile} | {FormatNumber(row.Score, 3)} | {FormatNullable(row.EncodeUs)} | {FormatNullable(row.StrictUs)} | {FormatNullable(row.DecodeUs)} | {FormatNullable(row.VerifyUs)} | {FormatNullable(row.ApplyAfterStrictUs)} | {row.EncodedBytes} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string RenderRankingCsv(OverviewReport report)
    {
        var rows = BuildRankingRows(report);
        var sb = new StringBuilder();
        sb.AppendLine("dataset,rank,engine,profile,score,encode_us,strict_us,decode_us,verify_us,apply_after_strict_us,encoded_bytes");
        foreach (var row in rows.OrderBy(r => ParseDatasetBytes(r.Dataset)).ThenBy(r => r.Rank))
        {
            sb.AppendLine($"{row.Dataset},{row.Rank},{row.Engine},{row.Profile},{FormatNumber(row.Score, 3)},{FormatNullable(row.EncodeUs)},{FormatNullable(row.StrictUs)},{FormatNullable(row.DecodeUs)},{FormatNullable(row.VerifyUs)},{FormatNullable(row.ApplyAfterStrictUs)},{row.EncodedBytes}");
        }
        return sb.ToString();
    }

    private static List<RankingRow> BuildRankingRows(OverviewReport report)
    {
        var rows = new List<RankingRow>();
        var entriesByDataset = report.Entries
            .GroupBy(e => e.Dataset, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var (dataset, entries) in entriesByDataset)
        {
            var metricRanks = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                metricRanks[$"{entry.Engine}|{entry.Profile}"] = 0;
            }

            ApplyRank(entries, metricRanks, e => e.Encode.Median);
            ApplyRank(entries.Where(e => e.Strict != null), metricRanks, e => e.Strict.Median);
            ApplyRank(entries.Where(e => string.Equals(e.ConsumeLabel, "decode", StringComparison.OrdinalIgnoreCase) && e.Consume != null), metricRanks, e => e.Consume!.Median);
            ApplyRank(entries.Where(e => e.Verify != null), metricRanks, e => e.Verify!.Median);
            ApplyRank(entries.Where(e => string.Equals(e.ConsumeLabel, "apply_after_strict", StringComparison.OrdinalIgnoreCase) && e.Consume != null), metricRanks, e => e.Consume!.Median);
            ApplyRank(entries, metricRanks, e => (double)e.EncodedBytes);

            var ranked = entries
                .Select(e =>
                {
                    string key = $"{e.Engine}|{e.Profile}";
                    int usedMetrics = 2; // encode + bytes
                    if (e.Strict != null) usedMetrics++;
                    if (e.Consume != null && string.Equals(e.ConsumeLabel, "decode", StringComparison.OrdinalIgnoreCase)) usedMetrics++;
                    if (e.Verify != null) usedMetrics++;
                    if (e.Consume != null && string.Equals(e.ConsumeLabel, "apply_after_strict", StringComparison.OrdinalIgnoreCase)) usedMetrics++;

                    double score = metricRanks[key] / usedMetrics;
                    return new RankingRow
                    {
                        Dataset = dataset,
                        Engine = e.Engine,
                        Profile = e.Profile,
                        Score = score,
                        EncodeUs = e.Encode?.Median,
                        StrictUs = e.Strict?.Median,
                        DecodeUs = string.Equals(e.ConsumeLabel, "decode", StringComparison.OrdinalIgnoreCase) ? e.Consume?.Median : null,
                        VerifyUs = e.Verify?.Median,
                        ApplyAfterStrictUs = string.Equals(e.ConsumeLabel, "apply_after_strict", StringComparison.OrdinalIgnoreCase) ? e.Consume?.Median : null,
                        EncodedBytes = e.EncodedBytes
                    };
                })
                .OrderBy(r => r.Score)
                .ThenBy(r => r.Engine, StringComparer.Ordinal)
                .ThenBy(r => r.Profile, StringComparer.Ordinal)
                .ToList();

            for (int i = 0; i < ranked.Count; i++)
            {
                ranked[i].Rank = i + 1;
            }

            rows.AddRange(ranked);
        }

        return rows;
    }

    private static void ApplyRank(IEnumerable<OverviewEntry> entries, Dictionary<string, double> totals, Func<OverviewEntry, double> selector)
    {
        var ordered = entries
            .Select(e => new { Entry = e, Value = selector(e) })
            .OrderBy(x => x.Value)
            .ThenBy(x => x.Entry.Engine, StringComparer.Ordinal)
            .ThenBy(x => x.Entry.Profile, StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            var key = $"{ordered[i].Entry.Engine}|{ordered[i].Entry.Profile}";
            totals[key] += i + 1;
        }
    }

    private static void CleanupOldArtifacts(string outDir, string currentLabel)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"overview_{currentLabel}.json",
            $"overview_{currentLabel}.md",
            $"overview_{currentLabel}.csv",
            $"overview_{currentLabel}_ranking.md",
            $"overview_{currentLabel}_ranking.csv",
            "overview_latest.json",
            "overview_latest.md",
            "overview_latest.csv",
            "overview_latest_ranking.md",
            "overview_latest_ranking.csv"
        };

        foreach (var path in Directory.GetFiles(outDir, "overview_*.*"))
        {
            string name = Path.GetFileName(path);
            if (keep.Contains(name))
                continue;

            try
            {
                File.Delete(path);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    private static long ParseDatasetBytes(string dataset) =>
        dataset switch
        {
            "1KB" => 1024L,
            "10KB" => 10L * 1024L,
            "100KB" => 100L * 1024L,
            "1MB" => 1024L * 1024L,
            "10MB" => 10L * 1024L * 1024L,
            _ => long.MaxValue
        };

    private static string FormatNullable(double? value) => value.HasValue ? FormatNumber(value.Value, 3) : "-";

    private static string Format(StatsSummary? summary) => summary == null ? "-" : $"{FormatNumber(summary.Median, 2)} us";

    private static string FormatWithError(StatsSummary? summary, string? error)
    {
        if (summary != null)
            return Format(summary);

        return string.IsNullOrEmpty(error) ? "-" : $"ERR: {error}";
    }

    private static string FormatNumber(double value, int decimals = 3) =>
        value.ToString($"F{decimals}", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed class OverviewReport
    {
        public string TimestampUtc { get; set; } = "";
        public string Label { get; set; } = "";
        public int SampleCount { get; set; }
        public long TargetUs { get; set; }
        public List<OverviewEntry> Entries { get; set; } = new();
    }

    private sealed class OverviewEntry
    {
        public string Engine { get; set; } = "";
        public string Dataset { get; set; } = "";
        public string Profile { get; set; } = "";
        public int EncodedBytes { get; set; }
        public StatsSummary Encode { get; set; } = new();
        public StatsSummary? Fast { get; set; }
        public StatsSummary Strict { get; set; } = new();
        public StatsSummary? Consume { get; set; }
        public string ConsumeLabel { get; set; } = "";
        public string? ConsumeError { get; set; }
        public StatsSummary? Verify { get; set; }
        public string? VerifyError { get; set; }
    }

    private sealed class RankingRow
    {
        public string Dataset { get; set; } = "";
        public int Rank { get; set; }
        public string Engine { get; set; } = "";
        public string Profile { get; set; } = "";
        public double Score { get; set; }
        public double? EncodeUs { get; set; }
        public double? StrictUs { get; set; }
        public double? DecodeUs { get; set; }
        public double? VerifyUs { get; set; }
        public double? ApplyAfterStrictUs { get; set; }
        public int EncodedBytes { get; set; }
    }
}
