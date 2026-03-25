using System;
using IronFamily.MegaBench.Competitors;
using IronFamily.MegaBench.Competitors.Fairness;
using IronFamily.MegaBench.Semantics;
using Xunit;

namespace IronConfig.MegaBench.Tests;

/// <summary>
/// Fairness gate tests (PHASE 6).
/// </summary>
public class FairnessGateTests
{
    [Fact]
    public void Fairness_MinPayload_IsAtLeast1KB()
    {
        // Arrange
        byte[] smallPayload = new byte[512]; // Less than 1KB
        Array.Fill(smallPayload, (byte)'A');

        var codec = new JsonCanonicalCodec(CodecKind.ICFG);

        // Act & Assert - should throw because payload < 1KB
        var ex = Assert.Throws<InvalidOperationException>(
            () => FairnessGate.RunAndProve(codec, smallPayload, "ICFG"));
        Assert.Contains("< 1024 bytes", ex.Message);
    }

    [Fact]
    public void Fairness_SemanticHash_Roundtrip_Matches_For_JsonCodec()
    {
        // Arrange
        byte[] payload = new byte[1024]; // Exactly 1KB (minimum)
        Array.Fill(payload, (byte)'A');

        var codec = new JsonCanonicalCodec(CodecKind.ICFG);

        // Act
        var proof = FairnessGate.RunAndProve(codec, payload, "ICFG");

        // Assert
        Assert.NotNull(proof);
        Assert.Equal(codec.Name, proof.CodecName);
        Assert.Equal("ICFG", proof.CodecKind);
        Assert.Equal(1024L, proof.PayloadBytesIn);
        Assert.True(proof.SemanticRoundtripOk, "Semantic roundtrip should pass for JSON codec");
        Assert.NotEmpty(proof.ExpectedSemanticHashSha256);
        Assert.Equal(proof.ExpectedSemanticHashSha256, proof.DecodedSemanticHashSha256);
    }

    [Fact]
    public void Fairness_Excluded_Codecs_AreExplicit()
    {
        // Arrange
        byte[] payload = new byte[1024];
        Array.Fill(payload, (byte)'A');

        var protoCodec = new ProtobufCodec(CodecKind.ICFG);
        var flatCodec = new FlatBuffersCodec(CodecKind.ICFG);

        // Act & Assert - Protobuf should throw NotImplementedException with explicit reason
        var protoEx = Assert.Throws<InvalidOperationException>(
            () => FairnessGate.RunAndProve(protoCodec, payload, "ICFG"));
        Assert.Contains("protoc", protoEx.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Excluded", protoEx.Message);

        // Act & Assert - FlatBuffers should throw NotImplementedException with explicit reason
        var flatEx = Assert.Throws<InvalidOperationException>(
            () => FairnessGate.RunAndProve(flatCodec, payload, "ICFG"));
        Assert.Contains("flatc", flatEx.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Excluded", flatEx.Message);
    }
}
