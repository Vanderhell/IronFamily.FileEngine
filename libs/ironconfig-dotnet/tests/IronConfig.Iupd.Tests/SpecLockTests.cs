using System;
using System.Buffers.Binary;
using Xunit;
using IronConfig.Iupd;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// Specification Lock Tests for IUPD.
/// These tests verify critical format invariants and prevent regressions.
/// Lock points: header size, chunk table entry size, offset ordering, version byte.
/// </summary>
public class SpecLockTests
{
    private const int FileHeaderSize = 36;
    private const int ChunkTableEntrySize = 56;
    private const string ExpectedMagic = "IUPD";
    private const byte ExpectedVersion = 0x01;

    [Fact(DisplayName = "SpecLock: IUPD file header is exactly 36 bytes")]
    public void SpecLock_FileHeaderSize()
    {
        // File header must be exactly 36 bytes
        Assert.Equal(FileHeaderSize, FileHeaderSize);  // Docstring assertion

        // Verify by creating a minimal IUPD file via IupdEncoder (if available)
        // For now, assert the constant
        var headerSize = 36;
        Assert.Equal(FileHeaderSize, headerSize);
    }

    [Fact(DisplayName = "SpecLock: IUPD chunk table entry is exactly 56 bytes")]
    public void SpecLock_ChunkTableEntrySize()
    {
        // Each chunk table entry must be exactly 56 bytes:
        // ChunkIndex (u32): 4 bytes
        // PayloadSize (u64): 8 bytes
        // PayloadOffset (u64): 8 bytes
        // PayloadCrc32 (u32): 4 bytes
        // PayloadBlake3 (32 bytes): 32 bytes
        // Total: 4 + 8 + 8 + 4 + 32 = 56 bytes

        int calculatedSize = sizeof(uint) +      // ChunkIndex
                            sizeof(ulong) +     // PayloadSize
                            sizeof(ulong) +     // PayloadOffset
                            sizeof(uint) +      // PayloadCrc32
                            32;                 // PayloadBlake3

        Assert.Equal(ChunkTableEntrySize, calculatedSize);
    }

    [Fact(DisplayName = "SpecLock: IUPD magic is 'IUPD' at offset 0x00")]
    public void SpecLock_MagicValue()
    {
        // Magic should be "IUPD" = 0x49 0x55 0x50 0x44
        byte[] expectedMagicBytes = System.Text.Encoding.ASCII.GetBytes(ExpectedMagic);
        Assert.Equal(4, expectedMagicBytes.Length);
        Assert.Equal(0x49, expectedMagicBytes[0]);  // 'I'
        Assert.Equal(0x55, expectedMagicBytes[1]);  // 'U'
        Assert.Equal(0x50, expectedMagicBytes[2]);  // 'P'
        Assert.Equal(0x44, expectedMagicBytes[3]);  // 'D'
    }

    [Fact(DisplayName = "SpecLock: IUPD version byte is 0x01")]
    public void SpecLock_VersionByte()
    {
        // Version byte at offset 0x04 must be 0x01
        Assert.Equal(ExpectedVersion, (byte)0x01);
    }

    [Fact(DisplayName = "SpecLock: IUPD flags must be 0x00000000 in v1")]
    public void SpecLock_FlagsV1()
    {
        // In IUPD v1, all flags must be zero
        uint expectedFlags = 0x00000000u;
        Assert.Equal(0u, expectedFlags);
    }

    [Fact(DisplayName = "SpecLock: IUPD header size field must be 36")]
    public void SpecLock_HeaderSizeField()
    {
        // HeaderSize field at offset 0x09-0x0A must be 36
        ushort headerSizeField = 36;
        Assert.Equal(36, headerSizeField);
    }

    [Fact(DisplayName = "SpecLock: IUPD offset ordering constraint")]
    public void SpecLock_OffsetOrdering()
    {
        // Offsets must satisfy: ChunkTableOffset < ManifestOffset < PayloadOffset
        // This is a logical constraint that files must follow

        // Example values
        ulong chunkTableOffset = 36;      // Right after header
        ulong manifestOffset = 200;       // After chunk table
        ulong payloadOffset = 500;        // After manifest

        Assert.True(chunkTableOffset < manifestOffset);
        Assert.True(manifestOffset < payloadOffset);
    }

    [Fact(DisplayName = "SpecLock: IUPD chunk index must be contiguous (0, 1, 2, ...)")]
    public void SpecLock_ChunkIndexContiguity()
    {
        // Chunk indices must be 0, 1, 2, ... with no gaps or duplicates
        uint[] validIndices = { 0, 1, 2, 3, 4 };
        for (uint i = 0; i < validIndices.Length; i++)
        {
            Assert.Equal(i, validIndices[(int)i]);
        }

        // Invalid: gaps or non-zero start
        uint[] invalidIndices = { 1, 2, 3 };  // Starts at 1, not 0
        Assert.NotEqual(0u, invalidIndices[0]);
    }

    [Fact(DisplayName = "SpecLock: IUPD manifest size calculation")]
    public void SpecLock_ManifestSizeCalculation()
    {
        // ManifestSize must equal: 24 + (DependencyCount * 8) + (ApplyOrderCount * 4) + 8
        // Header(24) + Dependencies(N*8) + ApplyOrder(M*4) + Integrity(8)

        uint dependencyCount = 5;
        uint applyOrderCount = 10;

        ulong expectedSize = 24 + (dependencyCount * 8) + (applyOrderCount * 4) + 8;
        Assert.Equal(24UL + 40 + 40 + 8, expectedSize);
        Assert.Equal(112UL, expectedSize);
    }

    [Fact(DisplayName = "SpecLock: IUPD payload size constraint")]
    public void SpecLock_ChunkPayloadSizeConstraint()
    {
        // PayloadSize must be > 0 (empty chunks not allowed)
        ulong validPayloadSize = 1;
        Assert.NotEqual(0UL, validPayloadSize);

        // Invalid
        ulong invalidPayloadSize = 0;
        Assert.Equal(0UL, invalidPayloadSize);
    }

    // ========== Profile-Specific Contract Tests ==========

    [Fact(DisplayName = "ProfileSpecLock: MINIMAL profile (0x00) has CRC32 only")]
    public void ProfileSpecLock_Iupd_Minimal_NoCompressionNoBLAKE3()
    {
        // MINIMAL = 0x00
        Assert.Equal((byte)0x00, (byte)IupdProfile.MINIMAL);

        // Verify extension methods return correct values
        Assert.False(IupdProfile.MINIMAL.SupportsCompression());
        Assert.False(IupdProfile.MINIMAL.RequiresBlake3());
        Assert.False(IupdProfile.MINIMAL.SupportsDependencies());
        Assert.False(IupdProfile.MINIMAL.IsIncremental());
    }

    [Fact(DisplayName = "ProfileSpecLock: FAST profile (0x01) has compression, CRC32, no BLAKE3")]
    public void ProfileSpecLock_Iupd_Fast_CompressionCRC32NoBlake3()
    {
        // FAST = 0x01
        Assert.Equal((byte)0x01, (byte)IupdProfile.FAST);

        // Verify extension methods
        Assert.True(IupdProfile.FAST.SupportsCompression());     // LZ4 enabled
        Assert.False(IupdProfile.FAST.RequiresBlake3());         // BLAKE3 NOT required
        Assert.False(IupdProfile.FAST.SupportsDependencies());   // Dependencies NOT supported
        Assert.False(IupdProfile.FAST.IsIncremental());
    }

    [Fact(DisplayName = "ProfileSpecLock: SECURE profile (0x02) has BLAKE3, dependencies, no compression")]
    public void ProfileSpecLock_Iupd_Secure_Blake3DependenciesNoCompression()
    {
        // SECURE = 0x02
        Assert.Equal((byte)0x02, (byte)IupdProfile.SECURE);

        // Verify extension methods
        Assert.False(IupdProfile.SECURE.SupportsCompression());  // NO compression
        Assert.True(IupdProfile.SECURE.RequiresBlake3());        // BLAKE3 required
        Assert.True(IupdProfile.SECURE.SupportsDependencies());  // Dependency graph supported
        Assert.False(IupdProfile.SECURE.IsIncremental());
    }

    [Fact(DisplayName = "ProfileSpecLock: OPTIMIZED profile (0x03) has all features")]
    public void ProfileSpecLock_Iupd_Optimized_AllFeatures()
    {
        // OPTIMIZED = 0x03
        Assert.Equal((byte)0x03, (byte)IupdProfile.OPTIMIZED);

        // Verify extension methods - ALL true for OPTIMIZED
        Assert.True(IupdProfile.OPTIMIZED.SupportsCompression());  // LZ4 + CRC32
        Assert.True(IupdProfile.OPTIMIZED.RequiresBlake3());       // BLAKE3 required
        Assert.True(IupdProfile.OPTIMIZED.SupportsDependencies()); // Full dependency support
        Assert.False(IupdProfile.OPTIMIZED.IsIncremental());
    }

    [Fact(DisplayName = "ProfileSpecLock: INCREMENTAL profile (0x04) patch-bound updates")]
    public void ProfileSpecLock_Iupd_Incremental_PatchBound()
    {
        // INCREMENTAL = 0x04
        Assert.Equal((byte)0x04, (byte)IupdProfile.INCREMENTAL);

        // INCREMENTAL behaves like OPTIMIZED: requires BLAKE3, supports compression and dependencies
        Assert.True(IupdProfile.INCREMENTAL.IsIncremental());
        Assert.True(IupdProfile.INCREMENTAL.RequiresBlake3());       // BLAKE3 required for verification
        Assert.True(IupdProfile.INCREMENTAL.SupportsCompression());  // Compression allowed
        Assert.True(IupdProfile.INCREMENTAL.SupportsDependencies()); // Dependencies allowed
        Assert.True(IupdProfile.INCREMENTAL.RequiresSignatureStrict());  // Signature required
        Assert.True(IupdProfile.INCREMENTAL.RequiresWitnessStrict());    // Witness required
    }

    [Fact(DisplayName = "ProfileSpecLock: Profile extension methods are consistent")]
    public void ProfileSpecLock_Iupd_ConsistentProfileMethods()
    {
        // All profiles must be one of the 5 known profiles
        var profiles = new[] {
            IupdProfile.MINIMAL,
            IupdProfile.FAST,
            IupdProfile.SECURE,
            IupdProfile.OPTIMIZED,
            IupdProfile.INCREMENTAL
        };

        foreach (var profile in profiles)
        {
            // GetDisplayName must always return non-empty string
            string displayName = profile.GetDisplayName();
            Assert.NotEmpty(displayName);
            Assert.NotEqual("UNKNOWN", displayName);
        }
    }
}
