#!/usr/bin/env dotnet-script
// Dataset Generator for IRONFAMILY Benchmarking
// Usage: dotnet script dataset_generator.csx --output benchmarks/datasets

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

// Configuration
var outputDir = Args.FirstOrDefault("--output", "benchmarks/datasets");
var sizes = new[] { 1_000, 100_000, 1_000_000, 10_000_000, 50_000_000 };
var sizeNames = new[] { "1kb", "100kb", "1mb", "10mb", "50mb" };

// Create directories
Directory.CreateDirectory($"{outputDir}/config");
Directory.CreateDirectory($"{outputDir}/logs");
Directory.CreateDirectory($"{outputDir}/patches");

Console.WriteLine("📊 IRONFAMILY Dataset Generator");
Console.WriteLine("================================\n");

// Generate Config Datasets
Console.WriteLine("🔧 Generating configuration datasets...");
for (int i = 0; i < sizes.Length; i++)
{
    var configData = GenerateConfigData(sizes[i]);
    var jsonFile = $"{outputDir}/config/config_{sizeNames[i]}.json";
    File.WriteAllText(jsonFile, JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true }));
    var info = new FileInfo(jsonFile);
    Console.WriteLine($"  ✓ {sizeNames[i]:>6} config: {info.Length:N0} bytes ({Path.GetFileName(jsonFile)})");
}

// Generate Log Datasets
Console.WriteLine("\n📝 Generating log datasets...");
for (int i = 0; i < sizes.Length; i++)
{
    var logData = GenerateLogData(sizes[i]);
    var logFile = $"{outputDir}/logs/logs_{sizeNames[i]}.jsonl";
    using (var writer = new StreamWriter(logFile))
    {
        foreach (var entry in logData)
        {
            writer.WriteLine(JsonSerializer.Serialize(entry));
        }
    }
    var info = new FileInfo(logFile);
    Console.WriteLine($"  ✓ {sizeNames[i]:>6} logs:   {info.Length:N0} bytes ({Path.GetFileName(logFile)})");
}

// Generate Patch Datasets
Console.WriteLine("\n📦 Generating patch datasets...");
for (int i = 0; i < sizes.Length; i++)
{
    var patchData = GeneratePatchData(sizes[i]);
    var patchFile = $"{outputDir}/patches/patches_{sizeNames[i]}.json";
    File.WriteAllText(patchFile, JsonSerializer.Serialize(patchData, new JsonSerializerOptions { WriteIndented = true }));
    var info = new FileInfo(patchFile);
    Console.WriteLine($"  ✓ {sizeNames[i]:>6} patches: {info.Length:N0} bytes ({Path.GetFileName(patchFile)})");
}

Console.WriteLine("\n✅ Dataset generation complete!");
Console.WriteLine($"📁 Output directory: {Path.GetFullPath(outputDir)}\n");

// Helper functions
object GenerateConfigData(int targetSize)
{
    var rand = new Random(42); // Deterministic seed
    var config = new
    {
        version = "1.0.0",
        timestamp = DateTime.UtcNow.ToString("O"),
        services = GenerateServices(targetSize, rand),
        flags = GenerateFeatureFlags(targetSize, rand),
        limits = GenerateLimits(targetSize, rand),
        metadata = new { generator = "ironfamily-benchmark", target_size = targetSize }
    };
    return config;
}

object[] GenerateServices(int targetSize, Random rand)
{
    var services = new List<object>();
    var estSize = 0;

    while (estSize < targetSize)
    {
        var service = new
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 8),
            name = $"service_{rand.Next(1000)}",
            endpoints = new[]
            {
                new { host = $"host{rand.Next(100)}.local", port = 5000 + rand.Next(100), weight = rand.Next(1, 10) },
                new { host = $"host{rand.Next(100)}.local", port = 5000 + rand.Next(100), weight = rand.Next(1, 10) }
            },
            config = new { timeout_ms = 1000 + rand.Next(5000), retries = rand.Next(1, 5) }
        };

        services.Add(service);
        estSize += 200; // Rough estimate
    }

    return services.ToArray();
}

object GenerateFeatureFlags(int targetSize, Random rand)
{
    var flags = new Dictionary<string, object>();
    var count = Math.Max(10, targetSize / 100);

    for (int i = 0; i < count; i++)
    {
        flags[$"feature_{i:D4}"] = new
        {
            enabled = rand.Next(2) == 0,
            rollout_percentage = rand.Next(0, 101),
            variants = new[] { "control", "variant_a", "variant_b" }[rand.Next(3)]
        };
    }

    return flags;
}

object GenerateLimits(int targetSize, Random rand)
{
    return new
    {
        max_connections = 1000 + rand.Next(9000),
        max_requests_per_sec = 100 + rand.Next(9900),
        max_body_size = 1_000_000 + rand.Next(9_000_000),
        timeout_sec = 5 + rand.Next(55),
        burst_size = 10 + rand.Next(90)
    };
}

object[] GenerateLogData(int targetSize)
{
    var logs = new List<object>();
    var rand = new Random(42);
    var estSize = 0;
    var levels = new[] { "DEBUG", "INFO", "WARN", "ERROR", "FATAL" };

    while (estSize < targetSize)
    {
        var log = new
        {
            timestamp = DateTime.UtcNow.AddSeconds(-rand.Next(86400)).ToString("O"),
            level = levels[rand.Next(levels.Length)],
            logger = $"component.{rand.Next(20)}",
            message = GenerateLogMessage(rand),
            context = new {
                request_id = Guid.NewGuid().ToString("N").Substring(0, 16),
                user_id = rand.Next(10000),
                duration_ms = rand.Next(1000)
            }
        };

        logs.Add(log);
        estSize += 150; // Rough estimate
    }

    return logs.ToArray();
}

string GenerateLogMessage(Random rand)
{
    var templates = new[]
    {
        "Request processed in {0}ms",
        "Cache hit for key: {0}",
        "Database query executed: {0}",
        "User logged in: {0}",
        "Error occurred: {0}",
        "Configuration loaded from {0}",
        "Service health check passed",
        "Rate limit exceeded for {0}"
    };

    return string.Format(templates[rand.Next(templates.Length)],
        rand.Next(10000));
}

object GeneratePatchData(int targetSize)
{
    var rand = new Random(42);
    var manifest = new
    {
        version = "2.0.0",
        from_version = "1.0.0",
        timestamp = DateTime.UtcNow.ToString("O"),
        chunks = GenerateChunks(targetSize, rand),
        metadata = new { generator = "ironfamily-benchmark", target_size = targetSize }
    };
    return manifest;
}

object[] GenerateChunks(int targetSize, Random rand)
{
    var chunks = new List<object>();
    var estSize = 0;
    var chunkId = 0;

    while (estSize < targetSize)
    {
        var chunk = new
        {
            id = chunkId++,
            hash = GenerateHash(rand),
            size = 1000 + rand.Next(10000),
            compression = rand.Next(2) == 0 ? "none" : "zstd",
            dependencies = chunkId > 3 ? new[] { rand.Next(Math.Max(0, chunkId - 3)) } : new int[] { }
        };

        chunks.Add(chunk);
        estSize += chunk.size;
    }

    return chunks.ToArray();
}

string GenerateHash(Random rand)
{
    var bytes = new byte[32];
    rand.NextBytes(bytes);
    return Convert.ToHexString(bytes).ToLower();
}

// Simple argument parser
static class Extensions
{
    public static string FirstOrDefault(this IEnumerable<string> args, string flag, string defaultValue)
    {
        var index = args.ToList().IndexOf(flag);
        return index >= 0 && index + 1 < args.Count()
            ? args.ElementAt(index + 1)
            : defaultValue;
    }
}
