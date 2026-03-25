using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IronConfig.ILog;
using IronFamily.MegaBench.Validation;

namespace IronFamily.MegaBench.Datasets.ILog;

/// <summary>
/// Deterministic ILOG dataset generator.
///
/// Event schema: timestamp (uint64), level (enum), source (string), message (string), context (map)
/// Sizes: 10KB, 100KB, 1MB, 10MB, 100MB (heavy mode)
/// Profiles: MINIMAL, INTEGRITY, SEARCHABLE, ARCHIVED, AUDITED
/// Uses IRONFAMILY_DETERMINISTIC=1 for fixed seed
/// </summary>
public static class ILogDatasetGenerator
{
    /// <summary>
    /// Generate a deterministic ILOG dataset of specified size.
    /// Throws MegaBenchDatasetException on any failure (no silent returns).
    /// </summary>
    public static byte[] GenerateDataset(string sizeLabel, IlogProfile profile)
    {
        int targetSize = ParseSize(sizeLabel);
        if (targetSize == 0)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.SIZE_PARSE_FAILED,
                $"ilog_{profile}_{sizeLabel}",
                $"Unknown size label: {sizeLabel}",
                engine: "ilog",
                profile: profile.ToString(),
                sizeLabel: sizeLabel);
            ex.LogToArtifacts();
            throw ex;
        }

        // Use fixed seed if IRONFAMILY_DETERMINISTIC=1
        int seed = Environment.GetEnvironmentVariable("IRONFAMILY_DETERMINISTIC") == "1" ? 42 : Random.Shared.Next();
        var rng = new Random(seed);

        // Generate event data to reach target size
        // Simple format: timestamp(8) + level(1) + source_len(2) + source + message_len(2) + message
        byte[] eventData;
        try
        {
            eventData = GenerateEventData(rng, targetSize);
        }
        catch (Exception genEx)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.GENERATOR_EXCEPTION,
                $"ilog_{profile}_{sizeLabel}",
                $"Event data generation failed: {genEx.Message}",
                engine: "ilog",
                profile: profile.ToString(),
                sizeLabel: sizeLabel,
                innerException: genEx);
            ex.LogToArtifacts();
            throw ex;
        }

        byte[] encoded;
        try
        {
            var encoder = new IlogEncoder();

            // For AUDITED profile, provide Ed25519 signing keys via options
            IlogEncodeOptions? options = null;
            if (profile == IlogProfile.AUDITED)
            {
                // Fixed bench keypair for deterministic benchmarking
                var benchPrivateKey = new byte[]
                {
                    0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57,
                    0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
                    0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67,
                    0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F
                };

                var benchPublicKey = new byte[]
                {
                    0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77,
                    0x78, 0x79, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F,
                    0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
                    0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F
                };

                options = new IlogEncodeOptions
                {
                    Ed25519PrivateKey32 = benchPrivateKey.AsMemory(),
                    Ed25519PublicKey32 = benchPublicKey.AsMemory()
                };
            }

            encoded = encoder.Encode(eventData, profile, options);
        }
        catch (Exception encEx)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.ENCODE_FAILED,
                $"ilog_{profile}_{sizeLabel}",
                $"ILOG encoding failed: {encEx.Message}",
                engine: "ilog",
                profile: profile.ToString(),
                sizeLabel: sizeLabel,
                innerException: encEx);
            ex.LogToArtifacts();
            throw ex;
        }

        // Mandatory validation gate
        var datasetId = $"ilog_{profile}_{sizeLabel}";
        var outputDir = Path.Combine(GetRepoRoot(), "artifacts", "bench", "megabench_datasets", datasetId);

        System.IO.Directory.CreateDirectory(outputDir);

        var filePath = System.IO.Path.Combine(outputDir, $"{datasetId}.bin");
        System.IO.File.WriteAllBytes(filePath, encoded);

        var validator = new Validation.IlogDatasetValidator();
        var validationResults = validator.Validate(datasetId, sizeLabel, outputDir, filePath, profile.ToString());
        Validation.ValidationWriter.WriteResults(outputDir, validationResults);

        // Check validation results - strict must pass
        foreach (var result in validationResults)
        {
            if (!result.Passed && result.Mode == "Strict")
            {
                var ex = new MegaBenchDatasetException(
                    DatasetErrorCode.VALIDATION_FAILED,
                    datasetId,
                    $"ILOG validation failed in mode '{result.Mode}': {result.ErrorCode}",
                    engine: "ilog",
                    profile: profile.ToString(),
                    sizeLabel: sizeLabel);
                ex.LogToArtifacts();
                throw ex;
            }
        }

        return encoded;
    }

    /// <summary>
    /// Generate raw event data to reach target byte count.
    /// </summary>
    private static byte[] GenerateEventData(Random rng, int targetSize)
    {
        var events = new List<byte[]>();
        int totalSize = 0;

        while (totalSize < targetSize)
        {
            var eventBytes = GenerateSingleEvent(rng);
            events.Add(eventBytes);
            totalSize += eventBytes.Length;
        }

        // Concatenate all events
        var allEvents = new byte[totalSize];
        int offset = 0;
        foreach (var eventBytes in events)
        {
            Array.Copy(eventBytes, 0, allEvents, offset, eventBytes.Length);
            offset += eventBytes.Length;
        }

        return allEvents;
    }

    /// <summary>
    /// Generate a single raw event (timestamp + level + source + message).
    /// </summary>
    private static byte[] GenerateSingleEvent(Random rng)
    {
        var parts = new List<byte[]>();

        // Timestamp (8 bytes, uint64) - Fixed for determinism
        ulong fixedTimestamp = 1704067200000UL; // 2024-01-01 00:00:00 UTC in ms
        byte[] timestamp = BitConverter.GetBytes(fixedTimestamp);
        parts.Add(timestamp);

        // Level (1 byte, 0-3)
        parts.Add(new[] { (byte)rng.Next(4) });

        // Source string
        string source = $"source_{rng.Next(1000)}";
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        parts.Add(BitConverter.GetBytes((ushort)sourceBytes.Length));
        parts.Add(sourceBytes);

        // Message string
        string message = GenerateMessage(rng);
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        parts.Add(BitConverter.GetBytes((ushort)messageBytes.Length));
        parts.Add(messageBytes);

        // Combine all parts
        int totalLen = parts.Sum(p => p.Length);
        var result = new byte[totalLen];
        int offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }

    /// <summary>
    /// Generate a random message string.
    /// </summary>
    private static string GenerateMessage(Random rng)
    {
        var lengths = new[] { 50, 100, 200, 500 };
        int len = lengths[rng.Next(lengths.Length)];
        var sb = new StringBuilder();
        for (int i = 0; i < len; i++)
        {
            sb.Append((char)('a' + rng.Next(26)));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parse size label (10KB, 100KB, etc.) to bytes.
    /// </summary>
    private static int ParseSize(string label)
    {
        return label switch
        {
            "10KB" => 10 * 1024,
            "100KB" => 100 * 1024,
            "1MB" => 1 * 1024 * 1024,
            "10MB" => 10 * 1024 * 1024,
            "100MB" => 100 * 1024 * 1024,
            _ => 0
        };
    }

    private static string GetRepoRoot()
    {
        string? current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "tools", "megabench", "MegaBench.csproj")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "tools", "megabench", "MegaBench.csproj")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
