using System;

namespace IronConfig.Crypto;

/// <summary>
/// Signature algorithm identifier for explicit contract clarity.
/// Used in file formats (ILOG, IUPD) to identify which signing scheme was used.
/// NOT RFC8032 compliant - uses SommerEngineering-based implementation.
/// </summary>
public enum SignatureAlgorithmId : byte
{
    /// <summary>
    /// No signature (0). Used in MINIMAL/FAST profiles and non-signing contexts.
    /// </summary>
    None = 0,

    /// <summary>
    /// SommerEngineering-based Ed25519 (internal, NOT RFC8032).
    /// 32-byte seed → 32-byte public key → 64-byte signature.
    /// Deterministic: same seed+message → same signature.
    /// </summary>
    SommerEdInternal = 1
}
