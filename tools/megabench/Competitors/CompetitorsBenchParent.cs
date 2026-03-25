using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Parent process benchmark orchestrator.
/// Spawns child processes (N=7 times per dataset×codec combo).
/// Collects JSON results from child stdout.
/// Enforces isolation via environment variables (DOTNET_TieredPGO=0, etc.).
/// </summary>
public static class CompetitorsBenchParent
{
    private const int RUNS_PER_COMBO = 7;

    /// <summary>
    /// Run all benchmarks in multi-process mode.
    /// Returns: exit code 0 (success), 3 (gates failed), 2 (error)
    /// </summary>
    public static int RunAllMultiProcess(
        string engineFilter,
        string sizeFilter,
        string profileFilter,
        bool ciMode,
        bool realWorld)
    {
        try
        {
            var artifactDir = Path.Combine("artifacts", "_gauntlet", "2026-02-25_megabench_competitors_v5");
            Directory.CreateDirectory(artifactDir);

            var rawSamplesPath = Path.Combine(artifactDir, "raw_samples.ndjson");
            var summaryPath = Path.Combine(artifactDir, "summary.json");
            var summaryMdPath = Path.Combine(artifactDir, "summary.md");
            var envPath = Path.Combine(artifactDir, "environment.json");

            // Log environment
            LogEnvironment(envPath);

            // Generate list of jobs
            var jobs = GenerateJobs(engineFilter, sizeFilter, profileFilter, ciMode, realWorld);
            if (jobs.Count == 0)
            {
                Console.Error.WriteLine("ERROR: No benchmark jobs generated");
                return 2;
            }

            Console.WriteLine($"=== MegaBench Parent: Multi-Process Runner ===");
            Console.WriteLine($"Total jobs: {jobs.Count}");
            Console.WriteLine($"Runs per job: {RUNS_PER_COMBO}");
            Console.WriteLine($"Total child processes: {jobs.Count * RUNS_PER_COMBO}");
            Console.WriteLine();

            // Run jobs and collect samples
            var allSamples = new List<CompetitorResult>();
            int jobIndex = 0;

            using (var writer = new StreamWriter(rawSamplesPath, append: false))
            {
                foreach (var job in jobs)
                {
                    jobIndex++;
                    Console.WriteLine($"[{jobIndex}/{jobs.Count}] {job.Engine}_{job.Codec}_{job.Size}_{job.Profile}");

                    for (int run = 0; run < RUNS_PER_COMBO; run++)
                    {
                        var result = RunChildProcess(job);
                        if (result != null)
                        {
                            allSamples.Add(result);
                            // Write NDJSON (1 line per sample)
                            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                            string line = JsonSerializer.Serialize(result, options);
                            writer.WriteLine(line);
                        }
                    }
                }
            }

            // Compute summary
            var summary = ComputeSummary(allSamples);
            SaveSummary(summaryPath, summary);
            SaveSummaryMarkdown(summaryMdPath, summary);

            // Export per-profile CSV
            var csvPath = Path.Combine(artifactDir, "results.csv");
            ExportResultsCsv(csvPath, allSamples);

            Console.WriteLine();
            Console.WriteLine($"=== Results ===");
            Console.WriteLine($"Raw samples: {rawSamplesPath}");
            Console.WriteLine($"Summary JSON: {summaryPath}");
            Console.WriteLine($"Summary MD: {summaryMdPath}");
            Console.WriteLine($"Results CSV: {csvPath}");
            Console.WriteLine($"Environment: {envPath}");
            Console.WriteLine();

            // Check gates
            return CheckGates(summary, allSamples) ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static void LogEnvironment(string path)
    {
        var env = new
        {
            timestamp = DateTime.UtcNow.ToString("O"),
            dotnetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            osVersion = Environment.OSVersion.VersionString,
            processCount = Environment.ProcessorCount,
            tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "default",
            readyToRun = Environment.GetEnvironmentVariable("COMPlus_ReadyToRun") ?? "default",
            quickJit = Environment.GetEnvironmentVariable("COMPlus_TC_QuickJitForLoops") ?? "default"
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        string json = JsonSerializer.Serialize(env, options);
        File.WriteAllText(path, json);
    }

    private static List<BenchmarkJob> GenerateJobs(
        string engineFilter,
        string sizeFilter,
        string profileFilter,
        bool ciMode,
        bool realWorld)
    {
        var jobs = new List<BenchmarkJob>();

        var engines = string.IsNullOrEmpty(engineFilter) ? new[] { "icfg", "ilog", "iupd" } : new[] { engineFilter };
        var sizes = ciMode ? new[] { "10KB", "1MB" } : new[] { "10KB", "100KB", "1MB", "10MB" };
        var ilogProfiles = new[] { "MINIMAL", "INTEGRITY", "SEARCHABLE", "ARCHIVED", "AUDITED" };
        var iupdProfiles = new[] { "MINIMAL", "FAST", "SECURE", "OPTIMIZED" };

        foreach (var engine in engines)
        {
            if (engine == "icfg" || engine == "all")
            {
                var codecs = new[] { "protobuf", "flatbuffers", "messagepack", "cbor" };
                foreach (var size in sizes)
                {
                    foreach (var codec in codecs)
                    {
                        jobs.Add(new BenchmarkJob
                        {
                            Engine = "icfg",
                            Codec = codec,
                            Size = size,
                            Profile = null,
                            IsRealWorld = false
                        });
                    }
                }
            }

            if (engine == "ilog" || engine == "all")
            {
                var codecs = new[] { "protobuf", "messagepack", "cbor" };
                var profiles = string.IsNullOrEmpty(profileFilter) ? ilogProfiles : new[] { profileFilter };
                foreach (var profile in profiles)
                {
                    foreach (var size in sizes)
                    {
                        foreach (var codec in codecs)
                        {
                            jobs.Add(new BenchmarkJob
                            {
                                Engine = "ilog",
                                Codec = codec,
                                Size = size,
                                Profile = profile,
                                IsRealWorld = false
                            });
                        }
                    }
                }
            }

            if (engine == "iupd" || engine == "all")
            {
                var codecs = new[] { "protobuf", "messagepack", "cbor" };
                var profiles = string.IsNullOrEmpty(profileFilter) ? iupdProfiles : new[] { profileFilter };
                foreach (var profile in profiles)
                {
                    foreach (var size in sizes)
                    {
                        foreach (var codec in codecs)
                        {
                            jobs.Add(new BenchmarkJob
                            {
                                Engine = "iupd",
                                Codec = codec,
                                Size = size,
                                Profile = profile,
                                IsRealWorld = false
                            });
                        }
                    }
                }
            }
        }

        return jobs;
    }

    private static CompetitorResult? RunChildProcess(BenchmarkJob job)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project tools/megabench/MegaBench.csproj -c Release --no-build -- bench-child",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set isolation environment
            psi.Environment["DOTNET_TieredPGO"] = "0";
            psi.Environment["COMPlus_ReadyToRun"] = "0";
            psi.Environment["COMPlus_TC_QuickJitForLoops"] = "0";
            psi.Environment["IRONFAMILY_DETERMINISTIC"] = "1";

            // Set job parameters
            psi.Environment["BENCH_ENGINE"] = job.Engine;
            psi.Environment["BENCH_SIZE"] = job.Size;
            psi.Environment["BENCH_PROFILE"] = job.Profile ?? "";
            psi.Environment["BENCH_CODEC"] = job.Codec;
            psi.Environment["BENCH_MODE"] = "encode";
            psi.Environment["BENCH_REALWORLD"] = job.IsRealWorld ? "true" : "false";

            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    Console.Error.WriteLine($"  ✗ Failed to start child process");
                    return null;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Console.Error.WriteLine($"  ✗ Child process exited with code {process.ExitCode}");
                    if (!string.IsNullOrEmpty(stderr))
                        Console.Error.WriteLine($"     stderr: {stderr}");
                    return null;
                }

                // Parse JSON from stdout
                if (string.IsNullOrEmpty(stdout))
                {
                    Console.Error.WriteLine($"  ✗ No output from child process");
                    return null;
                }

                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var result = JsonSerializer.Deserialize<CompetitorResult>(stdout, options);
                if (result == null)
                {
                    Console.Error.WriteLine($"  ✗ Failed to parse child result JSON");
                    return null;
                }

                // Extract median from samples
                if (result.EncodeSamplesUs != null && result.EncodeSamplesUs.Length > 0)
                {
                    var sorted = result.EncodeSamplesUs.OrderBy(x => x).ToArray();
                    double median = sorted.Length % 2 == 0
                        ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
                        : sorted[sorted.Length / 2];
                    Console.WriteLine($"  ✓ {job.Codec}: {median:F2} us");
                }
                else
                {
                    Console.WriteLine($"  ✓ {job.Codec}: (no samples)");
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  ✗ Exception: {ex.Message}");
            return null;
        }
    }

    private static BenchmarkSummary ComputeSummary(List<CompetitorResult> samples)
    {
        var summary = new BenchmarkSummary
        {
            Timestamp = DateTime.UtcNow,
            TotalSamples = samples.Count,
            CodecMetrics = new Dictionary<string, CodecMetrics>()
        };

        var grouped = samples.GroupBy(s => s.CodecName);
        foreach (var group in grouped)
        {
            // Extract median timing from each sample's encoded samples array
            var timings = new List<double>();
            var allocs = new List<long>();

            foreach (var sample in group)
            {
                if (sample.EncodeSamplesUs != null && sample.EncodeSamplesUs.Length > 0)
                {
                    var sorted = sample.EncodeSamplesUs.OrderBy(x => x).ToArray();
                    double median = sorted.Length % 2 == 0
                        ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
                        : sorted[sorted.Length / 2];
                    timings.Add(median);
                }
                allocs.Add(sample.AllocBytes);
            }

            timings.Sort();
            allocs.Sort();

            var metric = new CodecMetrics
            {
                CodecName = group.Key,
                SampleCount = timings.Count,
                ElapsedUsMin = timings.First(),
                ElapsedUsMedian = timings[timings.Count / 2],
                ElapsedUsMax = timings.Last(),
                AllocBytesMin = allocs.First(),
                AllocBytesMedian = allocs[allocs.Count / 2],
                AllocBytesMax = allocs.Last(),
                CvElapsed = ComputeCV(timings),
                CvAlloc = ComputeCV(allocs.Select(a => (double)a).ToList()),
                RoundtripPassCount = group.Count(s => s.RoundtripOk),
                StabilityPass = ComputeCV(timings) <= 0.15
            };

            summary.CodecMetrics[group.Key] = metric;
        }

        return summary;
    }

    private static double ComputeCV(List<double> values)
    {
        if (values.Count == 0)
            return 0;

        double mean = values.Average();
        if (mean == 0)
            return 0;

        double variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        double stdDev = Math.Sqrt(variance);
        return stdDev / mean;
    }

    private static void SaveSummary(string path, BenchmarkSummary summary)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        string json = JsonSerializer.Serialize(summary, options);
        File.WriteAllText(path, json);
    }

    private static void SaveSummaryMarkdown(string path, BenchmarkSummary summary)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# MegaBench Summary Report");
        sb.AppendLine();
        sb.AppendLine($"**Timestamp**: {summary.Timestamp:O}");
        sb.AppendLine($"**Total Samples**: {summary.TotalSamples}");
        sb.AppendLine();
        sb.AppendLine("## Codec Metrics");
        sb.AppendLine();
        sb.AppendLine("| Codec | Samples | Elapsed (us) | CV | Alloc (bytes) | CV | Status |");
        sb.AppendLine("|-------|---------|------|-----|--------------|-----|--------|");

        foreach (var kvp in summary.CodecMetrics.OrderBy(x => x.Key))
        {
            var m = kvp.Value;
            string status = m.StabilityPass ? "✓ PASS" : "✗ FAIL";
            sb.AppendLine(
                $"| {m.CodecName} | {m.SampleCount} | " +
                $"{m.ElapsedUsMedian:F2} | {m.CvElapsed:F3} | " +
                $"{m.AllocBytesMedian} | {m.CvAlloc:F3} | {status} |");
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void ExportResultsCsv(string path, List<CompetitorResult> allSamples)
    {
        try
        {
            using (var writer = new StreamWriter(path, append: false))
            {
                // Write header
                writer.WriteLine("engine,profile,codec,sizeLabel,inputBytes,encodedBytes,compressionRatio,encodeMedianUs,encodeP95Us,decodeMedianUs,decodeP95Us,allocBytes,roundtripOk,status,signMedianUs,signP95Us,signatureLenBytes,witnessVerifyMedianUs,witnessVerifyP95Us,timestamp");

                // Group by engine+profile+codec+size
                var grouped = allSamples
                    .GroupBy(s => new { s.Engine, s.Profile, s.CodecName, s.SizeLabel })
                    .OrderBy(g => g.Key.Engine)
                    .ThenBy(g => g.Key.Profile ?? "")
                    .ThenBy(g => g.Key.CodecName)
                    .ThenBy(g => g.Key.SizeLabel);

                foreach (var group in grouped)
                {
                    // Compute aggregates
                    var timings = new List<double>();
                    var decodingTimings = new List<double>();
                    var signTimings = new List<double>();
                    var witnessVerifyTimings = new List<double>();
                    var allocs = new List<long>();
                    int roundtripPass = 0;
                    long signatureLenBytes = 0;

                    foreach (var sample in group)
                    {
                        if (sample.EncodeSamplesUs != null && sample.EncodeSamplesUs.Length > 0)
                        {
                            timings.AddRange(sample.EncodeSamplesUs);
                        }
                        if (sample.DecodeSamplesUs != null && sample.DecodeSamplesUs.Length > 0)
                        {
                            decodingTimings.AddRange(sample.DecodeSamplesUs);
                        }
                        if (sample.SignSamplesUs != null && sample.SignSamplesUs.Length > 0)
                        {
                            signTimings.AddRange(sample.SignSamplesUs);
                        }
                        if (sample.WitnessVerifySamplesUs != null && sample.WitnessVerifySamplesUs.Length > 0)
                        {
                            witnessVerifyTimings.AddRange(sample.WitnessVerifySamplesUs);
                        }
                        allocs.Add(sample.AllocBytes);
                        if (sample.RoundtripOk)
                            roundtripPass++;
                        if (sample.SignatureLenBytes > 0)
                            signatureLenBytes = sample.SignatureLenBytes;
                    }

                    timings.Sort();
                    decodingTimings.Sort();
                    signTimings.Sort();
                    witnessVerifyTimings.Sort();
                    allocs.Sort();

                    double encodeMedian = timings.Count > 0 ? timings[timings.Count / 2] : 0;
                    double encodeP95 = timings.Count > 0 ? timings[(int)(timings.Count * 0.95)] : 0;
                    double decodeMedian = decodingTimings.Count > 0 ? decodingTimings[decodingTimings.Count / 2] : 0;
                    double decodeP95 = decodingTimings.Count > 0 ? decodingTimings[(int)(decodingTimings.Count * 0.95)] : 0;
                    double signMedian = signTimings.Count > 0 ? signTimings[signTimings.Count / 2] : 0;
                    double signP95 = signTimings.Count > 0 ? signTimings[(int)(signTimings.Count * 0.95)] : 0;
                    double witnessVerifyMedian = witnessVerifyTimings.Count > 0 ? witnessVerifyTimings[witnessVerifyTimings.Count / 2] : 0;
                    double witnessVerifyP95 = witnessVerifyTimings.Count > 0 ? witnessVerifyTimings[(int)(witnessVerifyTimings.Count * 0.95)] : 0;
                    long allocMedian = allocs.Count > 0 ? allocs[allocs.Count / 2] : 0;

                    double compressionRatio = group.First().InputBytes > 0
                        ? (double)group.First().EncodedBytes / group.First().InputBytes
                        : 1.0;

                    string status = roundtripPass == group.Count() ? "PASS" : "FAIL";

                    writer.WriteLine(
                        $"{group.Key.Engine}," +
                        $"{group.Key.Profile ?? "DEFAULT"}," +
                        $"{group.Key.CodecName}," +
                        $"{group.Key.SizeLabel}," +
                        $"{group.First().InputBytes}," +
                        $"{group.First().EncodedBytes}," +
                        $"{compressionRatio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{encodeMedian.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{encodeP95.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{decodeMedian.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{decodeP95.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{allocMedian}," +
                        $"{(roundtripPass == group.Count())}," +
                        $"{status}," +
                        $"{signMedian.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{signP95.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{signatureLenBytes}," +
                        $"{witnessVerifyMedian.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{witnessVerifyP95.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}");
                }
            }
            Console.WriteLine($"✓ CSV exported: {path}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Failed to export CSV: {ex.Message}");
        }
    }

    private static bool CheckGates(BenchmarkSummary summary, List<CompetitorResult> allSamples)
    {
        bool allPass = true;

        // GATE A: Correctness
        Console.WriteLine();
        Console.WriteLine("=== GATE A: Correctness ===");
        var roundtripPass = allSamples.Count(s => s.RoundtripOk);
        Console.WriteLine($"Roundtrip OK: {roundtripPass}/{allSamples.Count}");
        if (roundtripPass != allSamples.Count)
            allPass = false;

        // GATE B: Stability
        Console.WriteLine();
        Console.WriteLine("=== GATE B: Stability ===");
        var stabilityFails = new List<string>();
        foreach (var kvp in summary.CodecMetrics)
        {
            if (kvp.Value.ElapsedUsMedian <= 0)
            {
                Console.WriteLine($"  ✗ {kvp.Key}: Median is 0");
                stabilityFails.Add(kvp.Key);
                allPass = false;
            }
            else if (kvp.Value.CvElapsed > 0.15)
            {
                Console.WriteLine($"  ✗ {kvp.Key}: CV={kvp.Value.CvElapsed:F3} > 0.15");
                stabilityFails.Add(kvp.Key);
                allPass = false;
            }
            else
            {
                Console.WriteLine($"  ✓ {kvp.Key}: CV={kvp.Value.CvElapsed:F3}");
            }
        }

        // GATE C: Fairness (stub)
        Console.WriteLine();
        Console.WriteLine("=== GATE C: Fairness ===");
        Console.WriteLine("  (Real-world payload entropy checks pending PHASE 2 completion)");

        return allPass;
    }
}

class BenchmarkJob
{
    public string Engine { get; set; } = "";
    public string Codec { get; set; } = "";
    public string Size { get; set; } = "";
    public string? Profile { get; set; }
    public bool IsRealWorld { get; set; }
}

class BenchmarkSummary
{
    public DateTime Timestamp { get; set; }
    public int TotalSamples { get; set; }
    public Dictionary<string, CodecMetrics> CodecMetrics { get; set; } = new();
}

class CodecMetrics
{
    public string CodecName { get; set; } = "";
    public int SampleCount { get; set; }
    public double ElapsedUsMin { get; set; }
    public double ElapsedUsMedian { get; set; }
    public double ElapsedUsMax { get; set; }
    public long AllocBytesMin { get; set; }
    public long AllocBytesMedian { get; set; }
    public long AllocBytesMax { get; set; }
    public double CvElapsed { get; set; }
    public double CvAlloc { get; set; }
    public int RoundtripPassCount { get; set; }
    public bool StabilityPass { get; set; }
}
