using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IronFamily.MegaBench.Bench;
using IronFamily.MegaBench.Datasets.IronCfg;
using IronFamily.MegaBench.Datasets.ILog;
using IronFamily.MegaBench.Datasets.IUpd;
using IronFamily.MegaBench.Semantics;
using IronConfig.ILog;
using IronConfig.Iupd;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Orchestrates benchmark execution for all competitor codecs.
/// PHASE 3+: Roundtrip validation, metrics collection, gates.
/// </summary>
public static class CompetitorsBenchRunner
{
    /// <summary>
    /// Run benchmarks for all codecs across specified datasets.
    /// </summary>
    public static CompetitorsBenchResult RunAll(
        string? engineFilter = null,
        string? sizeFilter = null,
        string? profileFilter = null,
        bool ciMode = false)
    {
        var result = new CompetitorsBenchResult
        {
            RunId = Guid.NewGuid().ToString("N").Substring(0, 8),
            RunAtUtc = DateTime.UtcNow,
            Engine = engineFilter ?? "all",
            CiMode = ciMode
        };

        var metricsPerCodec = new Dictionary<string, List<CompetitorResult>>();

        try
        {
            // Get all datasets
            var datasets = GetDatasets(engineFilter, sizeFilter, profileFilter).ToList();

            foreach (var dataset in datasets)
            {
                Console.WriteLine($"[Competitors] Benchmarking {dataset.Engine} {dataset.SizeLabel}...");

                // Get canonical input
                byte[]? canonicalInput = null;
                try
                {
                    switch (dataset.Engine.ToLower())
                    {
                        case "icfg":
                            canonicalInput = IronCfgDatasetGenerator.GenerateDataset(dataset.SizeLabel);
                            break;
                        case "ilog":
                            var ilogProfile = dataset.Profile switch
                            {
                                "MINIMAL" => IlogProfile.MINIMAL,
                                "INTEGRITY" => IlogProfile.INTEGRITY,
                                "SEARCHABLE" => IlogProfile.SEARCHABLE,
                                "ARCHIVED" => IlogProfile.ARCHIVED,
                                "AUDITED" => IlogProfile.AUDITED,
                                _ => IlogProfile.MINIMAL
                            };
                            canonicalInput = ILogDatasetGenerator.GenerateDataset(dataset.SizeLabel, ilogProfile);
                            break;
                        case "iupd":
                            var iupdProfile = dataset.Profile switch
                            {
                                "MINIMAL" => IupdProfile.MINIMAL,
                                "FAST" => IupdProfile.FAST,
                                "SECURE" => IupdProfile.SECURE,
                                "OPTIMIZED" => IupdProfile.OPTIMIZED,
                                _ => IupdProfile.MINIMAL
                            };
                            canonicalInput = IUpdDatasetGenerator.GenerateDataset(dataset.SizeLabel, iupdProfile);
                            break;
                        default:
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Competitors] Failed to generate {dataset.Engine} {dataset.SizeLabel}: {ex.Message}");
                    continue;
                }

                if (canonicalInput == null)
                    continue;

                // Determine codec kind
                var kind = dataset.Engine.ToLower() switch
                {
                    "icfg" => CodecKind.ICFG,
                    "ilog" => CodecKind.ILOG,
                    "iupd" => CodecKind.IUPD_Manifest,
                    _ => CodecKind.ICFG
                };

                // Get codecs for this kind
                var codecs = CompetitorCodecFactory.GetCodecsForKind(kind).ToList();

                // Run each codec
                foreach (var codec in codecs)
                {
                    var benchResult = CompetitorRunner.RunCodec(
                        codec,
                        canonicalInput,
                        dataset.Engine,
                        dataset.Profile,
                        dataset.SizeLabel);

                    if (!metricsPerCodec.ContainsKey(codec.Name))
                        metricsPerCodec[codec.Name] = new List<CompetitorResult>();

                    metricsPerCodec[codec.Name].Add(benchResult);

                    // Log result
                    if (benchResult.Excluded)
                        Console.WriteLine($"  [{codec.Name}] EXCLUDED: {benchResult.ExclusionReason}");
                    else
                        Console.WriteLine($"  [{codec.Name}] encode={benchResult.EncodeSummary?.Median:F2}µs " +
                            $"decode={benchResult.DecodeSummary?.Median:F2}µs roundtrip={benchResult.RoundtripOk}");
                }
            }

            result.Metrics = metricsPerCodec.SelectMany(kvp => kvp.Value).ToList();
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            result.Success = false;
        }

        return result;
    }

    /// <summary>
    /// Get datasets to benchmark based on filters.
    /// </summary>
    private static IEnumerable<(string Engine, string? Profile, string SizeLabel)> GetDatasets(
        string? engineFilter,
        string? sizeFilter,
        string? profileFilter)
    {
        var engines = new[] { "icfg", "ilog", "iupd" };
        var sizes = new[] { "1KB", "10KB" };
        var ilogProfiles = new[] { "MINIMAL", "INTEGRITY", "SEARCHABLE", "ARCHIVED", "AUDITED" };
        var iupdProfiles = new[] { "MINIMAL", "FAST", "SECURE", "OPTIMIZED" };

        foreach (var engine in engines)
        {
            if (engineFilter != null && !engine.Equals(engineFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (engine == "icfg")
            {
                foreach (var size in sizes)
                {
                    if (sizeFilter != null && !size.Equals(sizeFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    yield return (engine, null, size);
                }
            }
            else if (engine == "ilog")
            {
                var size = "10KB";
                foreach (var profile in ilogProfiles)
                {
                    if (profileFilter != null && !profile.Equals(profileFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    yield return (engine, profile, size);
                }
            }
            else if (engine == "iupd")
            {
                var size = "10KB";
                foreach (var profile in iupdProfiles)
                {
                    if (profileFilter != null && !profile.Equals(profileFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    yield return (engine, profile, size);
                }
            }
        }
    }
}

/// <summary>
/// Overall benchmark result container.
/// </summary>
public class CompetitorsBenchResult
{
    public string RunId { get; set; } = string.Empty;
    public DateTime RunAtUtc { get; set; }
    public string Engine { get; set; } = "all";
    public bool CiMode { get; set; }
    public List<CompetitorResult> Metrics { get; set; } = new();
    public bool Success { get; set; }
    public string? Error { get; set; }

    public void WriteToDisk(string artifactDir)
    {
        Directory.CreateDirectory(artifactDir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(Path.Combine(artifactDir, "competitors.json"), json);
    }
}
