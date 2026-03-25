using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronFamily.MegaBench.Datasets;

/// <summary>
/// Represents metadata for a generated dataset.
/// Written to artifacts/bench/megabench_datasets/{id}/manifest.json
/// </summary>
public class DatasetManifest
{
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("sizeLabel")]
    public string SizeLabel { get; set; } = "";

    [JsonPropertyName("targetSizeBytes")]
    public long TargetSizeBytes { get; set; }

    [JsonPropertyName("actualSizeBytes")]
    public long ActualSizeBytes { get; set; }

    [JsonPropertyName("seed")]
    public int Seed { get; set; }

    [JsonPropertyName("isDeterministic")]
    public bool IsDeterministic { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; }

    [JsonPropertyName("gitCommit")]
    public string? GitCommit { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

/// <summary>
/// Utility for creating manifests and computing hashes for generated datasets.
/// </summary>
public static class DatasetManifestGenerator
{
    /// <summary>
    /// Create manifest and sha256 hash for a generated dataset.
    /// Writes to artifacts/bench/megabench_datasets/{datasetId}/
    /// </summary>
    public static void CreateManifest(
        string datasetId,
        byte[] encodedData,
        string engine,
        string? profile,
        string sizeLabel,
        long targetSize,
        int seed)
    {
        try
        {
            string artifactDir = Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "artifacts",
                "bench",
                "megabench_datasets",
                datasetId);

            Directory.CreateDirectory(artifactDir);

            // Compute SHA256
            string sha256Hash = ComputeSha256(encodedData);

            // Get current git commit
            string? gitCommit = GetGitCommit();

            // Create manifest
            var manifest = new DatasetManifest
            {
                Engine = engine,
                Profile = profile,
                SizeLabel = sizeLabel,
                TargetSizeBytes = targetSize,
                ActualSizeBytes = encodedData.Length,
                Seed = seed,
                IsDeterministic = Environment.GetEnvironmentVariable("IRONFAMILY_DETERMINISTIC") == "1",
                Sha256 = sha256Hash,
                GeneratedAt = DateTime.UtcNow,
                GitCommit = gitCommit,
                Notes = $"Generated via hardened dataset generator (PHASE 1.2 hardening)",
            };

            // Write manifest.json
            string manifestFile = Path.Combine(artifactDir, "manifest.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(manifest, options);
            File.WriteAllText(manifestFile, json);

            // Write sha256.txt (single line for easy diffing)
            string sha256File = Path.Combine(artifactDir, "sha256.txt");
            File.WriteAllText(sha256File, $"{sha256Hash}  {datasetId}.bin");

            // Write encoded data
            string dataFile = Path.Combine(artifactDir, $"{datasetId}.bin");
            File.WriteAllBytes(dataFile, encodedData);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to create manifest for {datasetId}: {ex.Message}");
            // Don't throw - this is supplementary logging, not critical to dataset generation
        }
    }

    /// <summary>
    /// Compute SHA256 hash of data.
    /// </summary>
    private static string ComputeSha256(byte[] data)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// Get current git commit hash (best effort).
    /// </summary>
    private static string? GetGitCommit()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };

            using (var proc = System.Diagnostics.Process.Start(psi))
            {
                if (proc == null)
                    return null;

                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(1000);
                return output.Length == 40 ? output : null;
            }
        }
        catch
        {
            return null;
        }
    }
}
