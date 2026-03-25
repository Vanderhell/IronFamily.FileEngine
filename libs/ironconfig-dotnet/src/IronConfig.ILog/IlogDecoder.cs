using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronConfig.Crypto;

namespace IronConfig.ILog;

/// <summary>
/// ILOG binary decoder (reader).
/// Supports all 5 profiles: MINIMAL, INTEGRITY, SEARCHABLE, ARCHIVED, AUDITED.
/// </summary>
public class IlogDecoder
{
    private const uint FileHeaderMagic = 0x474F4C49;  // "ILOG"
    private const uint BlockHeaderMagic = 0x314B4C42; // "BLK1"
    private const int FileHeaderSize = 16;
    private const int BlockHeaderSize = 72;

    // Block type registry
    private const ushort BlockType_L0_DATA = 0x0001;
    private const ushort BlockType_L1_TOC = 0x0002;
    private const ushort BlockType_L2_INDEX = 0x0003;
    private const ushort BlockType_L3_ARCHIVE = 0x0004;
    private const ushort BlockType_L4_SEAL = 0x0005;

    public class IlogFile
    {
        public byte[] Magic { get; set; } = new byte[4];
        public byte Version { get; set; }
        public byte Flags { get; set; }
        public ulong TocBlockOffset { get; set; }
        public List<BlockEntry> Blocks { get; set; } = new();
        public byte[] RawData { get; set; } = Array.Empty<byte>();
    }

    public class BlockEntry
    {
        public uint BlockMagic { get; set; }
        public ushort BlockType { get; set; }
        public ulong BlockTimestamp { get; set; }
        public byte[] PayloadBytes { get; set; } = Array.Empty<byte>();
        public uint PayloadCrc32 { get; set; }
        public byte[] PayloadBlake3 { get; set; } = new byte[32];
    }

    /// <summary>
    /// Decode an ILOG file and extract the original data.
    /// </summary>
    public byte[] Decode(byte[] ilogData)
    {
        if (ilogData == null || ilogData.Length < FileHeaderSize)
            throw new ArgumentException("Invalid ILOG data: too short");

        var file = ReadFileHeader(ilogData);
        ReadBlocks(ilogData, file);

        // Extract raw data from L0 block when present.
        var l0Block = file.Blocks.FirstOrDefault(b => b.BlockType == BlockType_L0_DATA);
        if (l0Block != null)
        {
            var payloadBytes = l0Block.PayloadBytes ?? Array.Empty<byte>();

            // Skip L0 header if present: StreamVersion (u8) + EventCount (u32) + TimestampEpoch (u64) = 13 bytes
            const int L0_HEADER_SIZE = 13;
            if (payloadBytes.Length >= L0_HEADER_SIZE)
            {
                // Skip the 13-byte L0 header and return only the actual event data
                return payloadBytes[L0_HEADER_SIZE..];
            }

            return payloadBytes;
        }

        // ARCHIVED is storage-first and may carry only L1 + L3.
        var archivedPayload = file.Blocks.FirstOrDefault(b => b.BlockType == BlockType_L3_ARCHIVE);
        if (archivedPayload != null)
            return DecompressL3Payload(archivedPayload.PayloadBytes) ?? Array.Empty<byte>();

        throw new InvalidOperationException("No primary payload block found (expected L0_DATA or L3_ARCHIVE)");
    }

    /// <summary>
    /// Decode L2 INDEX block if present.
    /// </summary>
    public (uint entries, uint[] offsets, uint[] sizes)? DecodeL2Block(byte[] ilogData)
    {
        if (ilogData == null || ilogData.Length < FileHeaderSize)
            return null;

        var file = ReadFileHeader(ilogData);
        ReadBlocks(ilogData, file);

        // Find L2 block
        var l2Block = file.Blocks.FirstOrDefault(b => b.BlockType == BlockType_L2_INDEX);
        if (l2Block == null)
            return null;

        var payload = l2Block.PayloadBytes;
        if (payload.Length < 6) // Min: version(1) + type(1) + entries(4)
            return null;

        int offset = 0;
        byte indexVersion = payload[offset++];
        byte indexType = payload[offset++];

        uint numberOfEntries = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4));
        offset += 4;

        if (numberOfEntries == 0)
            return (0, Array.Empty<uint>(), Array.Empty<uint>());

        var offsets = new uint[numberOfEntries];
        var sizes = new uint[numberOfEntries];

        for (int i = 0; i < numberOfEntries; i++)
        {
            if (offset + 8 > payload.Length)
                break;

            offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4));
            offset += 4;
            sizes[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4));
            offset += 4;
        }

        return (numberOfEntries, offsets, sizes);
    }

    /// <summary>
    /// Decode L3 ARCHIVE block if present and return decompressed data.
    /// </summary>
    public byte[]? DecodeL3Block(byte[] ilogData)
    {
        if (ilogData == null || ilogData.Length < FileHeaderSize)
            return null;

        var file = ReadFileHeader(ilogData);
        ReadBlocks(ilogData, file);

        // Find L3 block
        var l3Block = file.Blocks.FirstOrDefault(b => b.BlockType == BlockType_L3_ARCHIVE);
        if (l3Block == null)
            return null;

        // Decompress L3 payload
        return DecompressL3Payload(l3Block.PayloadBytes);
    }

    /// <summary>
    /// Internal method to decompress L3 payload based on compression type.
    /// </summary>
    private byte[]? DecompressL3Payload(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
            return Array.Empty<byte>();

        if (IlogCompressor.TryDecompress(payload, out var decompressed, out var error))
        {
            return decompressed;
        }

        // Decompression failed
        throw new InvalidOperationException($"L3 decompression failed: {error}");
    }

    /// <summary>
    /// Verify the integrity of an ILOG file based on its profile.
    /// </summary>
    /// <summary>
    /// Verify ILOG integrity and optional signature.
    /// </summary>
    public bool Verify(byte[] ilogData, IlogVerifyOptions? options = null)
    {
        try
        {
            if (ilogData == null || ilogData.Length < FileHeaderSize)
                return false;

            var openError = IlogReader.Open(ilogData, out var view);
            if (openError != null || view == null)
                return false;

            var strictError = IlogReader.ValidateStrict(view);
            if (strictError != null)
                return false;

            // Verify witness chain based on feature flags (fail-closed)
            // Witness presence is signaled via WITNESS_ENABLED flag (bit 5 = 0x20) in file header
            const byte WITNESS_ENABLED_FLAG = 0x20; // Bit 5

            bool witnessEnabledFlag = (view.Data.Span[5] & WITNESS_ENABLED_FLAG) != 0;

            // Validate witness chain in L1 block
            if (IlogReader.TryGetBlockPayload(view, BlockType_L1_TOC, out var l1Payload) && !l1Payload.IsEmpty)
            {
                byte tocVersion = l1Payload[0];

                // Determine effective witness expectation
                // New path: WITNESS_ENABLED flag
                // Legacy path: TocVersion == 2 (deprecated, for backward compatibility)
                bool legacyWitnessPath = (tocVersion == 2);
                bool effectiveWitnessExpected = witnessEnabledFlag || legacyWitnessPath;

                if (effectiveWitnessExpected)
                {
                    // Witness header is expected - validate it (fail-closed)
                    if (l1Payload.Length < 34) // TocVersion(1) + WitnessVersion(1) + Reserved(1) + PrevSealHash(32)
                        return false;

                    byte witnessVersion = l1Payload[1];
                    byte reserved = l1Payload[2];

                    // Validate witness header structure
                    if (witnessVersion != 1 || reserved != 0)
                        return false;

                    // Validate that prev hash is all zeros (single-block model)
                    for (int i = 3; i < 35; i++)
                    {
                        if (l1Payload[i] != 0)
                            return false;
                    }
                }
                else if (tocVersion != 1)
                {
                    // TocVersion must be 1 for non-witness files
                    // (TocVersion == 2 is handled above as legacy witness path)
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private IlogFile ReadFileHeader(byte[] data)
    {
        var file = new IlogFile
        {
            Magic = new byte[4],
            RawData = data
        };

        Buffer.BlockCopy(data, 0, file.Magic, 0, 4);

        // Validate magic
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        if (magic != FileHeaderMagic)
            throw new InvalidOperationException($"Invalid ILOG magic: {magic:X8}");

        file.Version = data[4];
        file.Flags = data[5];
        file.TocBlockOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8, 8));

        return file;
    }

    private void ReadBlocks(byte[] data, IlogFile file)
    {
        var offset = FileHeaderSize;

        while (offset < data.Length)
        {
            if (offset + BlockHeaderSize > data.Length)
                break;

            var block = ReadBlockHeader(data, offset);
            if (block == null)
                break;

            file.Blocks.Add(block);

            // Read payload
            if (block.PayloadBytes.Length > 0)
            {
                var payloadOffset = offset + BlockHeaderSize;
                if (payloadOffset + block.PayloadBytes.Length <= data.Length)
                {
                    var payload = new byte[block.PayloadBytes.Length];
                    Buffer.BlockCopy(data, payloadOffset, payload, 0, payload.Length);
                    block.PayloadBytes = payload;
                }
            }

            offset += BlockHeaderSize + block.PayloadBytes.Length;
        }
    }

    private BlockEntry? ReadBlockHeader(byte[] data, int offset)
    {
        if (offset + BlockHeaderSize > data.Length)
            return null;

        var block = new BlockEntry();

        block.BlockMagic = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        if (block.BlockMagic != BlockHeaderMagic)
            return null;

        block.BlockType = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4, 2));
        block.BlockTimestamp = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset + 8, 8));

        var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 16, 4));
        block.PayloadBytes = new byte[payloadSize];

        block.PayloadCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 20, 4));

        block.PayloadBlake3 = new byte[32];
        Buffer.BlockCopy(data, offset + 24, block.PayloadBlake3, 0, 32);

        return block;
    }

    private uint CalculateCrc32(byte[] data)
    {
        var crc32 = new System.IO.Hashing.Crc32();
        crc32.Append(data);
        var hash = crc32.GetCurrentHash();
        return BinaryPrimitives.ReadUInt32LittleEndian(hash);
    }
}
