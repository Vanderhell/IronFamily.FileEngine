using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using IronConfig.Iupd.Delta;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== IUPD DELTA v1 vs v2 Comparison Matrix ===\n");

        string corpusDir = "artifacts/_gauntlet/2026-03-02_delta_efficiency_matrix_v1/corpus_temp";
        string outputDir = "artifacts/_gauntlet/2026-03-02_delta_v2_cdc_spike_v1";

        if (!Directory.Exists(corpusDir))
        {
            Console.WriteLine($"ERROR: Corpus directory not found: {corpusDir}");
            return;
        }

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"Created output directory: {outputDir}");
        }

        // Phase 2: Create mutation scenarios
        Console.WriteLine("PHASE 2: Creating mutation scenarios...");
        var mutations = CreateMutations(corpusDir);
        Console.WriteLine($"Created {mutations.Count} mutation scenarios\n");

        // Phase 3: Run measurements (v1 vs v2 comparison)
        Console.WriteLine("PHASE 3: Running delta v1 vs v2 comparison measurements...");
        var results = RunComparisonMeasurements(mutations);
        Console.WriteLine($"Completed {results.Count} measurements\n");

        // Write CSV
        string csvPath = Path.Combine(outputDir, "delta_v1_vs_v2_cdc.csv");
        WriteCsvComparison(csvPath, results);
        Console.WriteLine($"CSV written to: {csvPath}\n");

        Console.WriteLine($"Total comparison rows: {results.Count}");
        Console.WriteLine("Comparison matrix complete.");
    }

    class Mutation
    {
        public string BaseFile { get; set; }
        public string TargetFile { get; set; }
        public string ChangeKind { get; set; }
        public string BaseName { get; set; }
    }

    class Result
    {
        public string CaseId { get; set; }
        public string BaseName { get; set; }
        public string ChangeKind { get; set; }
        public long BaseBytes { get; set; }
        public long TargetBytes { get; set; }
        public long DeltaBytes { get; set; }
        public double DeltaRatio { get; set; }
        public double CreateMedianUs { get; set; }
        public double CreateP95Us { get; set; }
        public double ApplyMedianUs { get; set; }
        public double ApplyP95Us { get; set; }
        public bool Ok { get; set; }
    }

    class ComparisonResult
    {
        public string CaseId { get; set; }
        public string ChangeKind { get; set; }
        public long BaseBytes { get; set; }
        public long TargetBytes { get; set; }
        public long DeltaV1Bytes { get; set; }
        public double DeltaV1Ratio { get; set; }
        public long DeltaV2Bytes { get; set; }
        public double DeltaV2Ratio { get; set; }
        public bool V1Ok { get; set; }
        public bool V2Ok { get; set; }
        public double V1CreateMedianUs { get; set; }
        public double V2CreateMedianUs { get; set; }
        public double V1ApplyMedianUs { get; set; }
        public double V2ApplyMedianUs { get; set; }
    }

    static List<Mutation> CreateMutations(string corpusDir)
    {
        var mutations = new List<Mutation>();
        var baseFiles = Directory.GetFiles(corpusDir, "input*.bin").OrderBy(f => f).ToArray();

        foreach (var baseFile in baseFiles)
        {
            string baseName = Path.GetFileName(baseFile);
            byte[] baseData = ReadAllBytesWithRetry(baseFile);

            // A) Sparse flips: 3 bytes in 10 different 4KB chunks
            {
                byte[] target = (byte[])baseData.Clone();
                var rng = new Random(42); // Deterministic
                for (int i = 0; i < 10; i++)
                {
                    int chunkIdx = (rng.Next() % ((int)Math.Ceiling(baseData.Length / 4096.0)));
                    int offset = chunkIdx * 4096 + (rng.Next() % 4000);
                    if (offset < target.Length)
                        target[offset] ^= (byte)(rng.Next() % 256);
                }
                string targetFile = Path.Combine(corpusDir, baseName.Replace("input", "target").Replace(".bin", "_sparse.bin"));
                File.WriteAllBytes(targetFile, target);
                mutations.Add(new Mutation { BaseFile = baseFile, TargetFile = targetFile, ChangeKind = "sparse_flips", BaseName = baseName });
            }

            // B) Middle insert: 256 bytes at fileSize/2
            {
                byte[] target = new byte[baseData.Length + 256];
                int insertOffset = baseData.Length / 2;
                Array.Copy(baseData, 0, target, 0, insertOffset);
                // Insert deterministic pattern
                for (int i = 0; i < 256; i++)
                    target[insertOffset + i] = (byte)((insertOffset + i) ^ 0xAA);
                Array.Copy(baseData, insertOffset, target, insertOffset + 256, baseData.Length - insertOffset);
                string targetFile = Path.Combine(corpusDir, baseName.Replace("input", "target").Replace(".bin", "_midins.bin"));
                File.WriteAllBytes(targetFile, target);
                mutations.Add(new Mutation { BaseFile = baseFile, TargetFile = targetFile, ChangeKind = "middle_insert", BaseName = baseName });
            }

            // C) Header insert: 256 bytes at offset 0
            {
                byte[] target = new byte[baseData.Length + 256];
                for (int i = 0; i < 256; i++)
                    target[i] = (byte)(i ^ 0x55);
                Array.Copy(baseData, 0, target, 256, baseData.Length);
                string targetFile = Path.Combine(corpusDir, baseName.Replace("input", "target").Replace(".bin", "_headins.bin"));
                File.WriteAllBytes(targetFile, target);
                mutations.Add(new Mutation { BaseFile = baseFile, TargetFile = targetFile, ChangeKind = "header_insert", BaseName = baseName });
            }
        }

        return mutations;
    }

    static List<Result> RunMeasurements(List<Mutation> mutations)
    {
        var results = new List<Result>();
        int caseNum = 1;

        foreach (var mutation in mutations)
        {
            byte[] baseData = ReadAllBytesWithRetry(mutation.BaseFile);
            byte[] targetData = ReadAllBytesWithRetry(mutation.TargetFile);

            Console.WriteLine($"Case {caseNum}: {mutation.BaseName} + {mutation.ChangeKind}");

            // Warmup
            for (int i = 0; i < 3; i++)
            {
                byte[] delta = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
                IupdDeltaV1.ApplyDeltaV1(baseData, delta, out _);
            }

            // Measure CreateDeltaV1
            var createTimes = new List<double>();
            for (int i = 0; i < 9; i++)
            {
                var sw = Stopwatch.StartNew();
                byte[] delta = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
                sw.Stop();
                createTimes.Add(sw.Elapsed.TotalMicroseconds);
            }

            // Measure ApplyDeltaV1
            byte[] lastDelta = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
            var applyTimes = new List<double>();
            for (int i = 0; i < 9; i++)
            {
                var sw = Stopwatch.StartNew();
                byte[] appliedData = IupdDeltaV1.ApplyDeltaV1(baseData, lastDelta, out _);
                sw.Stop();
                applyTimes.Add(sw.Elapsed.TotalMicroseconds);
            }

            // Verify
            byte[] finalDelta = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
            byte[] appliedResult = IupdDeltaV1.ApplyDeltaV1(baseData, finalDelta, out var error);
            bool ok = error.IsOk && appliedResult.SequenceEqual(targetData);

            var result = new Result
            {
                CaseId = $"{Path.GetFileNameWithoutExtension(mutation.BaseName)}:{mutation.ChangeKind.Substring(0, 3)}",
                BaseName = mutation.BaseName,
                ChangeKind = mutation.ChangeKind,
                BaseBytes = baseData.Length,
                TargetBytes = targetData.Length,
                DeltaBytes = finalDelta.Length,
                DeltaRatio = (double)finalDelta.Length / targetData.Length,
                CreateMedianUs = Median(createTimes),
                CreateP95Us = Percentile95(createTimes),
                ApplyMedianUs = Median(applyTimes),
                ApplyP95Us = Percentile95(applyTimes),
                Ok = ok
            };

            results.Add(result);
            caseNum++;

            Console.WriteLine($"  Delta: {result.DeltaBytes} bytes ({result.DeltaRatio:P1})");
            Console.WriteLine($"  Create: {result.CreateMedianUs:F2}us (median), {result.CreateP95Us:F2}us (p95)");
            Console.WriteLine($"  Apply:  {result.ApplyMedianUs:F2}us (median), {result.ApplyP95Us:F2}us (p95)");
            Console.WriteLine($"  Status: {(ok ? "OK" : "FAIL")}\n");
        }

        return results;
    }

    static List<ComparisonResult> RunComparisonMeasurements(List<Mutation> mutations)
    {
        var results = new List<ComparisonResult>();
        int caseNum = 1;

        foreach (var mutation in mutations)
        {
            byte[] baseData = ReadAllBytesWithRetry(mutation.BaseFile);
            byte[] targetData = ReadAllBytesWithRetry(mutation.TargetFile);

            string caseId = $"{Path.GetFileNameWithoutExtension(mutation.BaseName)}:{mutation.ChangeKind.Substring(0, 3)}";
            Console.WriteLine($"Case {caseNum}: {caseId}");

            // Warmup both v1 and v2
            for (int i = 0; i < 3; i++)
            {
                byte[] deltaV1 = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
                IupdDeltaV1.ApplyDeltaV1(baseData, deltaV1, out _);

                byte[] deltaV2 = IupdDeltaV2Cdc.CreateDeltaV2(baseData, targetData);
                IupdDeltaV2Cdc.ApplyDeltaV2(baseData, deltaV2, out _);
            }

            // Measure V1 CreateDelta
            var v1CreateTimes = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                var sw = Stopwatch.StartNew();
                byte[] delta = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
                sw.Stop();
                v1CreateTimes.Add(sw.Elapsed.TotalMicroseconds);
            }

            // Measure V2 CreateDelta
            var v2CreateTimes = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                var sw = Stopwatch.StartNew();
                byte[] delta = IupdDeltaV2Cdc.CreateDeltaV2(baseData, targetData);
                sw.Stop();
                v2CreateTimes.Add(sw.Elapsed.TotalMicroseconds);
            }

            // Get final deltas for apply measurements
            byte[] finalDeltaV1 = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
            byte[] finalDeltaV2 = IupdDeltaV2Cdc.CreateDeltaV2(baseData, targetData);

            // Measure V1 ApplyDelta
            var v1ApplyTimes = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                var sw = Stopwatch.StartNew();
                byte[] appliedV1Temp = IupdDeltaV1.ApplyDeltaV1(baseData, finalDeltaV1, out _);
                sw.Stop();
                v1ApplyTimes.Add(sw.Elapsed.TotalMicroseconds);
            }

            // Measure V2 ApplyDelta
            var v2ApplyTimes = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                var sw = Stopwatch.StartNew();
                byte[] appliedV2Temp = IupdDeltaV2Cdc.ApplyDeltaV2(baseData, finalDeltaV2, out _);
                sw.Stop();
                v2ApplyTimes.Add(sw.Elapsed.TotalMicroseconds);
            }

            // Verify both
            byte[] appliedV1 = IupdDeltaV1.ApplyDeltaV1(baseData, finalDeltaV1, out var errorV1);
            byte[] appliedV2 = IupdDeltaV2Cdc.ApplyDeltaV2(baseData, finalDeltaV2, out var errorV2);

            bool v1Ok = errorV1.IsOk && appliedV1.SequenceEqual(targetData);
            bool v2Ok = errorV2.IsOk && appliedV2.SequenceEqual(targetData);

            var result = new ComparisonResult
            {
                CaseId = caseId,
                ChangeKind = mutation.ChangeKind,
                BaseBytes = baseData.Length,
                TargetBytes = targetData.Length,
                DeltaV1Bytes = finalDeltaV1.Length,
                DeltaV1Ratio = (double)finalDeltaV1.Length / targetData.Length,
                DeltaV2Bytes = finalDeltaV2.Length,
                DeltaV2Ratio = (double)finalDeltaV2.Length / targetData.Length,
                V1Ok = v1Ok,
                V2Ok = v2Ok,
                V1CreateMedianUs = Median(v1CreateTimes),
                V2CreateMedianUs = Median(v2CreateTimes),
                V1ApplyMedianUs = Median(v1ApplyTimes),
                V2ApplyMedianUs = Median(v2ApplyTimes)
            };

            results.Add(result);
            caseNum++;

            Console.WriteLine($"  V1 Delta: {result.DeltaV1Bytes} bytes ({result.DeltaV1Ratio:P1})");
            Console.WriteLine($"  V2 Delta: {result.DeltaV2Bytes} bytes ({result.DeltaV2Ratio:P1})");
            Console.WriteLine($"  V1 Create: {result.V1CreateMedianUs:F2}us, V2 Create: {result.V2CreateMedianUs:F2}us");
            Console.WriteLine($"  V1 Apply:  {result.V1ApplyMedianUs:F2}us, V2 Apply:  {result.V2ApplyMedianUs:F2}us");
            Console.WriteLine($"  V1 Status: {(v1Ok ? "OK" : "FAIL")}, V2 Status: {(v2Ok ? "OK" : "FAIL")}\n");
        }

        return results;
    }

    static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    static double Percentile95(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int idx = (int)Math.Ceiling(sorted.Count * 0.95) - 1;
        return sorted[Math.Max(0, Math.Min(idx, sorted.Count - 1))];
    }

    static void WriteCsv(string path, List<Result> results)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("caseId,baseName,changeKind,baseBytes,targetBytes,deltaBytes,deltaRatio,createMedianUs,createP95Us,applyMedianUs,applyP95Us,ok");

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var r in results)
        {
            writer.WriteLine(string.Format(culture, "{0},{1},{2},{3},{4},{5},{6:F6},{7:F2},{8:F2},{9:F2},{10:F2},{11}",
                r.CaseId, r.BaseName, r.ChangeKind, r.BaseBytes, r.TargetBytes, r.DeltaBytes, r.DeltaRatio,
                r.CreateMedianUs, r.CreateP95Us, r.ApplyMedianUs, r.ApplyP95Us, r.Ok));
        }
    }

    static void WriteCsvComparison(string path, List<ComparisonResult> results)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("caseId,changeKind,baseBytes,targetBytes,deltaV1Bytes,deltaV1Ratio,deltaV2Bytes,deltaV2Ratio,v1Ok,v2Ok,v1CreateMedianUs,v2CreateMedianUs,v1ApplyMedianUs,v2ApplyMedianUs");

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var r in results)
        {
            writer.WriteLine(string.Format(culture, "{0},{1},{2},{3},{4},{5:F6},{6},{7:F6},{8},{9},{10:F2},{11:F2},{12:F2},{13:F2}",
                r.CaseId, r.ChangeKind, r.BaseBytes, r.TargetBytes, r.DeltaV1Bytes, r.DeltaV1Ratio,
                r.DeltaV2Bytes, r.DeltaV2Ratio, r.V1Ok, r.V2Ok,
                r.V1CreateMedianUs, r.V2CreateMedianUs, r.V1ApplyMedianUs, r.V2ApplyMedianUs));
        }
    }

    static byte[] ReadAllBytesWithRetry(string path)
    {
        int maxRetries = 5;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: false))
                {
                    var buffer = new byte[stream.Length];
                    int bytesRead = 0;
                    while (bytesRead < stream.Length)
                    {
                        bytesRead += stream.Read(buffer, bytesRead, (int)stream.Length - bytesRead);
                    }
                    return buffer;
                }
            }
            catch (IOException) when (retry < maxRetries - 1)
            {
                System.Threading.Thread.Sleep(100 * (retry + 1));
            }
        }
        // Final attempt without retry
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: false))
        {
            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, (int)stream.Length);
            return buffer;
        }
    }
}
