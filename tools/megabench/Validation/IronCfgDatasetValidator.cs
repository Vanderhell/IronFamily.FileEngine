using System;
using System.Collections.Generic;
using System.IO;
using IronConfig;
using IronConfig.IronCfg;

namespace IronFamily.MegaBench.Validation;

/// <summary>
/// Validator for IRONCFG datasets using production API.
/// Runs Fast and Strict validation modes.
/// </summary>
public class IronCfgDatasetValidator : IDatasetValidator
{
    public IEnumerable<ValidationResult> Validate(
        string datasetId,
        string sizeLabel,
        string outputDir,
        string filePath,
        string? profile = null)
    {
        var results = new List<ValidationResult>();

        // Mode 1: Open validation
        IronCfgView? openView = null;
        try
        {
            var data = File.ReadAllBytes(filePath);
            var openErr = IronCfgValidator.Open(new ReadOnlyMemory<byte>(data), out var view);
            openView = view;
            bool openPassed = openErr.Code == IronCfgErrorCode.Ok;

            results.Add(new ValidationResult
            {
                Engine = "icfg",
                Profile = profile,
                DatasetId = datasetId,
                SizeLabel = sizeLabel,
                Mode = "Open",
                Passed = openPassed,
                ErrorCode = openPassed ? "" : openErr.Code.ToString(),
                DetailsPath = Path.Combine(outputDir, "validation_icfg_open.txt")
            });

            if (!openPassed)
            {
                File.WriteAllText(Path.Combine(outputDir, "validation_icfg_open.txt"),
                    $"Open validation failed: {openErr.Code}");
            }

            if (!openPassed)
            {
                return results;
            }
        }
        catch (Exception ex)
        {
            results.Add(new ValidationResult
            {
                Engine = "icfg",
                Profile = profile,
                DatasetId = datasetId,
                SizeLabel = sizeLabel,
                Mode = "Open",
                Passed = false,
                ErrorCode = ex.GetType().Name,
                DetailsPath = Path.Combine(outputDir, "validation_icfg_open_error.txt")
            });

            File.WriteAllText(Path.Combine(outputDir, "validation_icfg_open_error.txt"),
                $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
            return results;
        }

        // Mode 2: Fast validation
        try
        {
            var data = File.ReadAllBytes(filePath);
            var fastErr = IronCfgValidator.ValidateFast(new ReadOnlyMemory<byte>(data));
            bool fastPassed = fastErr.Code == IronCfgErrorCode.Ok;

            results.Add(new ValidationResult
            {
                Engine = "icfg",
                Profile = profile,
                DatasetId = datasetId,
                SizeLabel = sizeLabel,
                Mode = "Fast",
                Passed = fastPassed,
                ErrorCode = fastPassed ? "" : fastErr.Code.ToString(),
                DetailsPath = Path.Combine(outputDir, "validation_icfg_fast.txt")
            });

            if (!fastPassed)
            {
                File.WriteAllText(Path.Combine(outputDir, "validation_icfg_fast.txt"),
                    $"Fast validation failed: {fastErr.Code}");
            }

            // Mode 3: Strict validation (only if fast passed and we have a view)
            if (fastPassed && openView.HasValue)
            {
                try
                {
                    var data2 = File.ReadAllBytes(filePath);
                    var strictErr = IronCfgValidator.ValidateStrict(new ReadOnlyMemory<byte>(data2), openView.Value);
                    bool strictPassed = strictErr.Code == IronCfgErrorCode.Ok;

                    results.Add(new ValidationResult
                    {
                        Engine = "icfg",
                        Profile = profile,
                        DatasetId = datasetId,
                        SizeLabel = sizeLabel,
                        Mode = "Strict",
                        Passed = strictPassed,
                        ErrorCode = strictPassed ? "" : strictErr.Code.ToString(),
                        DetailsPath = Path.Combine(outputDir, "validation_icfg_strict.txt")
                    });

                    if (!strictPassed)
                    {
                        File.WriteAllText(Path.Combine(outputDir, "validation_icfg_strict.txt"),
                            $"Strict validation failed: {strictErr.Code}");
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new ValidationResult
                    {
                        Engine = "icfg",
                        Profile = profile,
                        DatasetId = datasetId,
                        SizeLabel = sizeLabel,
                        Mode = "Strict",
                        Passed = false,
                        ErrorCode = ex.GetType().Name,
                        DetailsPath = Path.Combine(outputDir, "validation_icfg_strict_error.txt")
                    });

                    File.WriteAllText(Path.Combine(outputDir, "validation_icfg_strict_error.txt"),
                        $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
                }
            }
        }
        catch (Exception ex)
        {
            results.Add(new ValidationResult
            {
                Engine = "icfg",
                Profile = profile,
                DatasetId = datasetId,
                SizeLabel = sizeLabel,
                Mode = "Fast",
                Passed = false,
                ErrorCode = ex.GetType().Name,
                DetailsPath = Path.Combine(outputDir, "validation_icfg_error.txt")
            });

            File.WriteAllText(Path.Combine(outputDir, "validation_icfg_error.txt"),
                $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
        }

        return results;
    }
}
