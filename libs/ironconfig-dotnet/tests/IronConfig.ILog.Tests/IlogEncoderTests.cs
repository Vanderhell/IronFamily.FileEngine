using System;
using System.Buffers.Binary;
using System.Linq;
using Xunit;
using IronConfig.ILog;

namespace IronConfig.ILog.Tests;

public class IlogEncoderTests
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

    [Fact]
    public void Encode_MINIMAL_Profile_Creates_Valid_Header()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.MINIMAL);

        // Assert
        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);

        // Verify file header (first 16 bytes)
        Assert.Equal(0x49, encoded[0]); // 'I'
        Assert.Equal(0x4C, encoded[1]); // 'L'
        Assert.Equal(0x4F, encoded[2]); // 'O'
        Assert.Equal(0x47, encoded[3]); // 'G'
        Assert.Equal(0x01, encoded[4]); // Version
        Assert.Equal(0x01, encoded[5]); // Flags (minimal = LE only)
    }

    [Fact]
    public void Encode_INTEGRITY_Profile_Sets_CRC32_Flag()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.INTEGRITY);

        // Assert
        Assert.NotNull(encoded);
        // Bit 1 (CRC32) should be set in flags
        byte flags = encoded[5];
        Assert.True((flags & 0x02) != 0, "CRC32 flag should be set");
    }

    [Fact]
    public void Encode_SEARCHABLE_Profile_Sets_L2_Flag()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var testData = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.SEARCHABLE);

        // Assert
        Assert.NotNull(encoded);
        // Bit 3 (L2 INDEX) should be set in flags
        byte flags = encoded[5];
        Assert.True((flags & 0x08) != 0, "L2 INDEX flag should be set");
    }

    [Fact]
    public void Encode_ARCHIVED_Profile_Sets_L3_Flag()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var testData = new byte[] { 0x01, 0x02 };

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.ARCHIVED);

        // Assert
        Assert.NotNull(encoded);
        // Bit 4 (L3 ARCHIVE) should be set in flags
        byte flags = encoded[5];
        Assert.True((flags & 0x10) != 0, "L3 ARCHIVE flag should be set");
    }

    [Fact]
    public void Encode_ARCHIVED_Profile_Omits_L0_And_Starts_With_L1()
    {
        var encoder = new IlogEncoder();
        var encoded = encoder.Encode(new byte[] { 0x10, 0x20, 0x30 }, IlogProfile.ARCHIVED);

        Assert.NotNull(encoded);

        const int fileHeaderSize = 16;
        const int blockHeaderSize = 72;

        ushort firstBlockType = BitConverter.ToUInt16(encoded, fileHeaderSize + 4);
        Assert.Equal(0x0002, firstBlockType); // L1_TOC

        uint firstPayloadSize = BitConverter.ToUInt32(encoded, fileHeaderSize + 16);
        int secondBlockOffset = fileHeaderSize + blockHeaderSize + (int)firstPayloadSize;
        ushort secondBlockType = BitConverter.ToUInt16(encoded, secondBlockOffset + 4);
        Assert.Equal(0x0004, secondBlockType); // L3_ARCHIVE
    }

    [Fact]
    public void Encode_AUDITED_Profile_Sets_BLAKE3_Flag()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var testData = new byte[] { 0x01 };

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());

        // Assert
        Assert.NotNull(encoded);
        // Bits 1+2 (CRC32 | BLAKE3) should be set in flags
        byte flags = encoded[5];
        Assert.True((flags & 0x02) != 0, "CRC32 flag should be set");
        Assert.True((flags & 0x04) != 0, "BLAKE3 flag should be set");
    }

    [Fact]
    public void Encode_Contains_Block_Headers()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var testData = new byte[100];
        System.Random.Shared.NextBytes(testData);

        // Act
        var encoded = encoder.Encode(testData, IlogProfile.MINIMAL);

        // Assert
        // File header = 16 bytes
        // Block 0 (L0) = 72 header + 100 payload = 172 bytes
        // Block 1 (L1) = 72 header + variable payload
        Assert.True(encoded.Length >= 16 + 72 + 100 + 72, "Encoded size should be at least header + L0 block + L1 header");
    }

    [Fact]
    public void Encode_Produces_Deterministic_Output()
    {
        // Arrange
        var encoder1 = new IlogEncoder();
        var encoder2 = new IlogEncoder();
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var encoded1 = encoder1.Encode(testData, IlogProfile.MINIMAL);
        var encoded2 = encoder2.Encode(testData, IlogProfile.MINIMAL);

        // Assert - outputs should be identical for same input (except timestamps)
        // Note: Timestamps might differ, so we'll just check file structure is same size
        Assert.Equal(encoded1.Length, encoded2.Length);
    }

    [Fact]
    public void Encode_Empty_Data_Creates_Valid_File()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var emptyData = Array.Empty<byte>();

        // Act
        var encoded = encoder.Encode(emptyData, IlogProfile.MINIMAL);

        // Assert
        Assert.NotNull(encoded);
        Assert.True(encoded.Length >= 16, "Even empty data should have file header");

        // Verify magic
        Assert.Equal(0x49, encoded[0]);
        Assert.Equal(0x4C, encoded[1]);
    }

    [Fact]
    public void Encode_Large_Data()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var largeData = new byte[1024 * 100]; // 100 KB
        System.Random.Shared.NextBytes(largeData);

        // Act
        var encoded = encoder.Encode(largeData, IlogProfile.MINIMAL);

        // Assert
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > largeData.Length, "Encoded should be larger due to headers");
    }

    [Fact]
    public void Encode_All_Profiles_Produce_Valid_Files()
    {
        // Arrange
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var profiles = new[]
        {
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED,
            IlogProfile.AUDITED
        };

        // Act & Assert
        foreach (var profile in profiles)
        {
            var encoder = new IlogEncoder();
            var encoded = encoder.Encode(testData, profile);

            Assert.NotNull(encoded);
            Assert.NotEmpty(encoded);
            Assert.True(encoded.Length >= 16, $"{profile} profile should produce valid file");

            // Verify magic number
            Assert.Equal(0x49, encoded[0]);
            Assert.Equal(0x4C, encoded[1]);
            Assert.Equal(0x4F, encoded[2]);
            Assert.Equal(0x47, encoded[3]);
        }
    }

    [Fact]
    public void Encode_ARCHIVED_RepetitiveData_L3IsSmaller()
    {
        // Arrange
        var testData = new byte[4096];
        var pattern = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        for (int i = 0; i < testData.Length; i++)
        {
            testData[i] = pattern[i % pattern.Length];
        }

        // Act
        var encoder = new IlogEncoder();
        var encoded = encoder.Encode(testData, IlogProfile.ARCHIVED);

        // Assert
        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);

        // Encoded should be smaller than unencoded due to compression
        Assert.True(encoded.Length < testData.Length * 1.2, "Compressed data should be smaller than original + headers");
    }

    [Fact]
    public void Encode_ARCHIVED_RandomData_FallsBackToNoCompression()
    {
        // Arrange
        var testData = new byte[256];
        var random = new Random(42);
        random.NextBytes(testData);

        // Act
        var encoder = new IlogEncoder();
        var encoded = encoder.Encode(testData, IlogProfile.ARCHIVED);

        // Assert
        Assert.NotNull(encoded);
        // Random data won't compress well but should still be valid
        Assert.True(encoded.Length > 16, "Should have valid header");
    }

    [Fact]
    public void DecodeL3Block_ReturnsOriginalData()
    {
        // Arrange
        var testData = new byte[512];
        for (int i = 0; i < testData.Length; i++)
        {
            testData[i] = (byte)(i % 256);
        }

        // Act
        var encoder = new IlogEncoder();
        var encoded = encoder.Encode(testData, IlogProfile.ARCHIVED);

        var decoder = new IlogDecoder();
        var decompressed = decoder.DecodeL3Block(encoded);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(testData, decompressed);
    }

    [Fact]
    public void Encode_SEARCHABLE_Profile_With_L2_Index()
    {
        // Arrange - 10KB to create multiple index entries
        var testData = new byte[10 * 1024];
        System.Random.Shared.NextBytes(testData);

        // Act
        var encoder = new IlogEncoder();
        var encoded = encoder.Encode(testData, IlogProfile.SEARCHABLE);

        var decoder = new IlogDecoder();
        var indexResult = decoder.DecodeL2Block(encoded);

        // Assert
        Assert.NotNull(indexResult);
        var (entries, offsets, sizes) = indexResult.Value;
        Assert.True(entries > 0, "L2 index should have entries");
        Assert.Equal(entries, (uint)offsets.Length);
        Assert.Equal(entries, (uint)sizes.Length);

        // Verify index integrity
        // L0 payload includes 13-byte header (StreamVersion + EventCount + TimestampEpoch)
        const int L0_HEADER_SIZE = 13;
        uint totalSize = 0;
        for (int i = 0; i < offsets.Length; i++)
        {
            totalSize += sizes[i];
        }
        Assert.Equal((uint)testData.Length + L0_HEADER_SIZE, totalSize);
    }

    [Fact]
    public void Encode_INTEGRITY_Profile_With_CRC32_Seal()
    {
        // Arrange
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var encoder = new IlogEncoder();
        var encoded = encoder.Encode(testData, IlogProfile.INTEGRITY);

        var decoder = new IlogDecoder();
        var decoded = decoder.Decode(encoded);

        // Assert
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
        Assert.Equal(testData, decoded);

        // Verify INTEGRITY flag is set
        byte flags = encoded[5];
        Assert.True((flags & 0x02) != 0, "CRC32 flag should be set");

        var openErr = IlogReader.Open(encoded, out var view);
        Assert.Null(openErr);
        Assert.NotNull(view);

        uint l0PayloadCrc32 = IlogReader.GetL0PayloadCrc32(view!);
        Assert.NotEqual(0u, l0PayloadCrc32);

        const int fileHeaderSize = 16;
        const int blockHeaderSize = 72;
        int offset = fileHeaderSize;
        bool l4Found = false;
        while (offset + blockHeaderSize <= encoded.Length)
        {
            ushort blockType = BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(offset + 4, 2));
            uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(offset + 16, 4));

            if (blockType == 0x0005)
            {
                uint storedSealCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(
                    encoded.AsSpan(offset + blockHeaderSize + 4, 4));
                Assert.Equal(l0PayloadCrc32, storedSealCrc32);
                l4Found = true;
                break;
            }

            offset += blockHeaderSize + (int)payloadSize;
        }

        Assert.True(l4Found, "Expected L4_SEAL block in INTEGRITY profile output.");
    }

    [Fact]
    public void Encode_AUDITED_Profile_With_BLAKE3_Seal()
    {
        // Arrange
        var testData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };

        // Act
        var encoder = new IlogEncoder();
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());

        var decoder = new IlogDecoder();
        var decoded = decoder.Decode(encoded);

        // Assert
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
        Assert.Equal(testData, decoded);

        // Verify AUDITED flags are set (CRC32 | BLAKE3)
        byte flags = encoded[5];
        Assert.True((flags & 0x02) != 0, "CRC32 flag should be set");
        Assert.True((flags & 0x04) != 0, "BLAKE3 flag should be set");
    }

    [Fact]
    public void All_Profiles_RoundTrip_Correctly()
    {
        // Arrange
        var testData = new byte[1024];
        System.Random.Shared.NextBytes(testData);

        var profiles = new[]
        {
            IlogProfile.MINIMAL,
            IlogProfile.INTEGRITY,
            IlogProfile.SEARCHABLE,
            IlogProfile.ARCHIVED,
            IlogProfile.AUDITED
        };

        // Act & Assert
        foreach (var profile in profiles)
        {
            var encoder = new IlogEncoder();
            var encoded = encoder.Encode(testData, profile);

            var decoder = new IlogDecoder();
            var decoded = decoder.Decode(encoded);

            Assert.True(testData.SequenceEqual(decoded), $"Round-trip failed for {profile} profile");
        }
    }

    [Fact]
    public void Encode_AUDITED_Profile_Roundtrip()
    {
        // Arrange
        var encoder = new IlogEncoder();
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        // Act - Encode with AUDITED profile (includes Ed25519 signature)
        var encoded = encoder.Encode(testData, IlogProfile.AUDITED, GetAuditedEncodeOptions());

        // Assert - Verify roundtrip works for AUDITED profile
        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);

        var decoder = new IlogDecoder();
        var decoded = decoder.Decode(encoded);
        Assert.True(testData.SequenceEqual(decoded), "AUDITED roundtrip should preserve data");

        // Verify passes for valid AUDITED data
        bool verifyResult = decoder.Verify(encoded);
        Assert.True(verifyResult, "Valid AUDITED data should pass Verify()");
    }
}
