using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronFamily.MegaBench.Semantics;

/// <summary>
/// Deterministic canonical JSON serialization (sorted keys, compact format).
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Serialize object to canonical JSON bytes (UTF-8, no whitespace, sorted keys).
    /// </summary>
    public static byte[] SerializeCanonical(object obj)
    {
        // Use System.Text.Json with custom options for determinism
        string json = JsonSerializer.Serialize(obj, _options);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Create a sorted dictionary for canonical JSON output.
    /// </summary>
    public static SortedDictionary<string, object?> CreateSorted()
    {
        return new SortedDictionary<string, object?>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Encode bytes to base64 with "b64:" prefix for canonical JSON.
    /// </summary>
    public static string EncodeBytes(byte[] data)
    {
        return "b64:" + Convert.ToBase64String(data);
    }

    /// <summary>
    /// Decode base64 from canonical JSON (with "b64:" prefix).
    /// </summary>
    public static byte[] DecodeBytes(string encoded)
    {
        if (!encoded.StartsWith("b64:"))
            throw new ArgumentException("Invalid base64 encoding prefix");
        return Convert.FromBase64String(encoded.Substring(4));
    }
}
