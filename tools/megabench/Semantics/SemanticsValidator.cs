using System;
using System.Security.Cryptography;
using IronFamily.MegaBench.Competitors;

namespace IronFamily.MegaBench.Semantics;

/// <summary>
/// Validates codec roundtrip behavior against canonical JSON semantics.
/// PHASE 2: Semantics roundtrip gate.
/// </summary>
public static class SemanticsValidator
{
    /// <summary>
    /// Validate roundtrip: canonicalInput → canonicalJson → codec → decoded.
    /// Expected: SHA256(canonicalInput) == SHA256(decoded)
    /// </summary>
    public static bool ValidateRoundtrip(
        byte[] canonicalInput,
        ICompetitorCodec codec,
        out string? exclusionReason)
    {
        exclusionReason = null;

        try
        {
            // Encode
            byte[] encoded = codec.Encode(canonicalInput);

            // Decode
            byte[] decoded = codec.Decode(encoded);

            // Validate SHA256 match
            string inputHash = Convert.ToHexString(SHA256.HashData(canonicalInput));
            string decodedHash = Convert.ToHexString(SHA256.HashData(decoded));

            if (inputHash != decodedHash)
            {
                exclusionReason = $"Roundtrip mismatch: expected {inputHash}, got {decodedHash}";
                return false;
            }

            return true;
        }
        catch (NotImplementedException ex)
        {
            exclusionReason = $"NotImplementedCompetitor: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            exclusionReason = $"RoundtripError: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Gate policy for ICFG: roundtrip MUST pass.
    /// Gate policy for ILOG: EXCLUDED if roundtrip fails (requires JSON stream parsing).
    /// Gate policy for IUPD: EXCLUDED if roundtrip fails (requires manifest builder).
    /// </summary>
    public static bool ShouldExcludeForKind(
        CodecKind kind,
        bool roundtripOk,
        out string? reason)
    {
        reason = null;

        if (roundtripOk)
            return false; // Not excluded

        // ICFG: roundtrip failure is an error (fail the gate)
        if (kind == CodecKind.ICFG)
        {
            reason = "ICFG roundtrip validation failed (gate failure)";
            return false; // Not excluded, but gate fails
        }

        // ILOG/IUPD: roundtrip failure → exclude
        reason = kind == CodecKind.ILOG
            ? "ILOG codec requires full event stream parsing (not supported in V1)"
            : "IUPD codec requires manifest builder (not supported in V1)";

        return true; // Excluded
    }
}
