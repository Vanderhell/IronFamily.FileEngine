using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IronConfig.Iupd;

namespace IronFamily.MegaBench.Validation;

/// <summary>
/// Validator for IUPD datasets using production API.
/// Runs Decode validation mode (opens and parses the IUPD package).
/// </summary>
public class IupdDatasetValidator : IDatasetValidator
{
    public IEnumerable<ValidationResult> Validate(
        string datasetId,
        string sizeLabel,
        string outputDir,
        string filePath,
        string? profile = null)
    {
        var results = new List<ValidationResult>();

        // Check for DELTA profile (not yet fully implemented in megabench)
        if (profile == "DELTA")
        {
            var exclusionRecord = new
            {
                datasetId,
                engine = "IUPD",
                profile = "DELTA",
                excluded = true,
                excludedReason = "NotImplemented",
                generatedUtc = DateTime.UtcNow.ToString("O")
            };

            File.WriteAllText(
                Path.Combine(outputDir, "exclusion.json"),
                JsonSerializer.Serialize(exclusionRecord, new JsonSerializerOptions { WriteIndented = true })
            );

            // Return empty list — DELTA exclusion is not a validation failure
            return new List<ValidationResult>();
        }

        // Mode 1: Open validation (open and parse the IUPD package)
        IupdReader? reader = null;
        try
        {
            var data = File.ReadAllBytes(filePath);
            reader = IupdReader.Open(new ReadOnlySpan<byte>(data), out var openErr);

            bool passed = reader != null && openErr.IsOk;

            results.Add(new ValidationResult
            {
                Engine = "iupd",
                Profile = profile,
                DatasetId = datasetId,
                SizeLabel = sizeLabel,
                Mode = "Open",
                Passed = passed,
                ErrorCode = passed ? "" : openErr.Code.ToString(),
                DetailsPath = Path.Combine(outputDir, "validation_iupd_open.txt")
            });

            if (!passed)
            {
                File.WriteAllText(Path.Combine(outputDir, "validation_iupd_open.txt"),
                    $"Open failed: {openErr.Code}");
            }

            if (!passed)
            {
                return results;
            }
        }
        catch (Exception ex)
        {
            results.Add(new ValidationResult
            {
                Engine = "iupd",
                Profile = profile,
                DatasetId = datasetId,
                SizeLabel = sizeLabel,
                Mode = "Open",
                Passed = false,
                ErrorCode = ex.GetType().Name,
                DetailsPath = Path.Combine(outputDir, "validation_iupd_error.txt")
            });

            File.WriteAllText(Path.Combine(outputDir, "validation_iupd_error.txt"),
                $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
            return results;
        }

        // Mode 2: Fast validation
        if (reader != null)
        {
            try
            {
                var fastErr = reader.ValidateFast();
                bool fastPassed = fastErr.IsOk;

                results.Add(new ValidationResult
                {
                    Engine = "iupd",
                    Profile = profile,
                    DatasetId = datasetId,
                    SizeLabel = sizeLabel,
                    Mode = "Fast",
                    Passed = fastPassed,
                    ErrorCode = fastPassed ? "" : fastErr.Code.ToString(),
                    DetailsPath = Path.Combine(outputDir, "validation_iupd_fast.txt")
                });

                if (!fastPassed)
                {
                    File.WriteAllText(Path.Combine(outputDir, "validation_iupd_fast.txt"),
                        $"Fast validation failed: {fastErr.Code}");
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult
                {
                    Engine = "iupd",
                    Profile = profile,
                    DatasetId = datasetId,
                    SizeLabel = sizeLabel,
                    Mode = "Fast",
                    Passed = false,
                    ErrorCode = ex.GetType().Name,
                    DetailsPath = Path.Combine(outputDir, "validation_iupd_fast_error.txt")
                });

                File.WriteAllText(Path.Combine(outputDir, "validation_iupd_fast_error.txt"),
                    $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
            }
        }

        // Mode 3: Strict validation
        if (reader != null)
        {
            try
            {
                var strictErr = reader.ValidateStrict();
                bool strictPassed = strictErr.IsOk;

                results.Add(new ValidationResult
                {
                    Engine = "iupd",
                    Profile = profile,
                    DatasetId = datasetId,
                    SizeLabel = sizeLabel,
                    Mode = "Strict",
                    Passed = strictPassed,
                    ErrorCode = strictPassed ? "" : strictErr.Code.ToString(),
                    DetailsPath = Path.Combine(outputDir, "validation_iupd_strict.txt")
                });

                if (!strictPassed)
                {
                    File.WriteAllText(Path.Combine(outputDir, "validation_iupd_strict.txt"),
                        $"Strict validation failed: {strictErr.Code}");
                }
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult
                {
                    Engine = "iupd",
                    Profile = profile,
                    DatasetId = datasetId,
                    SizeLabel = sizeLabel,
                    Mode = "Strict",
                    Passed = false,
                    ErrorCode = ex.GetType().Name,
                    DetailsPath = Path.Combine(outputDir, "validation_iupd_strict_error.txt")
                });

                File.WriteAllText(Path.Combine(outputDir, "validation_iupd_strict_error.txt"),
                    $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
            }
        }

        return results;
    }
}
