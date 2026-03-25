using System;
using System.Security.Cryptography;

namespace IronFamily.MegaBench.Competitors.Fairness;

/// <summary>
/// Canonical payload with deterministic size guarantee (PHASE 4).
/// Used by fairness gate to ensure minimum payload size.
/// </summary>
public class CanonicalPayload
{
    public byte[] CanonicalJsonUtf8 { get; set; }
    public int SizeBytes => CanonicalJsonUtf8.Length;
    public string SemanticHashSha256 { get; set; }

    public CanonicalPayload(byte[] canonicalJsonUtf8)
    {
        CanonicalJsonUtf8 = canonicalJsonUtf8;
        SemanticHashSha256 = Convert.ToHexString(SHA256.HashData(canonicalJsonUtf8));
    }
}
