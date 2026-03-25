using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace IronFamily.MegaBench.Validation;

/// <summary>
/// Writes validation results to artifacts directory.
/// </summary>
public static class ValidationWriter
{
    /// <summary>
    /// Write validation results to JSON and text files.
    /// </summary>
    public static void WriteResults(
        string outputDir,
        IEnumerable<ValidationResult> results)
    {
        try
        {
            Directory.CreateDirectory(outputDir);

            var resultsList = results.ToList();

            // Write JSON array
            var jsonPath = Path.Combine(outputDir, "validation.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(resultsList, options);
            File.WriteAllText(jsonPath, json);

            // Write summary text
            var summaryPath = Path.Combine(outputDir, "validation_summary.txt");
            using (var writer = new StreamWriter(summaryPath))
            {
                foreach (var result in resultsList)
                {
                    var status = result.Passed ? "PASS" : "FAIL";
                    var errorInfo = result.Passed ? "" : $" [{result.ErrorCode}]";
                    writer.WriteLine($"{status} | {result.Engine} | {result.Profile ?? "N/A"} | {result.Mode}{errorInfo}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to write validation results: {ex.Message}");
        }
    }
}
