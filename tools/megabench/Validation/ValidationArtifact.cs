using System;
using System.Text.Json.Serialization;

namespace IronFamily.MegaBench.Validation;

/// <summary>
/// Validation artifact for a single dataset.
/// Mandatory truth gate: strict.ok MUST be true.
/// Used for all engines (IRONCFG, ILOG, IUPD).
/// </summary>
public class ValidationArtifact
{
    [JsonPropertyName("datasetId")]
    public string DatasetId { get; set; } = "";

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Profile { get; set; }

    [JsonPropertyName("sizeLabel")]
    public string SizeLabel { get; set; } = "";

    [JsonPropertyName("generatedUtc")]
    public string GeneratedUtc { get; set; } = DateTime.UtcNow.ToString("O");

    [JsonPropertyName("open")]
    public ValidationMode Open { get; set; } = new();

    [JsonPropertyName("fast")]
    public ValidationMode Fast { get; set; } = new();

    [JsonPropertyName("strict")]
    public ValidationMode Strict { get; set; } = new();

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    /// <summary>
    /// Validate artifact constraints:
    /// - strict.ok MUST be true (truth gate)
    /// - elapsedMs MUST be > 0 for fast and strict
    /// - errorCode consistency
    /// </summary>
    public bool IsValid(out string? errorMessage)
    {
        errorMessage = null;

        // Truth gate: strict must pass
        if (!Strict.Ok)
        {
            errorMessage = $"STRICT validation failed: {Strict.ErrorCode}";
            return false;
        }

        // Elapsed times must be positive
        if (Fast.ElapsedMs <= 0)
        {
            errorMessage = $"FAST elapsedMs must be > 0, got {Fast.ElapsedMs}";
            return false;
        }

        if (Strict.ElapsedMs <= 0)
        {
            errorMessage = $"STRICT elapsedMs must be > 0, got {Strict.ElapsedMs}";
            return false;
        }

        // If ok=true, errorCode should be Ok or null
        if (Open.Ok && !string.IsNullOrEmpty(Open.ErrorCode) && Open.ErrorCode != "Ok")
        {
            errorMessage = $"OPEN: ok=true but errorCode={Open.ErrorCode}";
            return false;
        }

        if (Fast.Ok && !string.IsNullOrEmpty(Fast.ErrorCode) && Fast.ErrorCode != "Ok")
        {
            errorMessage = $"FAST: ok=true but errorCode={Fast.ErrorCode}";
            return false;
        }

        if (Strict.Ok && !string.IsNullOrEmpty(Strict.ErrorCode) && Strict.ErrorCode != "Ok")
        {
            errorMessage = $"STRICT: ok=true but errorCode={Strict.ErrorCode}";
            return false;
        }

        // If ok=false, errorCode must exist
        if (!Open.Ok && string.IsNullOrEmpty(Open.ErrorCode))
        {
            errorMessage = $"OPEN: ok=false but errorCode is missing";
            return false;
        }

        if (!Fast.Ok && string.IsNullOrEmpty(Fast.ErrorCode))
        {
            errorMessage = $"FAST: ok=false but errorCode is missing";
            return false;
        }

        if (!Strict.Ok && string.IsNullOrEmpty(Strict.ErrorCode))
        {
            errorMessage = $"STRICT: ok=false but errorCode is missing";
            return false;
        }

        return true;
    }
}

/// <summary>
/// Single validation mode result (Open, Fast, or Strict).
/// </summary>
public class ValidationMode
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; set; }
}
