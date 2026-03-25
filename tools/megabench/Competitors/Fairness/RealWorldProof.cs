using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IronFamily.MegaBench.Competitors.Fairness;

/// <summary>
/// Real-world dataset proof: semantic hash consistency across roundtrips.
/// Ensures that real-world payloads are deterministic and not just padding.
/// </summary>
public class RealWorldProof
{
    [JsonPropertyName("datasetId")]
    public string DatasetId { get; set; } = "";

    [JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; set; } = "";

    [JsonPropertyName("canonicalJsonSha256")]
    public string CanonicalJsonSha256 { get; set; } = "";

    [JsonPropertyName("payloadBytes")]
    public long PayloadBytes { get; set; }

    [JsonPropertyName("minBytesSatisfied")]
    public bool MinBytesSatisfied { get; set; }

    [JsonPropertyName("distinctByteRatio")]
    public double DistinctByteRatio { get; set; }

    [JsonPropertyName("entropyQualityPass")]
    public bool EntropyQualityPass { get; set; }

    [JsonPropertyName("deterministicAcrossRuns")]
    public bool DeterministicAcrossRuns { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    /// <summary>
    /// Compute payload SHA256.
    /// </summary>
    public static string ComputePayloadHash(byte[] payload)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(payload);
            return Convert.ToHexString(hash);
        }
    }

    /// <summary>
    /// Compute distinct byte ratio (entropy quality check).
    /// Rule: must have >= 0.20 distinct bytes to avoid being "just padding".
    /// </summary>
    public static double ComputeDistinctByteRatio(byte[] payload)
    {
        if (payload.Length == 0)
            return 0.0;

        var distinctBytes = new HashSet<byte>(payload);
        return (double)distinctBytes.Count / 256.0;
    }

    /// <summary>
    /// Check if distinct byte ratio passes quality gate (>= 0.20).
    /// </summary>
    public static bool PassesEntropyQuality(byte[] payload)
    {
        double ratio = ComputeDistinctByteRatio(payload);
        return ratio >= 0.20;
    }

    /// <summary>
    /// Create a proof for a real-world dataset.
    /// </summary>
    public static RealWorldProof Create(
        string datasetId,
        byte[] payloadBytes,
        string canonicalJsonSha256)
    {
        return new RealWorldProof
        {
            DatasetId = datasetId,
            PayloadSha256 = ComputePayloadHash(payloadBytes),
            CanonicalJsonSha256 = canonicalJsonSha256,
            PayloadBytes = payloadBytes.Length,
            MinBytesSatisfied = payloadBytes.Length >= 1024,
            DistinctByteRatio = ComputeDistinctByteRatio(payloadBytes),
            EntropyQualityPass = PassesEntropyQuality(payloadBytes),
            DeterministicAcrossRuns = false, // Set to true after verification
            Timestamp = DateTime.UtcNow,
            Notes = null
        };
    }
}
