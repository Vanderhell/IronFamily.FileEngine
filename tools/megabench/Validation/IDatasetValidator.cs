using System.Collections.Generic;

namespace IronFamily.MegaBench.Validation;

/// <summary>
/// Interface for validating generated datasets.
/// Validators are responsible for checking dataset integrity and correctness.
/// </summary>
public interface IDatasetValidator
{
    /// <summary>
    /// Validate a generated dataset.
    /// </summary>
    /// <param name="datasetId">Dataset identifier</param>
    /// <param name="sizeLabel">Size label (10KB, 100KB, etc.)</param>
    /// <param name="outputDir">Directory containing the dataset file</param>
    /// <param name="filePath">Full path to the dataset binary file</param>
    /// <param name="profile">Profile name (nullable)</param>
    /// <returns>List of validation results (one per mode)</returns>
    IEnumerable<ValidationResult> Validate(
        string datasetId,
        string sizeLabel,
        string outputDir,
        string filePath,
        string? profile = null);
}
