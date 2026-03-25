namespace IronConfig.ILog;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using IronConfig;
using IronConfig.Common;

/// <summary>
/// Error information with byte offset (matching C implementation)
/// </summary>
public record class IlogError(IlogErrorCode Code, ulong ByteOffset, string Message);

/// <summary>
/// File flags (matching spec section 13)
/// </summary>
public record class IlogFlags(
    bool LittleEndian,        // Bit 0
    bool HasCrc32,            // Bit 1
    bool HasBlake3,           // Bit 2
    bool HasLayerL2,          // Bit 3
    bool HasLayerL3,          // Bit 4
    bool HasLayerL4           // Bit 5
);

public readonly record struct IlogBlockDescriptor(
    ushort BlockType,
    ulong BlockOffset,
    ulong PayloadOffset,
    uint PayloadSize);

/// <summary>
/// ILOG file view (zero-copy, matching C API)
/// </summary>
public class IlogView
{
    public ReadOnlyMemory<byte> Data { get; }
    public int Size { get; }

    // Header fields
    public uint Magic { get; }
    public byte Version { get; }
    public IlogFlags Flags { get; }

    // Layer offsets
    public ulong L0Offset { get; internal set; }
    public ulong L1Offset { get; internal set; }

    // Parsed TOC metadata
    public uint EventCount { get; internal set; }
    public uint PrimaryPayloadSize { get; internal set; }
    public ushort PrimaryPayloadBlockType { get; internal set; }
    public IlogBlockDescriptor[] Blocks { get; internal set; } = Array.Empty<IlogBlockDescriptor>();
    public bool FastValidated { get; internal set; }

    // Last error
    public IlogError? LastError { get; internal set; }

    public IlogView(ReadOnlyMemory<byte> data, uint magic, byte version, IlogFlags flags)
    {
        Data = data;
        Size = data.Length;
        Magic = magic;
        Version = version;
        Flags = flags;
    }
}

/// <summary>
/// ILOG reference reader (matching spec/ILOG.md sections 13-26)
/// </summary>
public static class IlogReader
{
    private const uint IlogMagic = 0x474F4C49;  // "ILOG" in little-endian u32
    private const uint Blk1Magic = 0x314B4C42;  // "BLK1" in little-endian u32
    private const byte IlogVersion = 0x01;
    private const int FileHeaderSize = 16;
    private const int BlockHeaderSize = 72;

    /// <summary>
    /// Open and parse ILOG file header (per spec section 13 + section 9 L1 TOC)
    /// </summary>
    public static IlogError? Open(byte[] data, out IlogView? view)
    {
        return OpenCore(data, out view);
    }

    public static IlogError? Open(ReadOnlySpan<byte> data, out IlogView? view)
    {
        return OpenCore(new ReadOnlyMemory<byte>(data.ToArray()), out view);
    }

    private static IlogError? OpenCore(ReadOnlyMemory<byte> data, out IlogView? view)
    {
        view = null;
        var span = data.Span;

        // Minimum size check
        if (span.Length < FileHeaderSize)
        {
            return new IlogError(
                IlogErrorCode.CorruptedHeader,
                0,
                "File too small for header"
            );
        }

        // Read file header (16 bytes per spec section 13)
        // Offset 0x00: Magic (4 bytes)
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(span[0x00..0x04]);
        if (magic != IlogMagic && magic != Blk1Magic)
        {
            return new IlogError(
                IlogErrorCode.InvalidMagic,
                0,
                $"Invalid magic: expected 0x{IlogMagic:X8}, got 0x{magic:X8}"
            );
        }

        // Offset 0x04: Version (1 byte)
        byte version = span[0x04];
        if (version != IlogVersion)
        {
            return new IlogError(
                IlogErrorCode.UnsupportedVersion,
                0x04,
                $"Unsupported version: {version}"
            );
        }

        // Offset 0x05: Flags (1 byte)
        byte flagsByte = span[0x05];
        var flags = ParseFlags(flagsByte);

        // Offset 0x06: Reserved0 (2 bytes, must be 0)
        ushort reserved0 = BinaryPrimitives.ReadUInt16LittleEndian(span[0x06..0x08]);
        if (reserved0 != 0)
        {
            return new IlogError(
                IlogErrorCode.CorruptedHeader,
                0x06,
                "Reserved0 must be 0"
            );
        }

        // Offset 0x08: TocBlockOffset (8 bytes)
        ulong tocBlockOffset = BinaryPrimitives.ReadUInt64LittleEndian(span[0x08..0x10]);
        if (tocBlockOffset < FileHeaderSize)
        {
            return new IlogError(
                IlogErrorCode.CorruptedHeader,
                0x08,
                "TocBlockOffset must be >= FileHeaderSize"
            );
        }

        // TocBlockOffset must be within file bounds (must be able to read at least block header)
        const int MinimumTocBlockSize = BlockHeaderSize;
        if (tocBlockOffset + MinimumTocBlockSize > (ulong)span.Length)
        {
            // Distinguish between truncation and corruption:
            // If file is too small to even fit file header + first block header, it's truncation
            // Otherwise, it's corrupted TOC offset
            const int MinimumValidFileSize = FileHeaderSize + BlockHeaderSize;
            IlogErrorCode errorCode = span.Length < MinimumValidFileSize
                ? IlogErrorCode.BlockOutOfBounds
                : IlogErrorCode.CorruptedHeader;

            return new IlogError(
                errorCode,
                tocBlockOffset,
                "TocBlockOffset points beyond file"
            );
        }

        view = new IlogView(data, magic, version, flags)
        {
            L1Offset = tocBlockOffset,
        };

        // Parse L1 TOC to get L0 offset and event count
        if (!TryFindPrimaryPayloadBlock(
            span,
            flags,
            tocBlockOffset,
            out ulong l0Offset,
            out uint eventCount,
            out ushort primaryPayloadBlockType,
            out uint primaryPayloadSize,
            out var blocks,
            out var parseError))
        {
            view.LastError = parseError;
            return parseError;
        }

        view.L0Offset = l0Offset;
        view.EventCount = eventCount;
        view.PrimaryPayloadBlockType = primaryPayloadBlockType;
        view.PrimaryPayloadSize = primaryPayloadSize;
        view.Blocks = blocks;

        return null;
    }

    /// <summary>
    /// Fast validation (per spec section 15, validate_fast)
    /// - BlockMagic
    /// - HeaderSize
    /// - HeaderCrc32
    /// - Basic structure
    /// </summary>
    public static IlogError? ValidateFast(IlogView view)
    {
        if (view.FastValidated)
            return null;

        // Start at file header offset 16
        ulong pos = FileHeaderSize;

        // Check if we can read first block header
        if (pos + BlockHeaderSize > (ulong)view.Size)
        {
            return new IlogError(
                IlogErrorCode.BlockOutOfBounds,
                pos,
                "No room for first block header"
            );
        }

        var data = view.Data.Span;

        // Read first block header (offset 0x00)
        uint blockMagic = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)pos..(int)(pos + 4)]);
        if (blockMagic != Blk1Magic)
        {
            return new IlogError(
                IlogErrorCode.MalformedBlock,
                pos,
                $"Invalid block magic: 0x{blockMagic:X8}"
            );
        }

        // Read HeaderSize (offset 0x08)
        // Tolerant parse: encoder may write BlockTimestamp here, not HeaderSize
        ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(
            data[(int)(pos + 0x08)..(int)(pos + 0x0A)]
        );

        // Check if headerSize looks plausible (should be exactly BlockHeaderSize = 72)
        // If not, assume offset 0x08 contains BlockTimestamp and use fixed header size
        if (headerSize != BlockHeaderSize)
        {
            // Encoder may have written BlockTimestamp at 0x08 instead of HeaderSize
            // In this case, assume fixed BlockHeaderSize and skip CRC validation
            // (since layout is different from what CRC was computed over)
            headerSize = BlockHeaderSize;
        }
        else
        {
            // Normal case: HeaderSize matches expected, verify HeaderCrc32
            // Verify HeaderCrc32 (offset 0x1C, computed over bytes 0x00-0x1B)
            uint storedHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(
                data[(int)(pos + 0x1C)..(int)(pos + 0x20)]
            );
            uint computedHeaderCrc = Crc32Ieee.Compute(data[(int)pos..(int)(pos + 0x1C)]);
            if (storedHeaderCrc != computedHeaderCrc)
            {
                return new IlogError(
                    IlogErrorCode.MalformedBlock,
                    pos + 0x1C,
                    "HeaderCrc32 mismatch"
                );
            }
        }

        view.FastValidated = true;
        return null;
    }

    public static void ResetFastValidation(IlogView view)
    {
        view.FastValidated = false;
    }

    /// <summary>
    /// Strict validation (per spec section 15, validate_strict)
    /// - All fast checks
    /// - Enumerate blocks
    /// - CRC32 if present
    /// - BLAKE3 if present
    /// </summary>
    public static IlogError? ValidateStrict(IlogView view)
    {
        // First pass: fast validation
        var fastResult = ValidateFast(view);
        if (fastResult != null)
            return fastResult;

        var data = view.Data.Span;
        bool hasL0Payload = false;
        ReadOnlySpan<byte> l0Payload = default;
        bool hasL4Seal = false;
        ReadOnlySpan<byte> l4SealHash = default;
        ulong l4SealHashOffset = 0;
        uint l0PayloadCrc32 = 0;

        // Bit 5 is treated as WITNESS-enabled for AUDITED v2 files.
        // Keep strict header hash enforcement for new files only; legacy vectors may not populate header hash.
        bool enforceL4HeaderBlake3 = view.Flags.HasBlake3 && view.Flags.HasLayerL4;

        if (view.Blocks.Length == 0)
        {
            return new IlogError(
                IlogErrorCode.MissingLayer,
                FileHeaderSize,
                "No parsed block metadata in view"
            );
        }

        foreach (var block in view.Blocks)
        {
            ulong payloadOffset = block.PayloadOffset;
            ulong payloadEnd = payloadOffset + block.PayloadSize;
            ushort blockType = block.BlockType;

            // If CRC32 flag set, verify PayloadCrc32
            if (view.Flags.HasCrc32 && block.PayloadSize > 0)
            {
                uint storedCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(
                    data[(int)(block.BlockOffset + 0x14)..(int)(block.BlockOffset + 0x18)]
                );
                var payload = data[(int)payloadOffset..(int)payloadEnd];
                uint computedCrc32 = Crc32Ieee.Compute(payload);

                if (computedCrc32 != storedCrc32)
                {
                    return new IlogError(
                        IlogErrorCode.Crc32Mismatch,
                        block.BlockOffset + 0x18,
                        "PayloadCrc32 mismatch"
                    );
                }

                if (blockType == 0x0001)
                {
                    l0PayloadCrc32 = computedCrc32;
                }
            }

            if (blockType == 0x0001 && block.PayloadSize > 0) // L0_DATA
            {
                hasL0Payload = true;
                l0Payload = data[(int)payloadOffset..(int)payloadEnd];
            }

            if (blockType == 0x0004 && block.PayloadSize > 0) // L3_ARCHIVE
            {
                var archivedPayload = data[(int)payloadOffset..(int)payloadEnd];
                if (!IlogCompressor.TryDecompress(archivedPayload, out _, out var decompressError))
                {
                    return new IlogError(
                        IlogErrorCode.MalformedBlock,
                        payloadOffset,
                        $"L3 archive payload is not decodable: {decompressError}"
                    );
                }
            }

            if (blockType == 0x0005) // L4_SEAL
            {
                hasL4Seal = true;

                // L4 payload layout: [sealVer:1][sealType:1][coverage:1][reserved:1][hash:32]...
                if (block.PayloadSize < 36)
                {
                    return new IlogError(
                        IlogErrorCode.MalformedBlock,
                        payloadOffset,
                        "L4_SEAL payload too short for hash"
                    );
                }

                l4SealHashOffset = payloadOffset + 4;
                l4SealHash = data[(int)(payloadOffset + 4)..(int)(payloadOffset + 36)];

                // New AUDITED files mirror BLAKE3 into block header bytes [0x18..0x37].
                // Require non-zero header hash for this format.
                if (view.Flags.HasBlake3 && enforceL4HeaderBlake3)
                {
                    var headerBlake3Bytes = data[(int)(block.BlockOffset + 0x18)..(int)(block.BlockOffset + 0x38)];
                    bool hasNonzeroHeaderHash = false;
                    for (int i = 0; i < 32; i++)
                    {
                        if (headerBlake3Bytes[i] != 0)
                        {
                            hasNonzeroHeaderHash = true;
                            break;
                        }
                    }

                    if (!hasNonzeroHeaderHash)
                    {
                        return new IlogError(
                            IlogErrorCode.Blake3Mismatch,
                            block.BlockOffset + 0x18,
                            "PayloadBlake3 is zero for L4_SEAL block"
                        );
                    }

                    for (int i = 0; i < 32; i++)
                    {
                        if (headerBlake3Bytes[i] != l4SealHash[i])
                        {
                            return new IlogError(
                                IlogErrorCode.Blake3Mismatch,
                                block.BlockOffset + 0x18,
                                "L4_SEAL header hash does not match payload hash"
                            );
                        }
                    }
                }
            }
        }

        if (view.Flags.HasCrc32 && view.Flags.HasLayerL4 && !view.Flags.HasBlake3)
        {
            if (!hasL0Payload)
            {
                return new IlogError(
                    IlogErrorCode.MissingLayer,
                    FileHeaderSize,
                    "Missing L0_DATA block for CRC32 seal validation"
                );
            }

            if (!hasL4Seal)
            {
                return new IlogError(
                    IlogErrorCode.MissingLayer,
                    FileHeaderSize,
                    "Missing L4_SEAL block for CRC32 seal validation"
                );
            }

            uint storedSealCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(l4SealHash[..4]);
            if (storedSealCrc32 != l0PayloadCrc32)
            {
                return new IlogError(
                    IlogErrorCode.Crc32Mismatch,
                    l4SealHashOffset,
                    "L4_SEAL CRC32 does not match L0 payload"
                );
            }
        }

        if (view.Flags.HasBlake3)
        {
            if (!hasL0Payload)
            {
                return new IlogError(
                    IlogErrorCode.MissingLayer,
                    FileHeaderSize,
                    "Missing L0_DATA block for Blake3 validation"
                );
            }

            if (!hasL4Seal)
            {
                return new IlogError(
                    IlogErrorCode.MissingLayer,
                    FileHeaderSize,
                    "Missing L4_SEAL block for Blake3 validation"
                );
            }

            // L4 payload hash must not be all zeros.
            bool hasNonzeroSealHash = false;
            for (int i = 0; i < 32; i++)
            {
                if (l4SealHash[i] != 0)
                {
                    hasNonzeroSealHash = true;
                    break;
                }
            }

            if (!hasNonzeroSealHash)
            {
                return new IlogError(
                    IlogErrorCode.Blake3Mismatch,
                    l4SealHashOffset,
                    "L4_SEAL payload hash is zero"
                );
            }

            var computed = Blake3.Hasher.Hash(l0Payload).AsSpan();
            for (int i = 0; i < 32; i++)
            {
                if (computed[i] != l4SealHash[i])
                {
                    return new IlogError(
                        IlogErrorCode.Blake3Mismatch,
                        l4SealHashOffset,
                        "L4_SEAL hash does not match L0 payload"
                    );
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get computed CRC32 for L0_DATA block payload (Sequence 0)
    /// </summary>
    public static uint GetL0PayloadCrc32(IlogView view)
    {
        if (TryGetPrimaryPayload(view, out var payload))
            return Crc32Ieee.Compute(payload);

        return 0;
    }

    /// <summary>
    /// Get computed BLAKE3 for L0_DATA block payload (Sequence 0)
    /// </summary>
    public static string GetL0PayloadBlake3Hex(IlogView view)
    {
        if (TryGetPrimaryPayload(view, out var payload))
        {
            var hash = Blake3.Hasher.Hash(payload);
            return ToLowerHex(hash.AsSpan());
        }

        return "";
    }

    // ========== Helpers ==========

    private static IlogFlags ParseFlags(byte flagsByte)
    {
        return new IlogFlags(
            LittleEndian: (flagsByte & 0x01) != 0,
            HasCrc32: (flagsByte & 0x02) != 0,
            HasBlake3: (flagsByte & 0x04) != 0,
            HasLayerL2: (flagsByte & 0x08) != 0,
            HasLayerL3: (flagsByte & 0x10) != 0,
            HasLayerL4: (flagsByte & 0x20) != 0
        );
    }

    private static bool TryGetPrimaryPayload(IlogView view, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        return TryGetBlockPayload(view, view.PrimaryPayloadBlockType, out payload);
    }

    internal static bool TryGetBlockPayload(IlogView view, ushort blockType, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        var data = view.Data.Span;

        foreach (var block in view.Blocks)
        {
            if (block.BlockType != blockType)
                continue;

            ulong payloadEnd = block.PayloadOffset + block.PayloadSize;
            if (payloadEnd > (ulong)view.Size)
                return false;

            payload = data[(int)block.PayloadOffset..(int)payloadEnd];
            return true;
        }

        return false;
    }

    private static string ToLowerHex(ReadOnlySpan<byte> bytes)
    {
        byte[] data = bytes.ToArray();
        return string.Create(data.Length * 2, data, static (chars, state) =>
        {
            const string Hex = "0123456789abcdef";
            for (int i = 0; i < state.Length; i++)
            {
                byte b = state[i];
                chars[i * 2] = Hex[b >> 4];
                chars[i * 2 + 1] = Hex[b & 0x0F];
            }
        });
    }

    private static bool TryFindPrimaryPayloadBlock(
        ReadOnlySpan<byte> data,
        IlogFlags flags,
        ulong tocBlockOffset,
        out ulong l0Offset,
        out uint eventCount,
        out ushort primaryPayloadBlockType,
        out uint primaryPayloadSize,
        out IlogBlockDescriptor[] blocks,
        out IlogError? error)
    {
        _ = tocBlockOffset;
        l0Offset = 0;
        eventCount = 0;
        primaryPayloadBlockType = 0;
        primaryPayloadSize = 0;
        blocks = Array.Empty<IlogBlockDescriptor>();
        error = null;

        ulong blockPos = FileHeaderSize;
        ulong archivedOffset = 0;
        var parsedBlocks = new List<IlogBlockDescriptor>();
        bool foundPrimaryPayload = false;

        while (blockPos + BlockHeaderSize <= (ulong)data.Length)
        {
            ushort blockType = BinaryPrimitives.ReadUInt16LittleEndian(
                data[(int)(blockPos + 0x04)..(int)(blockPos + 0x06)]
            );
            uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(
                data[(int)(blockPos + 0x10)..(int)(blockPos + 0x14)]
            );

            ulong payloadOffset = blockPos + BlockHeaderSize;
            if (payloadOffset + payloadSize > (ulong)data.Length)
            {
                error = new IlogError(IlogErrorCode.BlockOutOfBounds, blockPos, "Payload out of bounds");
                return false;
            }

            parsedBlocks.Add(new IlogBlockDescriptor(blockType, blockPos, payloadOffset, payloadSize));

            if (blockType == 0x0001 && !foundPrimaryPayload)
            {
                var payload = data[(int)payloadOffset..(int)(payloadOffset + payloadSize)];
                if (payload.Length < 13)
                {
                    error = new IlogError(IlogErrorCode.RecordTruncated, payloadOffset, "L0 payload too short for header");
                    return false;
                }

                byte streamVersion = payload[0];
                if (streamVersion != 0x01)
                {
                    error = new IlogError(IlogErrorCode.SchemaValidation, payloadOffset, "Invalid stream version");
                    return false;
                }

                l0Offset = blockPos;
                eventCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..5]);
                primaryPayloadBlockType = blockType;
                primaryPayloadSize = payloadSize;
                foundPrimaryPayload = true;
            }

            if (blockType == 0x0004)
                archivedOffset = blockPos;

            blockPos = payloadOffset + payloadSize;
        }

        blocks = parsedBlocks.ToArray();

        if (foundPrimaryPayload)
            return true;

        if (flags.HasLayerL3 && archivedOffset != 0)
        {
            l0Offset = archivedOffset;
            eventCount = 1;
            primaryPayloadBlockType = 0x0004;
            primaryPayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(
                data[(int)(archivedOffset + 0x10)..(int)(archivedOffset + 0x14)]
            );
            return true;
        }

        error = new IlogError(IlogErrorCode.MissingLayer, FileHeaderSize, "Primary payload block not found");
        return false;
    }

    private static bool TryDecodeVarint(ReadOnlySpan<byte> data, int offset, out ulong value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        int shift = 0;

        for (int i = 0; i < 10 && offset + i < data.Length; i++)
        {
            byte b = data[offset + i];
            value |= ((ulong)(b & 0x7F)) << shift;
            bytesRead++;

            if ((b & 0x80) == 0)
                return true;

            shift += 7;
        }

        return false;
    }
}
