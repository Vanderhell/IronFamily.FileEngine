using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using IronConfig.Iupd;
using IronConfig.Iupd.Delta;

namespace IUpd.RealBench;

/// <summary>
/// Real-binary benchmark runner for IUPD/Delta v1/IRONDEL2
/// Executes matrix of test cases and exports CSV results
/// </summary>
public class BenchRunner
{
    private class BenchResult
    {
        public string CaseId { get; set; }
        public string InputClass { get; set; }
        public string BasePath { get; set; }
        public string TargetPath { get; set; }
        public long BaseBytes { get; set; }
        public long TargetBytes { get; set; }
        public string DeltaFormat { get; set; }
        public string Operation { get; set; }
        public long PatchBytes { get; set; }
        public double Ratio { get; set; }
        public double TimeMs { get; set; }
        public bool Success { get; set; }
        public bool IsRealPair { get; set; }
        public string Notes { get; set; }
    }

    static byte[] ApplyDeltaPatch(byte[] baseData, byte[] patchData, out IupdError error)
    {
        // Detect patch format by magic bytes
        if (patchData.Length >= 8)
        {
            string magic = System.Text.Encoding.ASCII.GetString(patchData, 0, 8);
            if (magic == "IRONDEL2")
            {
                // Use Delta v2 (IRONDEL2) apply
                return IupdDeltaV2Cdc.ApplyDeltaV2(baseData, patchData, out error);
            }
        }
        // Default to Delta v1
        return IupdDeltaV1.ApplyDeltaV1(baseData, patchData, out error);
    }

    static void Main(string[] args)
    {
        Console.WriteLine("=== IUPD Real Binary Benchmark Runner ===");
        Console.WriteLine();

        var results = new List<BenchResult>();

        // Real vector group 1: Delta v2 case_01
        BenchDeltaV2Vectors(results);

        // Real vector group 2: DiffEngine case_01
        BenchDiffEngineVectors(results);

        // Real vector group 3: IUPD Delta v1
        BenchIupdVectors(results);

        // Synthetic corpus benchmarks
        BenchSyntheticCorpus(results);

        // Export CSV
        ExportCsv(results);

        // Summary
        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        var realCount = results.Count(r => r.IsRealPair);
        var syntheticCount = results.Count(r => !r.IsRealPair);
        var successCount = results.Count(r => r.Success);
        Console.WriteLine($"Real pairs benchmarked: {realCount}");
        Console.WriteLine($"Synthetic cases benchmarked: {syntheticCount}");
        Console.WriteLine($"Successful benchmarks: {successCount}/{results.Count}");
        Console.WriteLine();
    }

    static void BenchDeltaV2Vectors(List<BenchResult> results)
    {
        Console.WriteLine("Benchmarking Delta v2 golden vector...");

        string basePath = "artifacts/vectors/v1/delta2/v1/case_01.base.bin";
        string targetPath = "artifacts/vectors/v1/delta2/v1/case_01.out.bin";

        if (!File.Exists(basePath) || !File.Exists(targetPath))
        {
            Console.WriteLine($"ERROR: Vector files not found");
            return;
        }

        byte[] baseData = File.ReadAllBytes(basePath);
        byte[] targetData = File.ReadAllBytes(targetPath);

        // CREATE benchmark
        try
        {
            var sw = Stopwatch.StartNew();
            byte[] patch = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
            sw.Stop();

            results.Add(new BenchResult
            {
                CaseId = "RV001",
                InputClass = "DELTA_V2",
                BasePath = basePath,
                TargetPath = targetPath,
                BaseBytes = baseData.Length,
                TargetBytes = targetData.Length,
                DeltaFormat = "IRONDEL2",
                Operation = "CREATE",
                PatchBytes = patch.Length,
                Ratio = (double)patch.Length / targetData.Length,
                TimeMs = sw.Elapsed.TotalMilliseconds,
                Success = true,
                IsRealPair = true,
                Notes = "Golden vector from test suite"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in CREATE: {ex.Message}");
        }

        // APPLY benchmark
        try
        {
            byte[] patch = File.ReadAllBytes("artifacts/vectors/v1/delta2/v1/case_01.patch2.bin");

            var sw = Stopwatch.StartNew();
            byte[] applied = ApplyDeltaPatch(baseData, patch, out var error);
            sw.Stop();

            bool matches = applied != null && BytesEqual(applied, targetData);

            results.Add(new BenchResult
            {
                CaseId = "RV002",
                InputClass = "DELTA_V2",
                BasePath = basePath,
                TargetPath = targetPath,
                BaseBytes = baseData.Length,
                TargetBytes = targetData.Length,
                DeltaFormat = "IRONDEL2",
                Operation = "APPLY",
                PatchBytes = patch.Length,
                Ratio = (double)patch.Length / targetData.Length,
                TimeMs = sw.Elapsed.TotalMilliseconds,
                Success = error.IsOk && matches,
                IsRealPair = true,
                Notes = "Golden vector from test suite"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in APPLY: {ex.Message}");
        }

        Console.WriteLine("  Completed Delta v2 benchmarks");
    }

    static void BenchDiffEngineVectors(List<BenchResult> results)
    {
        Console.WriteLine("Benchmarking DiffEngine golden vector...");

        string basePath = "artifacts/vectors/v1/diff/v1/case_01.base.bin";
        string targetPath = "artifacts/vectors/v1/diff/v1/case_01.out.bin";

        if (!File.Exists(basePath) || !File.Exists(targetPath))
        {
            Console.WriteLine($"ERROR: Vector files not found");
            return;
        }

        byte[] baseData = File.ReadAllBytes(basePath);
        byte[] targetData = File.ReadAllBytes(targetPath);

        // CREATE benchmark
        try
        {
            var sw = Stopwatch.StartNew();
            byte[] patch = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
            sw.Stop();

            results.Add(new BenchResult
            {
                CaseId = "RV005",
                InputClass = "DIFFENGINE_V1",
                BasePath = basePath,
                TargetPath = targetPath,
                BaseBytes = baseData.Length,
                TargetBytes = targetData.Length,
                DeltaFormat = "DIFFENGINE_V1",
                Operation = "CREATE",
                PatchBytes = patch.Length,
                Ratio = (double)patch.Length / targetData.Length,
                TimeMs = sw.Elapsed.TotalMilliseconds,
                Success = true,
                IsRealPair = true,
                Notes = "Golden vector from test suite"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in CREATE: {ex.Message}");
        }

        Console.WriteLine("  Completed DiffEngine benchmarks");
    }

    static void BenchIupdVectors(List<BenchResult> results)
    {
        Console.WriteLine("Benchmarking IUPD Delta v1 vectors...");

        // case01 - small (CREATE)
        BenchIupdSingleCase(results, "RV007", "IUPD_SMALL", "case01", true);

        // case02 - medium (CREATE)
        BenchIupdSingleCase(results, "RV009", "IUPD_MEDIUM", "case02", true);

        Console.WriteLine("  Completed IUPD vector benchmarks");
    }

    static void BenchIupdSingleCase(List<BenchResult> results, string caseId, string inputClass, string caseNum, bool isCreate)
    {
        string basePath = $"vectors/small/iupd/delta/v1/{caseNum}/base.bin";
        string targetPath = $"vectors/small/iupd/delta/v1/{caseNum}/target.bin";

        if (!File.Exists(basePath) || !File.Exists(targetPath))
            return;

        byte[] baseData = File.ReadAllBytes(basePath);
        byte[] targetData = File.ReadAllBytes(targetPath);

        try
        {
            var sw = Stopwatch.StartNew();
            byte[] patch = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
            sw.Stop();

            results.Add(new BenchResult
            {
                CaseId = caseId,
                InputClass = inputClass,
                BasePath = basePath,
                TargetPath = targetPath,
                BaseBytes = baseData.Length,
                TargetBytes = targetData.Length,
                DeltaFormat = "DELTA_V1",
                Operation = "CREATE",
                PatchBytes = patch.Length,
                Ratio = (double)patch.Length / targetData.Length,
                TimeMs = sw.Elapsed.TotalMilliseconds,
                Success = true,
                IsRealPair = true,
                Notes = "Golden vector from test suite"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in {caseId}: {ex.Message}");
        }
    }

    static void BenchSyntheticCorpus(List<BenchResult> results)
    {
        Console.WriteLine("Benchmarking synthetic corpus mutations...");

        string corpusDir = "artifacts/_gauntlet/2026-03-02_delta_efficiency_matrix_v1/corpus_temp";

        if (!Directory.Exists(corpusDir))
        {
            Console.WriteLine($"ERROR: Corpus directory not found: {corpusDir}");
            return;
        }

        // Benchmark each input with sparse and headins mutations
        for (int inputNum = 1; inputNum <= 4; inputNum++)
        {
            string inputPath = Path.Combine(corpusDir, $"input_{inputNum:D2}.bin");

            if (!File.Exists(inputPath))
                continue;

            byte[] baseData = File.ReadAllBytes(inputPath);
            string sizeLabel = GetSizeLabel(inputNum);

            // Benchmark sparse mutation
            BenchSyntheticCase(results, inputNum, "sparse", baseData, corpusDir, sizeLabel);

            // Benchmark headins mutation
            BenchSyntheticCase(results, inputNum, "headins", baseData, corpusDir, sizeLabel);
        }

        Console.WriteLine("  Completed synthetic corpus benchmarks");
    }

    static void BenchSyntheticCase(List<BenchResult> results, int inputNum, string mutationType, byte[] baseData, string corpusDir, string sizeLabel)
    {
        string targetFileName = $"target_{inputNum:D2}_{mutationType}.bin";
        string targetPath = Path.Combine(corpusDir, targetFileName);

        if (!File.Exists(targetPath))
            return;

        byte[] targetData = File.ReadAllBytes(targetPath);

        string caseId = $"SY{(inputNum - 1) * 6 + (mutationType == "sparse" ? 1 : 3):D3}";
        string inputClass = $"SYNTHETIC_{sizeLabel}";

        try
        {
            var sw = Stopwatch.StartNew();
            byte[] patch = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
            sw.Stop();

            results.Add(new BenchResult
            {
                CaseId = caseId,
                InputClass = inputClass,
                BasePath = Path.Combine(corpusDir, $"input_{inputNum:D2}.bin"),
                TargetPath = targetPath,
                BaseBytes = baseData.Length,
                TargetBytes = targetData.Length,
                DeltaFormat = "DELTA_V1",
                Operation = "CREATE",
                PatchBytes = patch.Length,
                Ratio = (double)patch.Length / targetData.Length,
                TimeMs = sw.Elapsed.TotalMilliseconds,
                Success = true,
                IsRealPair = false,
                Notes = $"Synthetic mutation: {mutationType}"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in {caseId}: {ex.Message}");
        }
    }

    static string GetSizeLabel(int inputNum) => inputNum switch
    {
        1 => "512K",
        2 => "1MB",
        3 => "2MB",
        4 => "4MB",
        _ => "UNKNOWN"
    };

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null)
            return a == b;
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    static void ExportCsv(List<BenchResult> results)
    {
        string outputDir = "artifacts/_dev/exec_iupd_real_fw_bench_01";
        Directory.CreateDirectory(outputDir);

        string csvPath = Path.Combine(outputDir, "RESULTS.csv");

        Console.WriteLine();
        Console.WriteLine($"Exporting results to: {csvPath}");

        using (var writer = new StreamWriter(csvPath))
        {
            // Header
            writer.WriteLine("case_id,input_class,base_path,target_path,base_bytes,target_bytes,delta_format,operation,patch_bytes,ratio,time_ms,success,is_real_pair,notes");

            // Data rows
            foreach (var r in results)
            {
                writer.WriteLine($"{r.CaseId},{r.InputClass},{r.BasePath},{r.TargetPath},{r.BaseBytes},{r.TargetBytes},{r.DeltaFormat},{r.Operation},{r.PatchBytes},{r.Ratio:F4},{r.TimeMs:F2},{r.Success},{r.IsRealPair},\"{r.Notes}\"");
            }
        }

        Console.WriteLine($"CSV exported with {results.Count} rows");
    }
}
