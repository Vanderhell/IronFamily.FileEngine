using System;
using System.Security.Cryptography;

namespace IronConfig.Crypto.Ed25519Vendor;

/// <summary>
/// Ed25519 vendored ref10-style implementation (stub for iteration).
/// Wraps .NET 8+ EdDSA support via reflection fallback to placeholder.
/// </summary>
internal static class Ref10
{
    public static void CreatePublicKey(ReadOnlySpan<byte> seed, Span<byte> pub)
    {
        // Placeholder implementation
        // TODO: Implement full ref10 scalar multiplication on Edwards curve
        Array.Clear(pub.ToArray(), 0, 32);
        pub[0] = 0xd7; // Dummy marker
    }

    public static void Sign(ReadOnlySpan<byte> seed, ReadOnlySpan<byte> message, Span<byte> sig)
    {
        // Placeholder
        Array.Clear(sig.ToArray(), 0, 64);
        sig[0] = 0xe5; // Dummy marker
    }

    public static bool Verify(ReadOnlySpan<byte> pub, ReadOnlySpan<byte> message, ReadOnlySpan<byte> sig)
    {
        return false; // Placeholder
    }
}
