using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronFamily.MegaBench.Datasets.IronCfg;
using IronFamily.MegaBench.Datasets.ILog;
using IronFamily.MegaBench.Datasets.IUpd;
using IronConfig.ILog;
using IronConfig.Iupd;

namespace IronFamily.MegaBench.Determinism;

/// <summary>
/// Determinism verification runner: generates all datasets twice and verifies SHA256 match.
///
/// Purpose:
/// - Prove 100% determinism (2x generation must produce identical outputs)
/// - Exit with code 2 if any mismatch found
/// - Generate determinism_report.json and determinism_report.md
///
/// Implementation:
/// - Sets IRONFAMILY_DETERMINISTIC=1 before generation
/// - Runs generation in two separate directories: runA and runB
/// - Computes SHA256 for each dataset
/// - Compares hashes: must be identical
/// </summary>
public static class DeterminismRunner
{
    private static readonly string ReportDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "artifacts", "bench", "megabench_determinism");

    public static int RunDeterminismCheck()
    {
        Console.WriteLine("=== MegaBench Determinism Check ===");
        Console.WriteLine();

        // Ensure deterministic seed is set
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");

        Directory.CreateDirectory(ReportDir);
        var runADir = Path.Combine(ReportDir, "runA");
        var runBDir = Path.Combine(ReportDir, "runB");

        // Clean previous runs
        if (Directory.Exists(runADir)) Directory.Delete(runADir, true);
        if (Directory.Exists(runBDir)) Directory.Delete(runBDir, true);

        Console.WriteLine($"Run A directory: {runADir}");
        Console.WriteLine($"Run B directory: {runBDir}");
        Console.WriteLine();

        var manifest = new DeterminismManifest
        {
            RunAtUtc = DateTime.UtcNow.ToString("O"),
            Datasets = new List<DeterminismRecord>()
        };

        bool allMatch = true;
        int matchCount = 0;
        int mismatchCount = 0;

        try
        {
            // Run A: Generate all datasets
            Console.WriteLine("--- RUN A: Generating datasets ---");
            GenerateDatasetsInRun(runADir, manifest);

            // Run B: Generate all datasets again
            Console.WriteLine();
            Console.WriteLine("--- RUN B: Generating datasets (2nd time) ---");
            GenerateDatasetsInRun(runBDir, manifest);

            // Compare hashes
            Console.WriteLine();
            Console.WriteLine("--- COMPARISON ---");
            foreach (var record in manifest.Datasets)
            {
                var runAFile = Path.Combine(runADir, record.DatasetId, $"{record.DatasetId}.bin");
                var runBFile = Path.Combine(runBDir, record.DatasetId, $"{record.DatasetId}.bin");

                if (!File.Exists(runAFile))
                {
                    Console.Error.WriteLine($"✗ {record.DatasetId}: RunA file missing");
                    allMatch = false;
                    mismatchCount++;
                    continue;
                }

                if (!File.Exists(runBFile))
                {
                    Console.Error.WriteLine($"✗ {record.DatasetId}: RunB file missing");
                    allMatch = false;
                    mismatchCount++;
                    continue;
                }

                var runAHash = ComputeSha256(runAFile);
                var runBHash = ComputeSha256(runBFile);

                record.RunAHash = runAHash;
                record.RunBHash = runBHash;

                if (runAHash == runBHash)
                {
                    Console.WriteLine($"✓ {record.DatasetId}: SHA256 MATCH");
                    matchCount++;
                }
                else
                {
                    Console.Error.WriteLine($"✗ {record.DatasetId}: SHA256 MISMATCH");
                    Console.Error.WriteLine($"  RunA: {runAHash}");
                    Console.Error.WriteLine($"  RunB: {runBHash}");
                    allMatch = false;
                    mismatchCount++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"Matches: {matchCount}");
        Console.WriteLine($"Mismatches: {mismatchCount}");

        // Write reports
        WriteManifestReport(manifest);

        if (!allMatch)
        {
            Console.Error.WriteLine("❌ DETERMINISM CHECK FAILED");
            return 2;
        }

        Console.WriteLine("✓ DETERMINISM CHECK PASSED");
        return 0;
    }

    private static void GenerateDatasetsInRun(string runDir, DeterminismManifest manifest)
    {
        var iconfgSizes = new[] { "1KB", "10KB" };
        var otherSizes = new[] { "10KB" };

        // IRONCFG
        Console.WriteLine("  Generating IRONCFG datasets...");
        foreach (var size in iconfgSizes)
        {
            try
            {
                var data = IronCfgDatasetGenerator.GenerateDataset(size, useCrc32: true);
                var outputDir = Path.Combine(runDir, $"ironcfg_{size}");
                Directory.CreateDirectory(outputDir);
                var filePath = Path.Combine(outputDir, $"ironcfg_{size}.bin");
                File.WriteAllBytes(filePath, data);

                // Add to manifest if not already there (from RunA)
                if (manifest.Datasets.All(d => d.DatasetId != $"ironcfg_{size}"))
                {
                    manifest.Datasets.Add(new DeterminismRecord
                    {
                        DatasetId = $"ironcfg_{size}",
                        Engine = "ironcfg",
                        SizeLabel = size,
                        Bytes = data.Length
                    });
                }

                Console.WriteLine($"    ✓ ironcfg_{size}: {data.Length} bytes");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"    ✗ ironcfg_{size}: {ex.Message}");
            }
        }

        // ILOG (all profiles)
        Console.WriteLine("  Generating ILOG datasets...");
        var ilogProfiles = new[]
        {
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED,
            IlogProfile.AUDITED
        };

        foreach (var profile in ilogProfiles)
        {
            foreach (var size in otherSizes)
            {
                try
                {
                    var data = ILogDatasetGenerator.GenerateDataset(size, profile);
                    var datasetId = $"ilog_{profile}_{size}";
                    var outputDir = Path.Combine(runDir, datasetId);
                    Directory.CreateDirectory(outputDir);
                    var filePath = Path.Combine(outputDir, $"{datasetId}.bin");
                    File.WriteAllBytes(filePath, data);

                    if (manifest.Datasets.All(d => d.DatasetId != datasetId))
                    {
                        manifest.Datasets.Add(new DeterminismRecord
                        {
                            DatasetId = datasetId,
                            Engine = "ilog",
                            Profile = profile.ToString(),
                            SizeLabel = size,
                            Bytes = data.Length
                        });
                    }

                    Console.WriteLine($"    ✓ ilog_{profile}_{size}: {data.Length} bytes");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"    ✗ ilog_{profile}_{size}: {ex.Message}");
                }
            }
        }

        // IUPD (non-DELTA profiles)
        Console.WriteLine("  Generating IUPD datasets...");
        var iupdProfiles = new[]
        {
            IupdProfile.MINIMAL,
            IupdProfile.FAST,
            IupdProfile.SECURE,
            IupdProfile.OPTIMIZED
        };

        foreach (var profile in iupdProfiles)
        {
            foreach (var size in otherSizes)
            {
                try
                {
                    var data = IUpdDatasetGenerator.GenerateDataset(size, profile);
                    var datasetId = $"iupd_{profile}_{size}";
                    var outputDir = Path.Combine(runDir, datasetId);
                    Directory.CreateDirectory(outputDir);
                    var filePath = Path.Combine(outputDir, $"{datasetId}.bin");
                    File.WriteAllBytes(filePath, data);

                    if (manifest.Datasets.All(d => d.DatasetId != datasetId))
                    {
                        manifest.Datasets.Add(new DeterminismRecord
                        {
                            DatasetId = datasetId,
                            Engine = "iupd",
                            Profile = profile.ToString(),
                            SizeLabel = size,
                            Bytes = data.Length
                        });
                    }

                    Console.WriteLine($"    ✓ iupd_{profile}_{size}: {data.Length} bytes");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"    ✗ iupd_{profile}_{size}: {ex.Message}");
                }
            }
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private static void WriteManifestReport(DeterminismManifest manifest)
    {
        // JSON report
        var jsonPath = Path.Combine(ReportDir, "determinism_report.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifest, jsonOptions));
        Console.WriteLine($"\n✓ JSON report: {jsonPath}");

        // Markdown report
        var mdPath = Path.Combine(ReportDir, "determinism_report.md");
        using (var writer = new StreamWriter(mdPath))
        {
            writer.WriteLine("# Determinism Check Report");
            writer.WriteLine();
            writer.WriteLine($"**Generated**: {manifest.RunAtUtc}");
            writer.WriteLine();

            var matchCount = manifest.Datasets.Count(d => d.RunAHash == d.RunBHash);
            var mismatchCount = manifest.Datasets.Count(d => d.RunAHash != d.RunBHash);

            writer.WriteLine($"## Summary");
            writer.WriteLine($"- Total Datasets: {manifest.Datasets.Count}");
            writer.WriteLine($"- Matches: {matchCount}");
            writer.WriteLine($"- Mismatches: {mismatchCount}");
            writer.WriteLine();

            if (mismatchCount == 0)
            {
                writer.WriteLine("✅ **ALL DATASETS DETERMINISTIC**");
            }
            else
            {
                writer.WriteLine("❌ **DETERMINISM VIOLATIONS DETECTED**");
            }

            writer.WriteLine();
            writer.WriteLine("## Dataset Hashes");
            writer.WriteLine();
            writer.WriteLine("| DatasetId | Engine | Profile | Size | Bytes | RunA Hash | RunB Hash | Match |");
            writer.WriteLine("|-----------|--------|---------|------|-------|-----------|-----------|-------|");

            foreach (var record in manifest.Datasets)
            {
                var profileStr = record.Profile ?? "-";
                var match = record.RunAHash == record.RunBHash ? "✓" : "✗";
                writer.WriteLine($"| {record.DatasetId} | {record.Engine} | {profileStr} | {record.SizeLabel} | {record.Bytes} | {record.RunAHash?.Substring(0, 8) ?? "?"} | {record.RunBHash?.Substring(0, 8) ?? "?"} | {match} |");
            }
        }
        Console.WriteLine($"✓ Markdown report: {mdPath}");
    }
}

public class DeterminismManifest
{
    [JsonPropertyName("runAtUtc")]
    public string RunAtUtc { get; set; } = "";

    [JsonPropertyName("datasets")]
    public List<DeterminismRecord> Datasets { get; set; } = new();
}

public class DeterminismRecord
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
    public int Bytes { get; set; }

    [JsonPropertyName("runAHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RunAHash { get; set; }

    [JsonPropertyName("runBHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RunBHash { get; set; }
}
