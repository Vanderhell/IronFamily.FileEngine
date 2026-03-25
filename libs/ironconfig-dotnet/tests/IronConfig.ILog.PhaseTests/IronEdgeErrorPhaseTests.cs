// Phase 1.2: ILOG Error Mapping Tests
// Verify unified error model correctly maps ILog errors
// These tests are separated from main test suite pending Phase 1.2 implementation

using IronConfig;
using IronConfig.Common;
using IronConfig.ILog;

namespace IronConfig.ILog.PhaseTests;

public class IronEdgeErrorPhaseTests
{
    [Fact]
    public void Phase12_FromIlogError_InvalidMagic_MapsCorrectly()
    {
        var ilogErr = new IlogError(IlogErrorCode.InvalidMagic, 0, "Invalid ILOG magic bytes");
        var unified = global::IronConfig.IronEdgeError.FromIlogError(ilogErr);

        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidMagic, unified.Category);
        Assert.Equal(global::IronConfig.IronEdgeEngine.ILog, unified.Engine);
    }

    [Fact]
    public void Phase12_FromIlogError_CompressionFailed_MapsToCompressionError()
    {
        var ilogErr = new IlogError(IlogErrorCode.CompressionFailed, 512, "Zstd decompression failed");
        var unified = global::IronConfig.IronEdgeError.FromIlogError(ilogErr);

        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.CompressionError, unified.Category);
        Assert.Equal(global::IronConfig.IronEdgeEngine.ILog, unified.Engine);
        Assert.Equal(512u, unified.Offset);
    }
}
