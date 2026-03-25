// Runtime Verify Result DTO
// Designed for deterministic JSON serialization with stable field ordering

using System;
using System.Collections.Generic;

namespace IronConfig.Tooling;

/// <summary>
/// Error information in verify result.
/// Fields ordered for JSON determinism.
/// </summary>
public class ErrorObject
{
    public string Category { get; set; } = "";
    public int Code { get; set; }
    public long? Offset { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Unified result of file verification.
/// Designed for deterministic JSON serialization with exact field ordering:
/// 1. ok
/// 2. engine
/// 3. bytes_scanned
/// 4. error (if present)
/// </summary>
public class VerifyResult
{
    public bool Ok { get; set; }
    public string Engine { get; set; } = "";
    public long BytesScanned { get; set; }
    public ErrorObject? Error { get; set; }

    /// <summary>
    /// Serialize to deterministic JSON with stable field ordering.
    /// </summary>
    public string ToJson()
    {
        // Build JSON manually for deterministic field ordering
        var parts = new List<string>
        {
            $"\"ok\":{(Ok ? "true" : "false")}",
            $"\"engine\":{JsonEscape(Engine)}",
            $"\"bytes_scanned\":{BytesScanned}"
        };

        if (Error != null)
        {
            var errorParts = new List<string>
            {
                $"\"category\":{JsonEscape(Error.Category)}",
                $"\"code\":{Error.Code}",
            };

            // Include offset only if non-null
            if (Error.Offset.HasValue)
            {
                errorParts.Add($"\"offset\":{Error.Offset.Value}");
            }

            errorParts.Add($"\"message\":{JsonEscape(Error.Message)}");
            var errorJson = "{" + string.Join(",", errorParts) + "}";
            parts.Add($"\"error\":{errorJson}");
        }

        return "{" + string.Join(",", parts) + "}";
    }

    private static string JsonEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t") + "\"";
    }
}

/// <summary>
/// Exit code mapping for runtime verify command.
/// </summary>
public enum VerifyExitCode : int
{
    Success = 0,              // File verified successfully
    ValidationError = 1,      // IronEdgeError validation failure
    IoError = 2,              // File I/O error (not found, permission denied, etc.)
    InvalidArguments = 3,     // Invalid command arguments
    InternalFailure = 10      // Unexpected internal runtime error
}
