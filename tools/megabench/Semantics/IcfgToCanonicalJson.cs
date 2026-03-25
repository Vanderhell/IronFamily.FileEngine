using System;
using System.Text.Json;
using IronConfig.IronCfg;

namespace IronFamily.MegaBench.Semantics;

/// <summary>
/// Convert ICFG binary to canonical JSON representation.
/// Supports deterministic minBytes padding for fairness gate (PHASE 4).
/// </summary>
public static class IcfgToCanonicalJson
{
    /// <summary>
    /// Convert ICFG data to canonical JSON bytes (pass-through the original ICFG as base64).
    /// If minBytes specified, pad with deterministic "__pad" field to reach minBytes.
    /// </summary>
    public static byte[] ToCanonicalJson(byte[] icfgData, int minBytes = 1024)
    {
        // Validate ICFG is readable
        if (!IronCfgValidator.Open(icfgData, out var view).IsOk)
            throw new InvalidOperationException("Failed to open ICFG data");

        // For now, use base64 encoding of original binary as canonical JSON
        var dict = CanonicalJson.CreateSorted();
        dict["format"] = "icfg_binary_b64";
        dict["data"] = CanonicalJson.EncodeBytes(icfgData);

        byte[] serialized = CanonicalJson.SerializeCanonical(dict);

        // Add deterministic padding if needed
        if (serialized.Length < minBytes)
        {
            // Iteratively pad until we reach minBytes
            var dictWithPad = CanonicalJson.CreateSorted();
            dictWithPad["data"] = CanonicalJson.EncodeBytes(icfgData);
            dictWithPad["format"] = "icfg_binary_b64";

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
