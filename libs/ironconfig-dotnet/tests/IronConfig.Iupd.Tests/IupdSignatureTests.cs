using System;
using Xunit;
using IronConfig.Iupd;
using IronConfig.Crypto;
using IronConfig.Tests;
using IronConfig;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// Tests for Ed25519 signature in SECURE and OPTIMIZED profiles
/// </summary>
public class IupdSignatureTests
{
    [Fact]
    public void TestSignature_SECURE_Profile_Builds()
    {
        // Create SECURE profile IUPD file (supports dependencies and signatures)
        // Uses default bench keys (hardcoded in both IupdWriter and IupdReader)
        var payload1 = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var payload2 = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.AddChunk(0, payload1);
        writer.AddChunk(1, payload2);
        writer.SetApplyOrder(0, 1);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Verify file was created (basic check)
        Assert.True(iupdData.Length > 0);

        // Read it back
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Validate fast
        error = reader.ValidateFast();
        Assert.True(error.IsOk);

        // Validate strict - set verification key to match default bench public key
        reader.SetVerificationKey(IupdEd25519Keys.BenchPublicKey32);
        error = reader.ValidateStrict();
        Assert.True(error.IsOk);
    }

    [Fact]
    public void TestSignature_OPTIMIZED_Profile_Builds()
    {
        // Create OPTIMIZED profile IUPD file (supports dependencies and signatures)
        // Use unified bench keys for testing
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var payload = new byte[] { 0xFF, 0xEE, 0xDD, 0xCC };

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Verify file was created
        Assert.True(iupdData.Length > 0);

        // Read and validate
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Set verification key to match signing key
        reader.SetVerificationKey(benchPublicKey);

        error = reader.ValidateStrict();
        Assert.True(error.IsOk);
    }

    [Fact]
    public void TestSignature_FAST_Profile_NoSignature()
    {
        // SECURITY: FAST profile is no longer allowed. Test uses OPTIMIZED instead.
        // OPTIMIZED profile requires signature for v2 files
        var payload = new byte[] { 0x11, 0x22, 0x33 };

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);  // Changed from FAST (now disallowed)
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Read back
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Should validate with signature
        reader.SetVerificationKey(IupdEd25519Keys.BenchPublicKey32);
        error = reader.ValidateStrict();
        Assert.True(error.IsOk);
    }

    [Fact]
    public void TestSignature_API_WithSigningKey()
    {
        // Test the WithSigningKey() API using bench keys
        // (which are known to be valid Ed25519 keys)
        // Create IUPD file with unified bench keys
        var customPrivateKey = IupdEd25519Keys.BenchSeed32;
        var customPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var payload = new byte[] { 0x44, 0x55, 0x66, 0x77 };

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.WithSigningKey(customPrivateKey, customPublicKey);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Read back and set matching verification key
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Set matching verification key before validation
        reader.SetVerificationKey(customPublicKey);

        // Validation should pass
        error = reader.ValidateStrict();
        Assert.True(error.IsOk);
    }

    [Fact]
    public void TestSignature_WithDependencies()
    {
        // Test signature with dependencies (SECURE profile)
        // Use unified bench keys for testing
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, new byte[] { 0x11 });
        writer.AddChunk(1, new byte[] { 0x22 });
        writer.AddChunk(2, new byte[] { 0x33 });
        writer.AddDependency(0, 1);  // 1 depends on 0
        writer.AddDependency(1, 2);  // 2 depends on 1
        writer.SetApplyOrder(0, 1, 2);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Read and validate
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        // Set verification key to match signing key
        reader.SetVerificationKey(benchPublicKey);

        error = reader.ValidateStrict();
        Assert.True(error.IsOk);
    }

    [Fact]
    public void TestWitness_V2_SECURE_Pass()
    {
        // Test witness hash computation and verification for SECURE profile
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, new byte[] { 0xAA, 0xBB });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Read and validate - should pass witness verification
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);
        error = reader.ValidateStrict();
        Assert.True(error.IsOk, $"Witness validation failed: {error}");
    }

    [Fact]
    public void TestWitness_V2_SECURE_Tamper_Chunk_Fails()
    {
        // Test that tampering with chunk table causes witness hash mismatch
        // The witness hash covers the manifest (header + dependencies + apply order)
        // which includes references to chunks, so tampering is detected
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, new byte[] { 0xCC, 0xDD });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();
        var tampered = (byte[])iupdData.Clone();

        // Tamper with witness hash in the footer
        // Witness hash is located after signature in footer
        // Header is 37 bytes, so witness hash is roughly at: 37 + chunkTable + manifest + 4 + 64
        // For a simple file, this is around offset 200+.
        // We tamper with the stored witness hash to simulate detection of manifest tampering
        if (tampered.Length > 150)
        {
            // Flip a byte in the witness hash area (after signature)
            // Signature footer starts after manifest: header + chunkTable + manifest
            tampered[Math.Min(250, tampered.Length - 33)] ^= 0xFF;
        }

        // Read tampered file - should fail witness verification
        var reader = IupdReader.Open(tampered, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);
        error = reader.ValidateStrict();

        // Should fail with WitnessMismatch error (corrupted witness hash)
        Assert.False(error.IsOk, "Expected witness verification to fail");
        Assert.Equal(IupdErrorCode.WitnessMismatch, error.Code);
    }

    [Fact]
    public void TestWitness_OPTIMIZED_Profile_Pass()
    {
        // Test witness hash for OPTIMIZED profile (also requires witness)
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);  // OPTIMIZED also has witness
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, new byte[] { 0x11, 0x22, 0x33 });
        writer.AddChunk(1, new byte[] { 0x44, 0x55, 0x66 });
        writer.SetApplyOrder(0, 1);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Read and validate
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);
        error = reader.ValidateStrict();
        Assert.True(error.IsOk, $"OPTIMIZED profile witness validation failed: {error}");
    }

    [Fact]
    public void Profile_AllowedSECURE_Opens()
    {
        // SECURITY: SECURE profile should be accepted (in allowed list)
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Should successfully open with SECURE profile
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk, $"Failed to open SECURE profile: {error}");
        Assert.Equal(IupdProfile.SECURE, reader.Profile);
    }

    [Fact]
    public void Profile_AllowedOPTIMIZED_Opens()
    {
        // SECURITY: OPTIMIZED profile should be accepted (in allowed list)
        var payload = new byte[] { 0x04, 0x05, 0x06 };
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Should successfully open with OPTIMIZED profile
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk, $"Failed to open OPTIMIZED profile: {error}");
        Assert.Equal(IupdProfile.OPTIMIZED, reader.Profile);
    }

    [Fact]
    public void Profile_DisallowedMINIMAL_Fails()
    {
        // SECURITY: MINIMAL profile should be rejected (not in allowed list)
        // Create a v2 IUPD file with MINIMAL profile
        var payload = new byte[] { 0x07, 0x08, 0x09 };
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.MINIMAL);
        writer.AddChunk(0, payload);
        writer.SetApplyOrder(0);

        var iupdData = writer.Build();

        // Should fail to open with ProfileNotAllowed error
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.False(error.IsOk, "Expected MINIMAL profile to be rejected");
        Assert.Equal(IupdErrorCode.ProfileNotAllowed, error.Code);
    }
}
