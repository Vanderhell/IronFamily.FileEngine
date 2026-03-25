namespace IronFamily.MegaBench.Validation;

/// <summary>
/// Result of a single validation check.
/// </summary>
public record ValidationResult
{
    /// <summary>Engine name (icfg, ilog, iupd)</summary>
    public string Engine { get; init; } = "";

    /// <summary>Profile name (if applicable)</summary>
    public string? Profile { get; init; }

    /// <summary>Dataset ID</summary>
    public string DatasetId { get; init; } = "";

    /// <summary>Size label (10KB, 100KB, etc.)</summary>
    public string SizeLabel { get; init; } = "";

    /// <summary>Validation mode (Fast, Strict, Decode, etc.)</summary>
    public string Mode { get; init; } = "";

    /// <summary>Did validation pass?</summary>
    public bool Passed { get; init; }

    /// <summary>Error code if validation failed (e.g., exception type name)</summary>
    public string ErrorCode { get; init; } = "";

    /// <summary>Path to detailed validation output file</summary>
    public string DetailsPath { get; init; } = "";
}
