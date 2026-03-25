using System;
using System.IO;
using Xunit;
using IronConfig.Iupd.Delta;
using IronConfig.Iupd;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// Regression tests for IRONDEL2 (Delta v2) apply operations.
/// Ensures that real-world IRONDEL2 patches can be applied correctly.
/// </summary>
public class IupdDeltaV2ApplyTests
{
    /// <summary>
    /// RV002 Regression Test: Delta v2 golden vector patch apply.
    /// Tests that the IRONDEL2 format patch case_01.patch2.bin applies correctly
    /// to the base file and produces output matching the target.
    ///
    /// This test reproduces the RV002 case from EXEC_IUPD_REAL_FW_BENCH_01
    /// benchmark: case_01.base.bin (512KB) -> apply case_01.patch2.bin -> case_01.out.bin (512KB)
    /// </summary>
    [Fact]
    public void ApplyDeltaV2_GoldenVectorPatch_ProducesExactTarget()
    {
        // Arrange
        string basePath = "artifacts/vectors/v1/delta2/v1/case_01.base.bin";
        string patchPath = "artifacts/vectors/v1/delta2/v1/case_01.patch2.bin";
        string targetPath = "artifacts/vectors/v1/delta2/v1/case_01.out.bin";

        // Skip test if files don't exist (CI environment without artifacts)
        if (!File.Exists(basePath) || !File.Exists(patchPath) || !File.Exists(targetPath))
        {
            return;
        }

        byte[] baseData = File.ReadAllBytes(basePath);
        byte[] patchData = File.ReadAllBytes(patchPath);
        byte[] expectedTarget = File.ReadAllBytes(targetPath);

        // Act
        byte[] result = IupdDeltaV2Cdc.ApplyDeltaV2(baseData, patchData, out var error);

        // Assert
        Assert.True(error.IsOk, $"Apply should succeed. Error: {error.Code} - {error.Message}");
        Assert.NotNull(result);
        Assert.Equal(expectedTarget.Length, result.Length);
        Assert.Equal(expectedTarget, result);
    }

    /// <summary>
    /// Test that IronDel2.Apply correctly handles IRONDEL2 format.
    /// Verifies header CRC32, base hash, and target hash validation.
    /// </summary>
    [Fact]
    public void ApplyDeltaV2_ValidatesHeaderAndHashes()
    {
        // Arrange
        string basePath = "artifacts/vectors/v1/delta2/v1/case_01.base.bin";
        string patchPath = "artifacts/vectors/v1/delta2/v1/case_01.patch2.bin";

        if (!File.Exists(basePath) || !File.Exists(patchPath))
        {
            return;
        }

        byte[] baseData = File.ReadAllBytes(basePath);
        byte[] patchData = File.ReadAllBytes(patchPath);

        // Act
        byte[] result = IupdDeltaV2Cdc.ApplyDeltaV2(baseData, patchData, out var error);

        // Assert - should succeed if all validations pass
        Assert.True(error.IsOk, "Should validate header, base hash, and target hash successfully");
        Assert.NotNull(result);
    }

    /// <summary>
    /// Test that ApplyDeltaV2 rejects corrupted header CRC32.
    /// </summary>
    [Fact]
    public void ApplyDeltaV2_RejectsCrruptedHeaderCrc32()
    {
        // Arrange
        string patchPath = "artifacts/vectors/v1/delta2/v1/case_01.patch2.bin";
        string basePath = "artifacts/vectors/v1/delta2/v1/case_01.base.bin";

        if (!File.Exists(basePath) || !File.Exists(patchPath))
        {
            return;
        }

        byte[] baseData = File.ReadAllBytes(basePath);
        byte[] patchData = File.ReadAllBytes(patchPath);

        // Corrupt the CRC32 field (last 4 bytes of header, at offset 96-99)
        patchData[96] ^= 0xFF;

        // Act
        byte[] result = IupdDeltaV2Cdc.ApplyDeltaV2(baseData, patchData, out var error);

        // Assert
        Assert.False(error.IsOk, "Should reject patch with corrupted header CRC32");
        Assert.NotNull(result);  // Returns empty array on error
    }

    /// <summary>
    /// Test that ApplyDeltaV2 rejects wrong base data.
    /// </summary>
    [Fact]
    public void ApplyDeltaV2_RejectsWrongBaseData()
    {
        // Arrange
        string patchPath = "artifacts/vectors/v1/delta2/v1/case_01.patch2.bin";

        if (!File.Exists(patchPath))
        {
            return;
        }

        byte[] wrongBaseData = new byte[524288];  // All zeros, not the actual base
        byte[] patchData = File.ReadAllBytes(patchPath);

        // Act
        byte[] result = IupdDeltaV2Cdc.ApplyDeltaV2(wrongBaseData, patchData, out var error);

        // Assert
        Assert.False(error.IsOk, "Should reject patch when base data doesn't match");
        Assert.Equal(IupdErrorCode.DeltaBaseHashMismatch, error.Code);
    }
}
