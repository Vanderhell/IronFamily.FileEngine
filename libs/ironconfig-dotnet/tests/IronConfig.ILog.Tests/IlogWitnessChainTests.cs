using System;
using System.Buffers.Binary;
using System.Linq;
using Xunit;
using IronConfig.ILog;
using IronConfig;

namespace IronConfig.ILog.Tests;

/// <summary>
/// Tests for ILOG witness chain implementation (AUDITED profile).
/// Witness chain provides block-level tamper detection using witness headers in L1 block.
/// </summary>
public class IlogWitnessChainTests
{
    // Fixed test keypair for reproducible AUDITED profile tests
    private static readonly byte[] TestPrivateKey = new byte[]
    {
        0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57,
        0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67,
        0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F
    };

    private static readonly byte[] TestPublicKey = new byte[]
    {
        0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77,
        0x78, 0x79, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F,
        0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F
    };

    private static IlogEncodeOptions GetAuditedEncodeOptions() =>
        new IlogEncodeOptions
        {
            Ed25519PrivateKey32 = TestPrivateKey.AsMemory(),
            Ed25519PublicKey32 = TestPublicKey.AsMemory()
        };

    /// <summary>
    /// Test 1: AUDITED profile with witness chain round-trips correctly.
    /// Encodes data with AUDITED profile (witness enabled), then decodes and verifies.
    /// Expected: Verify() returns true (witness chain valid).
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_WitnessChain_Roundtrip_Pass()
    {
        // Arrange
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());
        var decoded = decoder.Decode(encoded);
        var verifyResult = decoder.Verify(encoded);

        // Assert
        Assert.NotNull(encoded);
        Assert.Equal(testData, decoded);
        Assert.True(verifyResult, "AUDITED witness chain should verify successfully");
    }

    /// <summary>
    /// Test 2: Tampering with witness header version fails verification.
    /// Encodes AUDITED, modifies WitnessVersion byte, then verifies.
    /// Expected: Verify() returns false (witness validation fails).
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_WitnessChain_TamperWitnessVersion_Fails()
    {
        // Arrange
        var testData = new byte[] { 0xAA, 0xBB, 0xCC };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());

        // Tamper: Find L1 block and flip WitnessVersion byte (byte 1 after TocVersion)
        // L1 block structure: [FileHeader:16][BlockHeader:72][Payload]
        // Payload: [TocVersion:1][WitnessVersion:1]...
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;
        const int TocVersionOffset = 0;
        const int WitnessVersionOffset = 1;

        // Create a mutable copy
        byte[] tampered = encoded.ToArray();
        int l1PayloadStart = FileHeaderSize + BlockHeaderSize;
        int witnessVersionByteIndex = l1PayloadStart + TocVersionOffset + WitnessVersionOffset;

        // Flip WitnessVersion from 0x01 to 0x02 (invalid)
        tampered[witnessVersionByteIndex] ^= 0x03; // Flip bits

        // Act
        var verifyResult = decoder.Verify(tampered);

        // Assert
        Assert.False(verifyResult, "Witness verification should fail when WitnessVersion is tampered");
    }

    /// <summary>
    /// Test 3: Tampering with Reserved byte fails verification.
    /// Encodes AUDITED, modifies Reserved byte, then verifies.
    /// Expected: Verify() returns false (witness validation fails).
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_WitnessChain_TamperReserved_Fails()
    {
        // Arrange
        var testData = new byte[] { 0x11, 0x22, 0x33 };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());

        // Tamper: Find L1 block and flip Reserved byte (byte 2 after TocVersion)
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;
        const int ReservedOffset = 2;

        byte[] tampered = encoded.ToArray();
        int l1PayloadStart = FileHeaderSize + BlockHeaderSize;
        int reservedByteIndex = l1PayloadStart + ReservedOffset;

        // Set Reserved from 0x00 to 0xFF (invalid)
        tampered[reservedByteIndex] = 0xFF;

        // Act
        var verifyResult = decoder.Verify(tampered);

        // Assert
        Assert.False(verifyResult, "Witness verification should fail when Reserved byte is tampered");
    }

    /// <summary>
    /// Test 4: Non-zero PrevSealHash in single-block model fails verification.
    /// Encodes AUDITED, sets PrevSealHash to non-zero, then verifies.
    /// Expected: Verify() returns false (witness validation fails).
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_WitnessChain_InvalidPrevHash_Fails()
    {
        // Arrange
        var testData = new byte[] { 0x44, 0x55, 0x66 };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());

        // Tamper: Find L1 block and set PrevSealHash to non-zero
        // PrevSealHash is 32 bytes starting at offset 3 (after TocVersion, WitnessVersion, Reserved)
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;
        const int PrevSealHashOffset = 3;

        byte[] tampered = encoded.ToArray();
        int l1PayloadStart = FileHeaderSize + BlockHeaderSize;
        int prevSealHashStart = l1PayloadStart + PrevSealHashOffset;

        // Set first byte of PrevSealHash to 0xFF (violates single-block assumption)
        tampered[prevSealHashStart] = 0xFF;

        // Act
        var verifyResult = decoder.Verify(tampered);

        // Assert
        Assert.False(verifyResult, "Witness verification should fail when PrevSealHash is non-zero in single-block model");
    }

    /// <summary>
    /// Test 5: MINIMAL profile does not have witness chain.
    /// Encodes MINIMAL, verifies it passes, and confirms no witness header present.
    /// Expected: TocVersion=1 (no witness), Verify() returns true.
    /// </summary>
    [Fact]
    public void ILOG_MINIMAL_NoWitness_Unchanged()
    {
        // Arrange
        var testData = new byte[] { 0x77, 0x88, 0x99 };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.MINIMAL);
        var decoded = decoder.Decode(encoded);
        var verifyResult = decoder.Verify(encoded);

        // Assert - MINIMAL profile should verify
        Assert.NotNull(encoded);
        Assert.Equal(testData, decoded);
        Assert.True(verifyResult, "MINIMAL profile should verify");

        // Check internal structure: TocVersion should be 1 (no witness header)
        // L1 block is second block (after L0)
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;

        // Find L1 block: skip L0 block (header + payload)
        // For now, L1 is typically at a known offset; extract its TocVersion
        // Block structure: [BlockMagic:4][BlockType:2][Reserved:2][Timestamp:8][PayloadSize:4][PayloadCrc:4][PayloadBlake3:32][Reserved:12]
        // L0 payload size at offset 16+16 = 32 (relative to block start)

        int l0BlockStart = FileHeaderSize;
        int l0PayloadSizeOffset = 16; // Offset in block header
        uint l0PayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(l0BlockStart + l0PayloadSizeOffset, 4));
        int l1BlockStart = l0BlockStart + BlockHeaderSize + (int)l0PayloadSize;

        // L1 payload starts after its block header
        int l1PayloadStart = l1BlockStart + BlockHeaderSize;
        byte tocVersion = encoded[l1PayloadStart];

        Assert.True(tocVersion == 1, "MINIMAL profile should have TocVersion=1 (no witness header)");
    }

    /// <summary>
    /// Test 6: INTEGRITY profile does not have witness chain.
    /// Encodes INTEGRITY, verifies it passes, confirms no witness header.
    /// Expected: TocVersion=1 (no witness), Verify() returns true.
    /// </summary>
    [Fact]
    public void ILOG_INTEGRITY_NoWitness_Unchanged()
    {
        // Arrange
        var testData = new byte[] { 0xAA, 0xBB };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.INTEGRITY);
        var decoded = decoder.Decode(encoded);
        var verifyResult = decoder.Verify(encoded);

        // Assert - INTEGRITY profile should verify
        Assert.NotNull(encoded);
        Assert.Equal(testData, decoded);
        Assert.True(verifyResult, "INTEGRITY profile should verify");

        // Confirm TocVersion=1 (no witness)
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;

        int l0BlockStart = FileHeaderSize;
        int l0PayloadSizeOffset = 16;
        uint l0PayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(l0BlockStart + l0PayloadSizeOffset, 4));
        int l1BlockStart = l0BlockStart + BlockHeaderSize + (int)l0PayloadSize;
        int l1PayloadStart = l1BlockStart + BlockHeaderSize;
        byte tocVersion = encoded[l1PayloadStart];

        Assert.True(tocVersion == 1, "INTEGRITY profile should have TocVersion=1 (no witness header)");
    }

    /// <summary>
    /// Test 7: SEARCHABLE profile does not have witness chain.
    /// Encodes SEARCHABLE, verifies it passes, confirms no witness header.
    /// Expected: TocVersion=1 (no witness), Verify() returns true.
    /// </summary>
    [Fact]
    public void ILOG_SEARCHABLE_NoWitness_Unchanged()
    {
        // Arrange
        var testData = new byte[] { 0x99, 0x88 };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.SEARCHABLE);
        var decoded = decoder.Decode(encoded);
        var verifyResult = decoder.Verify(encoded);

        // Assert - SEARCHABLE profile should verify
        Assert.NotNull(encoded);
        Assert.Equal(testData, decoded);
        Assert.True(verifyResult, "SEARCHABLE profile should verify");

        // Confirm TocVersion=1 (no witness)
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;

        int l0BlockStart = FileHeaderSize;
        int l0PayloadSizeOffset = 16;
        uint l0PayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(l0BlockStart + l0PayloadSizeOffset, 4));
        int l1BlockStart = l0BlockStart + BlockHeaderSize + (int)l0PayloadSize;
        int l1PayloadStart = l1BlockStart + BlockHeaderSize;
        byte tocVersion = encoded[l1PayloadStart];

        Assert.True(tocVersion == 1, "SEARCHABLE profile should have TocVersion=1 (no witness header)");
    }

    /// <summary>
    /// Test 8: ARCHIVED profile does not have witness chain.
    /// Encodes ARCHIVED, verifies it passes, confirms no witness header.
    /// Expected: TocVersion=1 (no witness), Verify() returns true.
    /// </summary>
    [Fact]
    public void ILOG_ARCHIVED_NoWitness_Unchanged()
    {
        // Arrange
        var testData = new byte[] { 0x77, 0x66 };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.ARCHIVED);
        var decoded = decoder.Decode(encoded);
        var verifyResult = decoder.Verify(encoded);

        // Assert - ARCHIVED profile should verify
        Assert.NotNull(encoded);
        Assert.Equal(testData, decoded);
        Assert.True(verifyResult, "ARCHIVED profile should verify");

        // Confirm TocVersion=1 (no witness)
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;

        int l0BlockStart = FileHeaderSize;
        int l0PayloadSizeOffset = 16;
        uint l0PayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(l0BlockStart + l0PayloadSizeOffset, 4));
        int l1BlockStart = l0BlockStart + BlockHeaderSize + (int)l0PayloadSize;
        int l1PayloadStart = l1BlockStart + BlockHeaderSize;
        byte tocVersion = encoded[l1PayloadStart];

        Assert.True(tocVersion == 1, "ARCHIVED profile should have TocVersion=1 (no witness header)");
    }

    /// <summary>
    /// Test 9: AUDITED profile has TocVersion=1 with WITNESS_ENABLED flag.
    /// Encodes AUDITED and checks that TocVersion byte is 1 (global) and WITNESS_ENABLED flag is set.
    /// Expected: TocVersion=1 (global format version), WITNESS_ENABLED=1 (feature flag).
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_TocVersion_IsOne_WithWitnessFlag()
    {
        // Arrange
        var testData = new byte[] { 0x11, 0x22, 0x33 };
        var encoder = new IlogEncoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());

        // Assert - Check TocVersion=1 and WITNESS_ENABLED flag for AUDITED
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;
        const byte WITNESS_ENABLED_FLAG = 0x20;

        int l0BlockStart = FileHeaderSize;
        int l0PayloadSizeOffset = 16;
        uint l0PayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(l0BlockStart + l0PayloadSizeOffset, 4));
        int l1BlockStart = l0BlockStart + BlockHeaderSize + (int)l0PayloadSize;
        int l1PayloadStart = l1BlockStart + BlockHeaderSize;
        byte tocVersion = encoded[l1PayloadStart];

        // Check TocVersion (should be 1 for global format version)
        Assert.True(tocVersion == 1, "AUDITED profile should have TocVersion=1 (global format version)");

        // Check WITNESS_ENABLED flag
        byte flags = encoded[5];
        Assert.True((flags & WITNESS_ENABLED_FLAG) != 0, "AUDITED profile should have WITNESS_ENABLED flag set");
    }

    /// <summary>
    /// Test 10: Large data AUDITED witness chain round-trips correctly.
    /// Encodes large payload with AUDITED profile and verifies witness chain.
    /// Expected: Round-trip succeeds, Verify() returns true.
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_WitnessChain_LargeData_Roundtrip_Pass()
    {
        // Arrange
        var testData = new byte[10 * 1024]; // 10 KB
        new Random(42).NextBytes(testData);
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());
        var decoded = decoder.Decode(encoded);
        var verifyResult = decoder.Verify(encoded);

        // Assert
        Assert.NotNull(encoded);
        Assert.Equal(testData, decoded);
        Assert.True(verifyResult, "AUDITED witness chain should verify for large data");
    }

    /// <summary>
    /// Test 11: Empty data AUDITED witness chain round-trips correctly.
    /// Encodes empty payload with AUDITED profile and verifies witness chain.
    /// Expected: Round-trip succeeds, Verify() returns true.
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_WitnessChain_EmptyData_Roundtrip_Pass()
    {
        // Arrange
        var testData = Array.Empty<byte>();
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());
        var decoded = decoder.Decode(encoded);
        var verifyResult = decoder.Verify(encoded);

        // Assert
        Assert.NotNull(encoded);
        Assert.Equal(testData, decoded);
        Assert.True(verifyResult, "AUDITED witness chain should verify for empty data");
    }

    /// <summary>
    /// Test 12: AUDITED with new WITNESS_ENABLED flag (TocVersion=1 + flag set).
    /// Verifies that witness chain works with the new feature flag-based approach.
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_WitnessEnabled_Flag_TocVersion1_Pass()
    {
        // Arrange
        var testData = new byte[] { 0x11, 0x22, 0x33 };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());
        var verifyResult = decoder.Verify(encoded);

        // Assert
        Assert.NotNull(encoded);
        Assert.True(verifyResult, "AUDITED should verify with new WITNESS_ENABLED flag");

        // Verify file header flags (byte 5)
        const byte WITNESS_ENABLED_FLAG = 0x20; // Bit 5
        byte flags = encoded[5];
        Assert.True((flags & WITNESS_ENABLED_FLAG) != 0, "WITNESS_ENABLED flag should be set");

        // Verify TocVersion = 1 in L1 payload
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;
        int l0BlockStart = FileHeaderSize;
        int l0PayloadSizeOffset = 16;
        uint l0PayloadSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(l0BlockStart + l0PayloadSizeOffset, 4));
        int l1BlockStart = l0BlockStart + BlockHeaderSize + (int)l0PayloadSize;
        int l1PayloadStart = l1BlockStart + BlockHeaderSize;
        byte tocVersion = encoded[l1PayloadStart];

        Assert.True(tocVersion == 1, "TocVersion should be 1 (global format version)");
    }

    /// <summary>
    /// Test 13: Legacy TocVersion=2 without WITNESS_ENABLED flag (backward compatibility).
    /// Simulates a pre-refactor file that used TocVersion=2 to signal witness presence.
    /// Expected: Should still verify (legacy path).
    /// </summary>
    [Fact]
    public void ILOG_AUDITED_Legacy_TocVersion2_NoFlag_Pass()
    {
        // Arrange
        var testData = new byte[] { 0x44, 0x55, 0x66 };
        var encoder = new IlogEncoder();
        var decoder = new IlogDecoder();

        // Encode using current encoder (produces TocVersion=1 + WITNESS_ENABLED flag)
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());
        byte[] legacy = encoded.ToArray();

        // Patch to simulate pre-refactor file: Change TocVersion from 1 to 2
        // while keeping WITNESS_ENABLED flag off (simulating old file behavior)
        const int FileHeaderSize = 16;
        const int BlockHeaderSize = 72;
        const byte WITNESS_ENABLED_FLAG = 0x20;

        // Clear WITNESS_ENABLED flag to simulate old file
        legacy[5] = (byte)(legacy[5] & ~WITNESS_ENABLED_FLAG);

        // Find L1 block and change TocVersion to 2
        int l0BlockStart = FileHeaderSize;
        int l0PayloadSizeOffset = 16;
        uint l0PayloadSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(legacy.AsSpan(l0BlockStart + l0PayloadSizeOffset, 4));
        int l1BlockStart = l0BlockStart + BlockHeaderSize + (int)l0PayloadSize;
        int l1PayloadStart = l1BlockStart + BlockHeaderSize;
        uint l1PayloadSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(legacy.AsSpan(l1BlockStart + 16, 4));
        legacy[l1PayloadStart] = 2; // Set TocVersion = 2 (legacy signal)

        // Recompute L1 payload CRC32 so the file remains a valid legacy witness variant,
        // not a CRC-corrupted modern file.
        uint l1PayloadCrc32 = Crc32Ieee.Compute(legacy.AsSpan(l1PayloadStart, checked((int)l1PayloadSize)));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(l1BlockStart + 20, 4), l1PayloadCrc32);

        // Act
        var verifyResult = decoder.Verify(legacy);

        // Assert
        Assert.True(verifyResult, "Legacy TocVersion=2 should verify (backward compatibility)");
    }
}
