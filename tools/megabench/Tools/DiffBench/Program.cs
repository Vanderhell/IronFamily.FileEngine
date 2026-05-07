using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using IronConfig.Iupd.Delta;
using IronConfig.DiffEngine;

namespace DiffBench;

/// <summary>
/// Comparison benchmark: Copy vs DELTA v1 vs DiffEngine v1
/// Tests all 12 scenarios from efficiency matrix
/// </summary>
class Program
{
    static void Main()
    {
        Console.WriteLine("=== DiffEngine Foundation Spike Benchmark ===");
        Console.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        // 12 test scenarios from DELTA evaluation corpus
        var testCases = GenerateTestCases();

        var results = new List<ComparisonResult>();

        foreach (var testCase in testCases)
        {
            Console.WriteLine($"Running: {testCase.CaseId}...");

            var result = RunComparison(testCase);
            results.Add(result);

            Console.WriteLine($"  Copy:      {result.CopyBytes:N0} bytes ({result.CopyRatio:P2})");
            Console.WriteLine($"  DELTA v1:  {result.DeltaV1Bytes:N0} bytes ({result.DeltaV1Ratio:P2})");
            Console.WriteLine($"  DiffEngine: {result.DiffEngineBytes:N0} bytes ({result.DiffEngineRatio:P2})");
            Console.WriteLine();
        }

        // Output CSV
        OutputCsv(results);

        // Summary
        Console.WriteLine("=== Summary ===");
        var diffWins = results.Count(r => r.DiffEngineRatio < r.DeltaV1Ratio);
        var deltaWins = results.Count(r => r.DeltaV1Ratio < r.DiffEngineRatio);
        Console.WriteLine($"DiffEngine wins: {diffWins}/{results.Count}");
        Console.WriteLine($"DELTA v1 wins:   {deltaWins}/{results.Count}");
        Console.WriteLine();

        // Check stop criteria
        CheckStopCriteria(results);
    }

    static List<TestCase> GenerateTestCases()
    {
        return new List<TestCase>
        {
            // File size: 512KB, Change: sparse
            new TestCase { CaseId = "512K_sparse", BaseSize = 512 * 1024, ChangeKind = "sparse" },
            // File size: 512KB, Change: header insert
            new TestCase { CaseId = "512K_header_ins", BaseSize = 512 * 1024, ChangeKind = "header_insert" },
            // File size: 512KB, Change: middle insert
            new TestCase { CaseId = "512K_middle_ins", BaseSize = 512 * 1024, ChangeKind = "middle_insert" },
            // File size: 512KB, Change: append
            new TestCase { CaseId = "512K_append", BaseSize = 512 * 1024, ChangeKind = "append" },

            // File size: 1MB, Change: sparse
            new TestCase { CaseId = "1MB_sparse", BaseSize = 1024 * 1024, ChangeKind = "sparse" },
            // File size: 1MB, Change: header insert
            new TestCase { CaseId = "1MB_header_ins", BaseSize = 1024 * 1024, ChangeKind = "header_insert" },
            // File size: 1MB, Change: middle insert
            new TestCase { CaseId = "1MB_middle_ins", BaseSize = 1024 * 1024, ChangeKind = "middle_insert" },
            // File size: 1MB, Change: append
            new TestCase { CaseId = "1MB_append", BaseSize = 1024 * 1024, ChangeKind = "append" },

            // File size: 2MB, Change: sparse
            new TestCase { CaseId = "2MB_sparse", BaseSize = 2 * 1024 * 1024, ChangeKind = "sparse" },
            // File size: 2MB, Change: header insert
            new TestCase { CaseId = "2MB_header_ins", BaseSize = 2 * 1024 * 1024, ChangeKind = "header_insert" },
            // File size: 2MB, Change: middle insert
            new TestCase { CaseId = "2MB_middle_ins", BaseSize = 2 * 1024 * 1024, ChangeKind = "middle_insert" },
            // File size: 2MB, Change: append
            new TestCase { CaseId = "2MB_append", BaseSize = 2 * 1024 * 1024, ChangeKind = "append" },
        };
    }

    static ComparisonResult RunComparison(TestCase testCase)
    {
        // Generate deterministic base
        byte[] baseBytes = GenerateDeterministicBase(testCase.BaseSize);

        // Generate target based on change kind
        byte[] targetBytes = ApplyChange(baseBytes, testCase.ChangeKind);

        // Copy (baseline): just the target
        byte[] copyBytes = targetBytes;

        // DELTA v1
        byte[] deltaV1Bytes = IupdDeltaV1.CreateDeltaV1(baseBytes, targetBytes);
        var deltaV1Applied = IupdDeltaV1.ApplyDeltaV1(baseBytes, deltaV1Bytes, out var deltaV1Error);
        bool deltaV1Ok = deltaV1Error.IsOk && SequenceEqual(deltaV1Applied, targetBytes);

        // DiffEngine v1
        byte[] diffEnginBytes = DiffEngineV1.CreateDiffV1(baseBytes, targetBytes);
        var diffEngineApplied = DiffEngineV1.ApplyDiffV1(baseBytes, diffEnginBytes, out var diffEngineError);
        bool diffEngineOk = diffEngineError.IsOk && SequenceEqual(diffEngineApplied, targetBytes);

        return new ComparisonResult
        {
            CaseId = testCase.CaseId,
            ChangeKind = testCase.ChangeKind,
            BaseBytes = baseBytes.Length,
            TargetBytes = targetBytes.Length,
            CopyBytes = copyBytes.Length,
            DeltaV1Bytes = deltaV1Bytes.Length,
            DiffEngineBytes = diffEnginBytes.Length,
            CopyRatio = (double)copyBytes.Length / targetBytes.Length,
            DeltaV1Ratio = (double)deltaV1Bytes.Length / targetBytes.Length,
            DiffEngineRatio = (double)diffEnginBytes.Length / targetBytes.Length,
            DeltaV1Ok = deltaV1Ok,
            DiffEngineOk = diffEngineOk,
        };
    }

    static byte[] GenerateDeterministicBase(int size)
    {
        byte[] result = new byte[size];
        for (int i = 0; i < size; i++)
        {
            result[i] = (byte)((i * 0x47 + 0x13) & 0xFF);
        }
        return result;
    }

    static byte[] ApplyChange(byte[] baseBytes, string changeKind)
    {
        return changeKind switch
        {
            "sparse" => ApplySparseChange(baseBytes),
            "header_insert" => ApplyHeaderInsert(baseBytes),
            "middle_insert" => ApplyMiddleInsert(baseBytes),
            "append" => ApplyAppend(baseBytes),
            _ => throw new ArgumentException($"Unknown change kind: {changeKind}")
        };
    }

    static byte[] ApplySparseChange(byte[] baseBytes)
    {
        // Flip ~1% of bytes
        var result = (byte[])baseBytes.Clone();
        int count = result.Length / 100;
        for (int i = 0; i < count; i++)
        {
            int pos = (i * 1331) % result.Length;
            result[pos] ^= 0xFF;
        }
        return result;
    }

    static byte[] ApplyHeaderInsert(byte[] baseBytes)
    {
        // Insert 256 bytes at position 0
        var insert = new byte[256];
        for (int i = 0; i < insert.Length; i++)
            insert[i] = (byte)((i * 0x53) & 0xFF);

        var result = new byte[baseBytes.Length + insert.Length];
        Array.Copy(insert, 0, result, 0, insert.Length);
        Array.Copy(baseBytes, 0, result, insert.Length, baseBytes.Length);
        return result;
    }

    static byte[] ApplyMiddleInsert(byte[] baseBytes)
    {
        // Insert 256 bytes in middle
        int midpoint = baseBytes.Length / 2;
        var insert = new byte[256];
        for (int i = 0; i < insert.Length; i++)
            insert[i] = (byte)((i * 0x53) & 0xFF);

        var result = new byte[baseBytes.Length + insert.Length];
        Array.Copy(baseBytes, 0, result, 0, midpoint);
        Array.Copy(insert, 0, result, midpoint, insert.Length);
        Array.Copy(baseBytes, midpoint, result, midpoint + insert.Length, baseBytes.Length - midpoint);
        return result;
    }

    static byte[] ApplyAppend(byte[] baseBytes)
    {
        // Append 256 bytes
        var append = new byte[256];
        for (int i = 0; i < append.Length; i++)
            append[i] = (byte)((i * 0x53) & 0xFF);

        var result = new byte[baseBytes.Length + append.Length];
        Array.Copy(baseBytes, result, baseBytes.Length);
        Array.Copy(append, 0, result, baseBytes.Length, append.Length);
        return result;
    }

    static bool SequenceEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    static void OutputCsv(List<ComparisonResult> results)
    {
        string csvPath = "diff_v1_comparison.csv";
        using var writer = new System.IO.StreamWriter(csvPath);

        // Header
        writer.WriteLine("caseId,changeKind,baseBytes,targetBytes,copyBytes,deltaV1Bytes,diffEngineBytes,copyRatio,deltaV1Ratio,diffEngineRatio,deltaV1Ok,diffEngineOk");

        // Rows
        foreach (var result in results)
        {
            writer.WriteLine($"{result.CaseId},{result.ChangeKind},{result.BaseBytes},{result.TargetBytes}," +
                $"{result.CopyBytes},{result.DeltaV1Bytes},{result.DiffEngineBytes}," +
                $"{result.CopyRatio:F4},{result.DeltaV1Ratio:F4},{result.DiffEngineRatio:F4}," +
                $"{result.DeltaV1Ok},{result.DiffEngineOk}");
        }

        Console.WriteLine($"CSV output: {csvPath}");
    }

    static void CheckStopCriteria(List<ComparisonResult> results)
    {
        Console.WriteLine("=== Stop Criteria Evaluation ===");

        // Criterion 1: Header insert ratio
        var headerResults = results.Where(r => r.ChangeKind == "header_insert").ToList();
        var headerRatios = headerResults.Select(r => r.DiffEngineRatio).ToList();

        Console.WriteLine($"Criterion 1: Header Insert Ratio");
        foreach (var result in headerResults)
        {
            Console.WriteLine($"  {result.CaseId}: {result.DiffEngineRatio:P2} (target: <30%)");
        }

        bool criterion1Met = headerRatios.All(r => r < 0.30);
        Console.WriteLine($"  Status: {(criterion1Met ? "✅ MET" : "❌ FAILED")}");
        Console.WriteLine();

        // Criterion 4: Roundtrip success
        bool criterion4Met = results.All(r => r.DiffEngineOk);
        Console.WriteLine($"Criterion 4: Roundtrip Success");
        Console.WriteLine($"  Status: {(criterion4Met ? "✅ ALL PASSED" : "❌ SOME FAILED")}");
        Console.WriteLine();

        if (criterion1Met && criterion4Met)
        {
            Console.WriteLine("✅ FOUNDATION SPIKE VIABLE - Proceed to production phase");
        }
        else
        {
            Console.WriteLine("❌ FOUNDATION SPIKE NOT VIABLE - Stop criteria not met");
        }
    }
}

class TestCase
{
    public string CaseId { get; set; }
    public int BaseSize { get; set; }
    public string ChangeKind { get; set; }
}

class ComparisonResult
{
    public string CaseId { get; set; }
    public string ChangeKind { get; set; }
    public int BaseBytes { get; set; }
    public int TargetBytes { get; set; }
    public int CopyBytes { get; set; }
    public int DeltaV1Bytes { get; set; }
    public int DiffEngineBytes { get; set; }
    public double CopyRatio { get; set; }
    public double DeltaV1Ratio { get; set; }
    public double DiffEngineRatio { get; set; }
    public bool DeltaV1Ok { get; set; }
    public bool DiffEngineOk { get; set; }
}
