// Phase 1.2: ILog Corruption Error Mapping Tests
// Verify unified error model correctly maps ILog errors
// These tests are separated from main test suite pending Phase 1.2 implementation

using IronConfig;
using IronConfig.Common;
using IronConfig.ILog;

namespace IronConfig.ILog.PhaseTests;

public class IlogCorruptionPhaseTests
{
    /// <summary>
    /// B1) InvalidMagic Error Mapping
    /// Verify that IlogError with InvalidMagic code maps to correct unified category
    /// </summary>
    [Fact]
    public void Phase12_CorruptionTest_ILog_InvalidMagic_MapsCorrectly()
    {
        // Create engine-specific error
        var ilogErr = new IlogError(IlogErrorCode.InvalidMagic, 0, "Invalid ILOG magic bytes");

        // Map to unified error
        var unified = global::IronConfig.IronEdgeError.FromIlogError(ilogErr);

        // Verify mapping
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidMagic, unified.Category);
        Assert.Equal(global::IronConfig.IronEdgeEngine.ILog, unified.Engine);
    }

    /// <summary>
    /// B2) Record Truncated Error Mapping
    /// Verify that IlogError with RecordTruncated code maps to Truncated category
    /// </summary>
    [Fact]
    public void Phase12_CorruptionTest_ILog_RecordTruncated_MapsCorrectly()
    {
        // Create engine-specific error
        var ilogErr = new IlogError(IlogErrorCode.RecordTruncated, 256, "Record ends prematurely");

        // Map to unified error
        var unified = global::IronConfig.IronEdgeError.FromIlogError(ilogErr);

        // Verify mapping
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.Truncated, unified.Category);
        Assert.Equal(global::IronConfig.IronEdgeEngine.ILog, unified.Engine);
        Assert.Equal(256u, unified.Offset);
    }

    /// <summary>
    /// B3) CRC32 Checksum Error Mapping
    /// Verify that IlogError with Crc32Mismatch code maps to InvalidChecksum
    /// </summary>
    [Fact]
    public void Phase12_CorruptionTest_ILog_Crc32Mismatch_MapsToInvalidChecksum()
    {
        // Create engine-specific error
        var ilogErr = new IlogError(IlogErrorCode.Crc32Mismatch, 0, "CRC32 validation failed");

        // Map to unified error
        var unified = global::IronConfig.IronEdgeError.FromIlogError(ilogErr);

        // Verify mapping
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidChecksum, unified.Category);
        Assert.Equal(global::IronConfig.IronEdgeEngine.ILog, unified.Engine);
    }
}
