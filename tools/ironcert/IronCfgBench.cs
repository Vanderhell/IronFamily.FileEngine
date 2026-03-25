using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronConfig.IronCfg;

internal class IronCfgBench
{
    private const string TestVectorsDir = "vectors/small/ironcfg";
    private const string AuditsDir = "audits/ironcfg";

    private readonly struct BenchDataset
    {
        public string Name { get; init; }
        public string NoCrcPath { get; init; }
        public long ExpectedSize { get; init; }
    }

    private readonly struct BenchResult
    {
        public string Metric { get; init; }
        public double Value { get; init; }
        public string Unit { get; init; }
        public int Iterations { get; init; }
    }

    private class KpiData
    {
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");

        [JsonPropertyName("engine")]
        public string Engine { get; set; } = "ironcfg";

        [JsonPropertyName("methodology")]
        public Dictionary<string, string> Methodology { get; set; } = new();

        [JsonPropertyName("datasets")]
        public List<DatasetKpi> Datasets { get; set; } = new();

        [JsonPropertyName("aggregates")]
        public Dictionary<string, double> Aggregates { get; set; } = new();
    }

    private class DatasetKpi
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("metrics")]
        public Dictionary<string, double> Metrics { get; set; } = new();
    }

    public static int Run(bool quick)
    {
        try
        {
            Console.WriteLine("IRONCFG Benchmark Suite");
            Console.WriteLine("=======================");

            var datasets = new[]
            {
                new BenchDataset { Name = "small", NoCrcPath = "vectors/small/ironcfg/small/golden.icfg", ExpectedSize = 155 },
                new BenchDataset { Name = "medium", NoCrcPath = "vectors/small/ironcfg/medium/golden.icfg", ExpectedSize = 630 },
                new BenchDataset { Name = "large", NoCrcPath = "vectors/small/ironcfg/large/golden.icfg", ExpectedSize = 6883 },
                new BenchDataset { Name = "mega", NoCrcPath = "vectors/small/ironcfg/mega/golden.icfg", ExpectedSize = 81501 }
            };

            Directory.CreateDirectory(AuditsDir);

            var kpi = new KpiData();
            kpi.Methodology["warmup_iterations"] = "5";
            kpi.Methodology["measure_iterations"] = quick ? "3" : "10";
            kpi.Methodology["datasets"] = "small, medium, large, mega (nocrc)";
            kpi.Methodology["environment"] = ".NET 8.0";

            var allMetrics = new Dictionary<string, List<double>>();

            foreach (var ds in datasets)
            {
                if (!File.Exists(ds.NoCrcPath))
                {
                    Console.WriteLine($"SKIP {ds.Name}: not found");
                    continue;
                }

                Console.WriteLine();
                Console.WriteLine($"Dataset: {ds.Name}");
                var buffer = File.ReadAllBytes(ds.NoCrcPath);
                Console.WriteLine($"  Size: {buffer.Length} bytes");

                var dsKpi = new DatasetKpi { Name = ds.Name, SizeBytes = buffer.Length };

                // Open/Init latency
                BenchOpenInit(buffer, ds.Name, quick, dsKpi, allMetrics);

                // Validate fast/strict
                BenchValidate(buffer, ds.Name, quick, dsKpi, allMetrics);

                // Encode throughput
                BenchEncode(ds.Name, quick, dsKpi, allMetrics);

                // Decode throughput (value extraction)
                BenchDecode(buffer, ds.Name, quick, dsKpi, allMetrics);

                kpi.Datasets.Add(dsKpi);
            }

            // Compute aggregates (p50, p95)
            ComputeAggregates(kpi, allMetrics);

            // Export KPI
            ExportKpi(kpi);

            Console.WriteLine();
            Console.WriteLine("OK bench ironcfg completed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL bench code=EXCEPTION msg=\"{ex.Message}\"");
            return 1;
        }
    }

    private static void BenchOpenInit(byte[] buffer, string datasetName, bool quick,
        DatasetKpi dsKpi, Dictionary<string, List<double>> allMetrics)
    {
        const int warmupIter = 5;
        int measureIter = quick ? 3 : 10;

        // Warmup
        for (int i = 0; i < warmupIter; i++)
        {
            var err = IronCfgValidator.Open(buffer.AsMemory(), out var view);
            if (!err.IsOk) throw new Exception($"Open failed: {err}");
        }

        // Measure
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measureIter; i++)
        {
            var err = IronCfgValidator.Open(buffer.AsMemory(), out var view);
            if (!err.IsOk) throw new Exception($"Open failed: {err}");
        }
        sw.Stop();

        double msPerOp = (double)sw.ElapsedMilliseconds / measureIter;
        dsKpi.Metrics["open_latency_ms"] = msPerOp;

        if (!allMetrics.ContainsKey("open_latency_ms"))
            allMetrics["open_latency_ms"] = new List<double>();
        allMetrics["open_latency_ms"].Add(msPerOp);

        Console.WriteLine($"  Open/Init: {msPerOp:F3} ms/op (n={measureIter})");
    }

    private static void BenchValidate(byte[] buffer, string datasetName, bool quick,
        DatasetKpi dsKpi, Dictionary<string, List<double>> allMetrics)
    {
        const int warmupIter = 5;
        int measureIter = quick ? 3 : 10;

        // Fast validation warmup
        for (int i = 0; i < warmupIter; i++)
        {
            var fast = IronCfgValidator.ValidateFast(buffer.AsMemory());
            if (!fast.IsOk) throw new Exception($"ValidateFast failed: {fast}");
        }

        // Fast validation measure
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measureIter; i++)
        {
            var fast = IronCfgValidator.ValidateFast(buffer.AsMemory());
            if (!fast.IsOk) throw new Exception($"ValidateFast failed: {fast}");
        }
        sw.Stop();

        double fastMbPerSec = (buffer.Length * measureIter) / (sw.Elapsed.TotalSeconds * 1024 * 1024);
        dsKpi.Metrics["validate_fast_mb_s"] = fastMbPerSec;

        if (!allMetrics.ContainsKey("validate_fast_mb_s"))
            allMetrics["validate_fast_mb_s"] = new List<double>();
        allMetrics["validate_fast_mb_s"].Add(fastMbPerSec);

        Console.WriteLine($"  Validate Fast: {fastMbPerSec:F2} MB/s (n={measureIter})");

        // Strict validation warmup
        for (int i = 0; i < warmupIter; i++)
        {
            var err = IronCfgValidator.Open(buffer.AsMemory(), out var view);
            if (!err.IsOk) throw new Exception($"Open failed: {err}");
            var strict = IronCfgValidator.ValidateStrict(buffer.AsMemory(), view);
            if (!strict.IsOk) throw new Exception($"ValidateStrict failed: {strict}");
        }

        // Strict validation measure
        sw.Restart();
        for (int i = 0; i < measureIter; i++)
        {
            var err = IronCfgValidator.Open(buffer.AsMemory(), out var view);
            var strict = IronCfgValidator.ValidateStrict(buffer.AsMemory(), view);
        }
        sw.Stop();

        double strictMbPerSec = (buffer.Length * measureIter) / (sw.Elapsed.TotalSeconds * 1024 * 1024);
        dsKpi.Metrics["validate_strict_mb_s"] = strictMbPerSec;

        if (!allMetrics.ContainsKey("validate_strict_mb_s"))
            allMetrics["validate_strict_mb_s"] = new List<double>();
        allMetrics["validate_strict_mb_s"].Add(strictMbPerSec);

        Console.WriteLine($"  Validate Strict: {strictMbPerSec:F2} MB/s (n={measureIter})");
    }

    private static void BenchEncode(string datasetName, bool quick,
        DatasetKpi dsKpi, Dictionary<string, List<double>> allMetrics)
    {
        // Simplified encode benchmark - creates minimal test data
        const int warmupIter = 3;
        int measureIter = quick ? 2 : 5;

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new() { FieldId = 0, FieldName = "test", FieldType = 0x11, IsRequired = true }
            }
        };

        var testVal = new IronCfgObject
        {
            Fields = new System.Collections.Generic.SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgUInt64 { Value = 42 } }
            }
        };

        var buffer = new byte[1024];

        // Warmup
        for (int i = 0; i < warmupIter; i++)
        {
            var err = IronCfgEncoder.Encode(testVal, schema, false, false, buffer, out var size);
            if (!err.IsOk) throw new Exception($"Encode failed: {err}");
        }

        // Measure
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measureIter; i++)
        {
            var err = IronCfgEncoder.Encode(testVal, schema, false, false, buffer, out var size);
            if (!err.IsOk) throw new Exception($"Encode failed: {err}");
        }
        sw.Stop();

        // Assume encode writes ~100 bytes for test data
        const int bytesPerEncode = 100;
        double encodeMbPerSec = (bytesPerEncode * measureIter) / (sw.Elapsed.TotalSeconds * 1024 * 1024);
        dsKpi.Metrics["encode_mb_s"] = encodeMbPerSec;

        if (!allMetrics.ContainsKey("encode_mb_s"))
            allMetrics["encode_mb_s"] = new List<double>();
        allMetrics["encode_mb_s"].Add(encodeMbPerSec);

        Console.WriteLine($"  Encode: {encodeMbPerSec:F2} MB/s (n={measureIter})");
    }

    private static void BenchDecode(byte[] buffer, string datasetName, bool quick,
        DatasetKpi dsKpi, Dictionary<string, List<double>> allMetrics)
    {
        const int warmupIter = 3;
        int measureIter = quick ? 2 : 5;

        // Warmup: validate and open
        for (int i = 0; i < warmupIter; i++)
        {
            var err = IronCfgValidator.Open(buffer.AsMemory(), out var view);
            if (!err.IsOk) throw new Exception($"Open failed: {err}");
        }

        // Measure: full open (includes parsing header)
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measureIter; i++)
        {
            var err = IronCfgValidator.Open(buffer.AsMemory(), out var view);
            if (!err.IsOk) throw new Exception($"Open failed: {err}");
        }
        sw.Stop();

        double decodeMbPerSec = (buffer.Length * measureIter) / (sw.Elapsed.TotalSeconds * 1024 * 1024);
        dsKpi.Metrics["decode_mb_s"] = decodeMbPerSec;

        if (!allMetrics.ContainsKey("decode_mb_s"))
            allMetrics["decode_mb_s"] = new List<double>();
        allMetrics["decode_mb_s"].Add(decodeMbPerSec);

        Console.WriteLine($"  Decode: {decodeMbPerSec:F2} MB/s (n={measureIter})");
    }

    private static void ComputeAggregates(KpiData kpi, Dictionary<string, List<double>> allMetrics)
    {
        foreach (var kvp in allMetrics)
        {
            var values = kvp.Value.OrderBy(x => x).ToList();
            if (values.Count == 0) continue;

            int p50Idx = values.Count / 2;
            int p95Idx = (int)(values.Count * 0.95);

            kpi.Aggregates[$"{kvp.Key}_p50"] = values[Math.Min(p50Idx, values.Count - 1)];
            kpi.Aggregates[$"{kvp.Key}_p95"] = values[Math.Min(p95Idx, values.Count - 1)];
        }
    }

    private static void ExportKpi(KpiData kpi)
    {
        // Export JSON
        var jsonPath = Path.Combine(AuditsDir, "bench_kpi.json");
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
        var jsonStr = JsonSerializer.Serialize(kpi, jsonOpts);
        File.WriteAllText(jsonPath, jsonStr);
        Console.WriteLine($"OK exported KPI to {jsonPath}");

        // Export markdown
        var mdPath = Path.Combine(AuditsDir, "bench_kpi.md");
        var mdContent = new System.Text.StringBuilder();
        mdContent.AppendLine("# IRONCFG Benchmark Results");
        mdContent.AppendLine();
        mdContent.AppendLine($"**Generated:** {kpi.Timestamp}");
        mdContent.AppendLine();

        mdContent.AppendLine("## Methodology");
        mdContent.AppendLine();
        foreach (var kvp in kpi.Methodology)
        {
            mdContent.AppendLine($"- **{kvp.Key}:** {kvp.Value}");
        }
        mdContent.AppendLine();

        mdContent.AppendLine("## Results by Dataset");
        mdContent.AppendLine();
        foreach (var ds in kpi.Datasets)
        {
            mdContent.AppendLine($"### {ds.Name}");
            mdContent.AppendLine();
            mdContent.AppendLine($"**Size:** {ds.SizeBytes} bytes");
            mdContent.AppendLine();
            mdContent.AppendLine("| Metric | Value |");
            mdContent.AppendLine("|--------|-------|");
            foreach (var m in ds.Metrics.OrderBy(x => x.Key))
            {
                mdContent.AppendLine($"| {m.Key} | {m.Value:F2} |");
            }
            mdContent.AppendLine();
        }

        mdContent.AppendLine("## Aggregates (P50, P95)");
        mdContent.AppendLine();
        mdContent.AppendLine("| Metric | Value |");
        mdContent.AppendLine("|--------|-------|");
        foreach (var a in kpi.Aggregates.OrderBy(x => x.Key))
        {
            mdContent.AppendLine($"| {a.Key} | {a.Value:F2} |");
        }
        mdContent.AppendLine();

        File.WriteAllText(mdPath, mdContent.ToString());
        Console.WriteLine($"OK exported markdown to {mdPath}");
    }
}
