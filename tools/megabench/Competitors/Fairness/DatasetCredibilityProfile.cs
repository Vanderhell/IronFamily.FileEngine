using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace IronFamily.MegaBench.Competitors.Fairness;

/// <summary>
/// Dataset credibility metrics for real-world benchmarking.
/// PHASE 4: Validate dataset quality beyond just size and entropy.
/// </summary>
public class DatasetCredibilityProfile
{
    [JsonPropertyName("datasetId")]
    public string DatasetId { get; set; } = "";

    [JsonPropertyName("payloadBytes")]
    public long PayloadBytes { get; set; }

    // Structural complexity
    [JsonPropertyName("distinctKeyCount")]
    public int DistinctKeyCount { get; set; }

    [JsonPropertyName("averageDepth")]
    public double AverageDepth { get; set; }

    [JsonPropertyName("averageStringLength")]
    public double AverageStringLength { get; set; }

    [JsonPropertyName("numericRatio")]
    public double NumericRatio { get; set; }

    [JsonPropertyName("distinctByteRatio")]
    public double DistinctByteRatio { get; set; }

    // Engine-specific counts
    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; set; } // ICFG

    [JsonPropertyName("eventCount")]
    public int EventCount { get; set; } // ILOG

    [JsonPropertyName("chunkCount")]
    public int ChunkCount { get; set; } // IUPD

    // Credibility gates
    [JsonPropertyName("distinctKeysPass")]
    public bool DistinctKeysPass { get; set; } // > 5

    [JsonPropertyName("depthPass")]
    public bool DepthPass { get; set; } // >= 2

    [JsonPropertyName("eventCountPass")]
    public bool EventCountPass { get; set; } // > 50 (ILOG only)

    [JsonPropertyName("entropyPass")]
    public bool EntropyPass { get; set; } // >= 0.10

    [JsonPropertyName("allGatesPass")]
    public bool AllGatesPass { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    /// <summary>
    /// Compute credibility profile for a dataset.
    /// Note: This is a simplified profile based on payload structure hints.
    /// Full implementation would parse semantic content.
    /// </summary>
    public static DatasetCredibilityProfile Create(
        string datasetId,
        byte[] payload,
        int estimatedKeyCount = 10,
        double estimatedDepth = 2.5,
        int estimatedEventCount = 100,
        int estimatedChunkCount = 10)
    {
        double distinctByteRatio = ComputeDistinctByteRatio(payload);
        double stringLength = EstimateStringLength(payload);
        double numericRatio = EstimateNumericRatio(payload);

        var profile = new DatasetCredibilityProfile
        {
            DatasetId = datasetId,
            PayloadBytes = payload.Length,
            DistinctKeyCount = estimatedKeyCount,
            AverageDepth = estimatedDepth,
            AverageStringLength = stringLength,
            NumericRatio = numericRatio,
            DistinctByteRatio = distinctByteRatio,
            NodeCount = estimatedKeyCount, // ICFG estimate
            EventCount = estimatedEventCount, // ILOG estimate
            ChunkCount = estimatedChunkCount, // IUPD estimate
            Timestamp = DateTime.UtcNow
        };

        // Apply gates
        profile.DistinctKeysPass = estimatedKeyCount > 5;
        profile.DepthPass = estimatedDepth >= 2.0;
        profile.EventCountPass = estimatedEventCount > 50;
        profile.EntropyPass = distinctByteRatio >= 0.10;
        profile.AllGatesPass =
            profile.DistinctKeysPass &&
            profile.DepthPass &&
            profile.EventCountPass &&
            profile.EntropyPass;

        return profile;
    }

    private static double ComputeDistinctByteRatio(byte[] payload)
    {
        if (payload.Length == 0)
            return 0.0;
        var distinctBytes = new HashSet<byte>(payload);
        return (double)distinctBytes.Count / 256.0;
    }

    private static double EstimateStringLength(byte[] payload)
    {
        // Heuristic: count ASCII letters/digits, estimate string length
        int stringBytes = payload.Count(b => (b >= 32 && b < 127) || b >= 128);
        return stringBytes > 0 ? (double)stringBytes / 10 : 1.0;
    }

    private static double EstimateNumericRatio(byte[] payload)
    {
        // Heuristic: count ASCII digits
        int digits = payload.Count(b => b >= 48 && b <= 57);
        return payload.Length > 0 ? (double)digits / payload.Length : 0.0;
    }
}
