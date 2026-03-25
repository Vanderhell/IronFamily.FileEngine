using System;
using System.Security.Cryptography;
using System.Text.Json;
using IronFamily.MegaBench.Semantics;

namespace IronFamily.MegaBench.Competitors.Fairness;

/// <summary>
/// Hard fairness gate for competitor codecs (PHASE 4).
/// Rules:
/// - PayloadBytesIn >= 1024 (minimum payload size)
/// - SemanticRoundtripOk == true (semantic validation)
/// - BytesEncoded > 0
/// </summary>
public static class FairnessGate
{
    /// <summary>
    /// Run codec fairness check and produce FairnessProof.
    /// Throws InvalidOperationException if validation fails (hard gate).
    /// Throws NotImplementedException if codec is excluded.
    /// </summary>
    public static FairnessProof RunAndProve(
        ICompetitorCodec codec,
        byte[] canonicalPayload,
        string codecKindName)
    {
        // Rule 1: Payload must be >= 1024 bytes
        if (canonicalPayload.Length < 1024)
        {
            throw new InvalidOperationException(
                $"Fairness gate FAIL: {codec.Name} payload {canonicalPayload.Length} < 1024 bytes");
        }

        // Encode
        byte[] encoded;
        try
        {
            encoded = codec.Encode(canonicalPayload);
        }
        catch (NotImplementedException ex)
        {
            // Re-throw NotImplementedException to signal codec is excluded
            throw new NotImplementedException($"{codec.Name}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Fairness gate FAIL: {codec.Name} encode failed: {ex.Message}", ex);
        }

        // Rule 2: BytesEncoded > 0
        if (encoded.Length == 0)
        {
            throw new InvalidOperationException(
                $"Fairness gate FAIL: {codec.Name} encoded 0 bytes");
        }

        // Decode
        byte[] decoded;
        try
        {
            decoded = codec.Decode(encoded);
        }
        catch (NotImplementedException ex)
        {
            // Re-throw NotImplementedException to signal codec is excluded
            throw new NotImplementedException($"{codec.Name}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Fairness gate FAIL: {codec.Name} decode failed: {ex.Message}", ex);
        }

        // Compute semantic hashes
        string expectedHash = Convert.ToHexString(SHA256.HashData(canonicalPayload));
        string decodedHash = Convert.ToHexString(SHA256.HashData(decoded));

        bool semanticOk = expectedHash == decodedHash;

        // Rule 3: SemanticRoundtripOk == true
        if (!semanticOk)
        {
            throw new InvalidOperationException(
                $"Fairness gate FAIL: {codec.Name} semantic mismatch: expected {expectedHash}, got {decodedHash}");
        }

        return new FairnessProof
        {
            CodecName = codec.Name,
            CodecKind = codecKindName,
            PayloadBytesIn = canonicalPayload.Length,
            BytesEncoded = encoded.Length,
            ExpectedSemanticHashSha256 = expectedHash,
            DecodedSemanticHashSha256 = decodedHash,
            SemanticRoundtripOk = semanticOk
        };
    }
}
