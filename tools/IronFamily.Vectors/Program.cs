using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronConfig;
using IronConfig.Iupd;
using IronConfig.Iupd.Delta;
using IronConfig.Crypto;

namespace IronFamily.Vectors;

/// <summary>
/// Minimal IUPD file structure parser (tool-only, no engine modifications)
/// Extracts offsets for computed mutation without magic constants
/// </summary>
class IupdStructureHelper
{
    public ulong ChunkTableOffset { get; private set; }
    public ulong ManifestOffset { get; private set; }
    public ulong PayloadOffset { get; private set; }
    public ulong ManifestSize { get; private set; }

    private const uint IUPD_MAGIC = 0x44505549;  // "IUPD"
    private const byte VERSION_V2 = 0x02;

    public static bool TryParse(byte[] data, out IupdStructureHelper? helper)
    {
        helper = null;

        // Minimum size for header parsing
        if (data.Length < 37)
            return false;

        // Check magic (little-endian at offset 0)
        uint magic = BitConverter.ToUInt32(data, 0);
        if (magic != IUPD_MAGIC)
            return false;

        // Check version (offset 4)
        byte version = data[4];
        if (version != VERSION_V2)
            return false;

        // Parse V2 header offsets (from IupdReader.cs)
        // V2 format (37 bytes):
        // [0-3] Magic
        // [4] Version
        // [5] Profile
        // [6-9] Flags
        // [10-11] Header size
        // [12] Reserved
        // [13-20] Chunk table offset
        // [21-28] Manifest offset
        // [29-36] Payload offset
        ulong chunkTableOffset = BitConverter.ToUInt64(data, 13);
        ulong manifestOffset = BitConverter.ToUInt64(data, 21);
        ulong payloadOffset = BitConverter.ToUInt64(data, 29);

        // Sanity checks
        if (chunkTableOffset < 37 || manifestOffset < chunkTableOffset || payloadOffset < manifestOffset)
            return false;

        if (manifestOffset + 24 > (ulong)data.Length)  // Min manifest header size is 24 bytes
            return false;

        // Parse manifest header to get manifest size
        // Manifest format: [header fields...][size at offset 16][crc32][reserved]
        ulong manifestSize = BitConverter.ToUInt64(data, (int)manifestOffset + 16);

        // Verify manifest fits in file
        if (manifestOffset + manifestSize > (ulong)data.Length)
            return false;

        helper = new IupdStructureHelper
        {
            ChunkTableOffset = chunkTableOffset,
            ManifestOffset = manifestOffset,
            PayloadOffset = payloadOffset,
            ManifestSize = manifestSize
        };

        return true;
    }
}

class Program
{
    static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            var outputDir = options.OutputDir;

            // Handle --dump-layout mode
            if (options.DumpLayout)
            {
                Console.WriteLine($"📋 Dumping IUPD v2 vector layout information...");
                DumpVectorLayout(outputDir);
                return 0;
            }

            Console.WriteLine($"🔄 Generating golden vectors...");
            Console.WriteLine($"   Output: {outputDir}");

            // Create output directories
            Directory.CreateDirectory(Path.Combine(outputDir, "iupd", "v2"));
            Directory.CreateDirectory(Path.Combine(outputDir, "diff", "v1"));
            Directory.CreateDirectory(Path.Combine(outputDir, "delta2", "v1"));

            // Generate IUPD v2 vectors
            Console.WriteLine($"\n📦 Generating IUPD v2 vectors...");
            GenerateIupdVectors(outputDir);

            // Generate DiffEngine vectors
            Console.WriteLine($"\n📦 Generating DiffEngine vectors...");
            GenerateDiffVectors(outputDir);

            // Generate Delta v2 vectors
            Console.WriteLine($"\n📦 Generating Delta v2 (IRONDEL2) vectors...");
            GenerateDeltaV2Vectors(outputDir);

            // Generate index
            Console.WriteLine($"\n📋 Generating vectors.json index...");
            GenerateVectorsIndex(outputDir);

            Console.WriteLine($"\n✅ All vectors generated successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static Options ParseArgs(string[] args)
    {
        var options = new Options
        {
            OutputDir = Path.Combine(AppContext.BaseDirectory, "artifacts", "vectors", "v1"),
            Force = false,
            DumpLayout = false
        };

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length)
                options.OutputDir = args[i + 1];
            else if (args[i] == "--force")
                options.Force = true;
            else if (args[i] == "--dump-layout")
                options.DumpLayout = true;
        }

        if (options.Force && Directory.Exists(options.OutputDir))
        {
            Directory.Delete(options.OutputDir, true);
        }

        return options;
    }

    static void DumpVectorLayout(string outputDir)
    {
        var iupdDir = Path.Combine(outputDir, "iupd", "v2");

        var vectorNames = new[] {
            "secure_ok_01",
            "secure_bad_sig_01",
            "secure_bad_seq_01",
            "secure_dos_manifest_01",
            "secure_dos_chunks_01",
            "secure_dos_chunk_size_01"
        };

        var outputFile = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "_dev", "exec_03e", "dotnet_layout_dump.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

        using (var writer = new StreamWriter(outputFile))
        {
            writer.WriteLine("=== IUPD v2 Vector Layout Dump (from .NET) ===");
            writer.WriteLine();

            foreach (var name in vectorNames)
            {
                var filePath = Path.Combine(iupdDir, name + ".iupd");
                writer.WriteLine($"[{name}]");
                writer.WriteLine($"  File: {filePath}");

                if (!File.Exists(filePath))
                {
                    writer.WriteLine($"  ERROR: File not found");
                    writer.WriteLine();
                    continue;
                }

                try
                {
                    byte[] data = File.ReadAllBytes(filePath);
                    writer.WriteLine($"  File size: {data.Length} bytes");

                    // Parse header
                    if (data.Length < 37)
                    {
                        writer.WriteLine($"  ERROR: File too small for header");
                        writer.WriteLine();
                        continue;
                    }

                    byte version = data[4];
                    byte profile = data[5];
                    ulong chunkTableOffset = BitConverter.ToUInt64(data, 13);
                    ulong manifestOffset = BitConverter.ToUInt64(data, 21);
                    ulong payloadOffset = BitConverter.ToUInt64(data, 29);

                    writer.WriteLine($"  Version: 0x{version:X2}");
                    writer.WriteLine($"  Profile: 0x{profile:X2}");
                    writer.WriteLine($"  chunk_table_offset: {chunkTableOffset}");
                    writer.WriteLine($"  manifest_offset: {manifestOffset}");
                    writer.WriteLine($"  payload_offset: {payloadOffset}");

                    // Parse manifest header if offset is valid
                    if (manifestOffset + 24 <= (ulong)data.Length)
                    {
                        ulong manifestSize = BitConverter.ToUInt64(data, (int)manifestOffset + 16);
                        uint chunk_count_raw = BitConverter.ToUInt32(data, (int)manifestOffset);

                        writer.WriteLine($"  manifest_size_declared: {manifestSize}");
                        writer.WriteLine($"  chunk_count_raw_at_manifest[0]: {chunk_count_raw}");

                        // Calculate signature offset (after manifest)
                        ulong sigFooterOffset = manifestOffset + manifestSize + 4;  // +4 for sig_len field
                        writer.WriteLine($"  signature_offset (after manifest): {sigFooterOffset}");

                        // Calculate trailer offset (before payload)
                        if (payloadOffset >= 21)
                        {
                            ulong trailerOffset = payloadOffset - 21;
                            writer.WriteLine($"  trailer_offset (payload-21): {trailerOffset}");

                            // Try to read trailer
                            if (trailerOffset + 21 <= (ulong)data.Length)
                            {
                                byte trailerVersion = data[(int)trailerOffset + 12];
                                ulong sequence = BitConverter.ToUInt64(data, (int)trailerOffset + 13);
                                writer.WriteLine($"  trailer_version: {trailerVersion}");
                                writer.WriteLine($"  trailer_sequence: {sequence}");
                            }
                        }

                        // Parse chunk table entry if valid
                        uint chunkCount = (uint)((manifestOffset - chunkTableOffset) / 56);
                        writer.WriteLine($"  chunk_count_calculated: {chunkCount}");

                        if (chunkTableOffset + 56 <= (ulong)data.Length && chunkCount > 0)
                        {
                            // First chunk entry: [index:8][crc32:4][reserved:4][uncompressed_size:8][compressed_size:8][flags:8][codec:2][reserved:6]
                            // uncompressed_size is at offset 16 (8+4+4=16)
                            ulong uncompSize = BitConverter.ToUInt64(data, (int)chunkTableOffset + 16);
                            writer.WriteLine($"  chunk[0].uncompressed_size: {uncompSize}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"  ERROR: {ex.Message}");
                }

                writer.WriteLine();
            }
        }

        Console.WriteLine($"✅ Layout dump written to: {outputFile}");
    }

    static void GenerateIupdVectors(string outputDir)
    {
        var iupdDir = Path.Combine(outputDir, "iupd", "v2");
        var selfCheckResults = new List<(string name, bool passed, string reason)>();

        // Case 1: Valid IUPD with SECURE profile, valid signature and UpdateSequence
        GenerateSecureOk(iupdDir);
        selfCheckResults.Add(VerifyVector(Path.Combine(iupdDir, "secure_ok_01.iupd"), shouldPass: true));

        // Case 2: Bad signature (flip 1 byte in signature region after writing)
        GenerateSecureBadSig(iupdDir);
        selfCheckResults.Add(VerifyVector(Path.Combine(iupdDir, "secure_bad_sig_01.iupd"), shouldPass: false, expectedFailReason: "signature"));

        // Case 3: Bad UpdateSequence (corrupt sequence value)
        GenerateSecureBadSeq(iupdDir);
        selfCheckResults.Add(VerifyVector(Path.Combine(iupdDir, "secure_bad_seq_01.iupd"), shouldPass: false, expectedFailReason: "sequence"));

        // Case 4: DoS - Manifest size exceeds limit
        GenerateSecureDosManifest(iupdDir);
        selfCheckResults.Add(VerifyVector(Path.Combine(iupdDir, "secure_dos_manifest_01.iupd"), shouldPass: false, expectedFailReason: "dos"));

        // Case 5: DoS - Chunk count exceeds limit
        GenerateSecureDosChunks(iupdDir);
        selfCheckResults.Add(VerifyVector(Path.Combine(iupdDir, "secure_dos_chunks_01.iupd"), shouldPass: false, expectedFailReason: "dos"));

        // Case 6: DoS - Chunk size exceeds limit
        GenerateSecureDosChunkSize(iupdDir);
        selfCheckResults.Add(VerifyVector(Path.Combine(iupdDir, "secure_dos_chunk_size_01.iupd"), shouldPass: false, expectedFailReason: "dos"));

        // Write self-check results
        WriteSelfCheckResults(selfCheckResults);

        // Verify all checks passed
        bool allPassed = selfCheckResults.All(r => r.passed);
        if (!allPassed)
        {
            Console.Error.WriteLine("❌ Self-check failures detected:");
            foreach (var (name, passed, reason) in selfCheckResults.Where(r => !r.passed))
            {
                Console.Error.WriteLine($"   {name}: {reason}");
            }
            throw new Exception("Vector self-checks failed");
        }

        Console.WriteLine("✅ All vector self-checks passed");
    }

    static void GenerateSecureOk(string iupdDir)
    {
        Console.WriteLine($"  - secure_ok_01.iupd");

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);

        // Add a small chunk with deterministic data
        byte[] payload = GenerateDeterministicPayload(4096);
        writer.AddChunk(0, payload);

        // Set apply order
        writer.SetApplyOrder(0);

        // Add UpdateSequence for anti-replay
        writer.WithUpdateSequence(1);

        byte[] iupdData = writer.Build();
        File.WriteAllBytes(Path.Combine(iupdDir, "secure_ok_01.iupd"), iupdData);

        // Verify it's valid
        var reader = IupdReader.Open(iupdData, out var error);
        if (!error.IsOk)
            throw new Exception($"Failed to create valid IUPD: {error}");

        error = reader.ValidateStrict();
        if (!error.IsOk)
            throw new Exception($"ValidateStrict failed on secure_ok_01: {error}");
    }

    static void GenerateSecureBadSig(string iupdDir)
    {
        Console.WriteLine($"  - secure_bad_sig_01.iupd");

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);

        byte[] payload = GenerateDeterministicPayload(4096);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        byte[] iupdData = writer.Build();

        // Corrupt signature by flipping 1 byte in signature region
        // Signature is located at: manifest_offset + manifest_size
        // Signature format: [sig_len:4][signature:64][witness:32]
        if (IupdStructureHelper.TryParse(iupdData, out var structure))
        {
            ulong signatureOffset = structure.ManifestOffset + structure.ManifestSize + 4;  // Skip sig_len field
            if (signatureOffset + 63 < (ulong)iupdData.Length)
            {
                // Flip first byte of signature
                iupdData[(int)signatureOffset] ^= 0xFF;
            }
        }

        File.WriteAllBytes(Path.Combine(iupdDir, "secure_bad_sig_01.iupd"), iupdData);
    }

    static void GenerateSecureBadSeq(string iupdDir)
    {
        Console.WriteLine($"  - secure_bad_seq_01.iupd");

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);

        byte[] payload = GenerateDeterministicPayload(4096);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        byte[] iupdData = writer.Build();

        // Corrupt UpdateSequence trailer located at: payload_offset - 21
        // Trailer format: [IUPDSEQ1:8][length:4][version:1][sequence:8]
        if (IupdStructureHelper.TryParse(iupdData, out var structure))
        {
            // Trailer is 21 bytes before payload_offset
            if (structure.PayloadOffset >= 21)
            {
                ulong trailerOffset = structure.PayloadOffset - 21;

                // Corrupt trailer version byte to make it invalid
                // Version byte is at trailer_offset + 12
                if (trailerOffset + 13 <= (ulong)iupdData.Length)
                {
                    iupdData[(int)(trailerOffset + 12)] = 0xFF;  // Invalid version makes trailer unparseable
                }
            }
        }

        File.WriteAllBytes(Path.Combine(iupdDir, "secure_bad_seq_01.iupd"), iupdData);
    }

    static void GenerateSecureDosManifest(string iupdDir)
    {
        Console.WriteLine($"  - secure_dos_manifest_01.iupd");

        // Create a minimal valid IUPD that declares a huge manifest size
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);

        byte[] payload = GenerateDeterministicPayload(100);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        byte[] iupdData = writer.Build();

        // Corrupt manifest size field to declare > 100MB (triggers DoS gate)
        // Manifest size field is at: manifest_offset + 16
        if (IupdStructureHelper.TryParse(iupdData, out var structure))
        {
            ulong manifestSizeFieldOffset = structure.ManifestOffset + 16;
            if (manifestSizeFieldOffset + 8 <= (ulong)iupdData.Length)
            {
                // Set manifest length to 100MB + 1 to exceed limit
                var hugeSizeBytes = BitConverter.GetBytes(100_000_001UL);
                Array.Copy(hugeSizeBytes, 0, iupdData, (int)manifestSizeFieldOffset, 8);
            }
        }

        File.WriteAllBytes(Path.Combine(iupdDir, "secure_dos_manifest_01.iupd"), iupdData);
    }

    static void GenerateSecureDosChunks(string iupdDir)
    {
        Console.WriteLine($"  - secure_dos_chunks_01.iupd");

        // Create IUPD then corrupt header to declare huge chunk count
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);

        byte[] payload = GenerateDeterministicPayload(100);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        byte[] iupdData = writer.Build();

        // Corrupt header to declare > 1,000,000 chunks
        // Chunk count is calculated as: (manifestOffset - chunkTableOffset) / 56
        // Set manifestOffset to create huge implied chunk count
        if (iupdData.Length >= 32)
        {
            // Keep chunkTableOffset = 37
            var chunkTableBytes = BitConverter.GetBytes(37UL);
            Array.Copy(chunkTableBytes, 0, iupdData, 13, 8);

            // Set manifestOffset = 37 + (1,000,001 * 56) = 56,000,093
            var manifestOffsetBytes = BitConverter.GetBytes(56_000_093UL);
            Array.Copy(manifestOffsetBytes, 0, iupdData, 21, 8);
        }

        File.WriteAllBytes(Path.Combine(iupdDir, "secure_dos_chunks_01.iupd"), iupdData);
    }

    static void GenerateSecureDosChunkSize(string iupdDir)
    {
        Console.WriteLine($"  - secure_dos_chunk_size_01.iupd");

        // Create IUPD then corrupt chunk entry to declare huge chunk size
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);

        byte[] payload = GenerateDeterministicPayload(100);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        byte[] iupdData = writer.Build();

        // Corrupt first chunk entry to declare > 1GB uncompressed size
        // Chunk table starts at: chunkTableOffset (parsed from header)
        // First chunk entry is at: chunkTableOffset + 0
        // uncompressed_size field is at entry offset 8 (8 bytes into entry)
        if (IupdStructureHelper.TryParse(iupdData, out var structure))
        {
            ulong uncompSizeOffset = structure.ChunkTableOffset + 8;
            if (uncompSizeOffset + 8 <= (ulong)iupdData.Length)
            {
                // Set uncompressed size to 1GB + 1 to exceed limit
                var hugeSizeBytes = BitConverter.GetBytes((1UL << 30) + 1);
                Array.Copy(hugeSizeBytes, 0, iupdData, (int)uncompSizeOffset, 8);
            }
        }

        File.WriteAllBytes(Path.Combine(iupdDir, "secure_dos_chunk_size_01.iupd"), iupdData);
    }

    static void GenerateDiffVectors(string outputDir)
    {
        var diffDir = Path.Combine(outputDir, "diff", "v1");

        Console.WriteLine($"  - case_01 (base, patch, out)");

        // Create deterministic base file (512 KB)
        byte[] baseData = GenerateDeterministicPayload(512 * 1024);

        // Create target with sparse modifications
        byte[] targetData = (byte[])baseData.Clone();
        targetData[1000] ^= 0xFF;
        targetData[50000] ^= 0xFF;
        targetData[250000] ^= 0xFF;

        // Generate patch
        byte[] patchData = IupdDeltaV1.CreateDeltaV1(baseData, targetData);

        // Verify patch applies correctly
        byte[] outData = IupdDeltaV1.ApplyDeltaV1(baseData, patchData, out var deltaError);
        if (!deltaError.IsOk)
            throw new Exception($"Delta apply failed: {deltaError}");

        if (!BytesEqual(outData, targetData))
            throw new Exception("Patch verification failed: output != target");

        // Write files
        File.WriteAllBytes(Path.Combine(diffDir, "case_01.base.bin"), baseData);
        File.WriteAllBytes(Path.Combine(diffDir, "case_01.patch.bin"), patchData);
        File.WriteAllBytes(Path.Combine(diffDir, "case_01.out.bin"), outData);

        Console.WriteLine($"    base={baseData.Length:N0} bytes, patch={patchData.Length:N0} bytes, out={outData.Length:N0} bytes");
    }

    static void GenerateDeltaV2Vectors(string outputDir)
    {
        var delta2Dir = Path.Combine(outputDir, "delta2", "v1");

        Console.WriteLine($"  - case_01 (base, patch2, out)");

        // Create deterministic base file (512 KB)
        byte[] baseData = GenerateDeterministicPayload(512 * 1024);

        // Create target with sparse modifications (same as v1 for consistency)
        byte[] targetData = (byte[])baseData.Clone();
        targetData[1000] ^= 0xFF;
        targetData[50000] ^= 0xFF;
        targetData[250000] ^= 0xFF;

        // Generate patch using Delta v2 (IRONDEL2)
        byte[] patchData = IupdDeltaV2Cdc.CreateDeltaV2(baseData, targetData);

        // Verify patch applies correctly
        byte[] outData = IupdDeltaV2Cdc.ApplyDeltaV2(baseData, patchData, out var deltaError);
        if (!deltaError.IsOk)
            throw new Exception($"Delta v2 apply failed: {deltaError}");

        if (!BytesEqual(outData, targetData))
            throw new Exception("Delta v2 patch verification failed: output != target");

        // Write files
        File.WriteAllBytes(Path.Combine(delta2Dir, "case_01.base.bin"), baseData);
        File.WriteAllBytes(Path.Combine(delta2Dir, "case_01.patch2.bin"), patchData);
        File.WriteAllBytes(Path.Combine(delta2Dir, "case_01.out.bin"), outData);

        Console.WriteLine($"    base={baseData.Length:N0} bytes, patch2={patchData.Length:N0} bytes, out={outData.Length:N0} bytes");
    }

    static void GenerateVectorsIndex(string outputDir)
    {
        var items = new List<VectorItem>();

        // Collect all generated files in stable order
        var files = Directory.GetFiles(outputDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("vectors.json"))
            .OrderBy(f => Path.GetRelativePath(outputDir, f))
            .ToList();

        foreach (var filePath in files)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            string relativePath = Path.GetRelativePath(outputDir, filePath);

            // Compute Blake3 hash
            byte[] hash = new byte[32];
            Blake3Ieee.Compute(fileBytes, hash);
            string hashHex = Convert.ToHexString(hash).ToLowerInvariant();

            items.Add(new VectorItem
            {
                Path = relativePath.Replace('\\', '/'),
                Bytes = fileBytes.Length,
                Blake3_256_Hex = hashHex
            });
        }

        // Get git HEAD
        string engineHead = GetGitHead();

        var index = new VectorsIndex
        {
            Format_Version = 1,
            Generated_By = "IronFamily.Vectors",
            Engine_Head = engineHead,
            Items = items
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null
        };

        string json = JsonSerializer.Serialize(index, options);
        File.WriteAllText(Path.Combine(outputDir, "vectors.json"), json);
    }

    static string GetGitHead()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(psi))
            {
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    return output;
                }
            }
        }
        catch { }

        return "unknown";
    }

    static byte[] GenerateDeterministicPayload(int size)
    {
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)((i * 0x47) & 0xFF);
        }
        return data;
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    static (string name, bool passed, string reason) VerifyVector(string filePath, bool shouldPass, string? expectedFailReason = null)
    {
        string name = Path.GetFileName(filePath);

        try
        {
            byte[] data = File.ReadAllBytes(filePath);

            // Open and validate with strict gates
            var reader = IupdReader.Open(data, out var parseError);
            if (reader == null && !shouldPass)
            {
                // Expected failure at parse time
                return (name, true, "Failed at parse (expected)");
            }

            if (reader == null)
            {
                return (name, false, $"Failed to parse but expected to pass");
            }

            // Try strict validation
            var validateError = reader.ValidateStrict();

            if (shouldPass)
            {
                if (validateError.IsOk)
                    return (name, true, "Passed validation (expected)");
                else
                    return (name, false, $"Failed validation: {validateError.Message}");
            }
            else
            {
                // Should fail
                if (!validateError.IsOk)
                {
                    // Verify failure reason matches expected category
                    if (expectedFailReason != null)
                    {
                        // Map expected failure reason to error patterns
                        bool reasonMatches = false;

                        if (expectedFailReason == "signature" &&
                            (validateError.Code == IupdErrorCode.SignatureInvalid ||
                             validateError.Message.Contains("signature", StringComparison.OrdinalIgnoreCase)))
                        {
                            reasonMatches = true;
                        }
                        else if (expectedFailReason == "sequence" &&
                                 (validateError.Code == IupdErrorCode.ReplayDetected ||
                                  validateError.Message.Contains("sequence", StringComparison.OrdinalIgnoreCase) ||
                                  validateError.Message.Contains("replay", StringComparison.OrdinalIgnoreCase)))
                        {
                            reasonMatches = true;
                        }
                        else if (expectedFailReason == "dos" &&
                                 (validateError.Code == IupdErrorCode.OffsetOutOfBounds ||
                                  validateError.Code == IupdErrorCode.ManifestSizeMismatch ||
                                  validateError.Message.Contains("exceeds", StringComparison.OrdinalIgnoreCase) ||
                                  validateError.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase)))
                        {
                            reasonMatches = true;
                        }

                        if (reasonMatches)
                            return (name, true, $"Failed with expected reason: {expectedFailReason}");
                        else
                            return (name, false, $"Failed but wrong reason. Expected: {expectedFailReason}, Got: {validateError.Code}");
                    }

                    return (name, true, $"Failed as expected: {validateError.Code}");
                }
                else
                {
                    return (name, false, "Passed validation but expected to fail");
                }
            }
        }
        catch (Exception ex)
        {
            return (name, false, $"Exception: {ex.Message}");
        }
    }

    static void WriteSelfCheckResults(List<(string name, bool passed, string reason)> results)
    {
        var devDir = Path.Combine(AppContext.BaseDirectory, "artifacts", "_dev", "exec_02b");
        Directory.CreateDirectory(devDir);

        var reportPath = Path.Combine(devDir, "selfcheck.txt");
        using (var writer = new StreamWriter(reportPath))
        {
            writer.WriteLine("IUPD v2 Vector Self-Check Results");
            writer.WriteLine("==================================");
            writer.WriteLine();

            int passedCount = results.Count(r => r.passed);
            int total = results.Count;
            writer.WriteLine($"Results: {passedCount}/{total} passed");
            writer.WriteLine();

            foreach (var (name, isSuccess, reason) in results)
            {
                string status = isSuccess ? "✓ PASS" : "✗ FAIL";
                writer.WriteLine($"{status} | {name,-30} | {reason}");
            }

            writer.WriteLine();
            string summary = passedCount == total ? "All vectors validated successfully" : $"{total - passedCount} failures detected";
            writer.WriteLine($"Summary: {summary}");
        }

        Console.WriteLine($"📋 Self-check results written to: {reportPath}");
    }

    class Options
    {
        public string OutputDir { get; set; } = "";
        public bool Force { get; set; }
        public bool DumpLayout { get; set; }
    }

    class VectorItem
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("bytes")]
        public int Bytes { get; set; }

        [JsonPropertyName("blake3_256_hex")]
        public string Blake3_256_Hex { get; set; } = "";
    }

    class VectorsIndex
    {
        [JsonPropertyName("format_version")]
        public int Format_Version { get; set; }

        [JsonPropertyName("generated_by")]
        public string Generated_By { get; set; } = "";

        [JsonPropertyName("engine_head")]
        public string Engine_Head { get; set; } = "";

        [JsonPropertyName("items")]
        public List<VectorItem> Items { get; set; } = new();
    }
}
