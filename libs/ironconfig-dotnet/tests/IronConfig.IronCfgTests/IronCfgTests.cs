using System;
using Xunit;
using IronConfig.IronCfg;

namespace IronConfig.Tests;

public class IronCfgTests
{
    /// <summary>
    /// Helper to create a valid header buffer
    /// </summary>
    private static byte[] CreateHeaderBuffer(uint fileSize, uint schemaSize, uint dataSize)
    {
        var buf = new byte[1024];
        Array.Clear(buf);

        // Magic: ICFG (little-endian)
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;
        // Version
        buf[4] = 1;
        // Flags: CRC32 enabled
        buf[5] = 0x01;
        // Reserved0
        buf[6] = 0; buf[7] = 0;
        // FileSize
        WriteUInt32LE(buf, 8, fileSize);
        // SchemaOffset = 64
        WriteUInt32LE(buf, 12, 64);
        // SchemaSize
        WriteUInt32LE(buf, 16, schemaSize);
        // StringPoolOffset = 0
        WriteUInt32LE(buf, 20, 0);
        // StringPoolSize = 0
        WriteUInt32LE(buf, 24, 0);
        // DataOffset
        uint dataOffset = 64 + schemaSize;
        WriteUInt32LE(buf, 28, dataOffset);
        // DataSize
        WriteUInt32LE(buf, 32, dataSize);
        // CrcOffset
        uint crcOffset = 64 + schemaSize + dataSize;
        WriteUInt32LE(buf, 36, crcOffset);
        // Blake3Offset = 0
        WriteUInt32LE(buf, 40, 0);
        // Reserved1 = 0
        WriteUInt32LE(buf, 44, 0);
        // Reserved2 (16 bytes) = 0

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
    public void TestTruncatedFile()
    {
        var buf = new byte[32];
        var error = IronCfgValidator.ValidateFast(buf);
        Assert.Equal(IronCfgErrorCode.TruncatedFile, error.Code);
        Assert.Equal(0u, error.Offset);
    }

    [Fact]
    public void TestInvalidMagic()
    {
        var buf = new byte[64];
        Array.Clear(buf);
        buf[0] = 0xFF; buf[1] = 0xFF; buf[2] = 0xFF; buf[3] = 0xFF;
        buf[4] = 1;   // valid version
        buf[5] = 0;   // valid flags

        var error = IronCfgValidator.ValidateFast(buf);
        Assert.Equal(IronCfgErrorCode.InvalidMagic, error.Code);
        Assert.Equal(0u, error.Offset);
    }

    [Fact]
    public void TestInvalidVersion()
    {
        var buf = new byte[64];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 3;   // invalid version (only 1 and 2 are valid)
        buf[5] = 0;

        var error = IronCfgValidator.ValidateFast(buf);
        Assert.Equal(IronCfgErrorCode.InvalidVersion, error.Code);
        Assert.Equal(4u, error.Offset);
    }

    [Fact]
    public void TestInvalidFlags()
    {
        var buf = new byte[64];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;
        buf[5] = 0x80;  // reserved bit 7 set

        var error = IronCfgValidator.ValidateFast(buf);
        Assert.Equal(IronCfgErrorCode.InvalidFlags, error.Code);
        Assert.Equal(5u, error.Offset);
    }

    [Fact]
    public void TestReserved0Nonzero()
    {
        var buf = new byte[64];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;
        buf[5] = 0;
        buf[6] = 0xFF;  // reserved0 non-zero

        var error = IronCfgValidator.ValidateFast(buf);
        Assert.Equal(IronCfgErrorCode.ReservedFieldNonzero, error.Code);
        Assert.Equal(6u, error.Offset);
    }

    [Fact]
    public void TestFlagMismatchCrc()
    {
        var buf = new byte[64];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;
        buf[5] = 0x01;  // CRC flag set
        buf[6] = 0; buf[7] = 0;
        // FileSize = 68
        WriteUInt32LE(buf, 8, 68);
        // SchemaOffset = 64
        WriteUInt32LE(buf, 12, 64);
        // SchemaSize = 1
        WriteUInt32LE(buf, 16, 1);
        // StringPoolOffset = 0
        WriteUInt32LE(buf, 20, 0);
        // StringPoolSize = 0
        WriteUInt32LE(buf, 24, 0);
        // DataOffset = 65
        WriteUInt32LE(buf, 28, 65);
        // DataSize = 1
        WriteUInt32LE(buf, 32, 1);
        // CrcOffset = 0 (mismatch!)
        WriteUInt32LE(buf, 36, 0);

        var error = IronCfgValidator.ValidateFast(buf);
        Assert.Equal(IronCfgErrorCode.FlagMismatch, error.Code);
        Assert.Equal(5u, error.Offset);
    }

    [Fact]
    public void TestBoundsViolation()
    {
        var buf = new byte[64];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;
        buf[5] = 0;
        // FileSize = 64
        WriteUInt32LE(buf, 8, 64);
        // SchemaOffset = 64
        WriteUInt32LE(buf, 12, 64);
        // SchemaSize = 100 (extends beyond file)
        WriteUInt32LE(buf, 16, 100);
        // StringPoolOffset = 0
        WriteUInt32LE(buf, 20, 0);
        // StringPoolSize = 0
        WriteUInt32LE(buf, 24, 0);
        // DataOffset = 64
        WriteUInt32LE(buf, 28, 64);
        // DataSize = 1
        WriteUInt32LE(buf, 32, 1);

        var error = IronCfgValidator.ValidateFast(buf);
        Assert.Equal(IronCfgErrorCode.BoundsViolation, error.Code);
    }

    [Fact]
    public void TestFileSizeMismatch()
    {
        var buf = new byte[64];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;
        buf[5] = 0;
        // FileSize = 100 but buffer is 64
        WriteUInt32LE(buf, 8, 100);
        WriteUInt32LE(buf, 12, 64);  // SchemaOffset
        WriteUInt32LE(buf, 16, 1);   // SchemaSize
        WriteUInt32LE(buf, 20, 0);   // StringPoolOffset
        WriteUInt32LE(buf, 24, 0);   // StringPoolSize
        WriteUInt32LE(buf, 28, 65);  // DataOffset
        WriteUInt32LE(buf, 32, 1);   // DataSize

        var error = IronCfgValidator.ValidateFast(buf);
        Assert.Equal(IronCfgErrorCode.BoundsViolation, error.Code);
    }

    [Fact]
    public void TestValidateFastOk()
    {
        var buf = new byte[70];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;
        buf[5] = 0;  // No CRC/BLAKE3
        buf[6] = 0; buf[7] = 0;
        // FileSize = 70
        WriteUInt32LE(buf, 8, 70);
        // SchemaOffset = 64
        WriteUInt32LE(buf, 12, 64);
        // SchemaSize = 1
        WriteUInt32LE(buf, 16, 1);
        // StringPoolOffset = 0
        WriteUInt32LE(buf, 20, 0);
        // StringPoolSize = 0
        WriteUInt32LE(buf, 24, 0);
        // DataOffset = 65
        WriteUInt32LE(buf, 28, 65);
        // DataSize = 5
        WriteUInt32LE(buf, 32, 5);
        // CrcOffset = 0
        WriteUInt32LE(buf, 36, 0);
        // Blake3Offset = 0
        WriteUInt32LE(buf, 40, 0);

        var error = IronCfgValidator.ValidateFast(buf);
        Assert.True(error.IsOk);
    }

    [Fact]
    public void TestOpenAndView()
    {
        var buf = new byte[70];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;
        buf[5] = 0;
        buf[6] = 0; buf[7] = 0;
        // FileSize = 70
        WriteUInt32LE(buf, 8, 70);
        WriteUInt32LE(buf, 12, 64);  // SchemaOffset
        WriteUInt32LE(buf, 16, 1);   // SchemaSize
        WriteUInt32LE(buf, 20, 0);   // StringPoolOffset
        WriteUInt32LE(buf, 24, 0);   // StringPoolSize
        WriteUInt32LE(buf, 28, 65);  // DataOffset
        WriteUInt32LE(buf, 32, 5);   // DataSize
        WriteUInt32LE(buf, 36, 0);   // CrcOffset
        WriteUInt32LE(buf, 40, 0);   // Blake3Offset

        var memory = new ReadOnlyMemory<byte>(buf);
        var openError = IronCfgValidator.Open(memory, out var view);
        Assert.True(openError.IsOk);

        // Test schema accessor
        var schemaError = view.GetSchema(out var schema);
        Assert.True(schemaError.IsOk);
        Assert.Equal(1, schema.Length);

        // Test data accessor
        var dataError = view.GetRoot(out var data);
        Assert.True(dataError.IsOk);
        Assert.Equal(5, data.Length);

        // Test properties
        Assert.Equal(70u, view.GetFileSize());
        Assert.False(view.HasCrc32());
        Assert.False(view.HasBlake3());
    }
}
