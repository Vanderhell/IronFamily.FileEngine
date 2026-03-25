using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronConfig;
using IronConfig.ILog;
using IronConfig.IronCfg;
using IronConfig.Iupd;
using IronFamily.MegaBench.Datasets.IronCfg;
using IronFamily.MegaBench.Datasets.ILog;
using IronFamily.MegaBench.Datasets.IUpd;

namespace IronFamily.MegaBench.Bench;

/// <summary>
/// Bench sanity runner: measure encode/decode/validate performance with statistical sampling.
/// </summary>
public static class BenchSanityRunner
{
    private static readonly string MetricsDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "artifacts", "bench", "megabench_metrics");

    private const int SamplesCount = 21;
    private const int WarmupCount = 3;
    private const long TargetTotalUs = 5000; // 5ms per sample

    public static int RunBenchSanity(bool ciMode = false)
    {
        Console.WriteLine("=== MegaBench Bench Sanity (Statistical) ===");
        Console.WriteLine($"Mode: {(ciMode ? "CI (subset, fast)" : "FULL (all datasets)")}");
        Console.WriteLine();

        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        Directory.CreateDirectory(MetricsDir);

        var metrics = new BenchMetrics
        {
            RunAtUtc = DateTime.UtcNow.ToString("O"),
            CiMode = ciMode,
            Environment = CaptureEnvironment(),
            Datasets = new List<BenchDatasetMetrics>()
        };

        try
        {
            if (ciMode)
            {
                BenchIronCfg(new[] { "1KB" }, metrics);
                BenchILog(new[] { "10KB" }, metrics);
                BenchIUpd(new[] { "10KB" }, metrics);
            }
            else
            {
                BenchIronCfg(new[] { "1KB", "10KB" }, metrics);
                BenchILog(new[] { "10KB" }, metrics);
                BenchIUpd(new[] { "10KB" }, metrics);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 2;
        }

        WriteMetricsReport(metrics);
        WriteEnvironmentJson(metrics.Environment);

        // Gate validation
        int failures = 0;
        foreach (var dataset in metrics.Datasets)
        {
            if (!dataset.IsValid())
            {
                Console.Error.WriteLine($"❌ GATE FAILED: {dataset.DatasetId}");
                failures++;
            }
        }

        if (failures > 0)
        {
            Console.Error.WriteLine($"❌ BENCH SANITY GATES FAILED: {failures}/{metrics.Datasets.Count} datasets");
            return 2;
        }

        Console.WriteLine($"✓ BENCH SANITY GATES PASSED ({metrics.Datasets.Count} datasets)");
        return 0;
    }

    /// <summary>
    /// Measure per-operation microseconds with adaptive batching.
    /// opBatch(batchN) executes batchN operations and returns the count executed (must == batchN).
    /// </summary>
    private static double[] MeasurePerOpUsSamples(Func<int, int> opBatch)
    {
        var samples = new List<double>();

        // Warmup
        for (int w = 0; w < WarmupCount; w++)
        {
            _ = opBatch(1);
        }

        // Collect samples
        for (int s = 0; s < SamplesCount; s++)
        {
            int batchN = 1;
            long elapsedUs = 0;

            // Adaptive batching: increase batchN until we reach targetTotalUs
            while (elapsedUs < TargetTotalUs)
            {
                var sw = Stopwatch.StartNew();
                int executed = opBatch(batchN);
                sw.Stop();

                elapsedUs = (sw.ElapsedTicks * 1_000_000) / Stopwatch.Frequency;

                if (elapsedUs < TargetTotalUs && elapsedUs > 0)
                {
                    // Estimate next batch size
                    double ratio = (double)TargetTotalUs / elapsedUs;
                    batchN = Math.Max(batchN + 1, (int)(batchN * ratio * 1.1));
                }
            }

            // Per-op microseconds
            double perOpUs = (double)elapsedUs / batchN;
            samples.Add(perOpUs);
        }

        return samples.ToArray();
    }

    private static void BenchIronCfg(string[] sizes, BenchMetrics metrics)
    {
        Console.WriteLine("  Measuring IRONCFG...");
        foreach (var size in sizes)
        {
            try
            {
                var data = IronCfgDatasetGenerator.GenerateDataset(size, useCrc32: true);
                var datasetId = $"icfg_{size}";

                // Encode samples
                var encodeSamples = MeasurePerOpUsSamples(batchN =>
                {
                    for (int i = 0; i < batchN; i++)
                    {
                        _ = IronCfgDatasetGenerator.GenerateDataset(size, useCrc32: true);
                    }
                    return batchN;
                });
                var encodeSummary = Stats.Compute(encodeSamples);

                // Decode samples
                var decodeSamples = MeasurePerOpUsSamples(batchN =>
                {
                    for (int i = 0; i < batchN; i++)
                    {
                        _ = IronCfgValidator.Open(data, out _);
                    }
                    return batchN;
                });
                var decodeSummary = Stats.Compute(decodeSamples);

                // ValidateStrict samples
                var validateSamples = MeasurePerOpUsSamples(batchN =>
                {
                    for (int i = 0; i < batchN; i++)
                    {
                        if (IronCfgValidator.Open(data, out var view).IsOk)
                        {
                            _ = IronCfgValidator.ValidateStrict(data, view);
                        }
                    }
                    return batchN;
                });
                var validateSummary = Stats.Compute(validateSamples);

                var result = new BenchDatasetMetrics
                {
                    DatasetId = datasetId,
                    Engine = "icfg",
                    SizeLabel = size,
                    Bytes = data.Length,
                    EncodeSamplesUs = encodeSamples,
                    EncodeSummary = encodeSummary,
                    DecodeSamplesUs = decodeSamples,
                    DecodeSummary = decodeSummary,
                    ValidateSamplesUs = validateSamples,
                    ValidateSummary = validateSummary
                };

                metrics.Datasets.Add(result);
                Console.WriteLine($"    ✓ {datasetId}: enc={encodeSummary.Median:F2}µs cv={encodeSummary.Cv:F3}, " +
                    $"dec={decodeSummary.Median:F2}µs cv={decodeSummary.Cv:F3}, " +
                    $"val={validateSummary.Median:F2}µs cv={validateSummary.Cv:F3}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"    ✗ icfg_{size}: {ex.Message}");
            }
        }
    }

    private static void BenchILog(string[] sizes, BenchMetrics metrics)
    {
        Console.WriteLine("  Measuring ILOG...");
        var profiles = new[] { IlogProfile.MINIMAL, IlogProfile.INTEGRITY };

        foreach (var profile in profiles)
        {
            foreach (var size in sizes)
            {
                try
                {
                    var data = ILogDatasetGenerator.GenerateDataset(size, profile);
                    var datasetId = $"ilog_{profile}_{size}";

                    // Encode samples
                    var encodeSamples = MeasurePerOpUsSamples(batchN =>
                    {
                        for (int i = 0; i < batchN; i++)
                        {
                            _ = ILogDatasetGenerator.GenerateDataset(size, profile);
                        }
                        return batchN;
                    });
                    var encodeSummary = Stats.Compute(encodeSamples);

                    // Decode samples
                    var decodeSamples = MeasurePerOpUsSamples(batchN =>
                    {
                        for (int i = 0; i < batchN; i++)
                        {
                            _ = IlogReader.Open(data, out _);
                        }
                        return batchN;
                    });
                    var decodeSummary = Stats.Compute(decodeSamples);

                    // ValidateStrict samples
                    var validateSamples = MeasurePerOpUsSamples(batchN =>
                    {
                        for (int i = 0; i < batchN; i++)
                        {
                            var openErr = IlogReader.Open(data, out var view);
                            if (openErr == null && view != null)
                            {
                                _ = IlogReader.ValidateStrict(view);
                            }
                        }
                        return batchN;
                    });
                    var validateSummary = Stats.Compute(validateSamples);

                    var result = new BenchDatasetMetrics
                    {
                        DatasetId = datasetId,
                        Engine = "ilog",
                        Profile = profile.ToString(),
                        SizeLabel = size,
                        Bytes = data.Length,
                        EncodeSamplesUs = encodeSamples,
                        EncodeSummary = encodeSummary,
                        DecodeSamplesUs = decodeSamples,
                        DecodeSummary = decodeSummary,
                        ValidateSamplesUs = validateSamples,
                        ValidateSummary = validateSummary
                    };

                    metrics.Datasets.Add(result);
                    Console.WriteLine($"    ✓ {datasetId}: enc={encodeSummary.Median:F2}µs cv={encodeSummary.Cv:F3}, " +
                        $"dec={decodeSummary.Median:F2}µs cv={decodeSummary.Cv:F3}, " +
                        $"val={validateSummary.Median:F2}µs cv={validateSummary.Cv:F3}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"    ✗ ilog_{profile}_{size}: {ex.Message}");
                }
            }
        }
    }

    private static void BenchIUpd(string[] sizes, BenchMetrics metrics)
    {
        Console.WriteLine("  Measuring IUPD...");
        var profiles = new[] { IupdProfile.MINIMAL, IupdProfile.FAST };

        foreach (var profile in profiles)
        {
            foreach (var size in sizes)
            {
                try
                {
                    var data = IUpdDatasetGenerator.GenerateDataset(size, profile);
                    var datasetId = $"iupd_{profile}_{size}";

                    // Encode samples
                    var encodeSamples = MeasurePerOpUsSamples(batchN =>
                    {
                        for (int i = 0; i < batchN; i++)
                        {
                            _ = IUpdDatasetGenerator.GenerateDataset(size, profile);
                        }
                        return batchN;
                    });
                    var encodeSummary = Stats.Compute(encodeSamples);

                    // Decode samples
                    var decodeSamples = MeasurePerOpUsSamples(batchN =>
                    {
                        for (int i = 0; i < batchN; i++)
                        {
                            _ = IupdReader.Open(data, out _);
                        }
                        return batchN;
                    });
                    var decodeSummary = Stats.Compute(decodeSamples);

                    // ValidateStrict samples
                    var validateSamples = MeasurePerOpUsSamples(batchN =>
                    {
                        for (int i = 0; i < batchN; i++)
                        {
                            var reader = IupdReader.Open(data, out _);
                            if (reader != null)
                            {
                                _ = reader.ValidateStrict();
                            }
                        }
                        return batchN;
                    });
                    var validateSummary = Stats.Compute(validateSamples);

                    var result = new BenchDatasetMetrics
                    {
                        DatasetId = datasetId,
                        Engine = "iupd",
                        Profile = profile.ToString(),
                        SizeLabel = size,
                        Bytes = data.Length,
                        EncodeSamplesUs = encodeSamples,
                        EncodeSummary = encodeSummary,
                        DecodeSamplesUs = decodeSamples,
                        DecodeSummary = decodeSummary,
                        ValidateSamplesUs = validateSamples,
                        ValidateSummary = validateSummary
                    };

                    metrics.Datasets.Add(result);
                    Console.WriteLine($"    ✓ {datasetId}: enc={encodeSummary.Median:F2}µs cv={encodeSummary.Cv:F3}, " +
                        $"dec={decodeSummary.Median:F2}µs cv={decodeSummary.Cv:F3}, " +
                        $"val={validateSummary.Median:F2}µs cv={validateSummary.Cv:F3}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"    ✗ iupd_{profile}_{size}: {ex.Message}");
                }
            }
        }
    }

    private static EnvironmentCapture CaptureEnvironment()
    {
        var machineNameHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName))
        )[..8];

        return new EnvironmentCapture
        {
            OsDescription = RuntimeInformation.OSDescription,
            ProcessArch = RuntimeInformation.ProcessArchitecture.ToString(),
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            CpuCount = Environment.ProcessorCount,
            DotnetVersion = RuntimeInformation.FrameworkDescription,
            MachineName = machineNameHash,
            TimestampUtc = DateTime.UtcNow.ToString("O")
        };
    }

    private static void WriteMetricsReport(BenchMetrics metrics)
    {
        var jsonPath = Path.Combine(MetricsDir, "metrics.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(metrics, jsonOptions));
        Console.WriteLine($"\n✓ Metrics report: {jsonPath}");
    }

    private static void WriteEnvironmentJson(EnvironmentCapture env)
    {
        var jsonPath = Path.Combine(MetricsDir, "environment.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(env, jsonOptions));
        Console.WriteLine($"✓ Environment report: {jsonPath}");
    }
}

public class BenchMetrics
{
    [JsonPropertyName("runAtUtc")]
    public string RunAtUtc { get; set; } = "";

    [JsonPropertyName("ciMode")]
    public bool CiMode { get; set; }

    [JsonPropertyName("environment")]
    public EnvironmentCapture Environment { get; set; } = new();

    [JsonPropertyName("datasets")]
    public List<BenchDatasetMetrics> Datasets { get; set; } = new();
}

public class EnvironmentCapture
{
    [JsonPropertyName("osDescription")]
    public string OsDescription { get; set; } = "";

    [JsonPropertyName("processArch")]
    public string ProcessArch { get; set; } = "";

    [JsonPropertyName("frameworkDescription")]
    public string FrameworkDescription { get; set; } = "";

    [JsonPropertyName("cpuCount")]
    public int CpuCount { get; set; }

    [JsonPropertyName("dotnetVersion")]
    public string DotnetVersion { get; set; } = "";

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = "";

    [JsonPropertyName("timestampUtc")]
    public string TimestampUtc { get; set; } = "";
}

public class BenchDatasetMetrics
{
    [JsonPropertyName("datasetId")]
    public string DatasetId { get; set; } = "";

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Profile { get; set; }

    [JsonPropertyName("sizeLabel")]
    public string SizeLabel { get; set; } = "";

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("encodeSamplesUs")]
    public double[]? EncodeSamplesUs { get; set; }

    [JsonPropertyName("encodeSummary")]
    public StatsSummary? EncodeSummary { get; set; }

    [JsonPropertyName("decodeSamplesUs")]
    public double[]? DecodeSamplesUs { get; set; }

    [JsonPropertyName("decodeSummary")]
    public StatsSummary? DecodeSummary { get; set; }

    [JsonPropertyName("validateSamplesUs")]
    public double[]? ValidateSamplesUs { get; set; }

    [JsonPropertyName("validateSummary")]
    public StatsSummary? ValidateSummary { get; set; }

    public bool IsValid()
    {
        // Gate rules
        if (Bytes <= 0)
            return false;

        // All three operations must have non-zero median
        if (EncodeSummary?.Median <= 0)
            return false;
        if (DecodeSummary?.Median <= 0)
            return false;
        if (ValidateSummary?.Median <= 0)
            return false;

        // CV gate (but not if noiseFloor)
        bool encodeNoiseFloor = EncodeSummary!.Median < 10;
        bool decodeNoiseFloor = DecodeSummary!.Median < 10;
        bool validateNoiseFloor = ValidateSummary!.Median < 10;

        // Apply CV gate only if not noiseFloor
        if (!encodeNoiseFloor && EncodeSummary.Cv > 0.10)
            return false;
        if (!decodeNoiseFloor && DecodeSummary.Cv > 0.10)
            return false;
        if (!validateNoiseFloor && ValidateSummary.Cv > 0.10)
            return false;

        return true;
    }
}
