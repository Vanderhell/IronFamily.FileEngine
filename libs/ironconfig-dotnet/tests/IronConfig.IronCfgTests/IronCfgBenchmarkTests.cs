using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using IronConfig.IronCfg;
using Newtonsoft.Json;
using MessagePack;
using MessagePack.Resolvers;

namespace IronConfig.Tests;

public class IronCfgBenchmarkTests
{
    private static bool IsFailedFormat(BenchmarkResult result) =>
        result.Format.Contains("(FAILED)", StringComparison.OrdinalIgnoreCase);

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
    public class Config
    {
        [Key(0)]
        public Dictionary<string, string> Metadata { get; set; } = new();
        [Key(1)]
        public List<ConfigEntry> Entries { get; set; } = new();
    }

    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    /// <summary>
    /// Generate realistic configuration data
    /// </summary>
    private Config GenerateConfigData(int targetSizeBytes)
    {
        var config = new Config
        {
            Metadata = new Dictionary<string, string>
            {
                { "version", "1.0.0" },
                { "timestamp", DateTime.UtcNow.ToString("O") },
                { "environment", "production" },
                { "datacenter", "us-east-1" },
                { "region", "primary" },
                { "cluster_id", "cluster-001" }
            }
        };

        int currentSize = Estimate(config);
        int entryIndex = 0;

        while (currentSize < targetSizeBytes)
        {
            var entry = new ConfigEntry
            {
                Id = entryIndex,
                Name = $"service-{entryIndex:D6}",
                Description = $"Configuration entry {entryIndex} with detailed description for service monitoring and health checks",
                Value = 123.456 + entryIndex,
                Active = entryIndex % 2 == 0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Tags = new[] { "production", "monitored", $"tier-{entryIndex % 5}", "critical" }
            };

            config.Entries.Add(entry);
            entryIndex++;

            currentSize = Estimate(config);

            if (entryIndex % 100 == 0 && currentSize >= targetSizeBytes * 0.9)
                break;
        }

        return config;
    }

    private int Estimate(Config config)
    {
        var json = JsonConvert.SerializeObject(config);
        return Encoding.UTF8.GetByteCount(json);
    }

    private IronCfgObject ConfigToIronCfg(Config config)
    {
        var fields = new SortedDictionary<uint, IronCfgValue?>();
        uint fieldId = 0;

        // Metadata as string
        var metadataJson = JsonConvert.SerializeObject(config.Metadata);
        fields[fieldId++] = new IronCfgString { Value = metadataJson };

        // Entries
        foreach (var entry in config.Entries)
        {
            fields[fieldId++] = new IronCfgString { Value = entry.Id.ToString() };
            fields[fieldId++] = new IronCfgString { Value = entry.Name };
            fields[fieldId++] = new IronCfgString { Value = entry.Description };
            fields[fieldId++] = new IronCfgFloat64 { Value = entry.Value };
            fields[fieldId++] = new IronCfgBool { Value = entry.Active };
            fields[fieldId++] = new IronCfgInt64 { Value = entry.Timestamp };
            fields[fieldId++] = new IronCfgString { Value = string.Join(",", entry.Tags) };
        }

        return new IronCfgObject { Fields = fields };
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

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_IronCfg_Comprehensive()
    {
        var outputPath = Path.Combine(Environment.CurrentDirectory, "benchmark_results.txt");
        using (var writer = new StreamWriter(outputPath, false))
        {
            var oldOut = Console.Out;
            Console.SetOut(writer);
            try
            {
                RunBenchmarkInternal();
            }
            finally
            {
                Console.SetOut(oldOut);
            }
        }

        // Also print to console
        if (File.Exists(outputPath))
        {
            var results = File.ReadAllText(outputPath);
            System.Diagnostics.Debug.WriteLine(results);
            Console.WriteLine(results);
        }
    }

    private void RunBenchmarkInternal()
    {
        Console.WriteLine("\n" + new string('═', 120));
        Console.WriteLine("                         CONFIGURATION FORMAT BENCHMARK - COMPREHENSIVE ANALYSIS");
        Console.WriteLine(new string('═', 120));

        var sizes = new[] { 1024, 100 * 1024, 1024 * 1024 };
        var allResults = new List<BenchmarkResult>();

        foreach (var size in sizes)
        {
            Console.WriteLine($"\n\n{'─', 120}");
            Console.WriteLine($"📊 DATA SIZE: {FormatBytes(size)}");
            Console.WriteLine(new string('─', 120));

            var config = GenerateConfigData(size);
            var json = JsonConvert.SerializeObject(config);
            var originalBytes = Encoding.UTF8.GetByteCount(json);

            Console.WriteLine($"   Generated {config.Entries.Count} configuration entries | Original size: {FormatBytes(originalBytes)}");
            Console.WriteLine();

            var results = new List<BenchmarkResult>();

            // Benchmark each format
            var ironcfgResult = BenchmarkIronCfg(config, originalBytes);
            results.Add(ironcfgResult);

            var jsonResult = BenchmarkJson(config, originalBytes);
            results.Add(jsonResult);

            var msgpackResult = BenchmarkMessagePack(config, originalBytes);
            results.Add(msgpackResult);

            allResults.AddRange(results);

            // Display detailed comparison
            DisplayDetailedComparison(results, originalBytes);
        }

        // Final Summary
        DisplayFinalSummary(allResults);
    }

    private BenchmarkResult BenchmarkIronCfg(Config config, int originalSize)
    {
        var schema = CreateSchema();
        var root = ConfigToIronCfg(config);

        // Encode
        byte[] encoded = new byte[50 * 1024 * 1024];
        var encodeStopwatch = Stopwatch.StartNew();
        var encodeErr = IronCfgEncoder.Encode(root, schema, true, false, encoded, out int encodedSize);
        encodeStopwatch.Stop();

        if (!encodeErr.IsOk)
            throw new InvalidOperationException($"Encode failed");

        double encodeSpeed = encodedSize / (1024.0 * 1024.0) / encodeStopwatch.Elapsed.TotalSeconds;

        // Decode
        var memory = new ReadOnlyMemory<byte>(encoded, 0, encodedSize);
        var decodeStopwatch = Stopwatch.StartNew();
        var openErr = IronCfgValidator.Open(memory, out var view);
        decodeStopwatch.Stop();

        if (!openErr.IsOk)
            throw new InvalidOperationException($"Decode failed");

        double decodeSpeed = encodedSize / (1024.0 * 1024.0) / decodeStopwatch.Elapsed.TotalSeconds;

        return new BenchmarkResult
        {
            Format = "IronCfg",
            OriginalSize = originalSize,
            EncodedSize = encodedSize,
            EncodeTimeMs = encodeStopwatch.Elapsed.TotalMilliseconds,
            DecodeTimeMs = decodeStopwatch.Elapsed.TotalMilliseconds,
            EncodeSpeedMBps = encodeSpeed,
            DecodeSpeedMBps = decodeSpeed
        };
    }

    private BenchmarkResult BenchmarkJson(Config config, int originalSize)
    {
        var json = JsonConvert.SerializeObject(config);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Encode
        var encodeStopwatch = Stopwatch.StartNew();
        var encoded = JsonConvert.SerializeObject(config);
        var encodedBytes = Encoding.UTF8.GetBytes(encoded);
        encodeStopwatch.Stop();

        double encodeSpeed = encodedBytes.Length / (1024.0 * 1024.0) / encodeStopwatch.Elapsed.TotalSeconds;

        // Decode
        var decodeStopwatch = Stopwatch.StartNew();
        var decoded = JsonConvert.DeserializeObject<Config>(encoded);
        decodeStopwatch.Stop();

        double decodeSpeed = encodedBytes.Length / (1024.0 * 1024.0) / decodeStopwatch.Elapsed.TotalSeconds;

        return new BenchmarkResult
        {
            Format = "JSON",
            OriginalSize = originalSize,
            EncodedSize = encodedBytes.Length,
            EncodeTimeMs = encodeStopwatch.Elapsed.TotalMilliseconds,
            DecodeTimeMs = decodeStopwatch.Elapsed.TotalMilliseconds,
            EncodeSpeedMBps = encodeSpeed,
            DecodeSpeedMBps = decodeSpeed
        };
    }

    private BenchmarkResult BenchmarkMessagePack(Config config, int originalSize)
    {
        try
        {
            // Encode
            var encodeStopwatch = Stopwatch.StartNew();
            byte[] encoded = MessagePackSerializer.Serialize(config, MsgPackOptions);
            encodeStopwatch.Stop();

            if (encoded == null || encoded.Length == 0)
                throw new InvalidOperationException("MessagePack encoding produced empty result");

            double encodeSpeed = encoded.Length > 0 ? (encoded.Length / (1024.0 * 1024.0)) / encodeStopwatch.Elapsed.TotalSeconds : 0;

            // Decode
            var decodeStopwatch = Stopwatch.StartNew();
            var decoded = MessagePackSerializer.Deserialize<Config>(encoded, MsgPackOptions);
            decodeStopwatch.Stop();

            if (decoded == null)
                throw new InvalidOperationException("MessagePack decoding produced null");

            double decodeSpeed = encoded.Length > 0 ? (encoded.Length / (1024.0 * 1024.0)) / decodeStopwatch.Elapsed.TotalSeconds : 0;

            return new BenchmarkResult
            {
                Format = "MessagePack",
                OriginalSize = originalSize,
                EncodedSize = encoded.Length,
                EncodeTimeMs = encodeStopwatch.Elapsed.TotalMilliseconds,
                DecodeTimeMs = decodeStopwatch.Elapsed.TotalMilliseconds,
                EncodeSpeedMBps = encodeSpeed,
                DecodeSpeedMBps = decodeSpeed
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️  MessagePack failed: {ex.Message}");
            // Return default so we can see it failed in results
            return new BenchmarkResult
            {
                Format = "MessagePack (FAILED)",
                OriginalSize = originalSize,
                EncodedSize = 0,
                EncodeTimeMs = 0,
                DecodeTimeMs = 0,
                EncodeSpeedMBps = 0,
                DecodeSpeedMBps = 0
            };
        }
    }

    private void DisplayDetailedComparison(List<BenchmarkResult> results, int originalSize)
    {
        var sorted = results.OrderBy(r => r.EncodedSize).ToList();
        var successful = results.Where(r => !IsFailedFormat(r)).ToList();
        var rankingSource = successful.Count > 0 ? successful : results;

        Console.WriteLine("┌─ FORMAT COMPARISON ───────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Format      │ Size        │ % Original  │ Encode (MB/s) │ Decode (MB/s) │ Time (ms)      │");
        Console.WriteLine("├─────────────┼─────────────┼─────────────┼───────────────┼───────────────┼────────────────┤");

        foreach (var result in sorted)
        {
            var sizeKb = result.EncodedSize / 1024.0;
            var ratio = (double)result.EncodedSize / originalSize * 100;
            var totalTime = result.EncodeTimeMs + result.DecodeTimeMs;
            var encSpeed = result.EncodeSpeedMBps > 0 ? result.EncodeSpeedMBps.ToString("F0") : "N/A";
            var decSpeed = result.DecodeSpeedMBps > 0 ? result.DecodeSpeedMBps.ToString("F0") : "N/A";
            Console.WriteLine($"│ {result.Format,-11} │ {sizeKb:F1} KB      │ {ratio:F1}%       │ {encSpeed,-13} │ {decSpeed,-13} │ {totalTime:F2}            │");
        }

        Console.WriteLine("└─────────────┴─────────────┴─────────────┴───────────────┴───────────────┴────────────────┘");

        // Rankings
        Console.WriteLine("\n   Winners:");
        var fastestEncode = rankingSource.OrderByDescending(r => r.EncodeSpeedMBps).First();
        var fastestDecode = rankingSource.OrderByDescending(r => r.DecodeSpeedMBps).First();
        var smallestSize = rankingSource.OrderBy(r => r.EncodedSize).First();
        Console.WriteLine($"      - Fastest Encode:  {fastestEncode.Format} ({fastestEncode.EncodeSpeedMBps:F0} MB/s)");
        Console.WriteLine($"      - Fastest Decode:  {fastestDecode.Format} ({fastestDecode.DecodeSpeedMBps:F0} MB/s)");
        Console.WriteLine($"      - Smallest Size:   {smallestSize.Format} ({FormatBytes(smallestSize.EncodedSize)})");
    }

    private void DisplayFinalSummary(List<BenchmarkResult> allResults)
    {
        Console.WriteLine("\n\n" + new string('═', 120));
        Console.WriteLine("                              FINAL BENCHMARK SUMMARY & RECOMMENDATIONS");
        Console.WriteLine(new string('═', 120));

        var byFormat = allResults.GroupBy(r => r.Format).ToList();
        var successfulGroups = byFormat.Where(g => g.All(r => !IsFailedFormat(r))).ToList();
        var recommendationGroups = successfulGroups.Count > 0 ? successfulGroups : byFormat;

        Console.WriteLine("\n📊 AVERAGE PERFORMANCE METRICS:\n");
        Console.WriteLine("┌──────────────┬──────────────┬──────────────┬──────────────┬──────────────┐");
        Console.WriteLine("│ Format       │ Avg Size (KB)│ Avg Enc (MB/s)│ Avg Dec (MB/s)│ Compression  │");
        Console.WriteLine("├──────────────┼──────────────┼──────────────┼──────────────┼──────────────┤");

        foreach (var group in byFormat.OrderByDescending(g => g.Average(r => r.DecodeSpeedMBps)))
        {
            var avgSize = group.Average(r => r.EncodedSize) / 1024.0;
            var avgEnc = group.Average(r => r.EncodeSpeedMBps);
            var avgDec = group.Average(r => r.DecodeSpeedMBps);
            var avgRatio = group.Average(r => (double)r.EncodedSize / r.OriginalSize) * 100;

            Console.WriteLine($"│ {group.Key,-12} │ {avgSize:F1}        │ {avgEnc:F0}        │ {avgDec:F0}        │ {avgRatio:F1}%      │");
        }
        Console.WriteLine("└──────────────┴──────────────┴──────────────┴──────────────┴──────────────┘");

        Console.WriteLine("\n🎯 RECOMMENDATIONS:\n");

        var bestSpeed = recommendationGroups.OrderByDescending(g => g.Average(r => r.DecodeSpeedMBps)).First();
        var bestSize = recommendationGroups.OrderBy(g => g.Average(r => r.EncodedSize)).First();
        var bestBalance = recommendationGroups.OrderByDescending(g =>
        {
            var speedScore = g.Average(r => r.DecodeSpeedMBps);
            var sizeScore = 1.0 / (g.Average(r => r.EncodedSize / 1024.0) / 1000.0);
            return (speedScore + sizeScore) / 2;
        }).First();

        Console.WriteLine($"   ⚡ SPEED:       Use {bestSpeed.Key,-15} - Avg {bestSpeed.Average(r => r.DecodeSpeedMBps):F0} MB/s decode speed");
        Console.WriteLine($"   📦 COMPRESSION: Use {bestSize.Key,-15} - Avg {bestSize.Average(r => r.EncodedSize) / 1024.0:F1} KB per test");
        Console.WriteLine($"   ⚖️  BALANCED:    Use {bestBalance.Key,-15} - Best trade-off between speed and size");

        Console.WriteLine("\n" + new string('═', 120) + "\n");
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
        public int OriginalSize { get; set; }
        public int EncodedSize { get; set; }
        public double EncodeTimeMs { get; set; }
        public double DecodeTimeMs { get; set; }
        public double EncodeSpeedMBps { get; set; }
        public double DecodeSpeedMBps { get; set; }
    }
}
