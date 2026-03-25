// IronCfg Guard Tests
// Strict format validation for IRONCFG corruption patterns.
// Ensures verify command rejects malformed files.

using System;
using System.IO;
using System.Text.Json;
using Xunit;
using IronConfig;
using IronConfig.IronCfg;
using IronConfig.Tooling;

namespace IronConfig.Tests.Runtime;

/// <summary>
/// Guard tests: strict format validation for IRONCFG corruption patterns.
/// Ensures verify command rejects malformed files.
/// </summary>
public class IronCfgGuardTests
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

    private string _tempDir = Path.Combine(Path.GetTempPath(), $"ironcfg_guard_{Guid.NewGuid()}");

    public IronCfgGuardTests()
    {
        if (!Directory.Exists(_tempDir))
            Directory.CreateDirectory(_tempDir);
    }

    private void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Verify_BadMagic_FailsInvalidMagic()
    {
        try
        {
            byte[] bad = new byte[70];
            System.Text.Encoding.ASCII.GetBytes("JUNK").CopyTo(bad, 0); // Wrong magic

            string path = Path.Combine(_tempDir, "bad_magic.icfg");
            File.WriteAllBytes(path, bad);

            var json = RuntimeVerifyCommand.Execute(path, out var exitCode);

            Assert.Equal(VerifyExitCode.ValidationError, exitCode);
            Assert.Contains("\"ok\":false", json);
            var doc = JsonDocument.Parse(json);
            Assert.Equal("InvalidMagic", doc.RootElement.GetProperty("error").GetProperty("category").GetString());
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_EmptyFile_FailsTruncated()
    {
        try
        {
            byte[] empty = Array.Empty<byte>();

            string path = Path.Combine(_tempDir, "empty.icfg");
            File.WriteAllBytes(path, empty);

            var json = RuntimeVerifyCommand.Execute(path, out var exitCode);

            Assert.Equal(VerifyExitCode.ValidationError, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_TruncatedHeader_FailsCorruptData()
    {
        try
        {
            byte[] truncated = new byte[8]; // Less than 64-byte header
            System.Text.Encoding.ASCII.GetBytes("ICFG").CopyTo(truncated, 0);

            string path = Path.Combine(_tempDir, "truncated_header.icfg");
            File.WriteAllBytes(path, truncated);

            var json = RuntimeVerifyCommand.Execute(path, out var exitCode);

            Assert.Equal(VerifyExitCode.ValidationError, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_TruncatedPayload_FailsCorruptData()
    {
        try
        {
            var valid = CreateValidIronCfgFile();
            // Truncate to 50 bytes (cuts into data payload)
            var truncated = new byte[50];
            Array.Copy(valid, truncated, 50);

            string path = Path.Combine(_tempDir, "truncated_payload.icfg");
            File.WriteAllBytes(path, truncated);

            var json = RuntimeVerifyCommand.Execute(path, out var exitCode);

            Assert.Equal(VerifyExitCode.ValidationError, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_TrailingBytes_Rejected_Strict()
    {
        try
        {
            var valid = CreateValidIronCfgFile();
            // Add trailing bytes
            var withTrailing = new byte[valid.Length + 10];
            Array.Copy(valid, withTrailing, valid.Length);
            Array.Fill<byte>(withTrailing, 0xFF, valid.Length, 10);

            string path = Path.Combine(_tempDir, "trailing_bytes.icfg");
            File.WriteAllBytes(path, withTrailing);

            var json = RuntimeVerifyCommand.Execute(path, out var exitCode);

            Assert.Equal(VerifyExitCode.ValidationError, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_InvalidVersion_Rejected()
    {
        try
        {
            var valid = CreateValidIronCfgFile();
            valid[4] = 99; // Invalid version

            string path = Path.Combine(_tempDir, "invalid_version.icfg");
            File.WriteAllBytes(path, valid);

            var json = RuntimeVerifyCommand.Execute(path, out var exitCode);

            Assert.Equal(VerifyExitCode.ValidationError, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_InvalidFileSizeField_FailsCorruptData()
    {
        try
        {
            var valid = CreateValidIronCfgFile();
            // Corrupt FileSize field (offset 8)
            WriteUInt32LE(valid, 8, 999); // Invalid size

            string path = Path.Combine(_tempDir, "invalid_filesize.icfg");
            File.WriteAllBytes(path, valid);

            var json = RuntimeVerifyCommand.Execute(path, out var exitCode);

            Assert.Equal(VerifyExitCode.ValidationError, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_NullPath_FailsArgsError()
    {
        try
        {
            var json = RuntimeVerifyCommand.Execute("", out var exitCode);

            Assert.Equal(VerifyExitCode.InvalidArguments, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_FileNotFound_FailsIoError()
    {
        try
        {
            string path = Path.Combine(_tempDir, "nonexistent.icfg");

            var json = RuntimeVerifyCommand.Execute(path, out var exitCode);

            Assert.Equal(VerifyExitCode.IoError, exitCode);
            Assert.Contains("\"ok\":false", json);
            Assert.Contains("File not found", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_JsonDeterministic_ByteIdentical_IRONCFG()
    {
        try
        {
            var valid = CreateValidIronCfgFile();
            string path = Path.Combine(_tempDir, "test.icfg");
            File.WriteAllBytes(path, valid);

            // Verify 3 times
            var json1 = RuntimeVerifyCommand.Execute(path, out _);
            var json2 = RuntimeVerifyCommand.Execute(path, out _);
            var json3 = RuntimeVerifyCommand.Execute(path, out _);

            // All must be byte-identical
            Assert.Equal(json1, json2);
            Assert.Equal(json2, json3);
        }
        finally { Cleanup(); }
    }
}
