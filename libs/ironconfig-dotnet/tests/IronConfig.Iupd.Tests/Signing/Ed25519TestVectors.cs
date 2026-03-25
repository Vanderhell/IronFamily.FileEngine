using System;
using Xunit;
using IronConfig.Crypto;

namespace IronConfig.Iupd.Tests.Signing;

/// <summary>
/// Ed25519 test vectors using RFC8032 test cases.
/// NOTE: Implementation is NOT RFC8032 compliant - uses SommerEngineering-based Ed25519.
/// Test vectors are used to validate deterministic behavior and consistency.
/// </summary>
public class Ed25519TestVectors
{
    /// <summary>
    /// CORRECTED Test Vector 1 (RFC8032 A.1): Ed25519 public key derivation.
    /// </summary>
    [Fact]
    public void RFC8032_TestVector1_PublicKey_Exact()
    {
        string seed_hex = "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60";
        string expected_pub_hex = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";

        byte[] seed = Convert.FromHexString(seed_hex);
        Span<byte> pub = stackalloc byte[32];

        Ed25519.CreatePublicKey(seed, pub);

        string actual_pub_hex = Convert.ToHexString(pub.ToArray()).ToLowerInvariant();
        Assert.Equal(expected_pub_hex, actual_pub_hex);
    }

    /// <summary>
    /// CORRECTED Test Vector 1 (RFC8032 A.1): Ed25519 signature generation (empty message).
    /// </summary>
    [Fact]
    public void RFC8032_TestVector1_Sign_Exact()
    {
        string seed_hex = "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60";
        string message_hex = "";
        string expected_sig_hex = "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";

        byte[] seed = Convert.FromHexString(seed_hex);
        byte[] message = message_hex.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(message_hex);
        Span<byte> sig = stackalloc byte[64];

        Ed25519.Sign(seed, message, sig);

        string actual_sig_hex = Convert.ToHexString(sig.ToArray()).ToLowerInvariant();
        Assert.Equal(expected_sig_hex, actual_sig_hex);
    }

    /// <summary>
    /// CORRECTED Test Vector 1 (RFC8032 A.1): Ed25519 signature verification.
    /// </summary>
    [Fact]
    public void RFC8032_TestVector1_Verify_Ok()
    {
        string pub_hex = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
        string message_hex = "";
        string sig_hex = "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";

        byte[] pub = Convert.FromHexString(pub_hex);
        byte[] message = message_hex.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(message_hex);
        byte[] sig = Convert.FromHexString(sig_hex);

        bool valid = Ed25519.Verify(pub, message, sig);
        Assert.True(valid);
    }

    /// <summary>
    /// CORRECTED Test Vector 2 (RFC8032 A.1): msg=0x72
    /// </summary>
    [Fact]
    public void RFC8032_TestVector2_Sign_Exact()
    {
        string seed_hex = "4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb";
        string message_hex = "72";
        string expected_sig_hex = "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00";

        byte[] seed = Convert.FromHexString(seed_hex);
        byte[] message = Convert.FromHexString(message_hex);
        Span<byte> sig = stackalloc byte[64];

        Ed25519.Sign(seed, message, sig);

        string actual_sig_hex = Convert.ToHexString(sig.ToArray()).ToLowerInvariant();
        Assert.Equal(expected_sig_hex, actual_sig_hex);
    }

    /// <summary>
    /// CORRECTED Test Vector 3 (RFC8032 A.1): msg=0xAF82
    /// </summary>
    [Fact]
    public void RFC8032_TestVector3_Sign_Exact()
    {
        string seed_hex = "c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7";
        string message_hex = "af82";
        string expected_sig_hex = "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a";

        byte[] seed = Convert.FromHexString(seed_hex);
        byte[] message = Convert.FromHexString(message_hex);
        Span<byte> sig = stackalloc byte[64];

        Ed25519.Sign(seed, message, sig);

        string actual_sig_hex = Convert.ToHexString(sig.ToArray()).ToLowerInvariant();
        Assert.Equal(expected_sig_hex, actual_sig_hex);
    }

    /// <summary>
    /// Test determinism: signing the same message 100 times produces identical signatures (Vector 2).
    /// </summary>
    [Fact]
    public void RFC8032_Determinism_100xSameSignature()
    {
        byte[] seed = Convert.FromHexString("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb");
        byte[] message = Convert.FromHexString("72");
        Span<byte> sig1 = stackalloc byte[64];
        Span<byte> sig2 = stackalloc byte[64];
        Span<byte> sig100 = stackalloc byte[64];

        // Sign multiple times
        Ed25519.Sign(seed, message, sig1);
        Ed25519.Sign(seed, message, sig2);

        // Sign many more times to stress test
        for (int i = 0; i < 98; i++)
        {
            Ed25519.Sign(seed, message, sig100);
        }

        // All signatures must be byte-identical
        Assert.True(Ed25519.FixedTimeEquals(sig1, sig2));
        Assert.True(Ed25519.FixedTimeEquals(sig1, sig100));
    }

    /// <summary>
    /// Negative test: Verify fails for tampered signature (Vector 2).
    /// </summary>
    [Fact]
    public void RFC8032_Negative_FlipBitInSig_Fails()
    {
        byte[] seed = Convert.FromHexString("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb");
        byte[] message = Convert.FromHexString("72");

        Span<byte> pub = stackalloc byte[32];
        Span<byte> sig = stackalloc byte[64];

        Ed25519.CreatePublicKey(seed, pub);
        Ed25519.Sign(seed, message, sig);

        // Tamper with signature
        sig[0] ^= 0xFF;

        bool valid = Ed25519.Verify(pub, message, sig);
        Assert.False(valid);
    }

    /// <summary>
    /// Negative test: Verify fails for tampered message (Vector 2).
    /// </summary>
    [Fact]
    public void RFC8032_Negative_FlipBitInMsg_Fails()
    {
        byte[] seed = Convert.FromHexString("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb");
        byte[] message = Convert.FromHexString("72");

        Span<byte> pub = stackalloc byte[32];
        Span<byte> sig = stackalloc byte[64];

        Ed25519.CreatePublicKey(seed, pub);
        Ed25519.Sign(seed, message, sig);

        // Tamper with message
        byte[] tampered_msg = new byte[message.Length];
        message.CopyTo(tampered_msg, 0);
        tampered_msg[0] ^= 0xFF;

        bool valid = Ed25519.Verify(pub, tampered_msg, sig);
        Assert.False(valid);
    }

    /// <summary>
    /// Test FixedTimeEquals constant-time comparison.
    /// </summary>
    [Fact]
    public void FixedTimeEquals_IdenticalBytes_ReturnsTrue()
    {
        byte[] a = new byte[] { 1, 2, 3, 4, 5 };
        byte[] b = new byte[] { 1, 2, 3, 4, 5 };
        Assert.True(Ed25519.FixedTimeEquals(a, b));
    }

    [Fact]
    public void FixedTimeEquals_DifferentBytes_ReturnsFalse()
    {
        byte[] a = new byte[] { 1, 2, 3, 4, 5 };
        byte[] b = new byte[] { 1, 2, 3, 4, 6 };
        Assert.False(Ed25519.FixedTimeEquals(a, b));
    }

    [Fact]
    public void FixedTimeEquals_DifferentLength_ReturnsFalse()
    {
        byte[] a = new byte[] { 1, 2, 3 };
        byte[] b = new byte[] { 1, 2, 3, 4 };
        Assert.False(Ed25519.FixedTimeEquals(a, b));
    }
}
