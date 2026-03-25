using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class IlogGenerator
{
    private static readonly string TestVectorsDir = Path.Combine("vectors/small", "ilog");

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

            // Generate each dataset
            manifests.Add(GenerateDataset("small", 3));
            manifests.Add(GenerateDataset("medium", 30));
            manifests.Add(GenerateDataset("large", 300));
            manifests.Add(GenerateDataset("mega", 3000));

            // Update root manifest
            UpdateRootManifest();

            Console.WriteLine($"OK generated ilog golden vectors to {TestVectorsDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL generate ilog code=GENERATION_ERROR msg=\"{ex.Message}\"");
            return 1;
        }
    }

    private static DatasetManifest GenerateDataset(string name, int eventCount)
    {
        var datasetDir = Path.Combine(TestVectorsDir, $"golden_{name}", "expected");
        var binPath = Path.Combine(datasetDir, "ilog.ilog");

        // Generate canonical events
        var events = GenerateCanonicalEvents(eventCount);

        // Encode ILOG file
        var fileBytes = EncodeIlogFile(events);

        // Write binary file
        File.WriteAllBytes(binPath, fileBytes);

        // Compute CRC32 and BLAKE3 of first L0_DATA block payload
        var (crc32, blake3) = ExtractPayloadHashes(fileBytes);

        // Create manifest
        var manifest = new DatasetManifest
        {
            Engine = "ilog",
            Version = 1,
            Dataset = name,
            ExpectedFast = "OK",
            ExpectedStrict = "OK",
            ExpectedEvents = eventCount,
            ExpectedCrc32 = crc32.ToString("x8"),
            ExpectedBlake3 = blake3
        };

        // Write manifest
        WriteManifest(Path.Combine(TestVectorsDir, $"golden_{name}"), manifest);

        Console.WriteLine($"Generated golden_{name}: {binPath} ({fileBytes.Length} bytes)");

        return manifest;
    }

    private static List<CanonicalEvent> GenerateCanonicalEvents(int count)
    {
        var events = new List<CanonicalEvent>();

        for (int i = 0; i < count; i++)
        {
            var eventTypeId = (uint)((i % 4) + 1);
            var timestampDelta = (ulong)ZigZagEncode((long)i);
            var field1Value = (ulong)i;
            var field2Value = (ulong)ZigZagEncode(-(long)(i + 1));
            var field3Value = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFE, 0xFF };

            events.Add(new CanonicalEvent
            {
                EventTypeId = eventTypeId,
                TimestampDelta = timestampDelta,
                Field1Value = field1Value,
                Field2Value = field2Value,
                Field3Value = field3Value
            });
        }

        return events;
    }

    private static byte[] EncodeIlogFile(List<CanonicalEvent> events)
    {
        using var ms = new MemoryStream();

        // Encode L0_DATA block payload
        var l0Payload = EncodeL0DataPayload(events);

        // Create L0_DATA block
        var l0Block = CreateL0DataBlock(l0Payload, sequence: 0);

        // Create L1_TOC block (comes after L0_DATA)
        var l1Payload = EncodeL1TocPayload();
        var l1Block = CreateL1TocBlock(l1Payload, sequence: 1);

        // Calculate file header (TocBlockOffset must point to L1_TOC block)
        // L0_DATA: 16 (header) + 72 (L0 block header) + l0Payload.Length
        var l0BlockSize = 72 + l0Payload.Length;
        var tocBlockOffset = 16UL + (ulong)l0BlockSize;

        // Write file header
        var fileHeader = EncodeFileHeader(tocBlockOffset);
        ms.Write(fileHeader, 0, fileHeader.Length);

        // Write L0_DATA block
        ms.Write(l0Block, 0, l0Block.Length);

        // Write L1_TOC block
        ms.Write(l1Block, 0, l1Block.Length);

        return ms.ToArray();
    }

    private static byte[] EncodeFileHeader(ulong tocBlockOffset)
    {
        var buf = new byte[16];
        var pos = 0;

        // Magic: "ILOG" (0x49 0x4C 0x4F 0x47)
        buf[pos++] = 0x49;
        buf[pos++] = 0x4C;
        buf[pos++] = 0x4F;
        buf[pos++] = 0x47;

        // Version: 0x01
        buf[pos++] = 0x01;

        // Flags: Bit 1 (CRC32) = 1, Bit 2 (BLAKE3) = 1
        buf[pos++] = 0x06; // 0b00000110 = CRC32 + BLAKE3

        // Reserved0: 0x0000 (u16)
        pos += WriteLittleEndianU16(buf, pos, 0x0000);

        // TocBlockOffset: (u64)
        pos += WriteLittleEndianU64(buf, pos, tocBlockOffset);

        return buf;
    }

    private static byte[] EncodeL0DataPayload(List<CanonicalEvent> events)
    {
        using var ms = new MemoryStream();
        var pos = 0;

        // StreamVersion (u8)
        ms.WriteByte(0x01);
        pos++;

        // EventCount (varint)
        var eventCountBytes = EncodeVarint((ulong)events.Count);
        ms.Write(eventCountBytes, 0, eventCountBytes.Length);
        pos += eventCountBytes.Length;

        // TimestampEpoch (u64 little-endian)
        var epochBytes = new byte[8];
        Array.Copy(BitConverter.GetBytes(0UL), epochBytes, 8);
        ms.Write(epochBytes, 0, 8);
        pos += 8;

        // Events
        foreach (var evt in events)
        {
            var eventBytes = EncodeEvent(evt);
            ms.Write(eventBytes, 0, eventBytes.Length);
            pos += eventBytes.Length;
        }

        return ms.ToArray();
    }

    private static byte[] EncodeEvent(CanonicalEvent evt)
    {
        using var ms = new MemoryStream();

        // EventTypeId (varint)
        var typeBytes = EncodeVarint(evt.EventTypeId);
        ms.Write(typeBytes, 0, typeBytes.Length);

        // TimestampDelta (ZigZag varint - already encoded)
        var deltaBytes = EncodeVarint(evt.TimestampDelta);
        ms.Write(deltaBytes, 0, deltaBytes.Length);

        // FieldCount (varint) = 3
        var fieldCountBytes = EncodeVarint(3);
        ms.Write(fieldCountBytes, 0, fieldCountBytes.Length);

        // Field 1: FieldId=1, WireType=0 (varint)
        ms.Write(EncodeVarint(1), 0, EncodeVarint(1).Length); // FieldId
        ms.Write(EncodeVarint(evt.Field1Value), 0, EncodeVarint(evt.Field1Value).Length); // Value

        // Field 2: FieldId=2, WireType=0 (ZigZag varint - already encoded)
        ms.Write(EncodeVarint(2), 0, EncodeVarint(2).Length); // FieldId
        ms.Write(EncodeVarint(evt.Field2Value), 0, EncodeVarint(evt.Field2Value).Length); // Value

        // Field 3: FieldId=3, WireType=2 (bytes)
        ms.Write(EncodeVarint(3), 0, EncodeVarint(3).Length); // FieldId
        ms.Write(EncodeVarint((ulong)evt.Field3Value.Length), 0, EncodeVarint((ulong)evt.Field3Value.Length).Length); // Length
        ms.Write(evt.Field3Value, 0, evt.Field3Value.Length); // Value

        return ms.ToArray();
    }

    private static byte[] EncodeL1TocPayload()
    {
        using var ms = new MemoryStream();

        // TocVersion (u8)
        ms.WriteByte(0x01);

        // LayerCount (u32 little-endian) = 2 (L0_DATA + L1_TOC)
        var buf = new byte[4];
        Array.Copy(BitConverter.GetBytes(2U), buf, 4);
        ms.Write(buf, 0, 4);

        // Layer entry 1: L0_DATA
        // LayerType (u16) = 0x0001
        buf = new byte[2];
        Array.Copy(BitConverter.GetBytes((ushort)0x0001), buf, 2);
        ms.Write(buf, 0, 2);

        // BlockCount (u32) = 1
        buf = new byte[4];
        Array.Copy(BitConverter.GetBytes(1U), buf, 4);
        ms.Write(buf, 0, 4);

        // Flags (u32) = 0
        buf = new byte[4];
        Array.Copy(BitConverter.GetBytes(0U), buf, 4);
        ms.Write(buf, 0, 4);

        // Reserved (u64) = 0
        buf = new byte[8];
        Array.Copy(BitConverter.GetBytes(0UL), buf, 8);
        ms.Write(buf, 0, 8);

        // Layer entry 2: L1_TOC
        // LayerType (u16) = 0x0002
        buf = new byte[2];
        Array.Copy(BitConverter.GetBytes((ushort)0x0002), buf, 2);
        ms.Write(buf, 0, 2);

        // BlockCount (u32) = 1
        buf = new byte[4];
        Array.Copy(BitConverter.GetBytes(1U), buf, 4);
        ms.Write(buf, 0, 4);

        // Flags (u32) = 0
        buf = new byte[4];
        Array.Copy(BitConverter.GetBytes(0U), buf, 4);
        ms.Write(buf, 0, 4);

        // Reserved (u64) = 0
        buf = new byte[8];
        Array.Copy(BitConverter.GetBytes(0UL), buf, 8);
        ms.Write(buf, 0, 8);

        return ms.ToArray();
    }

    private static byte[] CreateL0DataBlock(byte[] payload, ulong sequence)
    {
        var blockHeader = EncodeBlockHeader(0x0001, payload.Length, sequence, payload);
        var result = new byte[blockHeader.Length + payload.Length];
        Array.Copy(blockHeader, result, blockHeader.Length);
        Array.Copy(payload, 0, result, blockHeader.Length, payload.Length);
        return result;
    }

    private static byte[] CreateL1TocBlock(byte[] payload, ulong sequence)
    {
        var blockHeader = EncodeBlockHeader(0x0002, payload.Length, sequence, payload);
        var result = new byte[blockHeader.Length + payload.Length];
        Array.Copy(blockHeader, result, blockHeader.Length);
        Array.Copy(payload, 0, result, blockHeader.Length, payload.Length);
        return result;
    }

    private static byte[] EncodeBlockHeader(ushort blockType, int payloadSize, ulong sequence, byte[] payload)
    {
        var buf = new byte[72];
        var pos = 0;

        // BlockMagic (u32) = 0x314B4C42 ("BLK1")
        pos += WriteLittleEndianU32(buf, pos, 0x314B4C42);

        // BlockType (u16)
        pos += WriteLittleEndianU16(buf, pos, blockType);

        // BlockFlags (u16) = 0
        pos += WriteLittleEndianU16(buf, pos, 0);

        // HeaderSize (u16) = 72
        pos += WriteLittleEndianU16(buf, pos, 72);

        // Reserved0 (u16) = 0
        pos += WriteLittleEndianU16(buf, pos, 0);

        // PayloadSize (u32)
        pos += WriteLittleEndianU32(buf, pos, (uint)payloadSize);

        // Sequence (u64)
        pos += WriteLittleEndianU64(buf, pos, sequence);

        // PayloadCrc32 (u32) - calculate over payload
        var crc32 = Crc32.Compute(payload);
        pos += WriteLittleEndianU32(buf, pos, crc32);

        // HeaderCrc32 (u32) - will be calculated and set to 0 for now
        pos += WriteLittleEndianU32(buf, pos, 0);

        // PayloadBlake3 [32]
        var blake3 = Blake3Helper.Hash(payload);
        Array.Copy(blake3, 0, buf, pos, 32);
        pos += 32;

        // Reserved1 [8] = all 0x00
        pos += 8; // Already zeros

        // Calculate and write HeaderCrc32
        // CRC over bytes [0x00-0x1B] with this field set to 0
        var headerForCrc = new byte[0x1C]; // 28 bytes
        Array.Copy(buf, headerForCrc, 0x1C);
        var headerCrc = Crc32.Compute(headerForCrc);
        Array.Copy(BitConverter.GetBytes(headerCrc), 0, buf, 0x1C, 4);

        return buf;
    }

    private static (uint crc32, string blake3) ExtractPayloadHashes(byte[] fileBytes)
    {
        // Find first L0_DATA block (starts at offset 16)
        var pos = 16;

        // Read block header to get payload size
        var payloadSize = BitConverter.ToInt32(fileBytes, pos + 0x0C);
        var payloadStart = pos + 72;
        var payload = new byte[payloadSize];
        Array.Copy(fileBytes, payloadStart, payload, 0, payloadSize);

        // Read PayloadCrc32 from block header
        var crc32 = BitConverter.ToUInt32(fileBytes, pos + 0x18);

        // Read PayloadBlake3 from block header
        var blake3Bytes = new byte[32];
        Array.Copy(fileBytes, pos + 0x20, blake3Bytes, 0, 32);
        var blake3Hex = BitConverter.ToString(blake3Bytes).Replace("-", "").ToLowerInvariant();

        return (crc32, blake3Hex);
    }

    private static void WriteManifest(string datasetDir, DatasetManifest manifest)
    {
        var manifestPath = Path.Combine(datasetDir, "manifest.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(manifest, options);
        File.WriteAllText(manifestPath, json);
    }

    private static void UpdateRootManifest()
    {
        var manifestPath = Path.Combine("vectors/small", "manifest.json");

        if (!File.Exists(manifestPath))
            throw new Exception("vectors/small/manifest.json not found");

        var manifest = new Dictionary<string, object>();

        // Read existing manifest
        {
            using var fs = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;

            if (root.TryGetProperty("engines", out var engines))
            {
                foreach (var prop in engines.EnumerateObject())
                {
                    var deserialized = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    if (deserialized != null)
                    {
                        manifest[prop.Name] = deserialized;
                    }
                }
            }
        }

        // Update ILOG vectors
        var ilogVectors = new List<IlogVectorEntry>();
        foreach (var name in new[] { "small", "medium", "large", "mega" })
        {
            var manifestFile = Path.Combine(TestVectorsDir, $"golden_{name}", "manifest.json");
            if (File.Exists(manifestFile))
            {
                var manifestJson = File.ReadAllText(manifestFile);
                var manifestData = JsonSerializer.Deserialize<JsonElement>(manifestJson);

                ilogVectors.Add(new IlogVectorEntry
                {
                    Id = name,
                    Bin = Path.Combine("vectors/small", "ilog", $"golden_{name}", "expected", "ilog.ilog").Replace("\\", "/"),
                    Crc = true,
                    Expect = "OK",
                    Note = $"ILOG golden vector - {name} dataset deterministic"
                });
            }
        }

        var ilogEntry = new
        {
            magic = "ILOG",
            vectors = ilogVectors.Cast<object>().ToList()
        };

        var engineDicts = new Dictionary<string, object>(manifest)
        {
            ["ilog"] = ilogEntry
        };

        var fullManifest = new { engines = engineDicts };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(fullManifest, options));
    }

    // Utility functions

    private static long ZigZagEncode(long value)
    {
        return (value << 1) ^ (value >> 63);
    }

    private static byte[] EncodeVarint(ulong value)
    {
        var buf = new List<byte>();
        while ((value & 0xFFFFFFFFFFFFFF80) != 0)
        {
            buf.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        buf.Add((byte)(value & 0x7F));
        return buf.ToArray();
    }

    private static int WriteLittleEndianU16(byte[] buf, int pos, ushort value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, buf, pos, 2);
        return 2;
    }

    private static int WriteLittleEndianU32(byte[] buf, int pos, uint value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, buf, pos, 4);
        return 4;
    }

    private static int WriteLittleEndianU64(byte[] buf, int pos, ulong value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, buf, pos, 8);
        return 8;
    }

    // ====== CRC32 Implementation ======
    private static class Crc32
    {
        private static readonly uint[] Table = GenerateCrcTable();

        private static uint[] GenerateCrcTable()
        {
            var table = new uint[256];
            const uint polynomial = 0xEDB88320;

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
                table[i] = crc;
            }
            return table;
        }

        public static uint Compute(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
            }
            return crc ^ 0xFFFFFFFF;
        }
    }

    // ====== BLAKE3 Implementation (uses official BLAKE3 library) ======
    private static class Blake3Helper
    {
        public static byte[] Hash(byte[] data)
        {
            // Use official BLAKE3 library for spec-compliant hashing
            var hash = Blake3.Hasher.Hash(data);
            return hash.AsSpan().ToArray();
        }
    }

    // ====== Data Structures ======

    private class CanonicalEvent
    {
        public uint EventTypeId { get; set; }
        public ulong TimestampDelta { get; set; }
        public ulong Field1Value { get; set; }
        public ulong Field2Value { get; set; }
        public byte[] Field3Value { get; set; } = Array.Empty<byte>();
    }

    private class DatasetManifest
    {
        [JsonPropertyName("engine")]
        public string Engine { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("dataset")]
        public string Dataset { get; set; } = string.Empty;

        [JsonPropertyName("expected_fast")]
        public string ExpectedFast { get; set; } = string.Empty;

        [JsonPropertyName("expected_strict")]
        public string ExpectedStrict { get; set; } = string.Empty;

        [JsonPropertyName("expected_events")]
        public int ExpectedEvents { get; set; }

        [JsonPropertyName("expected_crc32")]
        public string ExpectedCrc32 { get; set; } = string.Empty;

        [JsonPropertyName("expected_blake3")]
        public string ExpectedBlake3 { get; set; } = string.Empty;
    }

    private class IlogVectorEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("bin")]
        public string Bin { get; set; } = string.Empty;

        [JsonPropertyName("crc")]
        public bool Crc { get; set; }

        [JsonPropertyName("expect")]
        public string Expect { get; set; } = string.Empty;

        [JsonPropertyName("note")]
        public string Note { get; set; } = string.Empty;
    }
}
