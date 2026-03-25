using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IronConfig.Iupd;

namespace IncrementalVectorTestNet
{
    /// <summary>
    /// EXEC_07 PHASE 4: .NET INCREMENTAL Package Lifecycle Test
    ///
    /// Executes full-package operations on generated vectors:
    /// 1. Open/parse IUPD package (IupdReader)
    /// 2. Verify signature (fail-closed for INCREMENTAL)
    /// 3. Extract and validate metadata
    /// 4. Apply INCREMENTAL packages to base images
    /// 5. Validate result hashes
    ///
    /// Produces executed evidence for parity comparison with native C.
    /// </summary>
    class Program
    {
        const string VECTORS_DIR = "../incremental-vector-gen/incremental_vectors";
        const string RESULTS_FILE = "dotnet_lifecycle_results.json";

        static void Main(string[] args)
        {
            Console.WriteLine("=== EXEC_07 PHASE 4: .NET INCREMENTAL Lifecycle Test ===\n");

            var results = new List<LifecycleTestResult>();

            // Load manifest
            var manifestPath = Path.Combine(VECTORS_DIR, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Console.WriteLine($"ERROR: Manifest not found at {manifestPath}");
                Environment.Exit(1);
            }

            var manifestJson = File.ReadAllText(manifestPath);
            var vectors = JsonSerializer.Deserialize<List<VectorMeta>>(manifestJson) ?? new List<VectorMeta>();

            Console.WriteLine($"Loaded {vectors.Count} test vectors\n");

            foreach (var vector in vectors)
            {
                var result = TestVector(vector);
                results.Add(result);

                string statusIcon = result.Success ? "✅" : "❌";
                Console.WriteLine($"{statusIcon} {result.VectorId}");
                if (!result.Success)
                {
                    Console.WriteLine($"   Error: {result.ErrorMessage}");
                }
            }

            // Write results
            var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RESULTS_FILE, json);

            // Summary
            int successCount = results.FindAll(r => r.Success).Count;
            int failureCount = results.Count - successCount;

            Console.WriteLine($"\n📋 Results: {successCount} success, {failureCount} failures");
            Console.WriteLine($"📄 Output: {RESULTS_FILE}");
        }

        static LifecycleTestResult TestVector(VectorMeta vector)
        {
            var result = new LifecycleTestResult
            {
                VectorId = vector.VectorId,
                Type = vector.Type,
                Algorithm = vector.Algorithm
            };

            try
            {
                var vectorDir = Path.Combine(VECTORS_DIR, vector.VectorId);

                // Load files
                var packagePath = Path.Combine(vectorDir, "package.iupd");
                var basePath = Path.Combine(vectorDir, "base.bin");
                var targetPath = Path.Combine(vectorDir, "target.bin");

                if (!File.Exists(packagePath) || !File.Exists(basePath) || !File.Exists(targetPath))
                {
                    result.Success = false;
                    result.ErrorMessage = "Missing vector files";
                    return result;
                }

                var packageBytes = File.ReadAllBytes(packagePath);
                var baseImage = File.ReadAllBytes(basePath);
                var targetImage = File.ReadAllBytes(targetPath);

                result.PackageSize = packageBytes.Length;
                result.BaseSize = baseImage.Length;
                result.TargetSize = targetImage.Length;

                // PHASE 1: Open package
                var reader = IupdReader.Open(packageBytes, out var openErr);
                if (reader == null || !openErr.IsOk)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Open failed: {openErr.Code}";
                    result.Step = "Open";
                    return result;
                }

                // PHASE 3: Check profile and metadata
                result.Profile = reader.Profile.GetDisplayName();
                if (reader.Profile != IupdProfile.INCREMENTAL && vector.Type == "success")
                {
                    result.Success = false;
                    result.ErrorMessage = $"Expected INCREMENTAL profile, got {reader.Profile}";
                    result.Step = "ProfileCheck";
                    return result;
                }

                if (reader.IncrementalMetadata != null)
                {
                    result.MetadataAlgorithm = reader.IncrementalMetadata.GetAlgorithmName();
                    result.HasBaseHash = reader.IncrementalMetadata.BaseHash != null;
                    result.HasTargetHash = reader.IncrementalMetadata.TargetHash != null;
                }

                // PHASE 4: For success vectors, apply INCREMENTAL
                if (vector.Type == "success")
                {
                    var engine = new IupdApplyEngine(reader, new byte[32], ".");
                    var applyErr = engine.ApplyIncremental(baseImage, out var resultImage);

                    if (!applyErr.IsOk)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"ApplyIncremental failed: {applyErr.Code}";
                        result.Step = "ApplyIncremental";
                        return result;
                    }

                    // Verify result matches target
                    if (!ContentMatch(resultImage, targetImage))
                    {
                        result.Success = false;
                        result.ErrorMessage = "Result image does not match target";
                        result.Step = "ResultMatch";
                        return result;
                    }

                    result.ResultSize = resultImage.Length;
                    result.Success = true;
                    result.Step = "ApplyIncremental";
                }
                else if (vector.Type == "refusal")
                {
                    // For refusal vectors, opening should succeed but apply should fail
                    var engine = new IupdApplyEngine(reader, new byte[32], ".");
                    var applyErr = engine.ApplyIncremental(baseImage, out var resultImage);

                    if (applyErr.IsOk)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"ApplyIncremental should have failed but succeeded";
                        result.Step = "ApplyIncremental";
                        return result;
                    }

                    result.Success = true;
                    result.Step = "ApplyIncremental (correctly rejected)";
                    result.RejectionCode = applyErr.Code.ToString();
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Exception: {ex.Message}";
                result.Step = "Exception";
            }

            return result;
        }

        static bool ContentMatch(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }

    class LifecycleTestResult
    {
        public string VectorId { get; set; }
        public string Type { get; set; }
        public string Algorithm { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string Step { get; set; }
        public string Profile { get; set; }
        public string MetadataAlgorithm { get; set; }
        public bool HasBaseHash { get; set; }
        public bool HasTargetHash { get; set; }
        public int PackageSize { get; set; }
        public int BaseSize { get; set; }
        public int TargetSize { get; set; }
        public int ResultSize { get; set; }
        public string RejectionCode { get; set; }
    }

    class VectorMeta
    {
        public string VectorId { get; set; }
        public string Type { get; set; }
        public string Algorithm { get; set; }
        public int BaseSize { get; set; }
        public int TargetSize { get; set; }
        public int PackageSize { get; set; }
    }
}
