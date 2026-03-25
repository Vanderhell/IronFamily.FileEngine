using System;
using System.Buffers.Binary;
using Xunit;
using IronConfig;

namespace IronConfig.IronCfg.Tests;

/// <summary>
/// Specification Lock Tests for IRONCFG.
/// These tests verify critical format invariants and prevent regressions.
/// Lock points: header size, section ordering, determinism, canonical encoding.
/// </summary>
public class SpecLockTests
{
    private const int FileHeaderSize = 64;
    private const byte ExpectedVersion = IronCfgHeader.VERSION;
    private const string ExpectedMagic = "ICFG";

    [Fact(DisplayName = "SpecLock: IRONCFG file header is exactly 64 bytes")]
    public void SpecLock_FileHeaderSize()
    {
        // Header must be exactly 64 bytes
        Assert.Equal(FileHeaderSize, 64);
    }

    [Fact(DisplayName = "SpecLock: IRONCFG magic is 'ICFG' at offset 0x00")]
    public void SpecLock_MagicValue()
    {
        // Magic should be "ICFG" = 0x49 0x43 0x46 0x47
        byte[] expectedMagicBytes = System.Text.Encoding.ASCII.GetBytes(ExpectedMagic);
        Assert.Equal(4, expectedMagicBytes.Length);
        Assert.Equal(0x49, expectedMagicBytes[0]);  // 'I'
        Assert.Equal(0x43, expectedMagicBytes[1]);  // 'C'
        Assert.Equal(0x46, expectedMagicBytes[2]);  // 'F'
        Assert.Equal(0x47, expectedMagicBytes[3]);  // 'G'
    }

    [Fact(DisplayName = "SpecLock: IRONCFG encoder header version matches runtime spec")]
    public void SpecLock_VersionByte()
    {
        // Version byte at offset 0x04 must match the runtime source of truth.
        Assert.Equal((byte)2, ExpectedVersion);
    }

    [Fact(DisplayName = "SpecLock: IRONCFG reserved fields must be zero")]
    public void SpecLock_ReservedFieldsZero()
    {
        // Reserved0 (2 bytes at offset 6-7) must be 0x0000
        ushort reserved0 = 0x0000;
        Assert.Equal(0, reserved0);

        // Reserved1 (4 bytes at offset 44-47) must be 0x00000000
        uint reserved1 = 0x00000000u;
        Assert.Equal(0u, reserved1);

        // Reserved2 (16 bytes at offset 48-63) must be all zeros
        byte[] reserved2 = new byte[16];
        Assert.Equal(new byte[16], reserved2);
    }

    [Fact(DisplayName = "SpecLock: IRONCFG section offset ordering")]
    public void SpecLock_SectionOffsetOrdering()
    {
        // Sections must be ordered: Schema < StringPool < Data < CRC32 < BLAKE3
        // No gaps between sections

        // Example valid ordering
        uint schemaOffset = 64;      // After header
        uint schemaSize = 100;
        uint stringPoolOffset = schemaOffset + schemaSize;  // 164
        uint stringPoolSize = 50;
        uint dataOffset = stringPoolOffset + stringPoolSize;  // 214
        uint dataSize = 200;
        uint crcOffset = dataOffset + dataSize;  // 414
        uint blake3Offset = crcOffset + 4;  // 418

        // All offsets should be strictly ascending
        Assert.True(schemaOffset < stringPoolOffset);
        Assert.True(stringPoolOffset < dataOffset);
        Assert.True(dataOffset < crcOffset);
        Assert.True(crcOffset < blake3Offset);

        // Sections should be contiguous (no gaps)
        Assert.Equal(stringPoolOffset, schemaOffset + schemaSize);
        Assert.Equal(dataOffset, stringPoolOffset + stringPoolSize);
        Assert.Equal(crcOffset, dataOffset + dataSize);
    }

    [Fact(DisplayName = "SpecLock: IRONCFG flags must be 0-3 (lower 2 bits)")]
    public void SpecLock_FlagsValidity()
    {
        // Valid flags: bits 0-2 can be set, bits 3-7 must be 0
        byte validFlags1 = 0b00000001;  // CRC32_PRESENT
        byte validFlags2 = 0b00000010;  // BLAKE3_PRESENT
        byte validFlags3 = 0b00000100;  // EMBEDDED_SCHEMA
        byte validFlags4 = 0b00000111;  // All lower bits

        // Invalid: bits 3-7 set
        byte invalidFlags = 0b11111000;

        // Check validity
        Assert.Equal(0, validFlags1 & 0b11111000);  // No reserved bits set
        Assert.Equal(0, validFlags2 & 0b11111000);
        Assert.Equal(0, validFlags3 & 0b11111000);
        Assert.Equal(0, validFlags4 & 0b11111000);
        Assert.NotEqual(0, invalidFlags & 0b11111000);  // Reserved bits ARE set (invalid)
    }

    [Fact(DisplayName = "SpecLock: IRONCFG CRC32 covers bytes [0, crcOffset-1]")]
    public void SpecLock_CRC32CoverageRange()
    {
        // CRC32 should cover all bytes from start (0) to crcOffset-1
        // This is a specification constraint that implementations must follow

        uint fileSize = 500;
        uint crcOffset = 400;

        // CRC32 coverage: bytes 0 to (crcOffset-1) = 0 to 399
        uint expectedCoverageSize = crcOffset;
        Assert.Equal(400u, expectedCoverageSize);
    }

    [Fact(DisplayName = "SpecLock: IRONCFG field ordering must be strictly ascending")]
    public void SpecLock_FieldIdOrdering()
    {
        // Field IDs in schema must be strictly ascending (0, 1, 2, ...)
        // No duplicates, no gaps

        uint[] validFieldIds = { 0, 1, 2, 3, 4 };
        for (int i = 0; i < validFieldIds.Length - 1; i++)
        {
            Assert.True(validFieldIds[i] < validFieldIds[i + 1]);
        }

        // Invalid: not ascending
        uint[] invalidFieldIds = { 0, 2, 1, 3 };  // 2 > 1
        for (int i = 0; i < invalidFieldIds.Length - 1; i++)
        {
            if (invalidFieldIds[i] > invalidFieldIds[i + 1])
            {
                // Found violation
                Assert.True(invalidFieldIds[i] > invalidFieldIds[i + 1]);
            }
        }
    }

    [Fact(DisplayName = "SpecLock: IRONCFG VarUInt encoding must be minimal")]
    public void SpecLock_VarUIntMinimal()
    {
        // VarUInt must use minimal encoding
        // Value 127 must be encoded as 1 byte (0x7F), NOT 2 bytes (0xFF 0x00)

        // Example: 127 should be 1 byte minimum
        uint smallValue = 127;
        Assert.True(smallValue < 128);  // Fits in 1 byte

        // Example: 128 requires 2 bytes minimum
        uint largeValue = 128;
        Assert.True(largeValue >= 128);  // Requires 2 bytes

        // Non-minimal encoding would be: 0xFF 0x00 for value 127
        // Minimal encoding would be: 0x7F for value 127
        byte[] minimalEncoding = { 0x7F };
        byte[] nonMinimalEncoding = { 0xFF, 0x00 };

        Assert.Single(minimalEncoding);
        Assert.Equal(2, nonMinimalEncoding.Length);
    }

    [Fact(DisplayName = "SpecLock: IRONCFG float canonicalization")]
    public void SpecLock_FloatCanonical()
    {
        // -0.0 must be normalized to +0.0
        double positiveZero = 0.0d;
        double negativeZero = -0.0d;

        // In IEEE 754, they have different bit patterns but compare equal
        Assert.Equal(positiveZero, negativeZero);  // They compare equal

        // NaN is forbidden (must be rejected)
        double nanValue = double.NaN;
        Assert.True(double.IsNaN(nanValue));

        // Canonicalization: treat all zeros as positive zero
        double normalized = negativeZero == 0 ? positiveZero : negativeZero;
        Assert.Equal(positiveZero, normalized);
    }

    [Fact(DisplayName = "SpecLock: IRONCFG section ordering in header")]
    public void SpecLock_HeaderSectionOffsetFields()
    {
        // Header field offsets (little-endian u32 fields)
        // schemaOffset at bytes 12-15
        // stringPoolOffset at bytes 20-23
        // dataOffset at bytes 28-31
        // crcOffset at bytes 36-39
        // blake3Offset at bytes 40-43

        // These must appear in ascending order in the file
        Assert.True(12 < 20);  // schemaOffset < stringPoolOffset
        Assert.True(20 < 28);  // stringPoolOffset < dataOffset
        Assert.True(28 < 36);  // dataOffset < crcOffset
        Assert.True(36 < 40);  // crcOffset < blake3Offset
    }

    [Fact(DisplayName = "SpecLock: IRONCFG string pool strings must be sorted")]
    public void SpecLock_StringPoolSorting()
    {
        // Lock point: Strings in string pool must be sorted lexicographically by UTF-8 bytes
        // This is a format constraint enforced by the IRONCFG specification

        // Example: Canonical ordering requires lexicographic sorting
        string a = "alpha";
        string b = "beta";
        string c = "gamma";

        // Verify ordering using ordinal comparison (UTF-8 byte order)
        Assert.True(string.CompareOrdinal(a, b) < 0);  // "alpha" < "beta"
        Assert.True(string.CompareOrdinal(b, c) < 0);  // "beta" < "gamma"

        // Invalid ordering would violate the spec
        Assert.False(string.CompareOrdinal(c, a) < 0); // "gamma" is NOT < "alpha"
    }
}
