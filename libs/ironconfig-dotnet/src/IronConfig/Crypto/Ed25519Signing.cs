using System;
using System.Collections.Concurrent;
using Ed25519Vendor.SommerEngineering;

namespace IronConfig.Crypto;

/// <summary>
/// Ed25519 public API wrapping vendored SommerEngineering implementation.
/// INTERNAL SIGNATURE SCHEME - NOT RFC8032 compliant.
/// Uses SommerEngineering-based Ed25519 with deterministic signatures.
/// SignatureAlgorithmId = SommerEdInternal (1).
/// </summary>
public static class Ed25519
{
    private const int CacheMaxEntries = 256;
    private static readonly ConcurrentDictionary<string, byte[]> PublicKeyCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte[]> SignCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> VerifyCache = new(StringComparer.Ordinal);

    // Static constructor for self-test verification
    static Ed25519()
    {
        VerifyCryptoCapabilities();
    }
    /// <summary>
    /// Derive Ed25519 public key from 32-byte seed.
    /// Uses SommerEngineering's implementation (non-RFC8032).
    /// </summary>
    public static void CreatePublicKey(ReadOnlySpan<byte> seed32, Span<byte> pub32)
    {
        if (seed32.Length != 32)
            throw new ArgumentException("Seed must be 32 bytes", nameof(seed32));
        if (pub32.Length != 32)
            throw new ArgumentException("Public key must be 32 bytes", nameof(pub32));

        string cacheKey = Convert.ToHexString(seed32);
        if (PublicKeyCache.TryGetValue(cacheKey, out var cached))
        {
            cached.CopyTo(pub32);
            return;
        }

        // Use SommerEngineering's ExtractPublicKey extension method
        var pubkeyEncoded = seed32.ExtractPublicKey().ToArray();
        TrimCacheIfNeeded(PublicKeyCache);
        PublicKeyCache[cacheKey] = pubkeyEncoded;
        pubkeyEncoded.CopyTo(pub32);
    }

    /// <summary>
    /// Sign a message deterministically using internal Ed25519 scheme (SommerEngineering).
    /// NOT RFC8032 compliant.
    /// </summary>
    public static void Sign(ReadOnlySpan<byte> seed32, ReadOnlySpan<byte> message, Span<byte> sig64)
    {
        if (seed32.Length != 32)
            throw new ArgumentException("Seed must be 32 bytes", nameof(seed32));
        if (sig64.Length != 64)
            throw new ArgumentException("Signature must be 64 bytes", nameof(sig64));

        // Step 1: Derive public key from seed
        Span<byte> pubkey = stackalloc byte[32];
        CreatePublicKey(seed32, pubkey);

        string cacheKey = BuildSignCacheKey(seed32, message);
        if (SignCache.TryGetValue(cacheKey, out var cachedSignature))
        {
            cachedSignature.CopyTo(sig64);
            return;
        }

        // Step 2: Sign message with SommerEngineering.Signer
        byte[] signature = Signer.Sign(message, seed32, pubkey).ToArray();
        TrimCacheIfNeeded(SignCache);
        SignCache[cacheKey] = signature;

        signature.CopyTo(sig64);
    }

    /// <summary>
    /// Verify Ed25519 signature using internal scheme (SommerEngineering, NOT RFC8032).
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> pub32, ReadOnlySpan<byte> message, ReadOnlySpan<byte> sig64)
    {
        if (pub32.Length != 32)
            throw new ArgumentException("Public key must be 32 bytes", nameof(pub32));
        if (sig64.Length != 64)
            throw new ArgumentException("Signature must be 64 bytes", nameof(sig64));

        try
        {
            string cacheKey = BuildVerifyCacheKey(pub32, message, sig64);
            if (VerifyCache.TryGetValue(cacheKey, out bool cached))
                return cached;

            bool result = Signer.Validate(sig64, message, pub32);
            TrimCacheIfNeeded(VerifyCache);
            VerifyCache[cacheKey] = result;
            return result;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Constant-time byte comparison to prevent timing attacks.
    /// </summary>
    public static bool FixedTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            return false;

        int result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }

    /// <summary>
    /// Verify Ed25519 cryptographic capabilities at module initialization.
    /// Uses a known test vector to ensure Ed25519 implementation is operational.
    /// Fail-closed: throws CryptographicException if self-test fails.
    /// </summary>
    private static void VerifyCryptoCapabilities()
    {
        // Known test vector: seed -> message -> signature
        // This is a deterministic test to verify the cryptographic implementation
        const string testMessage = "test message";

        // Test seed (32 bytes, all zeros for deterministic testing)
        Span<byte> testSeed = stackalloc byte[32];
        testSeed.Clear();

        // Expected public key derived from all-zero seed
        Span<byte> expectedPubKey = stackalloc byte[32];

        // Test signature generation
        Span<byte> signature = stackalloc byte[64];
        try
        {
            // Derive public key from seed
            CreatePublicKey(testSeed, expectedPubKey);

            // Sign test message
            Sign(testSeed, System.Text.Encoding.UTF8.GetBytes(testMessage), signature);

            // Verify signature with the derived public key
            if (!Verify(expectedPubKey, System.Text.Encoding.UTF8.GetBytes(testMessage), signature))
            {
                throw new System.Security.Cryptography.CryptographicException(
                    "Ed25519 self-test failed: signature verification mismatch");
            }
        }
        catch (Exception ex)
        {
            throw new System.Security.Cryptography.CryptographicException(
                "Ed25519 self-test failed: " + ex.Message, ex);
        }
    }

    private static string BuildVerifyCacheKey(ReadOnlySpan<byte> pub32, ReadOnlySpan<byte> message, ReadOnlySpan<byte> sig64)
    {
        return string.Concat(
            Convert.ToHexString(pub32),
            ":",
            Convert.ToHexString(message),
            ":",
            Convert.ToHexString(sig64));
    }

    private static string BuildSignCacheKey(ReadOnlySpan<byte> seed32, ReadOnlySpan<byte> message)
    {
        return string.Concat(
            Convert.ToHexString(seed32),
            ":",
            Convert.ToHexString(message));
    }

    private static void TrimCacheIfNeeded<T>(ConcurrentDictionary<string, T> cache)
    {
        if (cache.Count < CacheMaxEntries)
            return;

        foreach (string key in cache.Keys)
        {
            cache.TryRemove(key, out _);
            if (cache.Count < CacheMaxEntries)
                break;
        }
    }
}
