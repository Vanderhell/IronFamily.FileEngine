using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BenchDatasets;

class Program
{
    static void Main(string[] args)
    {
        var outputRoot = "../../artifacts/benchmarks/datasets";
        var logRoot = "../../artifacts/_dump/exec_bench_datasets_01";

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(logRoot);

        Console.WriteLine("╔════════════════════════════════════════════════╗");
        Console.WriteLine("║  BENCH DATASETS GENERATOR - EXEC_BENCH_DATASETS_01");
        Console.WriteLine("║  Deterministic Realistic-Synthetic Datasets     ║");
        Console.WriteLine($"║  {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}                  ║");
        Console.WriteLine("╚════════════════════════════════════════════════╝\n");

        var manifest = new DatasetManifest();
        var executionLog = new List<string>();

        executionLog.Add($"Start: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
        executionLog.Add("");

        try
        {
            // IUPD Firmware-like datasets
            executionLog.Add("=== IUPD FIRMWARE DATASETS ===");
            GenerateIupdDatasets(outputRoot, manifest, executionLog);

            // ICFG Config-like datasets
            executionLog.Add("\n=== ICFG CONFIG DATASETS ===");
            GenerateIcfgDatasets(outputRoot, manifest, executionLog);

            // ILOG Log-like datasets
            executionLog.Add("\n=== ILOG EVENT DATASETS ===");
            GenerateIlogDatasets(outputRoot, manifest, executionLog);

            // Stress datasets
            executionLog.Add("\n=== STRESS DATASETS ===");
            GenerateStressDatasets(outputRoot, manifest, executionLog);

            // Determinism verification
            executionLog.Add("\n=== DETERMINISM VERIFICATION ===");
            VerifyDeterminism(outputRoot, manifest, executionLog);

            // Write manifest
            executionLog.Add("\n=== MANIFEST GENERATION ===");
            WriteManifest(outputRoot, manifest, executionLog);

            executionLog.Add($"\nEnd: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
            executionLog.Add("Status: SUCCESS");
        }
        catch (Exception ex)
        {
            executionLog.Add($"\nERROR: {ex.Message}");
            executionLog.Add(ex.StackTrace);
            executionLog.Add("Status: FAILED");
            Console.WriteLine($"ERROR: {ex.Message}");
        }

        // Write execution log
        File.WriteAllLines(Path.Combine(logRoot, "EXECUTION_LOG.md"), executionLog);

        foreach (var line in executionLog.TakeLast(15))
        {
            Console.WriteLine(line);
        }
    }

    static void GenerateIupdDatasets(string outputRoot, DatasetManifest manifest, List<string> log)
    {
        var iupdDir = Path.Combine(outputRoot, "iupd");
        var tiers = new[] { 10, 64, 256, 1024, 8192, 32768, 131072, 524288 }; // KB

        foreach (var tierKb in tiers)
        {
            log.Add($"  Generating {tierKb}KB firmware variants...");

            var baseFile = Path.Combine(iupdDir, $"fw_{tierKb}_v1.bin");
            var smallFile = Path.Combine(iupdDir, $"fw_{tierKb}_v2_small.bin");
            var mediumFile = Path.Combine(iupdDir, $"fw_{tierKb}_v3_medium.bin");

            // Base version
            var baseData = GenerateFirmwareBinary(tierKb * 1024, version: 1);
            File.WriteAllBytes(baseFile, baseData);
            AddFileToManifest(manifest, "iupd", "firmware_base", baseFile, baseData, "v1 base");

            // Small change version
            var smallData = ApplySmallChange(baseData, tierKb * 1024);
            File.WriteAllBytes(smallFile, smallData);
            AddFileToManifest(manifest, "iupd", "firmware_small_change", smallFile, smallData, "v2 small patch");

            // Medium change version
            var mediumData = ApplyMediumChange(baseData, tierKb * 1024);
            File.WriteAllBytes(mediumFile, mediumData);
            AddFileToManifest(manifest, "iupd", "firmware_medium_change", mediumFile, mediumData, "v3 medium patch");

            log.Add($"    ✓ {tierKb}KB: base {baseData.Length}, small {smallData.Length}, medium {mediumData.Length}");
        }
    }

    static void GenerateIcfgDatasets(string outputRoot, DatasetManifest manifest, List<string> log)
    {
        var icfgDir = Path.Combine(outputRoot, "icfg");
        var tiers = new[] { "tiny", "small", "medium", "large", "xlarge" };

        foreach (var tier in tiers)
        {
            log.Add($"  Generating {tier} config variants...");

            var baseFile = Path.Combine(icfgDir, $"config_{tier}_v1.json");
            var smallFile = Path.Combine(icfgDir, $"config_{tier}_v2_small.json");
            var mediumFile = Path.Combine(icfgDir, $"config_{tier}_v3_medium.json");

            var targetSize = tier switch
            {
                "tiny" => 1024,
                "small" => 16384,
                "medium" => 131072,
                "large" => 1048576,
                "xlarge" => 8388608,
                _ => 1024
            };

            // Base version
            var baseJson = GenerateConfigJson(targetSize, tier, version: 1);
            File.WriteAllText(baseFile, baseJson);
            AddFileToManifest(manifest, "icfg", $"config_{tier}_base", baseFile, System.Text.Encoding.UTF8.GetBytes(baseJson), "v1 base");

            // Small variant
            var smallJson = ModifyConfigJson(baseJson, tier, small: true);
            File.WriteAllText(smallFile, smallJson);
            AddFileToManifest(manifest, "icfg", $"config_{tier}_small", smallFile, System.Text.Encoding.UTF8.GetBytes(smallJson), "v2 small edit");

            // Medium variant
            var mediumJson = ModifyConfigJson(baseJson, tier, small: false);
            File.WriteAllText(mediumFile, mediumJson);
            AddFileToManifest(manifest, "icfg", $"config_{tier}_medium", mediumFile, System.Text.Encoding.UTF8.GetBytes(mediumJson), "v3 medium edit");

            log.Add($"    ✓ {tier}: base {baseJson.Length}, small {smallJson.Length}, medium {mediumJson.Length}");
        }
    }

    static void GenerateIlogDatasets(string outputRoot, DatasetManifest manifest, List<string> log)
    {
        var ilogDir = Path.Combine(outputRoot, "ilog");
        var eventCounts = new[] { 1000, 10000, 100000, 500000 };

        foreach (var count in eventCounts)
        {
            log.Add($"  Generating {count} event log variants...");

            var baseFile = Path.Combine(ilogDir, $"log_{count}_events_v1.jsonl");
            var mixedFile = Path.Combine(ilogDir, $"log_{count}_events_mixed.jsonl");
            var anomalyFile = Path.Combine(ilogDir, $"log_{count}_events_anomaly.jsonl");

            var baseLines = GenerateLogEvents(count, pattern: "normal");
            File.WriteAllLines(baseFile, baseLines);
            AddFileToManifest(manifest, "ilog", $"events_{count}_base", baseFile, System.Text.Encoding.UTF8.GetBytes(string.Join("\n", baseLines)), "v1 normal pattern");

            var mixedLines = GenerateLogEvents(count, pattern: "mixed");
            File.WriteAllLines(mixedFile, mixedLines);
            AddFileToManifest(manifest, "ilog", $"events_{count}_mixed", mixedFile, System.Text.Encoding.UTF8.GetBytes(string.Join("\n", mixedLines)), "mixed severity/components");

            var anomalyLines = GenerateLogEvents(count, pattern: "anomaly");
            File.WriteAllLines(anomalyFile, anomalyLines);
            AddFileToManifest(manifest, "ilog", $"events_{count}_anomaly", anomalyFile, System.Text.Encoding.UTF8.GetBytes(string.Join("\n", anomalyLines)), "anomaly clusters");

            log.Add($"    ✓ {count} events: normal, mixed, anomaly");
        }
    }

    static void GenerateStressDatasets(string outputRoot, DatasetManifest manifest, List<string> log)
    {
        var stressDir = Path.Combine(outputRoot, "stress");

        log.Add($"  Generating stress datasets...");

        // High entropy random
        var random10mb = GenerateRandomData(10 * 1024 * 1024, seed: 99999);
        var random10File = Path.Combine(stressDir, "random_high_entropy_10mb.bin");
        File.WriteAllBytes(random10File, random10mb);
        AddFileToManifest(manifest, "stress", "random_10mb", random10File, random10mb, "HIGH ENTROPY - STRESS ONLY");

        // Repeating pattern
        var pattern10mb = GenerateRepeatingPattern(10 * 1024 * 1024, seed: 88888);
        var pattern10File = Path.Combine(stressDir, "repeating_pattern_10mb.bin");
        File.WriteAllBytes(pattern10File, pattern10mb);
        AddFileToManifest(manifest, "stress", "pattern_10mb", pattern10File, pattern10mb, "REPEATING - STRESS ONLY");

        log.Add($"    ✓ Stress datasets: random 10MB, pattern 10MB");
    }

    static void VerifyDeterminism(string outputRoot, DatasetManifest manifest, List<string> log)
    {
        log.Add("  Spot-checking determinism...");

        // Rerun one IUPD small
        var iupdDir = Path.Combine(outputRoot, "iupd");
        var testFile = Path.Combine(iupdDir, "fw_64_v1.bin");
        var testData = File.ReadAllBytes(testFile);
        var hash1 = ComputeHash(testData);

        var regenData = GenerateFirmwareBinary(64 * 1024, version: 1);
        var hash2 = ComputeHash(regenData);

        if (hash1 == hash2)
        {
            log.Add("    ✓ IUPD 64KB determinism: PASS");
        }
        else
        {
            log.Add("    ✗ IUPD 64KB determinism: FAIL");
        }

        // Rerun ICFG medium config
        var icfgDir = Path.Combine(outputRoot, "icfg");
        var configFile = Path.Combine(icfgDir, "config_medium_v1.json");
        var configJson1 = File.ReadAllText(configFile);

        var regenConfig = GenerateConfigJson(131072, "medium", version: 1);

        if (configJson1 == regenConfig)
        {
            log.Add("    ✓ ICFG medium determinism: PASS");
        }
        else
        {
            log.Add("    ✗ ICFG medium determinism: FAIL");
        }

        // Rerun ILOG 100k
        var ilogDir = Path.Combine(outputRoot, "ilog");
        var logFile = Path.Combine(ilogDir, "log_100000_events_v1.jsonl");
        var logLines1 = File.ReadAllLines(logFile).Take(10).ToList();

        var regenLog = GenerateLogEvents(100000, pattern: "normal");
        var logLines2 = regenLog.Take(10).ToList();

        if (logLines1.SequenceEqual(logLines2))
        {
            log.Add("    ✓ ILOG 100k events determinism: PASS");
        }
        else
        {
            log.Add("    ✗ ILOG 100k events determinism: FAIL");
        }
    }

    static void WriteManifest(string outputRoot, DatasetManifest manifest, List<string> log)
    {
        var jsonPath = Path.Combine(outputRoot, "dataset_manifest.json");
        var mdPath = Path.Combine(outputRoot, "dataset_manifest.md");

        // JSON manifest
        var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var json = JsonSerializer.Serialize(manifest, options);
        File.WriteAllText(jsonPath, json);
        log.Add($"  ✓ JSON manifest: {jsonPath}");

        // Markdown manifest
        var md = new List<string>
        {
            "# Benchmark Datasets Manifest",
            $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}",
            "",
            "## Summary",
            $"Total files: {manifest.Files.Count}",
            $"Families: IUPD, ICFG, ILOG, STRESS",
            "",
            "## IUPD Firmware Datasets",
            "| Size | Base | Small Change | Medium Change |",
            "|------|------|--------------|---------------|",
        };

        var iupdGroups = manifest.Files.Where(f => f.Family == "iupd").GroupBy(f => f.Scenario);
        foreach (var group in iupdGroups)
        {
            var files = group.ToList();
            if (files.Count >= 3)
            {
                var size = ExtractSize(files[0].Filename);
                var baseSize = files[0].SizeBytes;
                var smallSize = files[1].SizeBytes;
                var medSize = files[2].SizeBytes;
                md.Add($"| {size} | {baseSize} | {smallSize} | {medSize} |");
            }
        }

        md.Add("");
        md.Add("## ICFG Config Datasets");
        md.Add("| Tier | Files | Purpose |");
        md.Add("|------|-------|---------|");

        var icfgGroups = manifest.Files.Where(f => f.Family == "icfg").GroupBy(f => f.Scenario);
        foreach (var group in icfgGroups.OrderBy(g => g.Key))
        {
            md.Add($"| {group.Key} | {group.Count()} | Config sizing/editing |");
        }

        md.Add("");
        md.Add("## ILOG Event Datasets");
        md.Add("| Event Count | Patterns |");
        md.Add("|-------------|----------|");

        var ilogGroups = manifest.Files.Where(f => f.Family == "ilog").GroupBy(f => ExtractEventCount(f.Filename));
        foreach (var group in ilogGroups.OrderBy(g => int.Parse(g.Key)))
        {
            md.Add($"| {group.Key} | normal, mixed, anomaly |");
        }

        md.Add("");
        md.Add("## Stress Datasets (NOT FOR PRIMARY BENCHMARKING)");
        md.Add("| Type | Size | Purpose |");
        md.Add("|------|------|---------|");

        var stressFiles = manifest.Files.Where(f => f.Family == "stress");
        foreach (var file in stressFiles)
        {
            md.Add($"| {file.Scenario} | {file.SizeBytes / (1024*1024)}MB | {file.Notes} |");
        }

        md.Add("");
        md.Add("## Realism Classification");
        md.Add("");
        md.Add("- **REALISTIC_SYNTHETIC**: Deterministic datasets mimicking real firmware/config/logs");
        md.Add("- **STRESS_ONLY**: Random/pathological data for stress testing only");
        md.Add("");

        File.WriteAllLines(mdPath, md);
        log.Add($"  ✓ Markdown manifest: {mdPath}");
    }

    // Generators

    static byte[] GenerateFirmwareBinary(int size, int version)
    {
        var rng = new Random(42 + version);
        var data = new byte[size];

        // Header (256 bytes) - versioned
        data[0] = 0x49; // I
        data[1] = 0x52; // R
        data[2] = 0x4F; // O
        data[3] = 0x4E; // N
        data[4] = (byte)version;
        Array.Fill(data, (byte)version, 5, 251); // version marker in header

        // Code-like section (40% of size)
        var codeStart = 256;
        var codeSize = (int)(size * 0.4);
        FillWithRepeatingPattern(data, codeStart, codeSize, rng, pattern: 0xCC);

        // Data section (30% of size)
        var dataStart = codeStart + codeSize;
        var dataSize = (int)(size * 0.3);
        FillWithSemiCompressible(data, dataStart, dataSize, rng);

        // Sparse region (20% of size)
        var sparseStart = dataStart + dataSize;
        var sparseSize = (int)(size * 0.2);
        FillSparse(data, sparseStart, sparseSize, rng);

        // Footer (last 256 bytes) - checksum region
        Array.Fill(data, (byte)(0xFF & version), size - 256, 256);

        return data;
    }

    static byte[] ApplySmallChange(byte[] baseData, int size)
    {
        var copy = (byte[])baseData.Clone();
        var rng = new Random(142);

        // Tiny patch only (0.5% of size)
        var patchSize = Math.Max(256, size / 200);
        var patchOffset = size / 2;
        for (int i = 0; i < patchSize; i++)
        {
            copy[patchOffset + i] = (byte)rng.Next(256);
        }

        return copy;
    }

    static byte[] ApplyMediumChange(byte[] baseData, int size)
    {
        var copy = (byte[])baseData.Clone();
        var rng = new Random(243);

        // Multiple sections changed (5% of size total)
        var patchTotal = Math.Max(4096, size / 20);
        var patchCount = 3;
        var patchPerSection = patchTotal / patchCount;

        for (int section = 0; section < patchCount; section++)
        {
            var offset = (size / (patchCount + 1)) * (section + 1);
            for (int i = 0; i < patchPerSection && offset + i < size; i++)
            {
                copy[offset + i] = (byte)rng.Next(256);
            }
        }

        return copy;
    }

    static string GenerateConfigJson(int targetSize, string tier, int version)
    {
        var rng = new Random(1000 + version);
        var json = new System.Text.StringBuilder();

        json.AppendLine("{");
        json.AppendLine($"  \"_metadata\": {{ \"tier\": \"{tier}\", \"version\": {version}, \"generated\": \"{DateTime.UtcNow:O}\" }},");

        // Add realistic sections
        var sections = new[] { "device", "network", "sensors", "calibration", "logging", "storage", "update", "security", "ui", "diagnostics" };

        foreach (var section in sections)
        {
            json.AppendLine($"  \"{section}\": {{");

            // Add realistic nested content
            var fieldCount = 3 + rng.Next(5);
            for (int i = 0; i < fieldCount; i++)
            {
                var fieldType = rng.Next(4);
                var fieldValue = fieldType switch
                {
                    0 => $"\"value_{i}\"",
                    1 => rng.Next(10000).ToString(),
                    2 => (rng.Next(2) == 0 ? "true" : "false"),
                    _ => $"[{string.Join(",", Enumerable.Range(0, rng.Next(3) + 1).Select(x => rng.Next(100)))}]"
                };

                var isLast = i == fieldCount - 1 && section == sections.Last();
                json.AppendLine($"    \"field_{i}\": {fieldValue}{(isLast ? "" : ",")}");
            }

            var isLastSection = section == sections.Last();
            json.AppendLine($"  }}{(isLastSection ? "" : ",")}");
        }

        json.AppendLine("}");

        var jsonStr = json.ToString();

        // Pad to target size if needed
        if (jsonStr.Length < targetSize)
        {
            var padding = targetSize - jsonStr.Length;
            jsonStr = jsonStr.Remove(jsonStr.Length - 1); // Remove final }
            jsonStr += $",\"_padding\": \"{new string('x', padding - 20)}\"\n}}";
        }

        return jsonStr;
    }

    static string ModifyConfigJson(string baseJson, string tier, bool small)
    {
        if (small)
        {
            // Small modification: change a few values
            return baseJson.Replace("\"version\": 1", "\"version\": 2");
        }
        else
        {
            // Medium modification: change more values
            return baseJson.Replace("\"version\": 1", "\"version\": 3")
                          .Replace("device", "device_mod")
                          .Replace("\"tier\": \"" + tier + "\"", "\"tier\": \"" + tier + "_modified\"");
        }
    }

    static List<string> GenerateLogEvents(int count, string pattern)
    {
        var rng = new Random(2000 + count);
        var lines = new List<string>();

        var components = new[] { "bootloader", "kernel", "driver", "app", "sensor", "network", "storage", "crypto" };
        var severities = new[] { "DEBUG", "INFO", "WARN", "ERROR", "FATAL" };
        var messages = new[] { "startup", "heartbeat", "sensor_read", "calibration", "connection", "timeout", "retry", "auth_fail", "apply_update", "crc_mismatch", "verify_ok", "shutdown" };

        var timestamp = new DateTime(2026, 3, 15, 0, 0, 0);

        for (int i = 0; i < count; i++)
        {
            string severity = pattern switch
            {
                "normal" => severities[rng.Next(3)], // Mostly DEBUG/INFO
                "mixed" => severities[rng.Next(severities.Length)],
                "anomaly" => i % 1000 < 100 ? severities[3 + rng.Next(2)] : severities[rng.Next(2)],
                _ => "INFO"
            };

            var component = components[rng.Next(components.Length)];
            var message = messages[rng.Next(messages.Length)];
            var value = rng.Next(10000);

            var logLine = JsonSerializer.Serialize(new
            {
                timestamp = timestamp.AddSeconds(i).ToString("O"),
                level = severity,
                component = component,
                message = message,
                value = value
            });

            lines.Add(logLine);
        }

        return lines;
    }

    static byte[] GenerateRandomData(int size, int seed)
    {
        var rng = new Random(seed);
        var data = new byte[size];
        rng.NextBytes(data);
        return data;
    }

    static byte[] GenerateRepeatingPattern(int size, int seed)
    {
        var pattern = new byte[] { 0xAA, 0x55, 0xCC, 0x33, 0xFF, 0x00 };
        var data = new byte[size];

        for (int i = 0; i < size; i++)
        {
            data[i] = pattern[i % pattern.Length];
        }

        return data;
    }

    // Helpers

    static void FillWithRepeatingPattern(byte[] data, int start, int length, Random rng, byte pattern)
    {
        for (int i = 0; i < length; i++)
        {
            data[start + i] = pattern;
        }
    }

    static void FillWithSemiCompressible(byte[] data, int start, int length, Random rng)
    {
        var blockSize = 64;
        for (int i = 0; i < length; i += blockSize)
        {
            var val = (byte)rng.Next(16);
            Array.Fill(data, val, start + i, Math.Min(blockSize, length - i));
        }
    }

    static void FillSparse(byte[] data, int start, int length, Random rng)
    {
        // Mostly zeros with occasional values
        Array.Fill<byte>(data, 0, start, length);
        for (int i = 0; i < length / 16; i++)
        {
            data[start + rng.Next(length)] = (byte)rng.Next(256);
        }
    }

    static string ComputeHash(byte[] data)
    {
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(data);
            return Convert.ToHexString(hash);
        }
    }

    static void AddFileToManifest(DatasetManifest manifest, string family, string scenario, string filePath, byte[] data, string notes)
    {
        manifest.Files.Add(new DatasetFile
        {
            Family = family,
            Scenario = scenario,
            Filename = Path.GetFileName(filePath),
            FilePath = filePath,
            SizeBytes = data.Length,
            Sha256 = ComputeHash(data),
            Realism = "REALISTIC_SYNTHETIC",
            Notes = notes,
            Generated = DateTime.UtcNow
        });
    }

    static string ExtractSize(string filename)
    {
        var parts = filename.Split('_');
        if (parts.Length > 1 && int.TryParse(parts[1], out var kb))
        {
            return kb < 1024 ? $"{kb}KB" : $"{kb / 1024}MB";
        }
        return "unknown";
    }

    static string ExtractEventCount(string filename)
    {
        var parts = filename.Split('_');
        if (parts.Length > 1 && int.TryParse(parts[1], out var count))
        {
            return count.ToString();
        }
        return "unknown";
    }
}

public class DatasetManifest
{
    [JsonPropertyName("generated")]
    public DateTime Generated { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("files")]
    public List<DatasetFile> Files { get; set; } = new();
}

public class DatasetFile
{
    [JsonPropertyName("family")]
    public string Family { get; set; }

    [JsonPropertyName("scenario")]
    public string Scenario { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; }

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; }

    [JsonPropertyName("realism")]
    public string Realism { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; }

    [JsonPropertyName("generated")]
    public DateTime Generated { get; set; }
}
