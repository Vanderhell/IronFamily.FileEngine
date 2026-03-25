using System;
using System.Collections.Generic;
using System.IO;
using IronConfig.ILog;

namespace IronFamily.MegaBench.Validation;

/// <summary>
/// Validator for ILOG datasets using production API.
/// Runs both Fast and Strict validation modes.
/// </summary>
public class IlogDatasetValidator : IDatasetValidator
{
    public IEnumerable<ValidationResult> Validate(
        string datasetId,
        string sizeLabel,
        string outputDir,
        string filePath,
        string? profile = null)
    {
        var results = new List<ValidationResult>();

        // Mode 1: Open and basic validation
        try
        {
            var data = File.ReadAllBytes(filePath);
            var openErr = IlogReader.Open(new ReadOnlySpan<byte>(data), out var view);
            bool passed = openErr == null && view != null;

            results.Add(new ValidationResult
            {
                Engine = "ilog",
                Profile = profile,
                DatasetId = datasetId,
                SizeLabel = sizeLabel,
                Mode = "Open",
                Passed = passed,
                ErrorCode = passed ? "" : (openErr?.Code.ToString() ?? "NULL_VIEW"),
                DetailsPath = Path.Combine(outputDir, "validation_ilog_open.txt")
            });

            if (!passed && openErr != null)
            {
                File.WriteAllText(Path.Combine(outputDir, "validation_ilog_open.txt"),
                    $"Open failed: {openErr.Code}");
            }

            // Mode 2: Fast validation (only if opened successfully)
            if (passed && view != null)
            {
                try
                {
                    var fastErr = IlogReader.ValidateFast(view);
                    bool fastPassed = fastErr == null;

                    results.Add(new ValidationResult
                    {
                        Engine = "ilog",
                        Profile = profile,
                        DatasetId = datasetId,
                        SizeLabel = sizeLabel,
                        Mode = "Fast",
                        Passed = fastPassed,
                        ErrorCode = fastPassed ? "" : (fastErr?.Code.ToString() ?? "UNKNOWN"),
                        DetailsPath = Path.Combine(outputDir, "validation_ilog_fast.txt")
                    });

                    if (!fastPassed && fastErr != null)
                    {
                        File.WriteAllText(Path.Combine(outputDir, "validation_ilog_fast.txt"),
                            $"Fast validation failed: {fastErr.Code}");
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new ValidationResult
                    {
                        Engine = "ilog",
                        Profile = profile,
                        DatasetId = datasetId,
                        SizeLabel = sizeLabel,
                        Mode = "Fast",
                        Passed = false,
                        ErrorCode = ex.GetType().Name,
                        DetailsPath = Path.Combine(outputDir, "validation_ilog_fast_error.txt")
                    });

                    File.WriteAllText(Path.Combine(outputDir, "validation_ilog_fast_error.txt"),
                        $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
                }
            }

            // Mode 3: Strict validation (only if opened successfully)
            if (passed && view != null)
            {
                try
                {
                    var validateErr = IlogReader.ValidateStrict(view);
                    bool strictPassed = validateErr == null;

                    results.Add(new ValidationResult
                    {
                        Engine = "ilog",
                        Profile = profile,
                        DatasetId = datasetId,
                        SizeLabel = sizeLabel,
                        Mode = "Strict",
                        Passed = strictPassed,
                        ErrorCode = strictPassed ? "" : (validateErr?.Code.ToString() ?? "UNKNOWN"),
                        DetailsPath = Path.Combine(outputDir, "validation_ilog_strict.txt")
                    });

                    if (!strictPassed && validateErr != null)
                    {
                        File.WriteAllText(Path.Combine(outputDir, "validation_ilog_strict.txt"),
                            $"Strict validation failed: {validateErr.Code}");
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new ValidationResult
                    {
                        Engine = "ilog",
                        Profile = profile,
                        DatasetId = datasetId,
                        SizeLabel = sizeLabel,
                        Mode = "Strict",
                        Passed = false,
                        ErrorCode = ex.GetType().Name,
                        DetailsPath = Path.Combine(outputDir, "validation_ilog_strict_error.txt")
                    });

                    File.WriteAllText(Path.Combine(outputDir, "validation_ilog_strict_error.txt"),
                        $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
                }
            }
        }
        catch (Exception ex)
        {
            results.Add(new ValidationResult
            {
                Engine = "ilog",
                Profile = profile,
                DatasetId = datasetId,
                SizeLabel = sizeLabel,
                Mode = "Open",
                Passed = false,
                ErrorCode = ex.GetType().Name,
                DetailsPath = Path.Combine(outputDir, "validation_ilog_error.txt")
            });

            File.WriteAllText(Path.Combine(outputDir, "validation_ilog_error.txt"),
                $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
        }

        return results;
    }
}
