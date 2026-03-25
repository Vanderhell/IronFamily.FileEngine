using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using IronConfig.ILog;

namespace IronConfig.ILog.Tests;

/// <summary>
/// ILOG Streaming Isolation Tests
/// Tests compression streaming behavior with incremental batches
/// </summary>
public class IlogStreamingTests
{
    private byte[] GenerateTestData(int size)
    {
        var data = new byte[size];
        var rng = new Random(42);
        rng.NextBytes(data);
        return data;
    }

    [Fact]
    public void Streaming_SingleBatch_RoundTrip()
    {
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();
        var testData = GenerateTestData(1024);

        // Single batch encode/decode
        var encoded = encoder.Encode(testData, IlogProfile.SEARCHABLE);
        var decoded = decoder.Decode(encoded);

        Assert.True(testData.SequenceEqual(decoded));
    }

    [Fact]
    public void Streaming_MultipleBatches_Independent()
    {
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        var batch1 = GenerateTestData(512);
        var batch2 = GenerateTestData(512);
        var batch3 = GenerateTestData(512);

        // Each batch independently encoded/decoded
        var enc1 = encoder.Encode(batch1, IlogProfile.SEARCHABLE);
        var enc2 = encoder.Encode(batch2, IlogProfile.SEARCHABLE);
        var enc3 = encoder.Encode(batch3, IlogProfile.SEARCHABLE);

        var dec1 = decoder.Decode(enc1);
        var dec2 = decoder.Decode(enc2);
        var dec3 = decoder.Decode(enc3);

        Assert.True(batch1.SequenceEqual(dec1));
        Assert.True(batch2.SequenceEqual(dec2));
        Assert.True(batch3.SequenceEqual(dec3));
    }

    [Fact]
    public void Streaming_LargeBatch()
    {
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();
        var largeData = GenerateTestData(512 * 1024); // 512 KB

        var encoded = encoder.Encode(largeData, IlogProfile.ARCHIVED);
        var decoded = decoder.Decode(encoded);

        Assert.True(largeData.SequenceEqual(decoded));
        Assert.NotEmpty(encoded);
    }

    [Fact]
    public void Streaming_AllProfiles()
    {
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();
        var testData = GenerateTestData(4096);

        var profiles = new[]
        {
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED,
            IlogProfile.AUDITED
        };

        foreach (var profile in profiles)
        {
            var encoded = encoder.Encode(testData, profile);
            var decoded = decoder.Decode(encoded);
            Assert.True(testData.SequenceEqual(decoded), $"Profile {profile} streaming failed");
        }
    }

    [Fact]
    public void Streaming_EmptyBatch()
    {
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        var encoded = encoder.Encode(new byte[0], IlogProfile.SEARCHABLE);
        var decoded = decoder.Decode(encoded);

        Assert.Empty(decoded);
    }

    [Fact]
    public void Streaming_Repeated_RoundTrip()
    {
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();
        var singleRecord = GenerateTestData(256);

        // Create repeated data
        var repeated = new byte[singleRecord.Length * 50];
        for (int i = 0; i < 50; i++)
        {
            Array.Copy(singleRecord, 0, repeated, i * singleRecord.Length, singleRecord.Length);
        }

        var encoded = encoder.Encode(repeated, IlogProfile.ARCHIVED);
        var decoded = decoder.Decode(encoded);

        // Verify roundtrip
        Assert.True(repeated.SequenceEqual(decoded));
    }

}
