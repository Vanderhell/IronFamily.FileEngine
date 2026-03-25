// Phase D: Runtime Verify Command Tests
// Validates deterministic JSON output, exit codes, and error handling

using System;
using System.IO;
using System.Text.Json;
using Xunit;
using IronConfig;
using IronConfig.IronCfg;
using IronConfig.Iupd;
using IronConfig.Tooling;

namespace IronConfig.Tests;

public class RuntimeVerifyTests
{
    private static void WriteUInt32LE(byte[] buf, int off, uint val)
    {
        buf[off] = (byte)(val & 0xFF);
        buf[off + 1] = (byte)((val >> 8) & 0xFF);
        buf[off + 2] = (byte)((val >> 16) & 0xFF);
        buf[off + 3] = (byte)((val >> 24) & 0xFF);
    }

    private static byte[] CreateValidIronCfgFile()
    {
        var buf = new byte[70];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;  // version
        buf[5] = 0;  // flags
        buf[6] = 0; buf[7] = 0;
        WriteUInt32LE(buf, 8, 70);   // FileSize
        WriteUInt32LE(buf, 12, 64);  // SchemaOffset
        WriteUInt32LE(buf, 16, 1);   // SchemaSize
        WriteUInt32LE(buf, 20, 0);   // StringPoolOffset
        WriteUInt32LE(buf, 24, 0);   // StringPoolSize
        WriteUInt32LE(buf, 28, 65);  // DataOffset
        WriteUInt32LE(buf, 32, 5);   // DataSize
        WriteUInt32LE(buf, 36, 0);   // CrcOffset
        WriteUInt32LE(buf, 40, 0);   // Blake3Offset
        return buf;
    }

    /// <summary>
    /// D1) JSON Determinism Test
    /// Run verify twice on same valid file, assert exact string equality
    /// </summary>
    [Fact]
    public void RuntimeVerify_ValidFile_ProducesDeterministicJson()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            var validFile = CreateValidIronCfgFile();
            var filePath = TestHelpers.WriteTestFile(tempDir, "valid.icfg", validFile);

            // Run verify twice
            var json1 = RuntimeVerifyCommand.Execute(filePath, out var exitCode1);
            var json2 = RuntimeVerifyCommand.Execute(filePath, out var exitCode2);

            // Assert identical output
            Assert.Equal(json1, json2);
            Assert.Equal(VerifyExitCode.Success, exitCode1);
            Assert.Equal(VerifyExitCode.Success, exitCode2);

            // Verify it's valid JSON and contains expected fields
            var doc = JsonDocument.Parse(json1);
            Assert.True(doc.RootElement.TryGetProperty("ok", out var okProp));
            Assert.True(okProp.GetBoolean());
            Assert.True(doc.RootElement.TryGetProperty("engine", out var engineProp));
            Assert.Equal("IRONCFG", engineProp.GetString());
            Assert.True(doc.RootElement.TryGetProperty("bytes_scanned", out var bytesProp));
            Assert.Equal(70L, bytesProp.GetInt64());
            Assert.False(doc.RootElement.TryGetProperty("error", out _));
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// D2) Corruption Detection Test
    /// Verify corrupted file produces correct error category and exit code
    /// </summary>
    [Fact]
    public void RuntimeVerify_CorruptedFile_ReturnsValidationError()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            var corruptedFile = CreateValidIronCfgFile();
            // Flip magic byte to corrupt the file
            TestHelpers.FlipBit(corruptedFile, 0, 0);

            var filePath = TestHelpers.WriteTestFile(tempDir, "corrupted.icfg", corruptedFile);

            var json = RuntimeVerifyCommand.Execute(filePath, out var exitCode);

            // Assert validation error exit code
            Assert.Equal(VerifyExitCode.ValidationError, exitCode);

            // Parse and verify error fields
            var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(doc.RootElement.TryGetProperty("error", out var errorProp));

            var error = errorProp.GetProperty("category");
            Assert.Equal("InvalidMagic", error.GetString());

            var code = errorProp.GetProperty("code");
            Assert.Equal(0x04, code.GetInt32());
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// D3) File Not Found Test
    /// Verify IO error for non-existent file
    /// </summary>
    [Fact]
    public void RuntimeVerify_FileNotFound_ReturnsIoError()
    {
        var json = RuntimeVerifyCommand.Execute("/nonexistent/path/file.icfg", out var exitCode);

        Assert.Equal(VerifyExitCode.IoError, exitCode);

        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Runtime", doc.RootElement.GetProperty("engine").GetString());

        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("Io", error.GetProperty("category").GetString());
        Assert.Equal(0x02, error.GetProperty("code").GetInt32());
    }

    /// <summary>
    /// D4) Invalid Arguments Test
    /// Verify InvalidArguments exit code for empty file path
    /// </summary>
    [Fact]
    public void RuntimeVerify_EmptyPath_ReturnsInvalidArguments()
    {
        var json = RuntimeVerifyCommand.Execute("", out var exitCode);

        Assert.Equal(VerifyExitCode.InvalidArguments, exitCode);

        var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("category").GetString());
    }

    /// <summary>
    /// D5) JSON Field Ordering Test
    /// Verify exact field order in success and error cases
    /// </summary>
    [Fact]
    public void RuntimeVerify_JsonFieldOrder_IsStable()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            var validFile = CreateValidIronCfgFile();
            var filePath = TestHelpers.WriteTestFile(tempDir, "order_test.icfg", validFile);

            var json = RuntimeVerifyCommand.Execute(filePath, out _);

            // Check field order by finding substring positions
            int okPos = json.IndexOf("\"ok\":");
            int enginePos = json.IndexOf("\"engine\":");
            int bytesPos = json.IndexOf("\"bytes_scanned\":");

            Assert.True(okPos < enginePos, "ok should come before engine");
            Assert.True(enginePos < bytesPos, "engine should come before bytes_scanned");
            Assert.True(bytesPos < json.Length, "bytes_scanned should be present");

            // Verify error field (if present) comes last
            int errorPos = json.IndexOf("\"error\":");
            if (errorPos >= 0)
            {
                Assert.True(bytesPos < errorPos, "error should come after bytes_scanned");
            }
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// D6) Exit Code Success Test
    /// Verify exit code 0 for successful verification
    /// </summary>
    [Fact]
    public void RuntimeVerify_ValidFile_ReturnsExitCodeZero()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            var validFile = CreateValidIronCfgFile();
            var filePath = TestHelpers.WriteTestFile(tempDir, "success.icfg", validFile);

            RuntimeVerifyCommand.Execute(filePath, out var exitCode);

            Assert.Equal(VerifyExitCode.Success, exitCode);
            Assert.Equal(0, (int)exitCode);
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// D7) JSON Validity Test
    /// Ensure all outputs are valid JSON regardless of outcome
    /// </summary>
    [Fact]
    public void RuntimeVerify_AllOutputs_AreValidJson()
    {
        var testCases = new[]
        {
            "/nonexistent/file.icfg",  // IO error
            "",                         // Invalid args
        };

        foreach (var testPath in testCases)
        {
            var json = RuntimeVerifyCommand.Execute(testPath, out _);

            // Must be parseable JSON
            var doc = JsonDocument.Parse(json);
            Assert.NotNull(doc.RootElement);
            Assert.True(doc.RootElement.TryGetProperty("ok", out _));
            Assert.True(doc.RootElement.TryGetProperty("engine", out _));
            Assert.True(doc.RootElement.TryGetProperty("bytes_scanned", out _));
        }
    }

    /// <summary>
    /// D8) IUPD Engine Detection Test
    /// Verify IUPD files are correctly routed and validated
    /// </summary>
    [Fact]
    public void RuntimeVerify_IupdFile_RoutesToCorrectEngine()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            // Create a minimal IUPD file header
            var iupdFile = new byte[128];
            Array.Clear(iupdFile);
            iupdFile[0] = 0x49; iupdFile[1] = 0x55; iupdFile[2] = 0x50; iupdFile[3] = 0x44; // IUPD

            var filePath = TestHelpers.WriteTestFile(tempDir, "test.iupd", iupdFile);

            var json = RuntimeVerifyCommand.Execute(filePath, out _);

            var doc = JsonDocument.Parse(json);
            Assert.Equal("IUPD", doc.RootElement.GetProperty("engine").GetString());
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }
}
