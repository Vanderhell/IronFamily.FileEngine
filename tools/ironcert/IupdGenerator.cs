using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class IupdGenerator
{
    private static readonly string TestVectorsDir = Path.Combine("vectors/small", "iupd");

    public static int Generate()
    {
        try
        {
            // Create directories for all datasets
            Directory.CreateDirectory(TestVectorsDir);
            Directory.CreateDirectory(Path.Combine(TestVectorsDir, "golden_small", "expected"));
            Directory.CreateDirectory(Path.Combine(TestVectorsDir, "golden_medium", "expected"));
            Directory.CreateDirectory(Path.Combine(TestVectorsDir, "golden_large", "expected"));
            Directory.CreateDirectory(Path.Combine(TestVectorsDir, "golden_mega", "expected"));

            var manifests = new List<DatasetManifest>();

            // Generate each dataset with deterministic chunk counts
            manifests.Add(GenerateDataset("small", 2));
            manifests.Add(GenerateDataset("medium", 8));
            manifests.Add(GenerateDataset("large", 64));
            manifests.Add(GenerateDataset("mega", 512));

            // Update root manifest
            UpdateRootManifest();

            Console.WriteLine($"OK generated iupd golden vectors to {TestVectorsDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL generate iupd code=GENERATION_ERROR msg=\"{ex.Message}\"");
            return 1;
        }
    }

    private static DatasetManifest GenerateDataset(string name, int chunkCount)
    {
        var datasetDir = Path.Combine(TestVectorsDir, $"golden_{name}", "expected");
        var binPath = Path.Combine(datasetDir, "iupd.iupd");

        // Generate deterministic chunks
        var chunks = GenerateCanonicalChunks(chunkCount);

        // Encode IUPD file
        var fileBytes = EncodeIupdFile(chunks);

        // Write binary file
        File.WriteAllBytes(binPath, fileBytes);

        // Extract manifest CRC32 from the file
        // File structure: header (36) + chunk table (56*chunkCount) + manifest
        // Manifest structure: header (24) + apply order (4*chunkCount) + CRC32 (4) + reserved (4)
        int manifestOffset = 36 + (chunkCount * 56);
        int manifestCrc32Offset = manifestOffset + 24 + (chunkCount * 4);
        uint manifestCrc32 = BitConverter.ToUInt32(fileBytes, manifestCrc32Offset);

        var fileBlake3 = ComputeBlake3(fileBytes);

        // Create manifest
        var manifest = new DatasetManifest
        {
            Engine = "iupd",
            Version = 1,
            Dataset = name,
            ExpectedFast = "OK",
            ExpectedStrict = "OK",
            ExpectedChunks = chunkCount,
            ExpectedCrc32 = manifestCrc32.ToString("x8"),
            ExpectedBlake3 = fileBlake3
        };

        // Write manifest
        WriteManifest(Path.Combine(TestVectorsDir, $"golden_{name}"), manifest);

        Console.WriteLine($"Generated golden_{name}: {binPath} ({fileBytes.Length} bytes, {chunkCount} chunks)");

        return manifest;
    }

    private static List<CanonicalChunk> GenerateCanonicalChunks(int count)
    {
        var chunks = new List<CanonicalChunk>();

        for (int i = 0; i < count; i++)
        {
            // Deterministic payload: pattern based on chunk index
            // Each chunk is 64 bytes, but we make varying sizes for diversity
            int payloadSize = 16 + (i % 48);  // 16..63 bytes per chunk

            var payload = new byte[payloadSize];
            for (int j = 0; j < payloadSize; j++)
            {
                // Deterministic byte sequence: XOR of index and position
                payload[j] = (byte)((i ^ j) & 0xFF);
            }

            // Compute hashes
            var crc32 = ComputeCrc32(payload);
            var blake3 = ComputeBlake3(payload);

            chunks.Add(new CanonicalChunk
            {
                ChunkIndex = (uint)i,
                Payload = payload,
                PayloadCrc32 = crc32,
                PayloadBlake3 = blake3
            });
        }

        return chunks;
    }

    private static byte[] EncodeIupdFile(List<CanonicalChunk> chunks)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Calculate offsets
        const int headerSize = 36;
        const int chunkEntrySize = 56;
        int chunkTableOffset = headerSize;
        int manifestOffset = chunkTableOffset + (chunks.Count * chunkEntrySize);

        // Manifest: header (24 bytes) + dependencies (0) + apply order (chunks.Count * 4) + integrity (8)
        int manifestSize = 24 + (chunks.Count * 4) + 8;
        int payloadOffset = manifestOffset + manifestSize;

        // --- FILE HEADER (36 bytes) ---
        bw.Write(0x44505549U);  // "IUPD" magic in little-endian
        bw.Write((byte)0x01);   // Version
        bw.Write(0U);           // Flags (reserved, must be 0)
        bw.Write((ushort)36);   // HeaderSize
        bw.Write((byte)0);      // Reserved
        bw.Write((ulong)chunkTableOffset);  // ChunkTableOffset
        bw.Write((ulong)manifestOffset);    // ManifestOffset
        bw.Write((ulong)payloadOffset);     // PayloadOffset

        // --- CHUNK TABLE (56 bytes per entry) ---
        foreach (var chunk in chunks)
        {
            // Build 56-byte entry
            var entry = new byte[56];
            int pos = 0;

            // ChunkIndex (u32, 4 bytes)
            Array.Copy(BitConverter.GetBytes(chunk.ChunkIndex), 0, entry, pos, 4);
            pos += 4;

            // PayloadSize (u64, 8 bytes)
            Array.Copy(BitConverter.GetBytes((ulong)chunk.Payload.Length), 0, entry, pos, 8);
            pos += 8;

            // PayloadOffset (u64, 8 bytes) - placeholder
            Array.Copy(BitConverter.GetBytes(0UL), 0, entry, pos, 8);
            pos += 8;

            // PayloadCrc32 (u32, 4 bytes)
            Array.Copy(BitConverter.GetBytes(chunk.PayloadCrc32), 0, entry, pos, 4);
            pos += 4;

            // PayloadBlake3 (u8[32], 32 bytes) - convert hex string to bytes
            for (int i = 0; i < 32; i++)
            {
                string hex = chunk.PayloadBlake3.Substring(i * 2, 2);
                entry[pos + i] = Convert.ToByte(hex, 16);
            }

            bw.Write(entry);
        }

        // --- MANIFEST HEADER (24 bytes) ---
        bw.Write((byte)0x01);              // ManifestVersion at offset 0
        bw.Write((byte)0);                 // Reserved[0] at offset 1
        bw.Write((byte)0);                 // Reserved[1] at offset 2
        bw.Write((byte)0);                 // Reserved[2] at offset 3
        bw.Write(0U);                      // TargetVersion at offset 4
        bw.Write(0U);                      // DependencyCount at offset 8
        bw.Write((uint)chunks.Count);      // ApplyOrderCount at offset 12
        bw.Write((ulong)manifestSize);     // ManifestSize at offset 16

        // --- APPLY ORDER LIST ---
        for (int i = 0; i < chunks.Count; i++)
        {
            bw.Write((uint)i);  // ChunkIndex (in order 0..N-1)
        }

        // --- MANIFEST INTEGRITY ---
        // Compute CRC32 over manifest header (24 bytes) + dependencies (0) + apply order (excluding trailer)
        var manifestDataForCrc = ms.GetBuffer().Skip(manifestOffset).Take(24 + chunks.Count * 4).ToArray();
        uint manifestCrc32 = ComputeCrc32(manifestDataForCrc);
        bw.Write(manifestCrc32);   // ManifestCrc32
        bw.Write(0U);              // Reserved

        // --- PAYLOAD SECTION ---
        foreach (var chunk in chunks)
        {
            bw.Write(chunk.Payload);
        }

        // Now go back and fill in PayloadOffsets in chunk table
        byte[] result = ms.ToArray();
        int currentPayloadOffset = payloadOffset;

        for (int i = 0; i < chunks.Count; i++)
        {
            int offsetInFile = chunkTableOffset + (i * chunkEntrySize) + 12;  // 12 = offset of PayloadOffset field
            BitConverter.GetBytes((ulong)currentPayloadOffset).CopyTo(result, offsetInFile);
            currentPayloadOffset += chunks[i].Payload.Length;
        }

        return result;
    }

    private static uint ComputeCrc32(byte[] data)
    {
        const uint polynomial = 0xEDB88320;

        var crc32Table = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            uint crc = (uint)i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
            crc32Table[i] = crc;
        }

        uint result = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            result = (result >> 8) ^ crc32Table[(result ^ b) & 0xFF];
        }
        return result ^ 0xFFFFFFFF;
    }

    private static string ComputeBlake3(byte[] data)
    {
        // Use System.Security.Cryptography for deterministic hashing
        // BLAKE3 is not in standard .NET, so we use SHA256 as placeholder
        // In production, use Blake3.NET NuGet package
        using (var sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private static void WriteManifest(string datasetDir, DatasetManifest manifest)
    {
        var manifestPath = Path.Combine(datasetDir, "manifest.json");

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        string json = JsonSerializer.Serialize(manifest, options);
        File.WriteAllText(manifestPath, json);
    }

    private static void UpdateRootManifest()
    {
        var manifestPath = Path.Combine("vectors/small", "manifest.json");

        // Load existing manifest
        var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));

        // Convert to mutable structure
        var json = doc.RootElement.GetRawText();
        var root = JsonSerializer.Deserialize<JsonElement>(json);

        // Read existing engines object or create new one
        Dictionary<string, object> engines = new();

        if (root.TryGetProperty("engines", out var enginesEl))
        {
            // Copy existing engines (except iupd)
            foreach (var prop in enginesEl.EnumerateObject())
            {
                if (prop.Name != "iupd")
                {
                    engines[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                }
            }
        }

        // Add IUPD to engines
        var iupdVectors = new List<object>
        {
            new { id = "iupd_small", bin = "vectors/small/iupd/golden_small/expected/iupd.iupd", expect = "OK", note = "IUPD golden vector - small dataset (2 chunks)" },
            new { id = "iupd_medium", bin = "vectors/small/iupd/golden_medium/expected/iupd.iupd", expect = "OK", note = "IUPD golden vector - medium dataset (8 chunks)" },
            new { id = "iupd_large", bin = "vectors/medium/iupd/golden_large/expected/iupd.iupd", expect = "OK", note = "IUPD golden vector - large dataset (64 chunks)" },
            new { id = "iupd_mega", bin = "vectors/medium/iupd/golden_mega/expected/iupd.iupd", expect = "OK", note = "IUPD golden vector - mega dataset (512 chunks)" }
        };

        engines["iupd"] = new { magic = "IUPD", vectors = iupdVectors };

        // Build result with engines
        var result = new Dictionary<string, object> { { "engines", engines } };

        // Add other top-level entries (if any)
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name != "engines" && prop.Name != "iupd")
            {
                result[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
            }
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        string outputJson = JsonSerializer.Serialize(result, options);
        File.WriteAllText(manifestPath, outputJson);
    }

    // --- Types ---

    private class CanonicalChunk
    {
        public uint ChunkIndex { get; set; }
        public byte[] Payload { get; set; }
        public uint PayloadCrc32 { get; set; }
        public string PayloadBlake3 { get; set; }
    }

    private class DatasetManifest
    {
        [JsonPropertyName("engine")]
        public string Engine { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("dataset")]
        public string Dataset { get; set; }

        [JsonPropertyName("expected_fast")]
        public string ExpectedFast { get; set; }

        [JsonPropertyName("expected_strict")]
        public string ExpectedStrict { get; set; }

        [JsonPropertyName("expected_chunks")]
        public int? ExpectedChunks { get; set; }

        [JsonPropertyName("expected_crc32")]
        public string ExpectedCrc32 { get; set; }

        [JsonPropertyName("expected_blake3")]
        public string ExpectedBlake3 { get; set; }
    }
}
