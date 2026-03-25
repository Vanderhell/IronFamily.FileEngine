using System.Text.Json.Serialization;
using IronFamily.MegaBench.Bench;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Benchmark result for a single competitor codec.
/// </summary>
public class CompetitorResult
{
    [JsonPropertyName("codecName")]
    public string CodecName { get; set; } = "";

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Profile { get; set; }

    [JsonPropertyName("sizeLabel")]
    public string SizeLabel { get; set; } = "";

    [JsonPropertyName("inputBytes")]
    public long InputBytes { get; set; }

    [JsonPropertyName("encodedBytes")]
    public long EncodedBytes { get; set; }

    [JsonPropertyName("decodedBytes")]
    public long DecodedBytes { get; set; }

    // Memory allocation metrics (PHASE 1)
    [JsonPropertyName("allocBytes")]
    public long AllocBytes { get; set; }

    [JsonPropertyName("gen0")]
    public int Gen0 { get; set; }

    [JsonPropertyName("gen1")]
    public int Gen1 { get; set; }

    [JsonPropertyName("gen2")]
    public int Gen2 { get; set; }

    // Encode statistics
    [JsonPropertyName("encodeSamplesUs")]
    public double[]? EncodeSamplesUs { get; set; }

    [JsonPropertyName("encodeSummary")]
    public StatsSummary? EncodeSummary { get; set; }

    // Decode statistics
    [JsonPropertyName("decodeSamplesUs")]
    public double[]? DecodeSamplesUs { get; set; }

    [JsonPropertyName("decodeSummary")]
    public StatsSummary? DecodeSummary { get; set; }

    // ValidateStrict (if applicable)
    [JsonPropertyName("validateSamplesUs")]
    public double[]? ValidateSamplesUs { get; set; }

    [JsonPropertyName("validateSummary")]
    public StatsSummary? ValidateSummary { get; set; }

    // Sign statistics (ILOG/AUDITED only)
    [JsonPropertyName("signSamplesUs")]
    public double[]? SignSamplesUs { get; set; }

    [JsonPropertyName("signSummary")]
    public StatsSummary? SignSummary { get; set; }

    [JsonPropertyName("signatureLenBytes")]
    public long SignatureLenBytes { get; set; }

    // Witness verify statistics (ILOG/AUDITED only)
    [JsonPropertyName("witnessVerifySamplesUs")]
    public double[]? WitnessVerifySamplesUs { get; set; }

    [JsonPropertyName("witnessVerifySummary")]
    public StatsSummary? WitnessVerifySummary { get; set; }

    [JsonPropertyName("roundtripOk")]
    public bool RoundtripOk { get; set; }

    [JsonPropertyName("excluded")]
    public bool Excluded { get; set; }

    [JsonPropertyName("exclusionReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExclusionReason { get; set; }

    // Normalized metrics (PHASE 4: fairness audit)
    [JsonPropertyName("encodeUsPerKb")]
    public double EncodeUsPerKb { get; set; }

    [JsonPropertyName("decodeUsPerKb")]
    public double DecodeUsPerKb { get; set; }

    [JsonPropertyName("allocBytesPerKb")]
    public double AllocBytesPerKb { get; set; }

    [JsonPropertyName("compressionRatio")]
    public double CompressionRatio { get; set; }

    /// <summary>
    /// Verify result is valid (not excluded and all metrics non-zero).
    /// </summary>
    public bool IsValid()
    {
        if (Excluded)
            return false;

        // All measurements must be present and non-zero
        if (EncodeSummary?.Median <= 0)
            return false;
        if (DecodeSummary?.Median <= 0)
            return false;
        if (!RoundtripOk)
            return false;

        return true;
    }

    /// <summary>
    /// Compute normalized metrics from raw measurements (PHASE 4).
    /// </summary>
    public void ComputeNormalizedMetrics()
    {
        if (InputBytes <= 0)
            return;

        double kbSize = InputBytes / 1024.0;

        // Normalized timing (us/KB)
        if (EncodeSummary?.Median > 0)
            EncodeUsPerKb = EncodeSummary.Median / kbSize;

        if (DecodeSummary?.Median > 0)
            DecodeUsPerKb = DecodeSummary.Median / kbSize;

        // Normalized allocation (bytes/KB)
        if (AllocBytes > 0)
            AllocBytesPerKb = AllocBytes / kbSize;

        // Compression ratio
        if (EncodedBytes > 0)
            CompressionRatio = (double)EncodedBytes / InputBytes;
    }
}
