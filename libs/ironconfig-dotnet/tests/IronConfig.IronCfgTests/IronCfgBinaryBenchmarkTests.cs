using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using IronConfig.IronCfg;
using MessagePack;
using ProtoBuf;

namespace IronConfig.Tests;

/// <summary>
/// Binary Format Benchmark: IronCfg vs MessagePack vs Protocol Buffers
/// Real competitors - all binary, all optimized for serialization
/// </summary>
public class IronCfgBinaryBenchmarkTests
{
    [MessagePackObject]
    public class ConfigEntry
    {
        [Key(0)]
        public int Id { get; set; }

        [Key(1)]
        public string Name { get; set; } = "";

        [Key(2)]
        public string Description { get; set; } = "";

        [Key(3)]
        public double Value { get; set; }

        [Key(4)]
        public bool Active { get; set; }

        [Key(5)]
        public long Timestamp { get; set; }

        [Key(6)]
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    [MessagePackObject]
    public class ConfigData
    {
        [Key(0)]
        public Dictionary<string, string> Metadata { get; set; } = new();

        [Key(1)]
        public List<ConfigEntry> Entries { get; set; } = new();
    }

    // Protocol Buffers version
    [ProtoContract]
    public class ProtoBufConfigEntry
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; } = "";

        [ProtoMember(3)]
        public string Description { get; set; } = "";

        [ProtoMember(4)]
        public double Value { get; set; }

        [ProtoMember(5)]
        public bool Active { get; set; }

        [ProtoMember(6)]
        public long Timestamp { get; set; }

        [ProtoMember(7)]
        public string Tags { get; set; } = "";
    }

    [ProtoContract]
    public class ProtoBufConfigData
    {
        [ProtoMember(1)]
        public List<ProtoBufConfigEntry> Entries { get; set; } = new();

        [ProtoMember(2)]
        public string Metadata { get; set; } = "";
    }

    private ConfigData GenerateBinaryData(int targetSizeBytes)
    {
        var config = new ConfigData
        {
            Metadata = new Dictionary<string, string>
            {
                { "version", "1.0.0" },
                { "environment", "production" },
                { "cluster", "cluster-001" },
                { "region", "us-east-1" }
            }
        };

        int entryIndex = 0;
        while (EstimateSize(config) < targetSizeBytes)
        {
            var entry = new ConfigEntry
            {
                Id = entryIndex,
                Name = $"service-{entryIndex:D8}",
                Description = $"Service configuration for monitoring and health checks index {entryIndex}",
                Value = 123.456 + entryIndex,
                Active = entryIndex % 2 == 0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Tags = new[] { "prod", "monitored", $"tier-{entryIndex % 5}", "critical" }
            };

            config.Entries.Add(entry);
            entryIndex++;

            if (entryIndex % 50 == 0 && EstimateSize(config) >= targetSizeBytes * 0.95)
                break;
        }

        return config;
    }

    private int EstimateSize(ConfigData data)
    {
        try
        {
            var bytes = MessagePackSerializer.Serialize(data);
            return bytes.Length;
        }
        catch
        {
            return 0;
        }
    }

    private IronCfgObject ConfigToIronCfg(ConfigData config)
    {
        var fields = new SortedDictionary<uint, IronCfgValue?>();
        uint fieldId = 0;

        fields[fieldId++] = new IronCfgString { Value = string.Join("|", config.Metadata.Select(x => $"{x.Key}={x.Value}")) };

        foreach (var entry in config.Entries)
        {
            fields[fieldId++] = new IronCfgString { Value = $"{entry.Id}|{entry.Name}|{entry.Description}|{entry.Value}|{entry.Active}|{entry.Timestamp}|{string.Join(",", entry.Tags)}" };
        }

        return new IronCfgObject { Fields = fields };
    }

    private ProtoBufConfigData ToProtoBufConfig(ConfigData config)
    {
        var pbConfig = new ProtoBufConfigData
        {
            Metadata = string.Join("|", config.Metadata.Select(x => $"{x.Key}={x.Value}")),
            Entries = config.Entries.Select(e => new ProtoBufConfigEntry
            {
                Id = e.Id,
                Name = e.Name,
                Description = e.Description,
                Value = e.Value,
                Active = e.Active,
                Timestamp = e.Timestamp,
                Tags = string.Join(",", e.Tags)
            }).ToList()
        };
        return pbConfig;
    }

    private IronCfgSchema CreateSchema()
    {
        var fields = new List<IronCfgField>();
        for (uint i = 0; i < 500; i++)
        {
            fields.Add(new IronCfgField
            {
                FieldId = i,
                FieldName = $"field_{i}",
                FieldType = 0x00,
                IsRequired = false
            });
        }
        return new IronCfgSchema { Fields = fields };
    }


    private void RunBinaryBenchmark()
    {
        Console.WriteLine("\n" + new string('═', 130));
        Console.WriteLine("                 BINARY FORMAT BENCHMARK - IRONCFG vs MESSAGEPACK vs PROTOCOL BUFFERS");
        Console.WriteLine(new string('═', 130));

        var sizes = new[] { 10 * 1024, 100 * 1024, 1024 * 1024, 5 * 1024 * 1024 };
        var allResults = new List<BenchmarkResult>();

        foreach (var size in sizes)
        {
            Console.WriteLine($"\n\n{'─', 130}");
            Console.WriteLine($"📊 DATA SIZE: {FormatBytes(size)}");
            Console.WriteLine(new string('─', 130));

            var config = GenerateBinaryData(size);
            Console.WriteLine($"   Generated {config.Entries.Count} entries | Target: {FormatBytes(size)}");
            Console.WriteLine();

            var results = new List<BenchmarkResult>();

            // Benchmark each binary format
            var ironcfgResult = BenchmarkIronCfg(config);
            results.Add(ironcfgResult);

            var msgpackResult = BenchmarkMessagePack(config);
            results.Add(msgpackResult);

            var protobufResult = BenchmarkProtoBuf(config);
            results.Add(protobufResult);

            allResults.AddRange(results);

            // Display detailed comparison
            DisplayDetailedComparison(results);
        }

        // Final Summary
        DisplayFinalSummary(allResults);
    }

    private BenchmarkResult BenchmarkIronCfg(ConfigData config)
    {
        try
        {
            var schema = CreateSchema();
            var root = ConfigToIronCfg(config);

            // Encode
            byte[] encoded = new byte[50 * 1024 * 1024];
            var encodeStopwatch = Stopwatch.StartNew();
            var encodeErr = IronCfgEncoder.Encode(root, schema, true, false, encoded, out int encodedSize);
            encodeStopwatch.Stop();

            if (!encodeErr.IsOk)
                throw new InvalidOperationException("Encode failed");

            double encodeSpeed = encodedSize / (1024.0 * 1024.0) / Math.Max(encodeStopwatch.Elapsed.TotalSeconds, 0.001);

            // Decode
            var memory = new ReadOnlyMemory<byte>(encoded, 0, encodedSize);
            var decodeStopwatch = Stopwatch.StartNew();
            var openErr = IronCfgValidator.Open(memory, out var view);
            decodeStopwatch.Stop();

            if (!openErr.IsOk)
                throw new InvalidOperationException("Decode failed");

            double decodeSpeed = encodedSize / (1024.0 * 1024.0) / Math.Max(decodeStopwatch.Elapsed.TotalSeconds, 0.001);

            return new BenchmarkResult
            {
                Format = "IronCfg",
                EncodedSize = encodedSize,
                EncodeTimeMs = encodeStopwatch.Elapsed.TotalMilliseconds,
                DecodeTimeMs = decodeStopwatch.Elapsed.TotalMilliseconds,
                EncodeSpeedMBps = encodeSpeed,
                DecodeSpeedMBps = decodeSpeed
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️  IronCfg error: {ex.Message}");
            return CreateFailedResult("IronCfg");
        }
    }

    private BenchmarkResult BenchmarkMessagePack(ConfigData config)
    {
        try
        {
            // Encode
            var encodeStopwatch = Stopwatch.StartNew();
            byte[] encoded = MessagePackSerializer.Serialize(config);
            encodeStopwatch.Stop();

            if (encoded == null || encoded.Length == 0)
                throw new InvalidOperationException("Serialization produced empty result");

            double encodeSpeed = encoded.Length / (1024.0 * 1024.0) / Math.Max(encodeStopwatch.Elapsed.TotalSeconds, 0.001);

            // Decode
            var decodeStopwatch = Stopwatch.StartNew();
            var decoded = MessagePackSerializer.Deserialize<ConfigData>(encoded);
            decodeStopwatch.Stop();

            if (decoded == null)
                throw new InvalidOperationException("Deserialization failed");

            double decodeSpeed = encoded.Length / (1024.0 * 1024.0) / Math.Max(decodeStopwatch.Elapsed.TotalSeconds, 0.001);

            return new BenchmarkResult
            {
                Format = "MessagePack",
                EncodedSize = encoded.Length,
                EncodeTimeMs = encodeStopwatch.Elapsed.TotalMilliseconds,
                DecodeTimeMs = decodeStopwatch.Elapsed.TotalMilliseconds,
                EncodeSpeedMBps = encodeSpeed,
                DecodeSpeedMBps = decodeSpeed
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️  MessagePack error: {ex.Message}");
            return CreateFailedResult("MessagePack");
        }
    }

    private BenchmarkResult BenchmarkProtoBuf(ConfigData config)
    {
        try
        {
            var pbConfig = ToProtoBufConfig(config);

            // Encode
            var encodeStopwatch = Stopwatch.StartNew();
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, pbConfig);
                byte[] encoded = ms.ToArray();
                encodeStopwatch.Stop();

                double encodeSpeed = encoded.Length / (1024.0 * 1024.0) / Math.Max(encodeStopwatch.Elapsed.TotalSeconds, 0.001);

                // Decode
                var decodeStopwatch = Stopwatch.StartNew();
                ms.Position = 0;
                var decoded = Serializer.Deserialize<ProtoBufConfigData>(ms);
                decodeStopwatch.Stop();

                double decodeSpeed = encoded.Length / (1024.0 * 1024.0) / Math.Max(decodeStopwatch.Elapsed.TotalSeconds, 0.001);

                return new BenchmarkResult
                {
                    Format = "ProtoBuf",
                    EncodedSize = encoded.Length,
                    EncodeTimeMs = encodeStopwatch.Elapsed.TotalMilliseconds,
                    DecodeTimeMs = decodeStopwatch.Elapsed.TotalMilliseconds,
                    EncodeSpeedMBps = encodeSpeed,
                    DecodeSpeedMBps = decodeSpeed
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️  ProtoBuf error: {ex.Message}");
            return CreateFailedResult("ProtoBuf");
        }
    }

    private BenchmarkResult CreateFailedResult(string format)
    {
        return new BenchmarkResult
        {
            Format = $"{format} (FAILED)",
            EncodedSize = 0,
            EncodeTimeMs = 0,
            DecodeTimeMs = 0,
            EncodeSpeedMBps = 0,
            DecodeSpeedMBps = 0
        };
    }

    private void DisplayDetailedComparison(List<BenchmarkResult> results)
    {
        var sorted = results.OrderBy(r => r.EncodedSize).ToList();

        Console.WriteLine("┌─ BINARY FORMAT COMPARISON ────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Format      │ Size (KB)  │ Encode (MB/s) │ Decode (MB/s) │ E-Time (ms) │ D-Time (ms) │ Total (ms) │");
        Console.WriteLine("├─────────────┼────────────┼───────────────┼───────────────┼─────────────┼─────────────┼────────────┤");

        foreach (var result in sorted)
        {
            var sizeKb = result.EncodedSize / 1024.0;
            var encSpeed = result.EncodeSpeedMBps > 0 ? result.EncodeSpeedMBps.ToString("F0") : "FAIL";
            var decSpeed = result.DecodeSpeedMBps > 0 ? result.DecodeSpeedMBps.ToString("F0") : "FAIL";
            var totalTime = result.EncodeTimeMs + result.DecodeTimeMs;

            Console.WriteLine($"│ {result.Format,-11} │ {sizeKb:F1}     │ {encSpeed,-13} │ {decSpeed,-13} │ {result.EncodeTimeMs:F2}      │ {result.DecodeTimeMs:F2}      │ {totalTime:F2}     │");
        }

        Console.WriteLine("└─────────────┴────────────┴───────────────┴───────────────┴─────────────┴─────────────┴────────────┘");

        // Winners
        var validResults = results.Where(r => r.EncodeSpeedMBps > 0).ToList();
        if (validResults.Any())
        {
            Console.WriteLine("\n   🏆 Winners (this size):");
            Console.WriteLine($"      • Fastest Encode:  {validResults.OrderByDescending(r => r.EncodeSpeedMBps).First().Format} ({validResults.OrderByDescending(r => r.EncodeSpeedMBps).First().EncodeSpeedMBps:F0} MB/s)");
            Console.WriteLine($"      • Fastest Decode:  {validResults.OrderByDescending(r => r.DecodeSpeedMBps).First().Format} ({validResults.OrderByDescending(r => r.DecodeSpeedMBps).First().DecodeSpeedMBps:F0} MB/s)");
            Console.WriteLine($"      • Smallest Size:   {validResults.OrderBy(r => r.EncodedSize).First().Format} ({FormatBytes(validResults.OrderBy(r => r.EncodedSize).First().EncodedSize)})");
        }
    }

    private void DisplayFinalSummary(List<BenchmarkResult> allResults)
    {
        Console.WriteLine("\n\n" + new string('═', 130));
        Console.WriteLine("                                    FINAL SUMMARY - BINARY FORMAT COMPARISON");
        Console.WriteLine(new string('═', 130));

        var byFormat = allResults.Where(r => r.EncodeSpeedMBps > 0).GroupBy(r => r.Format).ToList();

        if (!byFormat.Any())
        {
            Console.WriteLine("\n❌ No valid benchmark results to summarize.");
            return;
        }

        Console.WriteLine("\n📊 AVERAGE PERFORMANCE ACROSS ALL SIZES:\n");
        Console.WriteLine("┌──────────────────┬──────────────┬──────────────┬──────────────┬──────────────┐");
        Console.WriteLine("│ Format           │ Avg Size(KB) │ Avg Enc(MB/s)│ Avg Dec(MB/s)│ Size vs Best │");
        Console.WriteLine("├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤");

        var bestSize = byFormat.Min(g => g.Average(r => r.EncodedSize));

        foreach (var group in byFormat.OrderByDescending(g => g.Average(r => r.DecodeSpeedMBps)))
        {
            var avgSize = group.Average(r => r.EncodedSize) / 1024.0;
            var avgEnc = group.Average(r => r.EncodeSpeedMBps);
            var avgDec = group.Average(r => r.DecodeSpeedMBps);
            var sizeRatio = group.Average(r => r.EncodedSize) / bestSize;
            var overhead = (sizeRatio - 1) * 100;

            Console.WriteLine($"│ {group.Key,-16} │ {avgSize:F1}       │ {avgEnc:F0}        │ {avgDec:F0}        │ +{overhead:F1}%     │");
        }
        Console.WriteLine("└──────────────────┴──────────────┴──────────────┴──────────────┴──────────────┘");

        Console.WriteLine("\n🎯 FINAL RECOMMENDATIONS:\n");

        var bestEnc = byFormat.OrderByDescending(g => g.Average(r => r.EncodeSpeedMBps)).First();
        var bestDec = byFormat.OrderByDescending(g => g.Average(r => r.DecodeSpeedMBps)).First();
        var bestSz = byFormat.OrderBy(g => g.Average(r => r.EncodedSize)).First();

        Console.WriteLine($"   ⚡ ENCODE SPEED:    {bestEnc.Key} ({bestEnc.Average(r => r.EncodeSpeedMBps):F0} MB/s avg)");
        Console.WriteLine($"   🚀 DECODE SPEED:    {bestDec.Key} ({bestDec.Average(r => r.DecodeSpeedMBps):F0} MB/s avg)");
        Console.WriteLine($"   📦 FILE SIZE:       {bestSz.Key} ({bestSz.Average(r => r.EncodedSize) / 1024.0:F1} KB avg)");
        Console.WriteLine($"   ⚖️  BALANCED CHOICE: {bestDec.Key} (best decode + competitive sizes)");

        Console.WriteLine("\n" + new string('═', 130) + "\n");
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:F2} {sizes[order]}";
    }

    private class BenchmarkResult
    {
        public string Format { get; set; } = "";
        public int EncodedSize { get; set; }
        public double EncodeTimeMs { get; set; }
        public double DecodeTimeMs { get; set; }
        public double EncodeSpeedMBps { get; set; }
        public double DecodeSpeedMBps { get; set; }
    }
}
