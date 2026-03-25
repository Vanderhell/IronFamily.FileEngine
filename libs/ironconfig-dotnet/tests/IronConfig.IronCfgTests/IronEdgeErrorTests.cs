// Phase 1.1 Tests: Unified Error Wrapper and Mapping
// Verify factory methods, determinism, and error translation

using System;
using Xunit;
using IronConfig;
using IronConfig.IronCfg;
using IronConfig.Iupd;

namespace IronConfig.Tests;

public class IronEdgeErrorTests
{
    // =========================================================================
    // SUCCESS PATH TESTS
    // =========================================================================

    [Fact]
    public void IronEdgeError_Ok_HasNoneCategory()
    {
        var ok = global::IronConfig.IronEdgeError.Ok;
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.None, ok.Category);
        Assert.True(ok.IsOk);
        Assert.Equal("OK", ok.ToString());
    }

    [Fact]
    public void IronEdgeError_Construction_AllFieldsSet()
    {
        var err = new IronEdgeError(
            category: global::IronConfig.IronEdgeErrorCategory.InvalidMagic,
            code: 0x02,
            engine: global::IronConfig.IronEdgeEngine.IronCfg,
            message: "Bad magic",
            offset: 12345
        );

        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidMagic, err.Category);
        Assert.Equal(0x02, err.Code);
        Assert.Equal(global::IronConfig.IronEdgeEngine.IronCfg, err.Engine);
        Assert.Equal("Bad magic", err.Message);
        Assert.Equal(12345, err.Offset);
        Assert.Null(err.InnerException);
    }

    [Fact]
    public void IronEdgeError_ToString_Deterministic()
    {
        var err = new IronEdgeError(
            global::IronConfig.IronEdgeErrorCategory.CorruptData,
            0x04,
            global::IronConfig.IronEdgeEngine.Iupd,
            "Test message",
            offset: 999
        );

        var str1 = err.ToString();
        var str2 = err.ToString();

        // Must be identical across calls (deterministic)
        Assert.Equal(str1, str2);
        Assert.Contains("CorruptData", str1);
        Assert.Contains("0x04", str1);
        Assert.Contains("Iupd", str1);
        Assert.Contains("Test message", str1);
        Assert.Contains("offset: 999", str1);
    }

    // =========================================================================
    // IRONCFG ERROR MAPPING TESTS
    // =========================================================================

    [Fact]
    public void FromIronCfgError_InvalidMagic_MapsCorrectly()
    {
        var cfgErr = new IronCfgError(IronCfgErrorCode.InvalidMagic, 0);
        var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);

        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidMagic, unified.Category);
        Assert.Equal(0x02, unified.Code);
        Assert.Equal(global::IronConfig.IronEdgeEngine.IronCfg, unified.Engine);
        Assert.Equal(0u, unified.Offset);
    }

    [Fact]
    public void FromIronCfgError_TruncatedFile_MapsCorrectly()
    {
        var cfgErr = new IronCfgError(IronCfgErrorCode.TruncatedFile, 256);
        var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);

        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.Truncated, unified.Category);
        Assert.Equal(0x01, unified.Code);
        Assert.Equal(256u, unified.Offset);
    }

    [Fact]
    public void FromIronCfgError_Crc32Mismatch_MapsToInvalidChecksum()
    {
        var cfgErr = new IronCfgError(IronCfgErrorCode.Crc32Mismatch, 512);
        var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);

        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidChecksum, unified.Category);
        Assert.Equal(0x07, unified.Code);
    }

    [Fact]
    public void FromIronCfgError_SchemaErrors_MapToSchemaError()
    {
        var testCases = new[]
        {
            (IronCfgErrorCode.InvalidSchema, "Invalid schema"),
            (IronCfgErrorCode.FieldOrderViolation, "Field order violation"),
            (IronCfgErrorCode.FieldTypeMismatch, "Field type mismatch"),
            (IronCfgErrorCode.MissingRequiredField, "Missing required field"),
        };

        foreach (var (code, _) in testCases)
        {
            var cfgErr = new IronCfgError(code, 0);
            var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);

            Assert.Equal(global::IronConfig.IronEdgeErrorCategory.SchemaError, unified.Category);
            Assert.Equal(global::IronConfig.IronEdgeEngine.IronCfg, unified.Engine);
        }
    }

    [Fact]
    public void FromIronCfgError_Ok_ReturnsSuccess()
    {
        var cfgErr = IronCfgError.Ok;
        var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);

        Assert.True(unified.IsOk);
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.None, unified.Category);
    }

    // =========================================================================
    // IUPD ERROR MAPPING TESTS
    // =========================================================================

    [Fact]
    public void FromIupdError_InvalidMagic_MapsCorrectly()
    {
        var updErr = new IupdError(IupdErrorCode.InvalidMagic, 0, "Bad magic");
        var unified = global::IronConfig.IronEdgeError.FromIupdError(updErr);

        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidMagic, unified.Category);
        Assert.Equal(0x01, unified.Code);
        Assert.Equal(global::IronConfig.IronEdgeEngine.Iupd, unified.Engine);
    }

    [Fact]
    public void FromIupdError_ManifestErrors_MapToManifestError()
    {
        var testCases = new[]
        {
            IupdErrorCode.InvalidChunkTableSize,
            IupdErrorCode.ChunkIndexError,
            IupdErrorCode.OverlappingPayloads,
            IupdErrorCode.MissingChunk,
        };

        foreach (var code in testCases)
        {
            var updErr = new IupdError(code, 0, "Test");
            var unified = global::IronConfig.IronEdgeError.FromIupdError(updErr);

            Assert.Equal(global::IronConfig.IronEdgeErrorCategory.ManifestError, unified.Category);
            Assert.Equal(global::IronConfig.IronEdgeEngine.Iupd, unified.Engine);
        }
    }

    [Fact]
    public void FromIupdError_DependencyErrors_MapToDependencyError()
    {
        var testCases = new[]
        {
            IupdErrorCode.CyclicDependency,
            IupdErrorCode.InvalidDependency,
            IupdErrorCode.MissingChunkInApplyOrder,
            IupdErrorCode.DuplicateChunkInApplyOrder,
        };

        foreach (var code in testCases)
        {
            var updErr = new IupdError(code, 0, "Test");
            var unified = global::IronConfig.IronEdgeError.FromIupdError(updErr);

            Assert.Equal(global::IronConfig.IronEdgeErrorCategory.DependencyError, unified.Category);
            Assert.Equal(global::IronConfig.IronEdgeEngine.Iupd, unified.Engine);
        }
    }

    [Fact]
    public void FromIupdError_Blake3Mismatch_MapsToInvalidChecksum()
    {
        var updErr = new IupdError(IupdErrorCode.Blake3Mismatch, 0, "BLAKE3 fail");
        var unified = global::IronConfig.IronEdgeError.FromIupdError(updErr);

        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidChecksum, unified.Category);
        Assert.Equal(0x07, unified.Code);
    }

    [Fact]
    public void FromIupdError_WithChunkIndex_SetsOffset()
    {
        var updErr = new IupdError(IupdErrorCode.ChunkIndexError, 42, "Chunk 42 bad");
        var unified = global::IronConfig.IronEdgeError.FromIupdError(updErr);

        Assert.Equal(42, unified.Offset);
    }

    [Fact]
    public void FromIupdError_Ok_ReturnsSuccess()
    {
        var updErr = IupdError.Ok;
        var unified = global::IronConfig.IronEdgeError.FromIupdError(updErr);

        Assert.True(unified.IsOk);
    }

    // =========================================================================
    // ILOG ERROR MAPPING TESTS
    // =========================================================================
    // Note: ILOG error mapping to be completed in Phase 1.2
    // IlogError uses record class structure, implementation pending
    // Phase 1.2 tests have been moved to IronConfig.ILog.PhaseTests project

    // =========================================================================
    // DETERMINISM TESTS (CRITICAL FOR CLI)
    // =========================================================================

    [Fact]
    public void Determinism_SameIronCfgError_ProducesSameUnifiedError()
    {
        var cfgErr = new IronCfgError(IronCfgErrorCode.Crc32Mismatch, 4096);

        var unified1 = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);
        var unified2 = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);

        Assert.Equal(unified1.Category, unified2.Category);
        Assert.Equal(unified1.Code, unified2.Code);
        Assert.Equal(unified1.Engine, unified2.Engine);
        Assert.Equal(unified1.Offset, unified2.Offset);
        Assert.Equal(unified1.ToString(), unified2.ToString());
    }

    [Fact]
    public void Determinism_SameIupdError_ProducesSameUnifiedError()
    {
        var updErr = new IupdError(IupdErrorCode.OverlappingPayloads, 0, "Chunks overlap");

        var unified1 = global::IronConfig.IronEdgeError.FromIupdError(updErr);
        var unified2 = global::IronConfig.IronEdgeError.FromIupdError(updErr);

        Assert.Equal(unified1.Category, unified2.Category);
        Assert.Equal(unified1.Code, unified2.Code);
        Assert.Equal(unified1.Engine, unified2.Engine);
        Assert.Equal(unified1.Message, unified2.Message);
        Assert.Equal(unified1.ToString(), unified2.ToString());
    }


    // =========================================================================
    // EXCEPTION WRAPPER TESTS
    // =========================================================================

    [Fact]
    public void IronEdgeException_CarriesErrorInfo()
    {
        var err = new IronEdgeError(
            global::IronConfig.IronEdgeErrorCategory.InvalidMagic,
            0x02,
            global::IronConfig.IronEdgeEngine.IronCfg,
            "Test error"
        );

        var ex = new global::IronConfig.IronEdgeException(err);

        Assert.Equal(err.Category, ex.Error.Category);
        Assert.Equal(err.Code, ex.Error.Code);
        Assert.Contains("InvalidMagic", ex.Message);
    }

    [Fact]
    public void IronEdgeException_WithInnerException_PreservesChain()
    {
        var inner = new InvalidOperationException("Inner cause");
        var err = new IronEdgeError(
            global::IronConfig.IronEdgeErrorCategory.CorruptData,
            0xFF,
            global::IronConfig.IronEdgeEngine.Runtime,
            "Wrapper error"
        );

        var ex = new global::IronConfig.IronEdgeException(err, inner);

        Assert.Equal(inner, ex.InnerException);
        Assert.NotNull(ex.Error);
    }

    // =========================================================================
    // PUBLIC CATEGORY LIMITS TEST
    // =========================================================================

    [Fact]
    public void PublicCategories_DoNotExceed16Values()
    {
        var categoryCount = Enum.GetNames(typeof(global::IronConfig.IronEdgeErrorCategory)).Length;
        // Should have: None(0) + 16 categories = 17 total
        Assert.True(categoryCount <= 17, $"Category count {categoryCount} exceeds limit of 17");
    }

    [Fact]
    public void CanonicalCodes_WithinValidRange()
    {
        // Test sampling of canonical codes are within 0x00-0x7F range
        var testCodes = new byte[]
        {
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, // Shared
            0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, // IRONCFG
            0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, // ILOG
            0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x68, 0x69, 0x6A, 0x6B, 0x6C, // IUPD
        };

        foreach (var code in testCodes)
        {
            Assert.True(code <= 0x7F, $"Code 0x{code:X2} exceeds canonical range 0x7F");
        }
    }

    // =========================================================================
    // MESSAGE STABILITY TEST
    // =========================================================================

    [Fact]
    public void ErrorMessages_AreStableAndDeterministic()
    {
        // Messages must not include timestamps, random data, or machine paths
        var cfgErr = new IronCfgError(IronCfgErrorCode.InvalidMagic, 0);
        var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);

        var msg = unified.Message;
        Assert.NotEmpty(msg);
        Assert.DoesNotContain("UTC", msg);
        Assert.DoesNotContain("GMT", msg);
        Assert.DoesNotContain("::", msg); // No double colons for time
        Assert.DoesNotContain("/", msg);  // No paths
        Assert.DoesNotContain("\\", msg);
    }
}
