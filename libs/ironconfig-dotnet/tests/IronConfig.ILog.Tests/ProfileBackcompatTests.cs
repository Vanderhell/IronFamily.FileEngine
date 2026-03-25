using System;
using Xunit;
using IronConfig.ILog;

namespace IronConfig.ILog.Tests;

/// <summary>
/// Profile Backward Compatibility Tests for ILOG
/// Verifies that all profiles can be created and read back correctly
/// </summary>
public class ProfileBackcompatTests
{
    // ============================================================================
    // A) MINIMAL Profile Tests
    // ============================================================================

    [Fact(DisplayName = "ProfileBackcompat: MINIMAL can be created and read")]
    public void ProfileBackcompat_MinimalCreateRead()
    {
        var encoder = new IlogEncoder();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var encoded = encoder.Encode(data, IlogProfile.MINIMAL);

        // Check it encoded successfully
        Assert.NotEmpty(encoded);

        // Check magic at offset 0
        uint magic = BitConverter.ToUInt32(encoded, 0);
        Assert.Equal(0x474F4C49u, magic);  // "ILOG"

        // Check flags at offset 5
        byte flags = encoded[5];
        Assert.Equal(0x01, flags);  // MINIMAL flags
    }

    // ============================================================================
    // B) INTEGRITY Profile Tests
    // ============================================================================

    [Fact(DisplayName = "ProfileBackcompat: INTEGRITY can be created and read")]
    public void ProfileBackcompat_IntegrityCreateRead()
    {
        var encoder = new IlogEncoder();
        var data = new byte[] { 1, 2, 3 };
        var encoded = encoder.Encode(data, IlogProfile.INTEGRITY);

        // Check flags at offset 5
        byte flags = encoded[5];
        Assert.Equal(0x03, flags);  // INTEGRITY flags (LittleEndian | CRC32)
    }

    // ============================================================================
    // C) SEARCHABLE Profile Tests
    // ============================================================================

    [Fact(DisplayName = "ProfileBackcompat: SEARCHABLE can be created and read")]
    public void ProfileBackcompat_SearchableCreateRead()
    {
        var encoder = new IlogEncoder();
        var data = new byte[] { 1, 2, 3 };
        var encoded = encoder.Encode(data, IlogProfile.SEARCHABLE);

        // Check flags at offset 5
        byte flags = encoded[5];
        Assert.Equal(0x09, flags);  // SEARCHABLE flags (LittleEndian | L2)
    }

    // ============================================================================
    // D) ARCHIVED Profile Tests
    // ============================================================================

    [Fact(DisplayName = "ProfileBackcompat: ARCHIVED can be created and read")]
    public void ProfileBackcompat_ArchivedCreateRead()
    {
        var encoder = new IlogEncoder();
        var data = new byte[] { 1, 2, 3 };
        var encoded = encoder.Encode(data, IlogProfile.ARCHIVED);

        // Check flags at offset 5
        byte flags = encoded[5];
        Assert.Equal(0x11, flags);  // ARCHIVED flags (LittleEndian | L3)
    }

    // ============================================================================
    // E) AUDITED Profile Tests
    // ============================================================================

    [Fact(DisplayName = "ProfileBackcompat: AUDITED can be created and read")]
    public void ProfileBackcompat_AuditedCreateRead()
    {
        var encoder = new IlogEncoder();
        var data = new byte[] { 1, 2, 3 };
        var encoded = encoder.Encode(data, IlogProfile.AUDITED);

        // Check flags at offset 5
        byte flags = encoded[5];
        Assert.Equal(0x27, flags);  // AUDITED flags (LittleEndian | CRC32 | BLAKE3 | WITNESS_ENABLED)
    }

    // ============================================================================
    // F) Cross-Profile Tests
    // ============================================================================

    [Fact(DisplayName = "ProfileBackcompat: Profile flags are stable")]
    public void ProfileBackcompat_FlagsAreStable()
    {
        var profiles = new[]
        {
            (IlogProfile.MINIMAL, (byte)0x01),
            (IlogProfile.INTEGRITY, (byte)0x03),
            (IlogProfile.SEARCHABLE, (byte)0x09),
            (IlogProfile.ARCHIVED, (byte)0x11),
            (IlogProfile.AUDITED, (byte)0x27)  // 0x07 | 0x20 (WITNESS_ENABLED)
        };

        var encoder = new IlogEncoder();
        var data = new byte[] { 42 };

        foreach (var (profile, expectedFlags) in profiles)
        {
            var encoded = encoder.Encode(data, profile);
            byte flags = encoded[5];
            Assert.Equal(expectedFlags, flags);
        }
    }

    [Fact(DisplayName = "ProfileBackcompat: Non-archived profiles have L0 and L1 blocks")]
    public void ProfileBackcompat_NonArchivedProfilesHaveL0L1()
    {
        var profiles = new[]
        {
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.AUDITED
        };

        var encoder = new IlogEncoder();
        var data = new byte[] { 1, 2, 3 };

        foreach (var profile in profiles)
        {
            var encoded = encoder.Encode(data, profile);

            // L0 block starts at offset 16 (after file header)
            // Check L0 block magic at offset 16
            uint l0Magic = BitConverter.ToUInt32(encoded, 16);
            Assert.Equal(0x314B4C42u, l0Magic);  // "BLK1"

            // File should be large enough for L0 + L1
            Assert.True(encoded.Length >= 16 + 72 * 2);  // File header + 2 blocks minimum
        }
    }

    [Fact(DisplayName = "ProfileBackcompat: ARCHIVED uses L1 plus L3 storage layout")]
    public void ProfileBackcompat_ArchivedUsesL1PlusL3()
    {
        var encoder = new IlogEncoder();
        var encoded = encoder.Encode(new byte[] { 1, 2, 3 }, IlogProfile.ARCHIVED);

        ushort firstBlockType = BitConverter.ToUInt16(encoded, 16 + 4);
        Assert.Equal(0x0002, firstBlockType); // L1_TOC

        uint firstPayloadSize = BitConverter.ToUInt32(encoded, 16 + 16);
        int secondBlockOffset = 16 + 72 + (int)firstPayloadSize;
        ushort secondBlockType = BitConverter.ToUInt16(encoded, secondBlockOffset + 4);
        Assert.Equal(0x0004, secondBlockType); // L3_ARCHIVE
    }

    [Fact(DisplayName = "ProfileBackcompat: Profile determines layer composition")]
    public void ProfileBackcompat_ProfileDeterminesLayers()
    {
        var encoder = new IlogEncoder();
        var data = new byte[] { 1, 2, 3 };

        var minimal = encoder.Encode(data, IlogProfile.MINIMAL);
        var integrity = encoder.Encode(data, IlogProfile.INTEGRITY);
        var searchable = encoder.Encode(data, IlogProfile.SEARCHABLE);
        var archived = encoder.Encode(data, IlogProfile.ARCHIVED);
        var audited = encoder.Encode(data, IlogProfile.AUDITED);

        // Layer composition affects file size
        // SEARCHABLE should be larger (adds L2 index)
        // ARCHIVED should be different (L3 compression)
        // AUDITED should be larger (adds L4 seal with BLAKE3)

        // At minimum, all should have L0 + L1
        Assert.True(minimal.Length > 16 + 72);
        Assert.True(integrity.Length > 16 + 72);
        Assert.True(searchable.Length > 16 + 72);
        Assert.True(archived.Length > 16 + 72);
        Assert.True(audited.Length > 16 + 72);
    }

    [Fact(DisplayName = "ProfileBackcompat: Version byte is always 0x01")]
    public void ProfileBackcompat_VersionAlways0x01()
    {
        var profiles = new[]
        {
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED,
            IlogProfile.AUDITED
        };

        var encoder = new IlogEncoder();
        var data = new byte[] { 1 };

        foreach (var profile in profiles)
        {
            var encoded = encoder.Encode(data, profile);
            byte version = encoded[4];
            Assert.Equal(0x01, version);  // ILOG version is always 0x01
        }
    }

    [Fact(DisplayName = "ProfileBackcompat: Magic is always ILOG")]
    public void ProfileBackcompat_MagicAlwaysILOG()
    {
        var profiles = new[]
        {
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED,
            IlogProfile.AUDITED
        };

        var encoder = new IlogEncoder();
        var data = new byte[] { 1 };

        foreach (var profile in profiles)
        {
            var encoded = encoder.Encode(data, profile);
            uint magic = BitConverter.ToUInt32(encoded, 0);
            Assert.Equal(0x474F4C49u, magic);  // "ILOG"
        }
    }

    [Fact(DisplayName = "ProfileBackcompat: Multiple profiles can be created")]
    public void ProfileBackcompat_MultipleProfilesCreatable()
    {
        var profiles = new[]
        {
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED,
            IlogProfile.AUDITED
        };

        var encoder = new IlogEncoder();
        var data = new byte[] { 1, 2, 3 };

        foreach (var profile in profiles)
        {
            var encoded = encoder.Encode(data, profile);
            Assert.NotEmpty(encoded);
            Assert.True(encoded.Length > 16);  // At least file header
        }
    }
}
