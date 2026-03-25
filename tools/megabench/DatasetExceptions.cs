using System;
using System.IO;
using System.Text.Json;

namespace IronFamily.MegaBench.Datasets;

/// <summary>
/// Error codes for dataset generation failures.
/// Must be logged with full artifacts for auditability.
/// </summary>
public enum DatasetErrorCode
{
    SIZE_PARSE_FAILED = 1001,
    ENCODE_FAILED = 1002,
    VALIDATION_FAILED = 1003,
    GENERATOR_EXCEPTION = 1004,
    PROFILE_INVALID = 1005,
    BUFFER_OVERFLOW = 1006,
}

/// <summary>
/// Exception thrown when dataset generation fails.
/// Always logged with artifacts: error.txt, stacktrace.txt, generator_params.json
/// </summary>
public class MegaBenchDatasetException : Exception
{
    public DatasetErrorCode ErrorCode { get; }
    public string DatasetId { get; }
    public string? Engine { get; }
    public string? Profile { get; }
    public string? SizeLabel { get; }

    public MegaBenchDatasetException(
        DatasetErrorCode code,
        string datasetId,
        string message,
        string? engine = null,
        string? profile = null,
        string? sizeLabel = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = code;
        DatasetId = datasetId;
        Engine = engine;
        Profile = profile;
        SizeLabel = sizeLabel;
    }

    /// <summary>
    /// Log this exception to artifacts directory with full diagnostic info.
    /// </summary>
    public void LogToArtifacts()
    {
        try
        {
            string artifactDir = Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "artifacts",
                "bench",
                "megabench_datasets",
                DatasetId);

            Directory.CreateDirectory(artifactDir);

            // Write error.txt
            string errorFile = Path.Combine(artifactDir, "error.txt");
            File.WriteAllText(errorFile, $"ErrorCode: {ErrorCode}\nMessage: {Message}\nTimestamp: {DateTime.UtcNow:O}");

            // Write stacktrace.txt
            string stackTraceFile = Path.Combine(artifactDir, "stacktrace.txt");
            File.WriteAllText(stackTraceFile, StackTrace ?? "(no stacktrace)");

            // Write generator_params.json
            string paramsFile = Path.Combine(artifactDir, "generator_params.json");
            string json = JsonSerializer.Serialize(new
            {
                engine = Engine,
                profile = Profile,
                sizeLabel = SizeLabel,
                datasetId = DatasetId,
                errorCode = (int)ErrorCode,
                timestamp = DateTime.UtcNow,
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(paramsFile, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to log exception artifacts: {ex.Message}");
        }
    }
}
