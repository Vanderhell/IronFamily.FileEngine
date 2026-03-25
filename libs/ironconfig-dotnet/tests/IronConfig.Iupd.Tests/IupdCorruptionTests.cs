// Phase 1.3: IUPD Corruption Tests
// Verify unified error model correctly maps IUPD errors

using IronConfig;
using IronConfig.Iupd;

namespace IronConfig.Iupd.Tests;

public class IupdCorruptionTests
{
    /// <summary>
    /// C1) InvalidMagic Error Mapping
    /// Verify that IupdError with InvalidMagic code maps to correct unified category
    /// </summary>
    [Fact]
    public void CorruptionTest_Iupd_InvalidMagic_MapsCorrectly()
    {
        // Create engine-specific error
        var iupdErr = new IupdError(IupdErrorCode.InvalidMagic, 0, "Invalid IUPD magic bytes");

        // Map to unified error
        var unified = global::IronConfig.IronEdgeError.FromIupdError(iupdErr);

        // Verify mapping
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidMagic, unified.Category);
        Assert.Equal(global::IronConfig.IronEdgeEngine.Iupd, unified.Engine);
    }

    /// <summary>
    /// C2) Manifest Error Mapping
    /// Verify that IupdError with ManifestSizeMismatch code maps to ManifestError category
    /// </summary>
    [Fact]
    public void CorruptionTest_Iupd_ManifestError_MapsCorrectly()
    {
        // Create engine-specific error
        var iupdErr = new IupdError(IupdErrorCode.ManifestSizeMismatch, 128, "Manifest size mismatch");

        // Map to unified error
        var unified = global::IronConfig.IronEdgeError.FromIupdError(iupdErr);

        // Verify mapping
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.ManifestError, unified.Category);
        Assert.Equal(global::IronConfig.IronEdgeEngine.Iupd, unified.Engine);
        Assert.Equal(128u, unified.Offset);
    }

    /// <summary>
    /// C3) BLAKE3 Checksum Error Mapping
    /// Verify that IupdError with Blake3Mismatch code maps to InvalidChecksum
    /// </summary>
    [Fact]
    public void CorruptionTest_Iupd_Blake3Mismatch_MapsToInvalidChecksum()
    {
        // Create engine-specific error
        var iupdErr = new IupdError(IupdErrorCode.Blake3Mismatch, 0, "BLAKE3 validation failed");

        // Map to unified error
        var unified = global::IronConfig.IronEdgeError.FromIupdError(iupdErr);

        // Verify mapping
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidChecksum, unified.Category);
        Assert.Equal(global::IronConfig.IronEdgeEngine.Iupd, unified.Engine);
    }
}
