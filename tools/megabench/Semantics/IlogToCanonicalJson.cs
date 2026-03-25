using System;
using IronConfig.ILog;

namespace IronFamily.MegaBench.Semantics;

/// <summary>
/// Convert ILOG binary to canonical JSON representation.
/// Supports deterministic minBytes padding for fairness gate (PHASE 4).
/// </summary>
public static class IlogToCanonicalJson
{
    /// <summary>
    /// Convert ILOG data to canonical JSON bytes.
    /// If minBytes specified, pad with deterministic "__pad" field to reach minBytes.
    /// </summary>
    public static byte[] ToCanonicalJson(byte[] ilogData, int minBytes = 1024)
    {
        // Validate ILOG is readable
        var openErr = IlogReader.Open(ilogData, out var view);
        if (openErr != null || view == null)
            throw new InvalidOperationException("Failed to open ILOG data");

        // Use base64 encoding of original binary
        var dict = CanonicalJson.CreateSorted();
        dict["format"] = "ilog_binary_b64";
        dict["data"] = CanonicalJson.EncodeBytes(ilogData);

        byte[] serialized = CanonicalJson.SerializeCanonical(dict);

        // Add deterministic padding if needed
        if (serialized.Length < minBytes)
        {
            // Iteratively pad until we reach minBytes
            var dictWithPad = CanonicalJson.CreateSorted();
            dictWithPad["data"] = CanonicalJson.EncodeBytes(ilogData);
            dictWithPad["format"] = "ilog_binary_b64";

            int padLen = 0;
            while (true)
            {
                dictWithPad["__pad"] = new string('A', padLen);
                serialized = CanonicalJson.SerializeCanonical(dictWithPad);
                if (serialized.Length >= minBytes)
                    break;
                padLen++;
            }
        }

        return serialized;
    }
}
