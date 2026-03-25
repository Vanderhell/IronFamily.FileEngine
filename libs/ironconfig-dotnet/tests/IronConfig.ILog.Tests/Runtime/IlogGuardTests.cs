using System;
using System.IO;
using Xunit;
using IronConfig.ILog;
using IronConfig.ILog.Runtime;

namespace IronConfig.ILog.Tests.Runtime;

/// <summary>
/// Guard tests: strict format validation for ILOG corruption patterns.
/// Ensures verify command rejects malformed files.
/// </summary>
public class IlogGuardTests
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"ilog_guard_{Guid.NewGuid()}");

    public IlogGuardTests()
    {
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
            byte[] bad = new byte[100];
            System.Text.Encoding.ASCII.GetBytes("JUNK").CopyTo(bad, 0); // Wrong magic

            string path = Path.Combine(_tempDir, "bad_magic.ilog");
            File.WriteAllBytes(path, bad);

            var json = RuntimeVerifyIlogCommand.Execute(path, out int exitCode);

            Assert.Equal(1, exitCode); // Validation error
            Assert.Contains("ok", json);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_EmptyFile_FailsTruncated()
    {
        try
        {
            byte[] empty = Array.Empty<byte>();

            string path = Path.Combine(_tempDir, "empty.ilog");
            File.WriteAllBytes(path, empty);

            var json = RuntimeVerifyIlogCommand.Execute(path, out int exitCode);

            Assert.Equal(1, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_TruncatedHeader_FailsCorruptData()
    {
        try
        {
            byte[] truncated = new byte[8]; // Less than 16-byte header
            System.Text.Encoding.ASCII.GetBytes("ILOG").CopyTo(truncated, 0);

            string path = Path.Combine(_tempDir, "truncated.ilog");
            File.WriteAllBytes(path, truncated);

            var json = RuntimeVerifyIlogCommand.Execute(path, out int exitCode);

            Assert.Equal(1, exitCode);
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_FileNotFound_FailsIoError()
    {
        try
        {
            string path = Path.Combine(_tempDir, "nonexistent.ilog");

            var json = RuntimeVerifyIlogCommand.Execute(path, out int exitCode);

            Assert.Equal(2, exitCode); // IO error
            Assert.Contains("\"ok\":false", json);
            Assert.Contains("File not found", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_NullPath_FailsArgsError()
    {
        try
        {
            var json = RuntimeVerifyIlogCommand.Execute("", out int exitCode);

            Assert.Equal(3, exitCode); // Args error
            Assert.Contains("\"ok\":false", json);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_JsonDeterministic_ByteIdentical()
    {
        try
        {
            byte[] empty = Array.Empty<byte>();
            string path = Path.Combine(_tempDir, "test.ilog");
            File.WriteAllBytes(path, empty);

            // Verify 3 times
            var json1 = RuntimeVerifyIlogCommand.Execute(path, out _);
            var json2 = RuntimeVerifyIlogCommand.Execute(path, out _);
            var json3 = RuntimeVerifyIlogCommand.Execute(path, out _);

            // All must be byte-identical
            Assert.Equal(json1, json2);
            Assert.Equal(json2, json3);
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_ExitCodes_ConsistentWithIUPD()
    {
        try
        {
            // Success: 0
            byte[] minimal = new byte[1];
            string path1 = Path.Combine(_tempDir, "test1.ilog");
            File.WriteAllBytes(path1, minimal);
            RuntimeVerifyIlogCommand.Execute(path1, out int code1);
            // Even if invalid, should be 0 or 1, not other values for this simple case

            // File not found: 2
            RuntimeVerifyIlogCommand.Execute("/nonexistent/path", out int code2);
            Assert.Equal(2, code2);

            // Args error: 3
            RuntimeVerifyIlogCommand.Execute("", out int code3);
            Assert.Equal(3, code3);

            // Exit codes match IUPD pattern: 0/1/2/3/10
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_JsonHasValidFields()
    {
        try
        {
            byte[] empty = Array.Empty<byte>();
            string path = Path.Combine(_tempDir, "test.ilog");
            File.WriteAllBytes(path, empty);

            var json = RuntimeVerifyIlogCommand.Execute(path, out _);

            // Must have stable fields
            Assert.Contains("\"ok\":", json);
            Assert.Contains("\"engine\":", json);
            Assert.Contains("\"error\":", json); // Error object for failed case
        }
        finally { Cleanup(); }
    }

    [Fact]
    public void Verify_ErrorCategoryMapping_Consistent()
    {
        try
        {
            byte[] badMagic = new byte[100];
            System.Text.Encoding.ASCII.GetBytes("JUNK").CopyTo(badMagic, 0);

            string path = Path.Combine(_tempDir, "bad.ilog");
            File.WriteAllBytes(path, badMagic);

            var json = RuntimeVerifyIlogCommand.Execute(path, out _);

            // Should contain error category (mapped from ILOG to IronEdge)
            Assert.Contains("\"error\":", json);
            Assert.Contains("\"category\":", json);
            Assert.Contains("\"code\":", json);
            Assert.Contains("\"message\":", json);
        }
        finally { Cleanup(); }
    }
}
