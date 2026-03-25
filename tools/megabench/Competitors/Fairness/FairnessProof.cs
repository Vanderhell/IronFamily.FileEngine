using System.Text.Json.Serialization;

namespace IronFamily.MegaBench.Competitors.Fairness;

/// <summary>
/// Proof that a codec roundtrip meets fairness requirements (PHASE 4: fairness audit).
/// </summary>
public class FairnessProof
{
    [JsonPropertyName("codecName")]
    public string CodecName { get; set; } = "";

    [JsonPropertyName("codecKind")]
    public string CodecKind { get; set; } = "";

    [JsonPropertyName("payloadBytesIn")]
    public long PayloadBytesIn { get; set; }

    [JsonPropertyName("bytesEncoded")]
    public long BytesEncoded { get; set; }

    [JsonPropertyName("decodedSemanticHashSha256")]
    public string DecodedSemanticHashSha256 { get; set; } = "";

    [JsonPropertyName("expectedSemanticHashSha256")]
    public string ExpectedSemanticHashSha256 { get; set; } = "";

    [JsonPropertyName("semanticRoundtripOk")]
    public bool SemanticRoundtripOk { get; set; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }
}
