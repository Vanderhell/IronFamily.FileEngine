using System;
using Xunit;
using IronConfig.Iupd;
using IronConfig;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// Backward Compatibility Tests for IUPD v1/v2 and profile changes
/// </summary>
public class BackcompatTests
{
    [Fact(DisplayName = "Backcompat: V2 reader creates file with OPTIMIZED profile")]
    public void Backcompat_V2_CreatesOptimizedProfile()
    {
        // Use unified bench keys
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, new byte[] { 1, 2, 3, 4, 5 });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        // Check version byte at offset 4
        Assert.Equal(0x02, data[4]);

        // Check profile byte at offset 5
        Assert.Equal((byte)IupdProfile.OPTIMIZED, data[5]);
    }

    [Fact(DisplayName = "Backcompat: V2 reader can read V2 created file")]
    public void Backcompat_V2_ReadsV2File()
    {
        // Use unified bench keys
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        // Create with v2
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, new byte[] { 1, 2, 3 });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);
        var data = writer.Build();

        // Read with v2
        var reader = IupdReader.Open(data, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);
        Assert.Equal(0x02, reader.Version);
        Assert.Equal(IupdProfile.OPTIMIZED, reader.Profile);
    }

    [Fact(DisplayName = "Backcompat: MINIMAL profile metadata preserved")]
    public void Backcompat_MinimalProfilePreserved()
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.MINIMAL);
        writer.AddChunk(0, new byte[] { 42, 43, 44 });
        writer.SetApplyOrder(0);

        var data = writer.Build();

        // SECURITY: MINIMAL profile is not allowed - should be rejected by IupdReader
        var reader = IupdReader.Open(data, out var error);
        Assert.False(error.IsOk, "Expected MINIMAL profile to be rejected");
        Assert.Equal(IupdErrorCode.ProfileNotAllowed, error.Code);
    }

    [Fact(DisplayName = "Backcompat: FAST profile metadata preserved")]
    public void Backcompat_FastProfilePreserved()
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.FAST);
        var testData = new byte[1024];
        for (int i = 0; i < testData.Length; i++)
            testData[i] = (byte)(i % 256);

        writer.AddChunk(0, testData);
        writer.SetApplyOrder(0);

        var data = writer.Build();

        // SECURITY: FAST profile is not allowed - should be rejected by IupdReader
        var reader = IupdReader.Open(data, out var error);
        Assert.False(error.IsOk, "Expected FAST profile to be rejected");
        Assert.Equal(IupdErrorCode.ProfileNotAllowed, error.Code);

        // Size should be compressed
        Assert.True(data.Length < testData.Length);
    }

    [Fact(DisplayName = "Backcompat: SECURE profile metadata preserved")]
    public void Backcompat_SecureProfilePreserved()
    {
        // Use unified bench keys
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, new byte[] { 1, 2, 3 });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        var reader = IupdReader.Open(data, out var error);
        Assert.True(error.IsOk);
        Assert.Equal(IupdProfile.SECURE, reader.Profile);

        // SECURE profile properties
        Assert.False(reader.Profile.SupportsCompression());
        Assert.True(reader.Profile.RequiresBlake3());
        Assert.True(reader.Profile.SupportsDependencies());
    }

    [Fact(DisplayName = "Backcompat: Profile-specific validation applies")]
    public void Backcompat_ProfileValidationApplies()
    {
        // Use unified bench keys
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, new byte[100]);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        var reader = IupdReader.Open(data, out var error);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);

        // Validate with strict rules for OPTIMIZED
        var validateError = reader.ValidateStrict();
        Assert.True(validateError.IsOk);
    }

    [Fact(DisplayName = "Backcompat: Round-trip v2 file preserves data")]
    public void Backcompat_RoundTripV2PreservesData()
    {
        // Use unified bench keys
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        byte[] originalData = new byte[256];
        for (int i = 0; i < originalData.Length; i++)
            originalData[i] = (byte)i;

        // Create with v2
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, originalData);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);
        var iupdData = writer.Build();

        // Read with v2
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);

        // Extract data
        var applier = reader.BeginApply();
        var recovered = new byte[0];
        while (applier.TryNext(out var chunk))
        {
            recovered = chunk.Payload.ToArray();
        }

        // Verify
        Assert.Equal(originalData.Length, recovered.Length);
        for (int i = 0; i < originalData.Length; i++)
            Assert.Equal(originalData[i], recovered[i]);
    }

    [Fact(DisplayName = "Backcompat: Multiple profiles can coexist")]
    public void Backcompat_MultipleProfilesCoexist()
    {
        // Use unified bench keys
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        // SECURITY: Only test allowed profiles (MINIMAL and FAST are no longer allowed)
        var profiles = new[]
        {
            IupdProfile.SECURE,
            IupdProfile.OPTIMIZED
        };

        foreach (var profile in profiles)
        {
            var writer = new IupdWriter();
            writer.SetProfile(profile);
            if (profile.SupportsDependencies())
            {
                writer.WithSigningKey(benchPrivateKey, benchPublicKey);
            }
            writer.AddChunk(0, new byte[] { 1 });
            writer.SetApplyOrder(0);
            writer.WithUpdateSequence(1);

            var data = writer.Build();

            var reader = IupdReader.Open(data, out var error);
            Assert.True(error.IsOk, $"Failed to read {profile} profile");
            Assert.Equal(profile, reader.Profile);

            if (profile.SupportsDependencies())
            {
                reader.SetVerificationKey(benchPublicKey);
            }
        }
    }

    [Fact(DisplayName = "Backcompat: Version byte uniquely identifies version")]
    public void Backcompat_VersionByteUnique()
    {
        // Create V2 file
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.AddChunk(0, new byte[] { 42 });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);
        var data = writer.Build();

        // Version should be 0x02
        Assert.Equal(0x02, data[4]);

        // When reading, version should be detected correctly
        var reader = IupdReader.Open(data, out var error);
        Assert.True(error.IsOk);
        Assert.Equal(0x02, reader.Version);
    }
}
