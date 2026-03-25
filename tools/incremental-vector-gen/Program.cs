using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IronConfig.Crypto;
using IronConfig.Iupd;
using IronConfig.Iupd.Delta;

namespace IncrementalVectorGen
{
    /// <summary>
    /// EXEC_07 INCREMENTAL Vector Generator
    ///
    /// Generates deterministic full-package INCREMENTAL vectors for cross-runtime parity testing.
    /// Produces:
    /// - Success vectors: Valid INCREMENTAL packages with correct hashes
    /// - Refusal vectors: Intentionally malformed packages for rejection testing
    ///
    /// All data is seeded (42) for deterministic reproduction.
    /// </summary>
    class Program
    {
        private const int SEED = 42;
        private const string VECTORS_DIR = "incremental_vectors";

        static void Main(string[] args)
        {
            Console.WriteLine("=== EXEC_07 INCREMENTAL Vector Generator ===\n");

            // Create output directory
            Directory.CreateDirectory(VECTORS_DIR);

            Console.WriteLine("[PHASE 2] Generating deterministic INCREMENTAL package vectors...\n");

            var vectorMetadata = new List<VectorMetadata>();

            // SUCCESS VECTORS: Generate DELTA_V1 and IRONDEL2 packages with valid hashes

            // Simple case: 1KB base, 10% modification
            GenerateSuccessVector(
                vectorMetadata,
                vectorId: "success_01_delta_v1_simple",
                baseSize: 1024,
                changePercent: 0.10,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
                includeTargetHash: true
            );

            GenerateSuccessVector(
                vectorMetadata,
                vectorId: "success_02_irondel2_simple",
                baseSize: 1024,
                changePercent: 0.10,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_IRONDEL2,
                includeTargetHash: true
            );

            // Medium case: 10KB base, 5% modification
            GenerateSuccessVector(
                vectorMetadata,
                vectorId: "success_03_delta_v1_medium",
                baseSize: 10240,
                changePercent: 0.05,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
                includeTargetHash: true
            );

            GenerateSuccessVector(
                vectorMetadata,
                vectorId: "success_04_irondel2_medium",
                baseSize: 10240,
                changePercent: 0.05,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_IRONDEL2,
                includeTargetHash: true
            );

            // No target hash case
            GenerateSuccessVector(
                vectorMetadata,
                vectorId: "success_05_delta_v1_no_target",
                baseSize: 1024,
                changePercent: 0.10,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
                includeTargetHash: false
            );

            Console.WriteLine("\n[PHASE 3] Generating refusal vectors...\n");

            // REFUSAL VECTORS: Test rejection cases

            // Wrong base hash
            GenerateRefusalVector(
                vectorMetadata,
                vectorId: "refusal_01_wrong_base_hash",
                baseSize: 1024,
                changePercent: 0.10,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
                refusalType: RefusalType.WrongBaseHash
            );

            // Unknown algorithm
            GenerateRefusalVector(
                vectorMetadata,
                vectorId: "refusal_02_unknown_algorithm",
                baseSize: 1024,
                changePercent: 0.10,
                algorithmId: 0x99,  // Invalid algorithm
                refusalType: RefusalType.UnknownAlgorithm
            );

            // Corrupted CRC32
            GenerateRefusalVector(
                vectorMetadata,
                vectorId: "refusal_03_corrupted_crc32",
                baseSize: 1024,
                changePercent: 0.10,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
                refusalType: RefusalType.CorruptedCrc32
            );

            // Target hash mismatch
            GenerateRefusalVector(
                vectorMetadata,
                vectorId: "refusal_04_target_hash_mismatch",
                baseSize: 1024,
                changePercent: 0.10,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
                refusalType: RefusalType.TargetHashMismatch
            );

            // Missing metadata (created as OPTIMIZED profile instead)
            GenerateRefusalVector(
                vectorMetadata,
                vectorId: "refusal_05_missing_metadata",
                baseSize: 1024,
                changePercent: 0.10,
                algorithmId: IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
                refusalType: RefusalType.MissingMetadata
            );

            // Write metadata manifest
            var manifestPath = Path.Combine(VECTORS_DIR, "manifest.json");
            var json = JsonSerializer.Serialize(vectorMetadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);

            Console.WriteLine($"\n✅ Generated {vectorMetadata.Count} vectors");
            Console.WriteLine($"📁 Output: {VECTORS_DIR}/");
            Console.WriteLine($"📋 Manifest: {manifestPath}");
        }

        static void GenerateSuccessVector(
            List<VectorMetadata> manifest,
            string vectorId,
            int baseSize,
            double changePercent,
            byte algorithmId,
            bool includeTargetHash)
        {
            Console.WriteLine($"  [{vectorId}]");

            // Generate deterministic base image
            var baseImage = GenerateDeterministicImage(SEED, baseSize);
            var baseHash = Blake3.Hasher.Hash(baseImage);

            // Generate deterministic target image
            var targetImage = ApplyDeterministicChanges(baseImage, changePercent, SEED);
            var targetHash = Blake3.Hasher.Hash(targetImage);

            // Create delta
            byte[] deltaPayload;
            switch (algorithmId)
            {
                case IupdIncrementalMetadata.ALGORITHM_DELTA_V1:
                    deltaPayload = IupdDeltaV1.CreateDeltaV1(baseImage, targetImage);
                    break;

                case IupdIncrementalMetadata.ALGORITHM_IRONDEL2:
                    deltaPayload = IupdDeltaV2Cdc.CreateDeltaV2(baseImage, targetImage);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown algorithm: 0x{algorithmId:X2}");
            }

            // Create IUPD package using fluent builder
            var packageBytes = new IupdBuilder()
                .WithProfile(IupdProfile.INCREMENTAL)
                .AddChunk(0, deltaPayload)
                .WithApplyOrder(0)
                .WithIncrementalMetadata(
                    algorithmId,
                    baseHash.AsSpan().ToArray(),
                    includeTargetHash ? targetHash.AsSpan().ToArray() : null
                )
                .Build();

            // Save vector files
            var vectorDir = Path.Combine(VECTORS_DIR, vectorId);
            Directory.CreateDirectory(vectorDir);

            File.WriteAllBytes(Path.Combine(vectorDir, "base.bin"), baseImage);
            File.WriteAllBytes(Path.Combine(vectorDir, "target.bin"), targetImage);
            File.WriteAllBytes(Path.Combine(vectorDir, "package.iupd"), packageBytes);

            // Add to manifest
            manifest.Add(new VectorMetadata
            {
                VectorId = vectorId,
                Type = "success",
                Algorithm = algorithmId == IupdIncrementalMetadata.ALGORITHM_DELTA_V1 ? "DELTA_V1" : "IRONDEL2",
                BaseSize = baseImage.Length,
                TargetSize = targetImage.Length,
                PackageSize = packageBytes.Length,
                DeltaSize = deltaPayload.Length,
                DeltaRatio = (double)deltaPayload.Length / baseImage.Length,
                BaseHash = Convert.ToHexString(baseHash.AsSpan()),
                TargetHash = Convert.ToHexString(targetHash.AsSpan()),
                IncludeTargetHash = includeTargetHash
            });

            Console.WriteLine($"    Base: {baseImage.Length} → Target: {targetImage.Length} (delta: {deltaPayload.Length} = {(double)deltaPayload.Length / baseImage.Length:P1})");
        }

        static void GenerateRefusalVector(
            List<VectorMetadata> manifest,
            string vectorId,
            int baseSize,
            double changePercent,
            byte algorithmId,
            RefusalType refusalType)
        {
            Console.WriteLine($"  [{vectorId}]");

            var baseImage = GenerateDeterministicImage(SEED, baseSize);
            var baseHash = Blake3.Hasher.Hash(baseImage);
            var targetImage = ApplyDeterministicChanges(baseImage, changePercent, SEED);
            var targetHash = Blake3.Hasher.Hash(targetImage);

            // Create delta
            byte[] deltaPayload;
            if (refusalType != RefusalType.MissingMetadata)
            {
                switch (algorithmId)
                {
                    case IupdIncrementalMetadata.ALGORITHM_DELTA_V1:
                        deltaPayload = IupdDeltaV1.CreateDeltaV1(baseImage, targetImage);
                        break;

                    case IupdIncrementalMetadata.ALGORITHM_IRONDEL2:
                        deltaPayload = IupdDeltaV2Cdc.CreateDeltaV2(baseImage, targetImage);
                        break;

                    default:
                        // For unknown algorithm, just use a dummy delta
                        deltaPayload = new byte[100];
                        new Random(SEED).NextBytes(deltaPayload);
                        break;
                }
            }
            else
            {
                // For missing metadata, create with DELTA_V1
                deltaPayload = IupdDeltaV1.CreateDeltaV1(baseImage, targetImage);
            }

            byte[] packageBytes;

            if (refusalType == RefusalType.MissingMetadata)
            {
                // Use OPTIMIZED profile instead of INCREMENTAL (no metadata trailer)
                packageBytes = new IupdBuilder()
                    .WithProfile(IupdProfile.OPTIMIZED)
                    .AddChunk(0, deltaPayload)
                    .WithApplyOrder(0)
                    .Build();
            }
            else
            {
                // Create base IUPD package with INCREMENTAL profile
                var builder = new IupdBuilder()
                    .WithProfile(IupdProfile.INCREMENTAL)
                    .AddChunk(0, deltaPayload)
                    .WithApplyOrder(0);

                // Apply refusal modifications to metadata
                byte[] metadataBaseHash = baseHash.AsSpan().ToArray();
                byte[] metadataTargetHash = targetHash.AsSpan().ToArray();
                byte metadataAlgorithmId = algorithmId;

                switch (refusalType)
                {
                    case RefusalType.WrongBaseHash:
                        // Flip first byte of base hash
                        metadataBaseHash[0] ^= 0xFF;
                        break;

                    case RefusalType.TargetHashMismatch:
                        // Flip first byte of target hash
                        metadataTargetHash[0] ^= 0xFF;
                        break;

                    case RefusalType.UnknownAlgorithm:
                        // Use provided unknown algorithm ID
                        metadataAlgorithmId = algorithmId;
                        break;

                    case RefusalType.CorruptedCrc32:
                        // We'll corrupt the CRC32 after serialization
                        break;
                }

                builder.WithIncrementalMetadata(metadataAlgorithmId, metadataBaseHash, metadataTargetHash);
                packageBytes = builder.Build();

                // For corrupted CRC32, we need to manually modify the trailer
                if (refusalType == RefusalType.CorruptedCrc32)
                {
                    packageBytes = CorruptTrailerCrc32(packageBytes);
                }
            }

            // Save vector files
            var vectorDir = Path.Combine(VECTORS_DIR, vectorId);
            Directory.CreateDirectory(vectorDir);

            File.WriteAllBytes(Path.Combine(vectorDir, "base.bin"), baseImage);
            File.WriteAllBytes(Path.Combine(vectorDir, "target.bin"), targetImage);
            File.WriteAllBytes(Path.Combine(vectorDir, "package.iupd"), packageBytes);

            // Add to manifest
            manifest.Add(new VectorMetadata
            {
                VectorId = vectorId,
                Type = "refusal",
                RefusalReason = refusalType.ToString(),
                Algorithm = algorithmId == IupdIncrementalMetadata.ALGORITHM_DELTA_V1 ? "DELTA_V1" :
                           algorithmId == IupdIncrementalMetadata.ALGORITHM_IRONDEL2 ? "IRONDEL2" : "UNKNOWN",
                BaseSize = baseImage.Length,
                TargetSize = targetImage.Length,
                PackageSize = packageBytes.Length,
                DeltaSize = deltaPayload.Length,
                DeltaRatio = (double)deltaPayload.Length / baseImage.Length,
                BaseHash = Convert.ToHexString(baseHash.AsSpan()),
                TargetHash = Convert.ToHexString(targetHash.AsSpan()),
                IncludeTargetHash = true
            });

            Console.WriteLine($"    Refusal type: {refusalType}");
        }

        static byte[] GenerateDeterministicImage(int seed, int size)
        {
            var rng = new Random(seed);
            var image = new byte[size];
            rng.NextBytes(image);
            return image;
        }

        static byte[] ApplyDeterministicChanges(byte[] baseImage, double changePercent, int seed)
        {
            var modified = (byte[])baseImage.Clone();
            var rng = new Random(seed ^ -1431655766);  // Different seed for modifications (0xAAAAAAAA as signed int)

            int changeCount = (int)(modified.Length * changePercent);
            for (int i = 0; i < changeCount; i++)
            {
                int pos = rng.Next(modified.Length);
                modified[pos] = (byte)rng.Next(256);
            }

            return modified;
        }

        static byte[] CorruptTrailerCrc32(byte[] package)
        {
            // Find INCREMENTAL trailer magic "IUPDINC1"
            // Metadata trailer is at the end of the file, search backward from EOF
            var magic = System.Text.Encoding.ASCII.GetBytes("IUPDINC1");
            int trailerPos = -1;

            // Search from the end of file backward, ensuring we check at least the last position
            // where an 8-byte magic string could start (at package.Length - 8)
            int searchStart = package.Length - 8;
            if (searchStart < 0) searchStart = 0;

            for (int i = searchStart; i >= 0; i--)
            {
                // Ensure we have at least 8 bytes to match the magic
                if (i + 8 > package.Length)
                    continue;

                bool found = true;
                for (int j = 0; j < 8; j++)
                {
                    if (package[i + j] != magic[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    trailerPos = i;
                    break;  // Found it, no need to continue searching
                }
            }

            if (trailerPos < 0)
                throw new InvalidOperationException("INCREMENTAL trailer not found");

            // Trailer layout: magic(8) + length(4) + ... + crc32(4)
            // CRC32 is at the end of the trailer
            uint declaredLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                package.AsSpan(trailerPos + 8, 4));
            int crc32Offset = trailerPos + checked((int)declaredLength) - 4;

            // Corrupt the CRC32 bytes
            package[crc32Offset] ^= 0xFF;
            package[crc32Offset + 1] ^= 0xFF;
            package[crc32Offset + 2] ^= 0xFF;
            package[crc32Offset + 3] ^= 0xFF;

            return package;
        }
    }

    enum RefusalType
    {
        WrongBaseHash,
        UnknownAlgorithm,
        CorruptedCrc32,
        TargetHashMismatch,
        MissingMetadata
    }

    class VectorMetadata
    {
        public string VectorId { get; set; }
        public string Type { get; set; }  // "success" or "refusal"
        public string Algorithm { get; set; }
        public string RefusalReason { get; set; }
        public int BaseSize { get; set; }
        public int TargetSize { get; set; }
        public int PackageSize { get; set; }
        public int DeltaSize { get; set; }
        public double DeltaRatio { get; set; }
        public string BaseHash { get; set; }
        public string TargetHash { get; set; }
        public bool IncludeTargetHash { get; set; }
    }
}
