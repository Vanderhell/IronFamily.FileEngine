using System;
using System.Collections.Generic;
using IronConfig.Iupd;

namespace IronFamily.MegaBench.Semantics;

/// <summary>
/// Convert IUPD manifest to canonical JSON representation.
/// Supports deterministic minBytes padding for fairness gate (PHASE 4).
/// </summary>
public static class IupdManifestToCanonicalJson
{
    /// <summary>
    /// Convert IUPD data to canonical JSON bytes (manifest only).
    /// If minBytes specified, pad with deterministic "__pad" field to reach minBytes.
    /// </summary>
    public static byte[] ToCanonicalJson(byte[] iupdData, int minBytes = 1024)
    {
        // Parse IUPD
        var reader = IupdReader.Open(iupdData, out var err);
        if (reader == null || !err.IsOk)
            throw new InvalidOperationException("Failed to open IUPD data");

        // Build manifest representation
        var manifest = CanonicalJson.CreateSorted();
        manifest["profile"] = reader.Profile.ToString();
        manifest["version"] = reader.Version;
        manifest["chunk_count"] = reader.ChunkCount;

        byte[] serialized = CanonicalJson.SerializeCanonical(manifest);

        // Add deterministic padding if needed
        if (serialized.Length < minBytes)
        {
            // Iteratively pad until we reach minBytes
            var manifestWithPad = CanonicalJson.CreateSorted();
            manifestWithPad["chunk_count"] = reader.ChunkCount;
            manifestWithPad["profile"] = reader.Profile.ToString();
            manifestWithPad["version"] = reader.Version;

            int padLen = 0;
            while (true)
            {
                manifestWithPad["__pad"] = new string('A', padLen);
                serialized = CanonicalJson.SerializeCanonical(manifestWithPad);
                if (serialized.Length >= minBytes)
                    break;
                padLen++;
            }
        }

        return serialized;
    }
}
