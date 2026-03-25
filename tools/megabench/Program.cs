// #!/usr/bin/env dotnet

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using IronFamily.MegaBench.Datasets.IronCfg;
using IronFamily.MegaBench.Datasets.ILog;
using IronFamily.MegaBench.Datasets.IUpd;
using IronFamily.MegaBench.Determinism;
using IronFamily.MegaBench.Bench;
using IronFamily.MegaBench.Competitors;
using IronFamily.MegaBench.Competitors.Fairness;
using IronFamily.MegaBench.Semantics;
using IronFamily.MegaBench.Internal;
using IronConfig.ILog;
using IronConfig.Iupd;

namespace IronFamily.MegaBench;

/// <summary>
/// MegaBench: Comprehensive benchmarking harness for IronFamily engines vs competitors.
///
/// Usage:
///   megabench run --engine icfg|ilog|iupd --profile {profile} --dataset {size} [--format {competitor}]
///
/// Examples:
///   megabench run --engine icfg --dataset 10KB --format icfg
///   megabench run --engine ilog --profile MINIMAL --dataset 100KB --format protobuf
///   megabench run --engine iupd --profile FAST --dataset 1MB --format tar
/// </summary>
class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("=== MegaBench v1.0 (Skeleton) ===");
            Console.WriteLine();
            PrintUsage();
            return 2;
        }

        string command = args[0];

        // bench-child does not print banner (child process must output pure JSON)
        if (command != "bench-child")
        {
            Console.WriteLine("=== MegaBench v1.0 (Skeleton) ===");
            Console.WriteLine();
        }

        if (command == "run")
        {
            return RunBenchmark(args);
        }
        else if (command == "list")
        {
            ListAvailable();
            return 0;
        }
        else if (command == "gen-all")
        {
            return GenerateAllDatasets(args);
        }
        else if (command == "determinism-check")
        {
            return DeterminismRunner.RunDeterminismCheck();
        }
        else if (command == "bench-sanity")
        {
            Console.Error.WriteLine("Command 'bench-sanity' is deprecated. Use 'bench-internal-realworld'.");
            return 2;
        }
        else if (command == "bench-competitors")
        {
            Console.Error.WriteLine("Command 'bench-competitors' is deprecated. Use 'bench-internal-realworld'.");
            return 2;
        }
        else if (command == "bench-competitors-v5")
        {
            return RunCompetitorsBenchV5(args);
        }
        else if (command == "bench-internal-realworld")
        {
            return InternalRealWorldBenchRunner.Run(args);
        }
        else if (command == "bench-icfg-layers")
        {
            return IronCfgFullVsLayersRunner.Run(args);
        }
        else if (command == "bench-ilog-mini")
        {
            return IlogMiniBenchRunner.Run(args);
        }
        else if (command == "bench-iupd-mini")
        {
            return IupdMiniBenchRunner.Run(args);
        }
        else if (command == "bench-overview")
        {
            return AllEnginesOverviewRunner.Run(args);
        }
        else if (command == "bench-child")
        {
            return CompetitorsBenchChild.RunJob();
        }
        else
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintUsage();
            return 2;
        }
    }

    static int RunBenchmark(string[] args)
    {
        // TODO: Implement argument parsing
        // Expected: --engine, --profile (optional), --dataset, --format (optional, defaults to engine name)

        var options = new BenchmarkOptions();
        if (!TryParseArgs(args, options))
        {
            return 2;
        }

        Console.WriteLine($"Engine: {options.Engine}");
        Console.WriteLine($"Profile: {options.Profile ?? "(default)"}");
        Console.WriteLine($"Dataset: {options.Dataset}");
        Console.WriteLine($"Format: {options.Format}");
        Console.WriteLine();

        // TODO: Implement benchmark runner
        // 1. Load or generate dataset
        // 2. Set up competitor encoders/decoders
        // 3. Run warmup iterations
        // 4. Run measurement iterations (7x)
        // 5. Calculate statistics (median, min, max)
        // 6. Output results.json + REPORT.md

        Console.WriteLine("âś“ Benchmark skeleton ready");
        Console.WriteLine("TODO: Implement actual benchmark logic in PHASE 3");

        return 0;
    }

    static void ListAvailable()
    {
        Console.WriteLine("=== Available Engines ===");
        Console.WriteLine();
        Console.WriteLine("IRONCFG:");
        Console.WriteLine("  Competitors: protobuf, flatbuffers, capnproto, messagepack, cbor");
        Console.WriteLine();
        Console.WriteLine("ILOG:");
        Console.WriteLine("  Profiles: MINIMAL, INTEGRITY, SEARCHABLE, ARCHIVED, AUDITED");
        Console.WriteLine("  Competitors: protobuf, protobuf-delimited, messagepack, cbor, sqlite, tar");
        Console.WriteLine();
        Console.WriteLine("IUPD:");
        Console.WriteLine("  Profiles: MINIMAL, FAST, SECURE, OPTIMIZED, DELTA");
        Console.WriteLine("  Competitors: tar, tar+lz4, tar+zstd, xdelta3, bsdiff");
    }

    static int RunCompetitorsBench(string[] args)
    {
        // Parse arguments: --engine, --sizes, --profiles, --ci-mode, --fairness
        string? engineFilter = null;
        string? sizeFilter = null;
        string? profileFilter = null;
        bool ciMode = args.Contains("--ci-mode");
        bool fairnessMode = args.Contains("--fairness");

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--engine" && i + 1 < args.Length)
            {
                engineFilter = args[i + 1];
                i++;
            }
            else if (args[i] == "--sizes" && i + 1 < args.Length)
            {
                sizeFilter = args[i + 1];
                i++;
            }
            else if (args[i] == "--profiles" && i + 1 < args.Length)
            {
                profileFilter = args[i + 1];
                i++;
            }
        }

        Console.WriteLine("=== MegaBench Competitors Benchmark ===");
        if (fairnessMode)
        {
            Console.WriteLine("[FAIRNESS MODE]");
        }
        Console.WriteLine();

        // If fairness mode, run fairness gate instead
        if (fairnessMode)
        {
            return RunFairnessGate(engineFilter);
        }

        var result = CompetitorsBenchRunner.RunAll(engineFilter, sizeFilter, profileFilter, ciMode);

        Console.WriteLine();
        Console.WriteLine($"=== Results ===");
        Console.WriteLine($"Run ID: {result.RunId}");
        Console.WriteLine($"Engine: {result.Engine}");
        Console.WriteLine($"Total codecs benchmarked: {result.Metrics.Count}");
        Console.WriteLine($"Success: {result.Success}");

        if (!result.Success && result.Error != null)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return 2;
        }

        // Write results to disk
        var artifactDir = Path.Combine("artifacts", "_gauntlet", $"2026-02-25_megabench_competitors_v4", "results", result.RunId);
        result.WriteToDisk(artifactDir);
        Console.WriteLine($"Results written to: {artifactDir}");

        return result.Success ? 0 : 2;
    }

    static int RunFairnessGate(string? engineFilter)
    {
        // PHASE 5: Fairness gate for all active codecs (ICFG, ILOG_MANIFEST, IUPD_MANIFEST)
        int passCount = 0;
        int failCount = 0;
        int excludedCount = 0;
        var excludeReasons = new List<string>();

        Console.WriteLine("=== Fairness Gate (PHASE 5) ===");
        Console.WriteLine();

        // Test ICFG
        Console.WriteLine("Testing ICFG fairness...");
        try
        {
            string icfgPath = "vectors/small/ironcfg/small/golden.icfg";
            if (File.Exists(icfgPath))
            {
                byte[] icfgData = File.ReadAllBytes(icfgPath);
                byte[] icfgPayload = IcfgToCanonicalJson.ToCanonicalJson(icfgData, minBytes: 1024);

                var codecs = CompetitorCodecFactory.GetCodecsForKind(CodecKind.ICFG).ToList();
                foreach (var codec in codecs)
                {
                    try
                    {
                        var proof = FairnessGate.RunAndProve(codec, icfgPayload, "ICFG");
                        Console.WriteLine($"  âś“ {codec.Name}: PASS");
                        passCount++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        Console.WriteLine($"  âś— {codec.Name}: FAIL - {ex.Message}");
                        failCount++;
                    }
                    catch (NotImplementedException ex)
                    {
                        Console.WriteLine($"  âŠ {codec.Name}: EXCLUDED - {ex.Message}");
                        excludedCount++;
                        excludeReasons.Add($"{codec.Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"ICFG test vector not found: {icfgPath}");
                failCount++;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ICFG fairness error: {ex.Message}");
            failCount++;
        }

        // Test ILOG
        Console.WriteLine();
        Console.WriteLine("Testing ILOG fairness...");
        try
        {
            string ilogPath = "vectors/small/ilog/small/expected/ilog.ilog";
            if (File.Exists(ilogPath))
            {
                byte[] ilogData = File.ReadAllBytes(ilogPath);
                byte[] ilogPayload = IlogToCanonicalJson.ToCanonicalJson(ilogData, minBytes: 1024);

                var codecs = CompetitorCodecFactory.GetCodecsForKind(CodecKind.ILOG).ToList();
                foreach (var codec in codecs)
                {
                    try
                    {
                        var proof = FairnessGate.RunAndProve(codec, ilogPayload, "ILOG");
                        Console.WriteLine($"  âś“ {codec.Name}: PASS");
                        passCount++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        Console.WriteLine($"  âś— {codec.Name}: FAIL - {ex.Message}");
                        failCount++;
                    }
                    catch (NotImplementedException ex)
                    {
                        Console.WriteLine($"  âŠ {codec.Name}: EXCLUDED - {ex.Message}");
                        excludedCount++;
                        excludeReasons.Add($"{codec.Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"ILOG test vector not found: {ilogPath}");
                failCount++;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ILOG fairness error: {ex.Message}");
            failCount++;
        }

        // Test IUPD
        Console.WriteLine();
        Console.WriteLine("Testing IUPD fairness...");
        try
        {
            string iupdPath = "vectors/small/iupd/golden_small/expected/iupd.iupd";
            if (File.Exists(iupdPath))
            {
                byte[] iupdData = File.ReadAllBytes(iupdPath);
                byte[] iupdPayload = IupdManifestToCanonicalJson.ToCanonicalJson(iupdData, minBytes: 1024);

                var codecs = CompetitorCodecFactory.GetCodecsForKind(CodecKind.IUPD_Manifest).ToList();
                foreach (var codec in codecs)
                {
                    try
                    {
                        var proof = FairnessGate.RunAndProve(codec, iupdPayload, "IUPD_MANIFEST");
                        Console.WriteLine($"  âś“ {codec.Name}: PASS");
                        passCount++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        Console.WriteLine($"  âś— {codec.Name}: FAIL - {ex.Message}");
                        failCount++;
                    }
                    catch (NotImplementedException ex)
                    {
                        Console.WriteLine($"  âŠ {codec.Name}: EXCLUDED - {ex.Message}");
                        excludedCount++;
                        excludeReasons.Add($"{codec.Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"IUPD test vector not found: {iupdPath}");
                failCount++;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"IUPD fairness error: {ex.Message}");
            failCount++;
        }

        // Summary
        Console.WriteLine();
        Console.WriteLine("=== Fairness Gate Summary ===");
        Console.WriteLine($"PASS: {passCount}");
        Console.WriteLine($"FAIL: {failCount}");
        Console.WriteLine($"EXCLUDED: {excludedCount}");
        if (excludeReasons.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Exclusion reasons:");
            foreach (var reason in excludeReasons)
            {
                Console.WriteLine($"  - {reason}");
            }
        }

        return failCount == 0 ? 0 : 3;
    }

    static int RunCompetitorsBenchV5(string[] args)
    {
        // PHASE 5: bench-competitors-v5 --engine all --realworld --ci-mode
        string? engineFilter = null;
        bool ciMode = args.Contains("--ci-mode");
        bool realWorld = args.Contains("--realworld");

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--engine" && i + 1 < args.Length)
            {
                engineFilter = args[i + 1];
                i++;
            }
        }

        Console.WriteLine("=== MegaBench Competitors Benchmark V5 (Multi-Process) ===");
        if (realWorld)
            Console.WriteLine("[REAL-WORLD DATASETS]");
        if (ciMode)
            Console.WriteLine("[CI MODE]");
        Console.WriteLine();

        return CompetitorsBenchParent.RunAllMultiProcess(engineFilter ?? "all", string.Empty, string.Empty, ciMode, realWorld);
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  megabench run --engine <engine> --profile <profile> --dataset <size> [--format <format>]");
        Console.WriteLine("  megabench list");
        Console.WriteLine("  megabench gen-all [--validate {none|fast|strict}]");
        Console.WriteLine("  megabench determinism-check");
        Console.WriteLine("  megabench bench-icfg-layers [--dataset <size>] [--read-mode <mode>] [--cold] [--output <dir>]");
        Console.WriteLine("  megabench bench-internal-realworld [--engine <engine>] [--ci-mode]");
        Console.WriteLine("  megabench bench-competitors-v5 [--engine <engine>] [--realworld] [--ci-mode]");
        Console.WriteLine();
        Console.WriteLine("Engines: icfg, ilog, iupd, all");
        Console.WriteLine("Datasets: 1KB, 10KB, 100KB, 1MB, 10MB, 100MB (heavy mode only)");
        Console.WriteLine("Validation modes: none (requires MEGABENCH_ALLOW_NO_VALIDATE=1), fast, strict (default)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  megabench run --engine icfg --dataset 10KB");
        Console.WriteLine("  megabench run --engine ilog --profile MINIMAL --dataset 100KB --format protobuf");
        Console.WriteLine("  megabench run --engine iupd --profile FAST --dataset 1MB");
        Console.WriteLine("  megabench gen-all");
        Console.WriteLine("  megabench gen-all --validate strict");
        Console.WriteLine("  megabench determinism-check");
        Console.WriteLine("  megabench bench-icfg-layers --dataset 10KB");
        Console.WriteLine("  megabench bench-icfg-layers --dataset 10KB --read-mode payload --cold");
        Console.WriteLine("  megabench bench-internal-realworld --engine all");
        Console.WriteLine("  megabench bench-internal-realworld --engine iupd --ci-mode");
        Console.WriteLine("  megabench bench-competitors-v5 --engine all --realworld --ci-mode");
    }

    static bool TryParseArgs(string[] args, BenchmarkOptions options)
    {
        // TODO: Implement proper argument parsing
        // For now, return stub success
        options.Engine = "icfg";
        options.Dataset = "10KB";
        options.Format = "icfg";
        return true;
    }

    static int GenerateAllDatasets(string[] args)
    {
        // Parse --validate flag (default: strict)
        string validateMode = "strict";
        bool allowNoValidate = Environment.GetEnvironmentVariable("MEGABENCH_ALLOW_NO_VALIDATE") == "1";

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--validate" && i + 1 < args.Length)
            {
                validateMode = args[i + 1].ToLowerInvariant();
                if (validateMode == "none" && !allowNoValidate)
                {
                    Console.Error.WriteLine("ERROR: --validate none requires MEGABENCH_ALLOW_NO_VALIDATE=1");
                    return 2;
                }
                i++;
            }
        }

        Console.WriteLine($"=== MegaBench: Generate All Datasets ===");
        Console.WriteLine($"Validation mode: {validateMode}");
        Console.WriteLine();

        // Set deterministic seed for reproducibility
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");

        var iconfgSizes = new[] { "1KB", "10KB" };
        var otherSizes = new[] { "10KB" };
        int generatedCount = 0;
        int failureCount = 0;

        try
        {
            // IRONCFG datasets
            Console.WriteLine("Generating IRONCFG datasets...");
            foreach (var size in iconfgSizes)
            {
                try
                {
                    var data = IronCfgDatasetGenerator.GenerateDataset(size, useCrc32: true);
                    Console.WriteLine($"  âś“ ironcfg_{size}: {data.Length} bytes");
                    generatedCount++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  âś— ironcfg_{size}: {ex.Message}");
                    failureCount++;
                }
            }

            // ILOG datasets (all profiles)
            Console.WriteLine("Generating ILOG datasets (all profiles)...");
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
                        Console.WriteLine($"  âś“ ilog_{profile}_{size}: {data.Length} bytes");
                        generatedCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  âś— ilog_{profile}_{size}: {ex.Message}");
                        failureCount++;
                    }
                }
            }

            // IUPD datasets (non-DELTA profiles)
            Console.WriteLine("Generating IUPD datasets (all profiles except DELTA)...");
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
                        Console.WriteLine($"  âś“ iupd_{profile}_{size}: {data.Length} bytes");
                        generatedCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  âś— iupd_{profile}_{size}: {ex.Message}");
                        failureCount++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine($"=== Summary ===");
        Console.WriteLine($"Generated: {generatedCount}");
        Console.WriteLine($"Failed: {failureCount}");

        if (failureCount > 0)
        {
            return 2;
        }

        if (generatedCount == 0)
        {
            Console.Error.WriteLine("ERROR: No datasets were generated");
            return 2;
        }

        return 0;
    }
}

class BenchmarkOptions
{
    public string Engine { get; set; } = "";
    public string? Profile { get; set; }
    public string Dataset { get; set; } = "";
    public string Format { get; set; } = "";
}
