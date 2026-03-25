using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Ed25519Vendor.SommerEngineering;

namespace IronConfig.ILog;

/// <summary>
/// ILOG binary encoder (writer).
/// Supports all 5 profiles: MINIMAL, INTEGRITY, SEARCHABLE, ARCHIVED, AUDITED.
/// </summary>
public class IlogEncoder
{
    private const uint FileHeaderMagic = 0x474F4C49;  // "ILOG"
    private const uint BlockHeaderMagic = 0x314B4C42; // "BLK1"
    private const int FileHeaderSize = 16;
    private const int BlockHeaderSize = 72;

    // Block type registry (spec section 14)
    private const ushort BlockType_L0_DATA = 0x0001;
    private const ushort BlockType_L1_TOC = 0x0002;
    private const ushort BlockType_L2_INDEX = 0x0003;
    private const ushort BlockType_L3_ARCHIVE = 0x0004;
    private const ushort BlockType_L4_SEAL = 0x0005;

    private byte _flags;
    private readonly List<BlockEntry> _blocks = new();

    /// <summary>
    /// Deterministic mode: Use fixed timestamp (0) instead of current time.
    /// Enable via IRONFAMILY_DETERMINISTIC=1 env var for testing/reproducibility.
    /// </summary>
    private static readonly bool IsDeterministicMode =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IRONFAMILY_DETERMINISTIC"));

    /// <summary>
    /// Get current timestamp in milliseconds, or 0 if in deterministic mode.
    /// </summary>
    private static long GetTimestampMs() =>
        IsDeterministicMode ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public class BlockEntry
    {
        public ushort BlockType { get; set; }
        public byte[] HeaderBytes { get; set; } = new byte[BlockHeaderSize];
        public byte[]? PayloadBytes { get; set; }
        public uint PayloadCrc32 { get; set; }
        public byte[]? PayloadBlake3 { get; set; }
    }

    /// <summary>
    /// Create ILOG file from event/record data.
    /// </summary>
    /// <summary>
    /// Encode event data with optional signing for AUDITED profile.
    /// </summary>
    public byte[] Encode(byte[] eventData, IlogProfile profile, IlogEncodeOptions? options = null)
    {
        // Validate options for profile
        options?.ValidateForProfile(profile);

        _blocks.Clear();
        var output = new MemoryStream();

        // Step 1: Determine flags based on profile
        _flags = profile.GetProfileFlags();

        // ARCHIVED is storage-first: L3 becomes the primary payload and raw L0 is omitted.
        if (profile != IlogProfile.ARCHIVED)
        {
            var l0Block = CreateL0Block(eventData, profile);
            _blocks.Add(l0Block);
        }

        // Step 2: Create L1 (TOC) block
        var l1Block = CreateL1Block(profile);
        _blocks.Add(l1Block);

        // Step 3: Create optional L2 (INDEX) if SEARCHABLE profile
        if (profile == IlogProfile.SEARCHABLE)
        {
            var l2Block = CreateL2Block();
            _blocks.Add(l2Block);
        }

        // Step 4: Create optional L3 (ARCHIVE) if ARCHIVED profile
        if (profile == IlogProfile.ARCHIVED)
        {
            var l3Block = CreateL3Block(eventData);
            _blocks.Add(l3Block);
        }

        // Step 5: Create optional L4 (SEAL) if INTEGRITY or AUDITED
        if (profile == IlogProfile.INTEGRITY || profile == IlogProfile.AUDITED)
        {
            var l4Block = CreateL4Block(profile, options);
            _blocks.Add(l4Block);
        }

        // Step 6: Calculate TOC offset based on actual block ordering.
        ulong tocOffset = FileHeaderSize;
        foreach (var block in _blocks)
        {
            if (block.BlockType == BlockType_L1_TOC)
                break;

            tocOffset += (ulong)(BlockHeaderSize + (block.PayloadBytes?.Length ?? 0));
        }

        // Step 7: Write file header
        WriteFileHeader(output, tocOffset);

        // Step 8: Write all blocks
        foreach (var block in _blocks)
        {
            output.Write(block.HeaderBytes, 0, BlockHeaderSize);
            if (block.PayloadBytes != null && block.PayloadBytes.Length > 0)
            {
                output.Write(block.PayloadBytes, 0, block.PayloadBytes.Length);
            }
        }

        return output.ToArray();
    }

    private BlockEntry CreateL0Block(byte[] eventData, IlogProfile profile)
    {
        // L0 payload structure: StreamVersion (u8), EventCount (u32), TimestampEpoch (u64), then event data
        var payload = new MemoryStream();

        // StreamVersion (u8)
        payload.WriteByte(0x01);

        // EventCount (u32) - for now, treat whole eventData as 1 event
        payload.Write(BitConverter.GetBytes(1U), 0, 4);

        // TimestampEpoch (u64) - use current time in milliseconds since epoch
        long epochMs = GetTimestampMs();
        payload.Write(BitConverter.GetBytes(epochMs), 0, 8);

        // Event data
        payload.Write(eventData, 0, eventData.Length);

        var payloadBytes = payload.ToArray();

        var block = new BlockEntry
        {
            BlockType = BlockType_L0_DATA,
            PayloadBytes = payloadBytes
        };

        // Calculate CRC32 if needed
        if ((profile == IlogProfile.INTEGRITY || profile == IlogProfile.AUDITED) && payloadBytes.Length > 0)
        {
            block.PayloadCrc32 = CalculateCrc32(payloadBytes);
        }

        WriteBlockHeader(block);
        return block;
    }

    private BlockEntry CreateL1Block(IlogProfile profile)
    {
        // L1 TOC payload structure (spec section 9)
        var tocPayload = new MemoryStream();

        // TocVersion (u8) - always 1 (global format version)
        // Witness presence is signaled via WITNESS_ENABLED flag (bit 5) in file header flags
        byte tocVersion = 1;
        tocPayload.WriteByte(tocVersion);

        // Witness header for AUDITED profile (34 bytes total)
        if (profile == IlogProfile.AUDITED)
        {
            // WitnessVersion (u8) = 1
            tocPayload.WriteByte(0x01);

            // Reserved (u8) = 0
            tocPayload.WriteByte(0x00);

            // PrevSealHash (32 bytes) = BLAKE3 of previous L4 SEAL block
            // For single-block model, this is all zeros (no previous block)
            // In future multi-block scenarios, this would reference previous block's L4 bytes
            var prevSealHash = new byte[32]; // All zeros for now
            tocPayload.Write(prevSealHash, 0, 32);
        }

        // LayerCount (u32). ARCHIVED = L1 + L3, other profiles start with L0 + L1.
        int layerCount = 2;
        if (profile == IlogProfile.SEARCHABLE) layerCount++;
        if (profile == IlogProfile.ARCHIVED) layerCount++;
        if (profile == IlogProfile.INTEGRITY || profile == IlogProfile.AUDITED) layerCount++;

        byte[] layerCountBytes = BitConverter.GetBytes((uint)layerCount);
        tocPayload.Write(layerCountBytes, 0, 4);

        // Write layer entries (18 bytes each)
        if (profile != IlogProfile.ARCHIVED)
            WriteLayerEntry(tocPayload, BlockType_L0_DATA, 1);

        WriteLayerEntry(tocPayload, BlockType_L1_TOC, 1);

        if (profile == IlogProfile.SEARCHABLE)
        {
            WriteLayerEntry(tocPayload, BlockType_L2_INDEX, 1);
        }

        if (profile == IlogProfile.ARCHIVED)
        {
            WriteLayerEntry(tocPayload, BlockType_L3_ARCHIVE, 1);
        }

        if (profile == IlogProfile.INTEGRITY || profile == IlogProfile.AUDITED)
        {
            WriteLayerEntry(tocPayload, BlockType_L4_SEAL, 1);
        }

        var payloadBytes = tocPayload.ToArray();
        var block = new BlockEntry
        {
            BlockType = BlockType_L1_TOC,
            PayloadBytes = payloadBytes
        };

        // Compute CRC32 if needed
        if ((profile == IlogProfile.INTEGRITY || profile == IlogProfile.AUDITED) && payloadBytes.Length > 0)
        {
            block.PayloadCrc32 = CalculateCrc32(payloadBytes);
        }

        WriteBlockHeader(block);
        return block;
    }

    private BlockEntry CreateL2Block()
    {
        // L2 INDEX layer - index over byte positions in L0 block
        var indexPayload = new MemoryStream();

        // IndexVersion (u8)
        indexPayload.WriteByte(0x01);

        // IndexType (u8) - 0x00 = position index
        indexPayload.WriteByte(0x00);

        // Get L0 block
        var l0Block = _blocks.FirstOrDefault(b => b.BlockType == BlockType_L0_DATA);
        uint numberOfEntries = 0;

        if (l0Block?.PayloadBytes != null && l0Block.PayloadBytes.Length > 0)
        {
            // Create index entries: every 4KB chunk gets an entry
            const int CHUNK_SIZE = 4096;
            numberOfEntries = (uint)((l0Block.PayloadBytes.Length + CHUNK_SIZE - 1) / CHUNK_SIZE);

            // Write NumberOfEntries (u32)
            indexPayload.Write(BitConverter.GetBytes(numberOfEntries), 0, 4);

            // Write index entries (each 8 bytes: offset u32 + size u32)
            for (uint i = 0; i < numberOfEntries; i++)
            {
                uint offset = i * CHUNK_SIZE;
                uint size = Math.Min(CHUNK_SIZE, (uint)l0Block.PayloadBytes.Length - offset);

                indexPayload.Write(BitConverter.GetBytes(offset), 0, 4);
                indexPayload.Write(BitConverter.GetBytes(size), 0, 4);
            }
        }
        else
        {
            // No entries
            indexPayload.Write(BitConverter.GetBytes(0u), 0, 4);
        }

        var payloadBytes = indexPayload.ToArray();
        var block = new BlockEntry
        {
            BlockType = BlockType_L2_INDEX,
            PayloadBytes = payloadBytes
        };

        // Compute CRC32 if needed (L2 blocks use INTEGRITY/AUDITED too, but L2 only for SEARCHABLE)
        // Since L2 is only created for SEARCHABLE, and SEARCHABLE doesn't use CRC32 per spec,
        // we skip CRC32 for L2 blocks

        WriteBlockHeader(block);
        return block;
    }

    private BlockEntry CreateL3Block(byte[] eventData)
    {
        // L3 ARCHIVE layer - compress data using LZ4 + LZ77 hybrid
        var compressedData = IlogCompressor.Compress(eventData);

        var block = new BlockEntry
        {
            BlockType = BlockType_L3_ARCHIVE,
            PayloadBytes = compressedData
        };

        // L3 is only created for ARCHIVED profile, which doesn't use CRC32 per spec
        // (ARCHIVED doesn't use INTEGRITY flags)

        WriteBlockHeader(block);
        return block;
    }

    private BlockEntry CreateL4Block(IlogProfile profile, IlogEncodeOptions? options = null)
    {
        // L4 SEAL layer (spec section 12)
        var sealPayload = new MemoryStream();

        // SealVersion (u8)
        sealPayload.WriteByte(0x01);

        // SealType (u8) - 0x00 = hash-based
        sealPayload.WriteByte(0x00);

        // CoverageType (u8) - 0x00 = all blocks, 0x01 = L0+L1 only
        sealPayload.WriteByte(0x01); // L0+L1 only

        // Reserved (u8)
        sealPayload.WriteByte(0x00);

        // Hash (32 bytes) - CRC32 (4 bytes zero-padded) or BLAKE3 (32 bytes)
        byte[] hashValue = new byte[32];

        if (profile == IlogProfile.INTEGRITY)
        {
            // For INTEGRITY: Reuse the already-computed L0 payload CRC32 in the seal payload.
            var l0Block = _blocks.FirstOrDefault(b => b.BlockType == BlockType_L0_DATA);
            if (l0Block != null && l0Block.PayloadBytes != null && l0Block.PayloadBytes.Length > 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(hashValue.AsSpan(0, 4), l0Block.PayloadCrc32);
            }
        }
        else if (profile == IlogProfile.AUDITED)
        {
            // For AUDITED: Compute BLAKE3 hash of all data blocks
            var l0Block = _blocks.FirstOrDefault(b => b.BlockType == BlockType_L0_DATA);
            if (l0Block != null && l0Block.PayloadBytes != null && l0Block.PayloadBytes.Length > 0)
            {
                // Use Blake3 hasher
                var hasher = Blake3.Hasher.New();
                hasher.Update(l0Block.PayloadBytes);
                var hash = hasher.Finalize();
                Buffer.BlockCopy(hash.AsSpan().ToArray(), 0, hashValue, 0, 32);
            }
        }

        sealPayload.Write(hashValue, 0, 32);

        // OptionalSignatureLength (u32) and signature bytes
        byte[] signature = new byte[0];

        if (profile == IlogProfile.AUDITED)
        {
            // Sign the BLAKE3 hash with Ed25519 (requires options with keys)
            try
            {
                if (options != null && options.Ed25519PrivateKey32.HasValue && options.Ed25519PublicKey32.HasValue)
                {
                    byte[] privKey = options.Ed25519PrivateKey32.Value.ToArray();
                    byte[] pubKey = options.Ed25519PublicKey32.Value.ToArray();
                    signature = Signer.Sign(hashValue, privKey, pubKey).ToArray();
                    // Signature should be 64 bytes
                }
                else
                {
                    // No options provided - use fallback to empty signature
                    signature = new byte[0];
                }
            }
            catch
            {
                // If signing fails, use empty signature (fallback for robustness)
                signature = new byte[0];
            }
        }

        // Write AlgorithmId (1 byte) - identifies signing algorithm
        // 0 = None (no signature), 1 = SommerEngineering-based Ed25519 (internal, NOT RFC8032)
        byte algorithmId = profile == IlogProfile.AUDITED && signature.Length > 0
            ? (byte)1 // SommerEdInternal
            : (byte)0; // None
        sealPayload.WriteByte(algorithmId);

        // Write signature length
        uint sigLen = (uint)signature.Length;
        sealPayload.Write(BitConverter.GetBytes(sigLen), 0, 4);

        // Write signature bytes if present
        if (signature.Length > 0)
        {
            sealPayload.Write(signature, 0, signature.Length);
        }

        var payloadBytes = sealPayload.ToArray();
        var block = new BlockEntry
        {
            BlockType = BlockType_L4_SEAL,
            PayloadBytes = payloadBytes
        };

        // For AUDITED profile, mirror the computed seal hash into block header PayloadBlake3
        // so strict readers can validate header/payload consistency on new-format files.
        if (profile == IlogProfile.AUDITED)
        {
            block.PayloadBlake3 = hashValue;
        }

        // Compute CRC32 if needed (L4 only exists for INTEGRITY/AUDITED)
        if ((profile == IlogProfile.INTEGRITY || profile == IlogProfile.AUDITED) && payloadBytes.Length > 0)
        {
            block.PayloadCrc32 = CalculateCrc32(payloadBytes);
        }

        WriteBlockHeader(block);
        return block;
    }

    private void WriteFileHeader(MemoryStream output, ulong tocOffset)
    {
        var header = new byte[FileHeaderSize];

        // Magic (4 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), FileHeaderMagic);

        // Version (1 byte)
        header[4] = 0x01;

        // Flags (1 byte)
        header[5] = _flags;

        // Reserved0 (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 0x0000);

        // TocBlockOffset (8 bytes)
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8, 8), tocOffset);

        output.Write(header, 0, FileHeaderSize);
    }

    private void WriteBlockHeader(BlockEntry block)
    {
        var header = new byte[BlockHeaderSize];

        // BlockMagic (4 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), BlockHeaderMagic);

        // BlockType (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), block.BlockType);

        // Reserved1 (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 0x0000);

        // BlockTimestamp (8 bytes)
        ulong timestamp = (ulong)GetTimestampMs();
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8, 8), timestamp);

        // PayloadSize (4 bytes)
        uint payloadSize = block.PayloadBytes != null ? (uint)block.PayloadBytes.Length : 0;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16, 4), payloadSize);

        // PayloadCrc32 (4 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20, 4), block.PayloadCrc32);

        // PayloadBlake3 (32 bytes)
        if (block.PayloadBlake3 != null)
        {
            Buffer.BlockCopy(block.PayloadBlake3, 0, header, 24, 32);
        }

        // Reserved2 (padding to 72 bytes total)
        // Bytes 56-71 are reserved/padding

        block.HeaderBytes = header;
    }

    private void WriteLayerEntry(MemoryStream output, ushort layerType, uint blockCount)
    {
        // LayerType (2 bytes)
        byte[] layerTypeBytes = BitConverter.GetBytes(layerType);
        output.Write(layerTypeBytes, 0, 2);

        // BlockCount (4 bytes)
        byte[] blockCountBytes = BitConverter.GetBytes(blockCount);
        output.Write(blockCountBytes, 0, 4);

        // Flags (4 bytes) - reserved for future use
        output.Write(BitConverter.GetBytes(0u), 0, 4);

        // Reserved (8 bytes) - must be zero
        output.Write(new byte[8], 0, 8);
    }

    private uint CalculateCrc32(byte[] data)
    {
        // Use Crc32 IEEE 802.3
        var crc32 = new System.IO.Hashing.Crc32();
        crc32.Append(data);
        var hash = crc32.GetCurrentHash();
        return BinaryPrimitives.ReadUInt32LittleEndian(hash);
    }

    private ulong CalculateBlocksSize(int startIndex, int endIndex)
    {
        ulong size = 0;
        for (int i = startIndex; i <= endIndex && i < _blocks.Count; i++)
        {
            size += BlockHeaderSize;
            var payloadBytes = _blocks[i].PayloadBytes;
            if (payloadBytes != null)
            {
                size += (ulong)payloadBytes.Length;
            }
        }
        return size;
    }
}
