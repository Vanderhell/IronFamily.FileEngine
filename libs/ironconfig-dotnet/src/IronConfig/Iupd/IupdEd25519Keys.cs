using System;
using IronConfig.Crypto;

namespace IronConfig.Iupd;

/// <summary>
/// Single source of truth for Ed25519 keypair derivation in IUPD.
/// Ensures writer and reader use identical keys and semantics.
///
/// NOTE: The Ed25519 implementation uses a proprietary seed format (not RFC 8032).
/// All IUPD code MUST use this helper for key derivation to maintain consistency.
/// </summary>
public static class IupdEd25519Keys
{
    /// <summary>
    /// Bench/test Ed25519 seed (32 bytes).
    /// Used for all default signature operations.
    /// Fixed for reproducibility across writer and reader.
    /// </summary>
    public static readonly byte[] BenchSeed32 = new byte[]
    {
        0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57,
        0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67,
        0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F
    };

    /// <summary>
    /// Bench public key (32 bytes), derived from BenchSeed32.
    /// Must be derived once and cached for consistency.
    /// </summary>
    public static readonly byte[] BenchPublicKey32 = DerivePublicKeyFromSeed(BenchSeed32);

    /// <summary>
    /// Derive Ed25519 public key from a seed (32 bytes).
    /// Uses the actual Ed25519.CreatePublicKey API.
    /// Must be the ONLY place in IUPD code that derives public keys.
    /// </summary>
    /// <param name="seed32">32-byte seed/private key</param>
    /// <returns>32-byte public key derived from seed</returns>
    public static byte[] DerivePublicKeyFromSeed(byte[] seed32)
    {
        if (seed32 == null)
            throw new ArgumentNullException(nameof(seed32));
        if (seed32.Length != 32)
            throw new ArgumentException("Seed must be 32 bytes", nameof(seed32));

        Span<byte> pubKey = stackalloc byte[32];
        Ed25519.CreatePublicKey(seed32, pubKey);
        return pubKey.ToArray();
    }

    /// <summary>
    /// Sign a message using Ed25519.
    /// Wrapper around Ed25519.Sign for semantic clarity.
    /// </summary>
    /// <param name="seed32">32-byte seed (private key in Ed25519 parlance)</param>
    /// <param name="message">Message bytes to sign</param>
    /// <returns>64-byte signature</returns>
    public static byte[] SignMessage(byte[] seed32, byte[] message)
    {
        if (seed32 == null)
            throw new ArgumentNullException(nameof(seed32));
        if (seed32.Length != 32)
            throw new ArgumentException("Seed must be 32 bytes", nameof(seed32));
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        Span<byte> signature = stackalloc byte[64];
        Ed25519.Sign(seed32, message, signature);
        return signature.ToArray();
    }

    /// <summary>
    /// Verify an Ed25519 signature.
    /// Wrapper for semantic clarity.
    /// </summary>
    /// <param name="publicKey32">32-byte public key</param>
    /// <param name="message">Message bytes that were signed</param>
    /// <param name="signature64">64-byte signature to verify</param>
    /// <returns>True if signature is valid, false otherwise</returns>
    public static bool VerifySignature(byte[] publicKey32, byte[] message, byte[] signature64)
    {
        if (publicKey32 == null)
            throw new ArgumentNullException(nameof(publicKey32));
        if (publicKey32.Length != 32)
            throw new ArgumentException("Public key must be 32 bytes", nameof(publicKey32));
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        if (signature64 == null)
            throw new ArgumentNullException(nameof(signature64));
        if (signature64.Length != 64)
            throw new ArgumentException("Signature must be 64 bytes", nameof(signature64));

        return Ed25519.Verify(publicKey32, message, signature64);
    }

    /// <summary>
    /// Get the bench public key (convenience method).
    /// </summary>
    /// <returns>32-byte bench public key</returns>
    public static byte[] GetBenchPublicKey() => (byte[])BenchPublicKey32.Clone();

    /// <summary>
    /// Get the bench seed (convenience method).
    /// </summary>
    /// <returns>32-byte bench seed</returns>
    public static byte[] GetBenchSeed() => (byte[])BenchSeed32.Clone();
}
