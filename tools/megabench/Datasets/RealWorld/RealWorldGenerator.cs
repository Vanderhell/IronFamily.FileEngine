using System;
using System.Collections.Generic;
using System.IO;
using System.Buffers.Binary;
using System.Text;
using IronConfig.ILog;
using IronConfig.IronCfg;
using IronConfig.Iupd;
using IronFamily.MegaBench.Validation;

namespace IronFamily.MegaBench.Datasets.RealWorld;

/// <summary>
/// Deterministic real-world dataset generator.
/// Generates realistic payloads for ICFG (device tree), ILOG (events), and IUPD (manifests).
/// Uses IRONFAMILY_DETERMINISTIC=1 seed=42 for reproducibility.
/// </summary>
public static class RealWorldDatasetGenerator
{
    /// <summary>
    /// Generate a real-world dataset with realistic semantic content.
    /// </summary>
    public static byte[] GenerateDataset(RealWorldDatasetId datasetId)
    {
        // Use fixed seed if IRONFAMILY_DETERMINISTIC=1
        int seed = Environment.GetEnvironmentVariable("IRONFAMILY_DETERMINISTIC") == "1" ? 42 : Random.Shared.Next();
        var rng = new Random(seed);

        return datasetId switch
        {
            RealWorldDatasetId.RW_ICFG_DEVICE_TREE_10KB => GenerateIcfgDeviceTree(rng, 10 * 1024),
            RealWorldDatasetId.RW_ICFG_DEVICE_TREE_100KB => GenerateIcfgDeviceTree(rng, 100 * 1024),
            RealWorldDatasetId.RW_ILOG_PLC_EVENTS_1MB => GenerateIlogPlcEvents(rng, 1 * 1024 * 1024),
            RealWorldDatasetId.RW_ILOG_PLC_EVENTS_10MB => GenerateIlogPlcEvents(rng, 10 * 1024 * 1024),
            RealWorldDatasetId.RW_IUPD_MANIFEST_1MB => GenerateIupdManifest(rng, 1 * 1024 * 1024),
            RealWorldDatasetId.RW_IUPD_MANIFEST_10MB => GenerateIupdManifest(rng, 10 * 1024 * 1024),
            _ => throw new ArgumentException($"Unknown dataset ID: {datasetId}")
        };
    }

    /// <summary>
    /// Generate a realistic device tree configuration (nested objects, arrays, mixed types).
    /// </summary>
    private static byte[] GenerateIcfgDeviceTree(Random rng, int targetSize)
    {
        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "deviceName", FieldType = 0x20, IsRequired = true },
                new IronCfgField { FieldId = 1, FieldName = "deviceId", FieldType = 0x10, IsRequired = true },
                new IronCfgField { FieldId = 2, FieldName = "enabled", FieldType = 0x01, IsRequired = true },
                new IronCfgField { FieldId = 3, FieldName = "nodes", FieldType = 0x30, IsRequired = true,
                    ElementSchema = new IronCfgSchema
                    {
                        Fields = new List<IronCfgField>
                        {
                            new IronCfgField { FieldId = 0, FieldName = "nodeName", FieldType = 0x20, IsRequired = true },
                            new IronCfgField { FieldId = 1, FieldName = "baseAddr", FieldType = 0x11, IsRequired = true },
                            new IronCfgField { FieldId = 2, FieldName = "irq", FieldType = 0x10, IsRequired = true },
                            new IronCfgField { FieldId = 3, FieldName = "online", FieldType = 0x01, IsRequired = true },
                            new IronCfgField { FieldId = 4, FieldName = "mode", FieldType = 0x20, IsRequired = true },
                            new IronCfgField { FieldId = 5, FieldName = "registerBlob", FieldType = 0x22, IsRequired = true }
                        }
                    }
                },
                new IronCfgField { FieldId = 4, FieldName = "firmware", FieldType = 0x22, IsRequired = false }
            }
        };

        byte[]? encoded = null;
        int attempts = 0;
        int bufferSize = Math.Max(targetSize * 2, 1024 * 1024);

        while (attempts < 10)
        {
            var buffer = new byte[bufferSize];
            var root = GenerateDeviceTreeObject(rng, targetSize, encoded?.Length ?? 0);

            var err = IronCfgEncoder.Encode(root, schema, computeCrc32: true, computeBlake3: true, buffer, out int encodedSize);
            if (!err.IsOk)
            {
                bufferSize *= 2;
                attempts++;
                continue;
            }

            encoded = new byte[encodedSize];
            Array.Copy(buffer, encoded, encodedSize);

            if (encoded.Length >= targetSize * 0.9)
            {
                return encoded;
            }

            bufferSize = Math.Max(bufferSize * 2, (int)(targetSize / 0.8));
            attempts++;
        }

        if (encoded == null)
        {
            throw new InvalidOperationException($"Failed to generate device tree after {attempts} attempts");
        }

        return encoded;
    }

    private static IronCfgObject GenerateDeviceTreeObject(Random rng, int targetSize, int currentSize)
    {
        var deviceNames = new[] { "cpu", "gpio", "uart", "spi", "i2c", "usb", "ethernet", "dma", "timer", "pwm" };
        var modes = new[] { "input", "output", "push-pull", "open-drain", "pwm", "capture", "dma", "loopback" };
        int sizeHint = currentSize > 0 ? currentSize : targetSize;
        int nodeCount = Math.Max(16, sizeHint / 160);
        int firmwareBytes = Math.Max(1024, sizeHint / 3);
        int registerBlobBytes = 48;

        var nodes = new List<IronCfgValue?>(nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            byte[] registerBlob = BuildRegisterBlob(rng, registerBlobBytes, i);
            nodes.Add(new IronCfgObject
            {
                Fields = new SortedDictionary<uint, IronCfgValue?>
                {
                    { 0, new IronCfgString { Value = $"{deviceNames[i % deviceNames.Length]}@0x{0x40000000 + (i * 0x1000):X8}" } },
                    { 1, new IronCfgUInt64 { Value = (ulong)(0x40000000 + (i * 0x1000)) } },
                    { 2, new IronCfgInt64 { Value = 32 + (i % 160) } },
                    { 3, new IronCfgBool { Value = (i % 7) != 0 } },
                    { 4, new IronCfgString { Value = modes[i % modes.Length] } },
                    { 5, new IronCfgBytes { Data = registerBlob } }
                }
            });
        }

        byte[] firmware = BuildFirmwareImage(rng, firmwareBytes);

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgString { Value = deviceNames[rng.Next(deviceNames.Length)] } },
                { 1, new IronCfgInt64 { Value = rng.NextInt64(1000, 9999) } },
                { 2, new IronCfgBool { Value = rng.Next(2) == 1 } },
                { 3, new IronCfgArray { Elements = nodes } },
                { 4, new IronCfgBytes { Data = firmware } }
            }
        };

        return root;
    }

    private static byte[] BuildRegisterBlob(Random rng, int length, int nodeIndex)
    {
        var data = new byte[length];
        for (int i = 0; i < data.Length; i += 16)
        {
            int remaining = data.Length - i;
            var chunk = data.AsSpan(i, Math.Min(16, remaining));
            chunk.Clear();

            if (chunk.Length >= 4)
            {
                uint registerBase = (uint)(nodeIndex * 0x40 + i);
                BinaryPrimitives.WriteUInt32LittleEndian(chunk.Slice(0, 4), registerBase);
            }

            if (chunk.Length >= 8)
                BinaryPrimitives.WriteUInt32LittleEndian(chunk.Slice(4, 4), (uint)(0xA5A50000 | (nodeIndex & 0xFFFF)));

            if (chunk.Length >= 12)
                BinaryPrimitives.WriteUInt32LittleEndian(chunk.Slice(8, 4), (uint)((i / 16) * 17));

            if (chunk.Length >= 16)
                BinaryPrimitives.WriteUInt32LittleEndian(chunk.Slice(12, 4), (uint)rng.Next());
        }

        return data;
    }

    private static byte[] BuildFirmwareImage(Random rng, int length)
    {
        var data = new byte[length];
        int offset = 0;

        while (offset < data.Length)
        {
            int segmentLen = Math.Min(256, data.Length - offset);
            var segment = data.AsSpan(offset, segmentLen);
            if ((offset / 256) % 5 == 0)
            {
                segment.Clear();
            }
            else if ((offset / 256) % 5 <= 2)
            {
                Encoding.ASCII.GetBytes($"CFG:board=IFX;rev={offset / 256:D4};mode=prod;").AsSpan().CopyTo(segment);
            }
            else
            {
                rng.NextBytes(segment);
            }

            offset += segmentLen;
        }

        return data;
    }

    /// <summary>
    /// Generate realistic PLC (Programmable Logic Controller) event logs.
    /// Each event has: timestamp, level, tag, payload.
    /// Mix of repeated fields and varied data.
    /// </summary>
    private static byte[] GenerateIlogPlcEvents(Random rng, int targetSize)
    {
        var profile = IlogProfile.INTEGRITY;
        var encoder = new IlogEncoder();

        var events = new List<IlogEvent>();
        long timestamp = 1704067200000; // 2024-01-01 timestamp
        int currentSize = 0;
        int eventIndex = 0;

        var tags = new[] { "PUMP", "VALVE", "SENSOR", "MOTOR", "HEATER", "COOLER", "SWITCH", "RELAY" };
        var levels = new[] { "INFO", "WARN", "ERROR", "DEBUG", "TRACE" };

        while (currentSize < targetSize * 0.95)
        {
            string tag = tags[rng.Next(tags.Length)];
            string level = levels[rng.Next(levels.Length)];
            var payload = BuildPlcEventPayload(rng, eventIndex, tag, level);

            var evt = new IlogEvent
            {
                Timestamp = timestamp,
                Level = level,
                Tag = tag,
                Payload = payload
            };

            events.Add(evt);
            currentSize += 32 + evt.Tag.Length + evt.Level.Length + payload.Length;
            timestamp += rng.Next(100, 5000);
            eventIndex++;
        }

        // Serialize events to bytes
        var eventData = SerializeEvents(events);

        // Encode using IlogEncoder
        var encoded = encoder.Encode(eventData, profile);

        if (encoded == null)
        {
            throw new InvalidOperationException("Failed to generate PLC events log");
        }

        return encoded;
    }

    private static byte[] BuildPlcEventPayload(Random rng, int eventIndex, string tag, string level)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        int station = (eventIndex % 12) + 1;
        int line = (eventIndex % 4) + 1;
        int batch = 2000 + (eventIndex % 37);
        int recipe = 100 + (eventIndex % 9);
        int phase = eventIndex % 6;

        string template = GetEventTemplate(tag, level, phase);
        string message = string.Format(
            template,
            station,
            line,
            batch,
            recipe,
            20 + (eventIndex % 80),
            950 + (eventIndex % 120),
            phase);

        WriteString(writer, $"station=STN-{station:D2};line=L{line};tag={tag};level={level};");
        WriteString(writer, $"batch=B{batch:D4};recipe=RCP-{recipe:D3};shift={(eventIndex % 3) + 1};");
        WriteString(writer, $"message={message}");

        // Repeating calibration and state segments to make long-term archive compression meaningful.
        for (int i = 0; i < 4; i++)
        {
            WriteString(writer,
                $"cfg[{i}]=gain:{1.000 + (i * 0.125):F3};offset:{(station * 16) + i:D4};limit:{(recipe * 8) + i:D4};");
        }

        int sampleCount = 12 + (eventIndex % 6);
        for (int i = 0; i < sampleCount; i++)
        {
            int value = 1200 + ((eventIndex * 13) + (i * 17) + station * 11) % 900;
            WriteString(writer, $"sample[{i:D2}]={value:D4};");
        }

        // Keep a small high-entropy suffix so the dataset is not unrealistically easy to compress.
        int entropyLen = 8 + (eventIndex % 16);
        var entropy = new byte[entropyLen];
        rng.NextBytes(entropy);
        writer.Write(entropy);

        return ms.ToArray();
    }

    private static string GetEventTemplate(string tag, string level, int phase) =>
        (tag, level, phase) switch
        {
            ("PUMP", "WARN", _) => "Pump station {0:D2} line {1} pressure drift detected in batch {2:D4}",
            ("VALVE", "ERROR", _) => "Valve manifold station {0:D2} failed transition during recipe {3:D3}",
            ("SENSOR", _, 0) => "Sensor array station {0:D2} completed calibration pass for batch {2:D4}",
            ("MOTOR", "DEBUG", _) => "Motor line {1} reported nominal torque envelope for phase {6}",
            ("HEATER", _, 3) => "Heater cell station {0:D2} recovered to thermal setpoint during batch {2:D4}",
            ("COOLER", _, _) => "Cooling loop station {0:D2} stabilized flow margin for recipe {3:D3}",
            ("SWITCH", _, _) => "Safety switch rack line {1} acknowledged state transition for phase {6}",
            ("RELAY", _, _) => "Relay bank station {0:D2} synchronized output map for batch {2:D4}",
            _ => "Process cell station {0:D2} line {1} recorded event for batch {2:D4}"
        };

    private static void WriteString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)'\n');
    }

    /// <summary>
    /// Serialize IlogEvent list to binary format for IlogEncoder.
    /// Simple format: timestamp(8) + level_len(2) + level + tag_len(2) + tag + payload_len(4) + payload
    /// </summary>
    private static byte[] SerializeEvents(List<IlogEvent> events)
    {
        using var ms = new MemoryStream();
        foreach (var evt in events)
        {
            ms.Write(BitConverter.GetBytes(evt.Timestamp), 0, 8);
            var levelBytes = System.Text.Encoding.UTF8.GetBytes(evt.Level);
            ms.Write(BitConverter.GetBytes((ushort)levelBytes.Length), 0, 2);
            ms.Write(levelBytes, 0, levelBytes.Length);
            var tagBytes = System.Text.Encoding.UTF8.GetBytes(evt.Tag);
            ms.Write(BitConverter.GetBytes((ushort)tagBytes.Length), 0, 2);
            ms.Write(tagBytes, 0, tagBytes.Length);
            ms.Write(BitConverter.GetBytes(evt.Payload.Length), 0, 4);
            ms.Write(evt.Payload, 0, evt.Payload.Length);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Generate realistic update manifest with chunk list, dependencies, and metadata.
    /// </summary>
    private static byte[] GenerateIupdManifest(Random rng, int targetSize)
    {
        var profile = IupdProfile.OPTIMIZED;
        var manifest = new IupdManifest();

        int currentSize = 0;
        int chunkCount = 0;

        while (currentSize < targetSize * 0.95 && chunkCount < 10000)
        {
            int chunkSize = rng.Next(10 * 1024, 100 * 1024);
            if (currentSize + chunkSize > targetSize)
            {
                chunkSize = Math.Min(chunkSize, (int)(targetSize * 0.95) - currentSize);
            }

            var chunkData = GenerateFirmwareLikeChunk(rng, chunkCount, chunkSize);

            var chunk = new IupdChunk
            {
                ChunkId = $"chunk_{chunkCount:D6}",
                Data = chunkData,
                Metadata = new Dictionary<string, string>
                {
                    { "version", "1.0.0" },
                    { "compressed", profile == IupdProfile.OPTIMIZED ? "true" : "false" },
                    { "timestamp", (638396640000000000L + chunkCount * 10_000_000L).ToString() }
                }
            };

            manifest.Chunks.Add(chunk);
            currentSize += chunkSize;
            chunkCount++;
        }

        // Build IUPD file using IupdWriter
        var writer = new IupdWriter();
        writer.SetProfile(profile);

        // Add chunks from manifest
        for (uint i = 0; i < manifest.Chunks.Count; i++)
        {
            writer.AddChunk(i, manifest.Chunks[(int)i].Data);
        }

        // Set apply order (simple sequential order)
        var applyOrder = new uint[manifest.Chunks.Count];
        for (uint i = 0; i < manifest.Chunks.Count; i++)
            applyOrder[i] = i;
        writer.SetApplyOrder(applyOrder);
        writer.WithUpdateSequence(1);

        // Build and return
        return writer.Build();
    }

    private static byte[] GenerateFirmwareLikeChunk(Random rng, int chunkIndex, int chunkSize)
    {
        if (chunkSize <= 0)
            return Array.Empty<byte>();

        using var ms = new MemoryStream(chunkSize);

        WriteFirmwareHeader(ms, chunkIndex, chunkSize);

        int patternSeed = chunkIndex % 7;
        byte[] codeBlock = BuildRepeatingBlock(
            Encoding.ASCII.GetBytes($"FUNC_{patternSeed:D2}_MOV R0,R1\nCMP R0,#0x{patternSeed + 16:X2}\nBNE retry\n"),
            Math.Max(12 * 1024, chunkSize / 4));
        WriteClamped(ms, codeBlock, chunkSize);

        byte[] manifestText = BuildManifestText(chunkIndex, Math.Max(2048, chunkSize / 8));
        WriteClamped(ms, manifestText, chunkSize);

        byte[] zeroPad = new byte[Math.Max(16 * 1024, chunkSize / 5)];
        WriteClamped(ms, zeroPad, chunkSize);

        byte[] calibration = BuildCalibrationBlock(chunkIndex, Math.Max(1024, chunkSize / 12));
        WriteClamped(ms, calibration, chunkSize);

        byte[] lookupTable = BuildLookupTable(chunkIndex, Math.Max(2048, chunkSize / 12));
        WriteClamped(ms, lookupTable, chunkSize);

        int remaining = chunkSize - (int)ms.Length;
        if (remaining > 0)
        {
            int randomTail = Math.Max(remaining / 16, Math.Min(remaining, 1024));
            randomTail = Math.Min(randomTail, remaining);
            int patternedTail = remaining - randomTail;

            if (patternedTail > 0)
            {
                byte[] footer = BuildRepeatingBlock(
                    Encoding.ASCII.GetBytes($"CRC_PLACEHOLDER_{chunkIndex:D6}|BOOT_OK|"),
                    patternedTail);
                ms.Write(footer, 0, footer.Length);
            }

            if (randomTail > 0)
            {
                byte[] entropy = new byte[randomTail];
                rng.NextBytes(entropy);
                ms.Write(entropy, 0, entropy.Length);
            }
        }

        return ms.ToArray();
    }

    private static void WriteFirmwareHeader(Stream stream, int chunkIndex, int chunkSize)
    {
        string header =
            $"IFW2|chunk={chunkIndex:D6}|target={chunkSize}|board=PLC-A7|slot={(chunkIndex % 4) + 1}|version=2026.03.{(chunkIndex % 28) + 1:D2}\n";
        byte[] bytes = Encoding.ASCII.GetBytes(header);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] BuildRepeatingBlock(byte[] seed, int targetLength)
    {
        if (targetLength <= 0)
            return Array.Empty<byte>();

        byte[] output = new byte[targetLength];
        for (int i = 0; i < output.Length; i++)
            output[i] = seed[i % seed.Length];
        return output;
    }

    private static byte[] BuildLookupTable(int chunkIndex, int targetLength)
    {
        if (targetLength <= 0)
            return Array.Empty<byte>();

        byte[] output = new byte[targetLength];
        for (int i = 0; i < output.Length; i++)
            output[i] = (byte)((i * 13 + chunkIndex * 17) & 0xFF);
        return output;
    }

    private static byte[] BuildManifestText(int chunkIndex, int targetLength)
    {
        if (targetLength <= 0)
            return Array.Empty<byte>();

        string text =
            "{\n" +
            $"  \"chunk\": \"chunk_{chunkIndex:D6}\",\n" +
            "  \"device\": \"plc-controller\",\n" +
            "  \"features\": [\"boot\", \"safety\", \"telemetry\", \"recipes\"],\n" +
            "  \"limits\": {\"temp\": 85, \"voltage\": 24, \"current\": 10},\n" +
            "  \"flags\": \"AAAAABBBBBCCCCCDDDDDEEEEEFFFFF\"\n" +
            "}\n";
        return BuildRepeatingBlock(Encoding.UTF8.GetBytes(text), targetLength);
    }

    private static byte[] BuildCalibrationBlock(int chunkIndex, int targetLength)
    {
        if (targetLength <= 0)
            return Array.Empty<byte>();

        using var ms = new MemoryStream(targetLength);
        int row = 0;
        while (ms.Length < targetLength)
        {
            string line = $"CAL,{chunkIndex:D6},{row:D4},{1000 + (row % 97)},{2000 + (row % 31)},{3000 + (row % 17)}\n";
            byte[] bytes = Encoding.ASCII.GetBytes(line);
            int remaining = targetLength - (int)ms.Length;
            int count = Math.Min(bytes.Length, remaining);
            ms.Write(bytes, 0, count);
            row++;
        }

        return ms.ToArray();
    }

    private static void WriteClamped(Stream stream, byte[] data, int limit)
    {
        int remaining = limit - (int)stream.Length;
        if (remaining <= 0 || data.Length == 0)
            return;

        int count = Math.Min(remaining, data.Length);
        stream.Write(data, 0, count);
    }
}

/// <summary>
/// Simple ILOG event structure for generation.
/// </summary>
public class IlogEvent
{
    public long Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Tag { get; set; } = "";
    public byte[] Payload { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Simple IUPD manifest structure for generation.
/// </summary>
public class IupdManifest
{
    public List<IupdChunk> Chunks { get; set; } = new();
}

/// <summary>
/// Simple chunk structure for IUPD manifest.
/// </summary>
public class IupdChunk
{
    public string ChunkId { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public Dictionary<string, string> Metadata { get; set; } = new();
}
