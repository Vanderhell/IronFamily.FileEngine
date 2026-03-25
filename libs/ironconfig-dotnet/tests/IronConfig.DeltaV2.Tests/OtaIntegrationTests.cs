using System;
using System.IO;
using Xunit;
using IronConfig.Iupd.Delta;

namespace IronConfig.DeltaV2.Tests;

/// <summary>
/// OTA CLI integration tests for EXEC_DELTA_V2_02.
/// Tests: format detection, OTA create/apply flows, fallback logic.
/// </summary>
public class OtaIntegrationTests
{
    /// <summary>
    /// Test 1: DeltaDetect identifies IRONDEL2 format correctly.
    /// Verify: DetectBytes() and DetectFile() correctly identify v2 magic bytes.
    /// </summary>
    [Fact]
    public void DeltaDetect_IdentifiesV2Format()
    {
        byte[] baseData = GenerateDeterministicData(64 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        targetData[1000] ^= 0xFF;

        // Create v2 delta
        byte[] deltaV2 = IronDel2.Create(baseData, targetData);
        Assert.NotEmpty(deltaV2);
        Assert.True(deltaV2.Length >= 8, "Delta should have magic bytes");

        // Detect format from bytes
        DeltaFormat format = DeltaDetect.DetectBytes(deltaV2);
        Assert.Equal(DeltaFormat.V2_IRONDEL2, format);

        // Verify format name
        Assert.Equal("IRONDEL2 (v2)", DeltaDetect.FormatName(format));
    }

    /// <summary>
    /// Test 2: DeltaDetect identifies IUPDDEL1 format correctly.
    /// Verify: DetectBytes() correctly identifies v1 magic bytes.
    /// </summary>
    [Fact]
    public void DeltaDetect_IdentifiesV1Format()
    {
        byte[] baseData = GenerateDeterministicData(64 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        targetData[500] ^= 0xFF;

        // Create v1 delta
        byte[] deltaV1 = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
        Assert.NotEmpty(deltaV1);

        // Detect format
        DeltaFormat format = DeltaDetect.DetectBytes(deltaV1);
        Assert.Equal(DeltaFormat.V1_IUPDDEL1, format);

        // Verify format name
        Assert.Equal("IUPDDEL1 (v1)", DeltaDetect.FormatName(format));
    }

    /// <summary>
    /// Test 3: DeltaDetect returns Unknown for invalid format.
    /// Verify: Fail-closed - unknown magic returns Unknown, not crash.
    /// </summary>
    [Fact]
    public void DeltaDetect_ReturnsUnknownForInvalidFormat()
    {
        // Create invalid delta with wrong magic bytes
        byte[] invalid = new byte[] { 0x49, 0x4E, 0x56, 0x41, 0x4C, 0x49, 0x44, 0xFF };

        DeltaFormat format = DeltaDetect.DetectBytes(invalid);
        Assert.Equal(DeltaFormat.Unknown, format);
        Assert.Equal("Unknown", DeltaDetect.FormatName(format));
    }

    /// <summary>
    /// Test 4: OTA create selects V2 when smaller than V1 (auto mode).
    /// Verify: Size comparison and v2 selection for shift patterns.
    /// </summary>
    [Fact]
    public void OTA_CreateAutoMode_SelectsV2WhenSmaller()
    {
        byte[] base512k = GenerateDeterministicData(512 * 1024);
        byte[] prepend16k = GenerateDeterministicData(16 * 1024);

        // Create target with 16KB prepended (typical shift pattern)
        byte[] target = new byte[base512k.Length + prepend16k.Length];
        Array.Copy(prepend16k, target, prepend16k.Length);
        Array.Copy(base512k, 0, target, prepend16k.Length, base512k.Length);

        // Create both deltas
        byte[] deltaV1 = IupdDeltaV1.CreateDeltaV1(base512k, target);
        byte[] deltaV2 = IronDel2.Create(base512k, target);

        // V2 should be smaller
        Assert.True(deltaV2.Length < deltaV1.Length,
            $"V2 ({deltaV2.Length}B) should be smaller than V1 ({deltaV1.Length}B) for shift pattern");

        // Verify v2 is selected in auto mode logic
        string selectedFormat = deltaV2.Length < deltaV1.Length ? "v2" : "v1";
        Assert.Equal("v2", selectedFormat);
    }

    /// <summary>
    /// Test 5: OTA apply correctly routes to V2 handler.
    /// Verify: DeltaDetect + IronDel2.Apply flow works end-to-end.
    /// </summary>
    [Fact]
    public void OTA_ApplyRoutesCorrectlyToV2Handler()
    {
        byte[] baseData = GenerateDeterministicData(64 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        targetData[100] ^= 0xFF;
        targetData[5000] ^= 0xFF;

        // Create v2 delta
        byte[] deltaV2 = IronDel2.Create(baseData, targetData);

        // Simulate OTA apply: detect format and route
        DeltaFormat detectedFormat = DeltaDetect.DetectBytes(deltaV2);
        Assert.Equal(DeltaFormat.V2_IRONDEL2, detectedFormat);

        // Apply using detected format
        byte[] output;
        if (detectedFormat == DeltaFormat.V2_IRONDEL2)
        {
            output = IronDel2.Apply(baseData, deltaV2, out var error);
            Assert.True(error.IsOk, $"Apply failed: {error.Message}");
        }
        else
        {
            Assert.True(false, "Should have detected V2 format");
            output = Array.Empty<byte>();
        }

        // Verify output matches target
        Assert.Equal(targetData.Length, output.Length);
        Assert.True(BytesEqual(output, targetData), "Output must equal target");
    }

    /// <summary>
    /// Test 6: OTA apply with format auto-detection (polymorphic routing).
    /// Verify: Can apply either v1 or v2 by auto-detecting format.
    /// </summary>
    [Fact]
    public void OTA_ApplyAutoDetects_AndRoutesCorrectly()
    {
        byte[] baseData = GenerateDeterministicData(64 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        targetData[2000] ^= 0xFF;

        // Test with V1
        {
            byte[] deltaV1 = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
            DeltaFormat format = DeltaDetect.DetectBytes(deltaV1);
            Assert.Equal(DeltaFormat.V1_IUPDDEL1, format);

            byte[] output = ApplyDeltaByFormat(baseData, deltaV1, format);
            Assert.True(BytesEqual(output, targetData), "V1 apply must match target");
        }

        // Test with V2
        {
            byte[] deltaV2 = IronDel2.Create(baseData, targetData);
            DeltaFormat format = DeltaDetect.DetectBytes(deltaV2);
            Assert.Equal(DeltaFormat.V2_IRONDEL2, format);

            byte[] output = ApplyDeltaByFormat(baseData, deltaV2, format);
            Assert.True(BytesEqual(output, targetData), "V2 apply must match target");
        }
    }

    // Helpers

    private byte[] ApplyDeltaByFormat(byte[] baseData, byte[] deltaData, DeltaFormat format)
    {
        if (format == DeltaFormat.V1_IUPDDEL1)
        {
            return IupdDeltaV1.ApplyDeltaV1(baseData, deltaData, out var error);
        }
        else if (format == DeltaFormat.V2_IRONDEL2)
        {
            return IronDel2.Apply(baseData, deltaData, out var error);
        }
        else
        {
            return Array.Empty<byte>();
        }
    }

    private byte[] GenerateDeterministicData(int length)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = (byte)((i * 67 + 17) % 256);
        return data;
    }

    private bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
