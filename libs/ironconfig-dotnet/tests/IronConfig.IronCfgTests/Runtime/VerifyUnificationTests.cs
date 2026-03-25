// Runtime Verify Unification Tests
// Validates consistency of JSON shape and exit codes across all engines

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using IronConfig;
using IronConfig.IronCfg;
using IronConfig.ILog.Runtime;
using IronConfig.Tooling;

namespace IronConfig.Tests.Runtime;

/// <summary>
/// Tests to validate unification of verify commands across engines.
/// Ensures consistent JSON shape, exit codes, and determinism.
/// </summary>
public class VerifyUnificationTests
{
    private static byte[] CreateValidIronCfgFile()
    {
        var buf = new byte[70];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;
        buf[5] = 0;
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

    private static void WriteUInt32LE(byte[] buf, int off, uint val)
    {
        buf[off] = (byte)(val & 0xFF);
        buf[off + 1] = (byte)((val >> 8) & 0xFF);
        buf[off + 2] = (byte)((val >> 16) & 0xFF);
        buf[off + 3] = (byte)((val >> 24) & 0xFF);
    }

    [Fact]
    public void Verify_IronCfgSuccess_JsonShape_HasStableTopLevelKeys()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            var valid = CreateValidIronCfgFile();
            var filePath = TestHelpers.WriteTestFile(tempDir, "valid.icfg", valid);

            var json = RuntimeVerifyCommand.Execute(filePath, out _);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Required top-level keys for success case
            Assert.True(root.TryGetProperty("ok", out _), "Missing 'ok' field");
            Assert.True(root.TryGetProperty("engine", out _), "Missing 'engine' field");
            Assert.True(root.TryGetProperty("bytes_scanned", out _), "Missing 'bytes_scanned' field");
            Assert.False(root.TryGetProperty("error", out _), "Should not have 'error' on success");

            // Verify field ordering (order in JSON document)
            var enumerator = root.EnumerateObject();
            var keys = new List<string>();
            foreach (var prop in root.EnumerateObject())
            {
                keys.Add(prop.Name);
            }
            Assert.Equal(new[] { "ok", "engine", "bytes_scanned" }, keys);
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Verify_IronCfgFailure_JsonShape_HasStableTopLevelKeys()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            var bad = CreateValidIronCfgFile();
            bad[0] = 0xFF; // Corrupt magic

            var filePath = TestHelpers.WriteTestFile(tempDir, "bad.icfg", bad);
            var json = RuntimeVerifyCommand.Execute(filePath, out _);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Required top-level keys for failure case
            Assert.True(root.TryGetProperty("ok", out _), "Missing 'ok' field");
            Assert.True(root.TryGetProperty("engine", out _), "Missing 'engine' field");
            Assert.True(root.TryGetProperty("bytes_scanned", out _), "Missing 'bytes_scanned' field");
            Assert.True(root.TryGetProperty("error", out _), "Missing 'error' field on failure");

            // Verify error has required fields
            var error = root.GetProperty("error");
            Assert.True(error.TryGetProperty("category", out _), "Missing 'error.category'");
            Assert.True(error.TryGetProperty("code", out _), "Missing 'error.code'");
            Assert.True(error.TryGetProperty("message", out _), "Missing 'error.message'");
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Verify_ExitCodes_MapCorrectly()
    {
        // Verify success case
        Assert.Equal(0, (int)VerifyExitCode.Success);

        // Verify failure cases
        Assert.Equal(1, (int)VerifyExitCode.ValidationError);
        Assert.Equal(2, (int)VerifyExitCode.IoError);
        Assert.Equal(3, (int)VerifyExitCode.InvalidArguments);
        Assert.Equal(10, (int)VerifyExitCode.InternalFailure);
    }

    [Fact]
    public void Verify_IronCfgAndIlogUseConsistentExitCodes()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            // Test IRONCFG failure
            var badIcfg = CreateValidIronCfgFile();
            badIcfg[0] = 0xFF;
            var icfgPath = TestHelpers.WriteTestFile(tempDir, "bad.icfg", badIcfg);
            RuntimeVerifyCommand.Execute(icfgPath, out var icfgExitCode);
            Assert.Equal(VerifyExitCode.ValidationError, icfgExitCode);

            // Test ILOG failure (bad magic)
            var badIlog = new byte[100];
            System.Text.Encoding.ASCII.GetBytes("JUNK").CopyTo(badIlog, 0);
            var ilogPath = TestHelpers.WriteTestFile(tempDir, "bad.ilog", badIlog);
            RuntimeVerifyIlogCommand.Execute(ilogPath, out var ilogExitCode);
            Assert.Equal(1, ilogExitCode); // ILOG uses int, same numeric value

            // Both should map to validation error (1)
            Assert.Equal((int)VerifyExitCode.ValidationError, ilogExitCode);
            Assert.Equal((int)VerifyExitCode.ValidationError, (int)icfgExitCode);
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Verify_FileNotFound_ExitCode_Consistent()
    {
        // IRONCFG verify missing file
        var json1 = RuntimeVerifyCommand.Execute("/nonexistent/path.icfg", out var code1);
        Assert.Equal(VerifyExitCode.IoError, code1);

        // ILOG verify missing file
        RuntimeVerifyIlogCommand.Execute("/nonexistent/path.ilog", out var code2);
        Assert.Equal(2, code2); // IoError = 2

        // Both should be IO error
        Assert.Equal((int)VerifyExitCode.IoError, (int)code1);
        Assert.Equal((int)VerifyExitCode.IoError, code2);
    }

    [Fact]
    public void Verify_JsonDeterministicAcrossEngines()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            // Create valid files
            var validIcfg = CreateValidIronCfgFile();
            var icfgPath = TestHelpers.WriteTestFile(tempDir, "valid.icfg", validIcfg);

            // Verify multiple times - should get identical JSON
            var json1 = RuntimeVerifyCommand.Execute(icfgPath, out _);
            var json2 = RuntimeVerifyCommand.Execute(icfgPath, out _);
            var json3 = RuntimeVerifyCommand.Execute(icfgPath, out _);

            Assert.Equal(json1, json2);
            Assert.Equal(json2, json3);

            // Parse and validate structure
            var doc = JsonDocument.Parse(json1);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal("IRONCFG", root.GetProperty("engine").GetString());
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }
}
