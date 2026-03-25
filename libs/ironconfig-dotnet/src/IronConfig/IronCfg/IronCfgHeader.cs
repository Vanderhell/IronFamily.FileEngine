using System;

namespace IronConfig.IronCfg;

/// <summary>
/// IRONCFG file header (64 bytes fixed, little-endian)
/// </summary>
public readonly struct IronCfgHeader
{
    public const uint MAGIC = 0x47464349; // "ICFG" in little-endian
    public const int HEADER_SIZE = 64;
    public const byte VERSION = 2; // v2: Added ElementSchema for Array fields

    public uint Magic { get; }
    public byte Version { get; }
    public byte Flags { get; }
    public ushort Reserved0 { get; }
    public uint FileSize { get; }
    public uint SchemaOffset { get; }
    public uint SchemaSize { get; }
    public uint StringPoolOffset { get; }
    public uint StringPoolSize { get; }
    public uint DataOffset { get; }
    public uint DataSize { get; }
    public uint CrcOffset { get; }
    public uint Blake3Offset { get; }
    public uint Reserved1 { get; }
    public ReadOnlyMemory<byte> Reserved2 { get; }

    public IronCfgHeader(
        uint magic,
        byte version,
        byte flags,
        ushort reserved0,
        uint fileSize,
        uint schemaOffset,
        uint schemaSize,
        uint stringPoolOffset,
        uint stringPoolSize,
        uint dataOffset,
        uint dataSize,
        uint crcOffset,
        uint blake3Offset,
        uint reserved1,
        ReadOnlyMemory<byte> reserved2)
    {
        Magic = magic;
        Version = version;
        Flags = flags;
        Reserved0 = reserved0;
        FileSize = fileSize;
        SchemaOffset = schemaOffset;
        SchemaSize = schemaSize;
        StringPoolOffset = stringPoolOffset;
        StringPoolSize = stringPoolSize;
        DataOffset = dataOffset;
        DataSize = dataSize;
        CrcOffset = crcOffset;
        Blake3Offset = blake3Offset;
        Reserved1 = reserved1;
        Reserved2 = reserved2;
    }

    public bool HasCrc32 => (Flags & 0x01) != 0;
    public bool HasBlake3 => (Flags & 0x02) != 0;
    public bool HasEmbeddedSchema => (Flags & 0x04) != 0;

    /// <summary>
    /// Parse header from buffer (64 bytes required)
    /// </summary>
    public static IronCfgError Parse(ReadOnlySpan<byte> buffer, out IronCfgHeader header)
    {
        header = default;

        // Step 1: File size >= 64 bytes
        if (buffer.Length < HEADER_SIZE)
            return new IronCfgError(IronCfgErrorCode.TruncatedFile, 0);

        // Step 2: Magic = "ICFG"
        uint magic = ReadUInt32LE(buffer, 0);
        if (magic != MAGIC)
            return new IronCfgError(IronCfgErrorCode.InvalidMagic, 0);

        // Step 3: Version = 1 or 2 (backward compatible)
        byte version = buffer[4];
        if (version != 1 && version != 2)
            return new IronCfgError(IronCfgErrorCode.InvalidVersion, 4);

        // Step 4: Flags bits 3-7 = 0
        byte flags = buffer[5];
        if ((flags & 0xF8) != 0)
            return new IronCfgError(IronCfgErrorCode.InvalidFlags, 5);

        // Step 5: reserved0 = 0x0000
        ushort reserved0 = ReadUInt16LE(buffer, 6);
        if (reserved0 != 0)
            return new IronCfgError(IronCfgErrorCode.ReservedFieldNonzero, 6);

        // Read all offsets
        uint fileSize = ReadUInt32LE(buffer, 8);
        uint schemaOffset = ReadUInt32LE(buffer, 12);
        uint schemaSize = ReadUInt32LE(buffer, 16);
        uint stringPoolOffset = ReadUInt32LE(buffer, 20);
        uint stringPoolSize = ReadUInt32LE(buffer, 24);
        uint dataOffset = ReadUInt32LE(buffer, 28);
        uint dataSize = ReadUInt32LE(buffer, 32);
        uint crcOffset = ReadUInt32LE(buffer, 36);
        uint blake3Offset = ReadUInt32LE(buffer, 40);
        uint reserved1 = ReadUInt32LE(buffer, 44);

        // Step 6: reserved1 = 0x00000000
        if (reserved1 != 0)
            return new IronCfgError(IronCfgErrorCode.ReservedFieldNonzero, 44);

        // Step 7: reserved2 = all 0x00
        for (int i = 0; i < 16; i++)
        {
            if (buffer[48 + i] != 0)
                return new IronCfgError(IronCfgErrorCode.ReservedFieldNonzero, 48);
        }

        // Step 8: CRC flag ↔ crcOffset consistency
        bool crcFlag = (flags & 0x01) != 0;
        if (crcFlag && crcOffset == 0)
            return new IronCfgError(IronCfgErrorCode.FlagMismatch, 5);
        if (!crcFlag && crcOffset != 0)
            return new IronCfgError(IronCfgErrorCode.FlagMismatch, 5);

        // Step 9: BLAKE3 flag ↔ blake3Offset consistency
        bool blake3Flag = (flags & 0x02) != 0;
        if (blake3Flag && blake3Offset == 0)
            return new IronCfgError(IronCfgErrorCode.FlagMismatch, 5);
        if (!blake3Flag && blake3Offset != 0)
            return new IronCfgError(IronCfgErrorCode.FlagMismatch, 5);

        // Step 10: Offset monotonicity
        uint expectedFileSize = dataOffset + dataSize;
        if (crcFlag) expectedFileSize += 4;
        if (blake3Flag) expectedFileSize += 32;

        // Schema must be non-zero
        if (schemaOffset == 0 || schemaSize == 0)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, 12);

        // Data must be non-zero
        if (dataOffset == 0 || dataSize == 0)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, 28);

        uint schemaEnd = schemaOffset + schemaSize;
        uint poolStart = stringPoolOffset > 0 ? stringPoolOffset : dataOffset;
        uint poolEnd = stringPoolOffset > 0 ? stringPoolOffset + stringPoolSize : dataOffset;

        // Check ordering: schema < schema+size <= pool < pool+size <= data
        if (!(schemaOffset < schemaEnd && schemaEnd <= poolStart &&
              poolStart <= poolEnd && poolEnd <= dataOffset &&
              dataOffset < dataOffset + dataSize))
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, 12);

        // CRC ordering
        if (crcOffset > 0 && !(dataOffset + dataSize <= crcOffset))
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, 36);

        // BLAKE3 ordering
        if (blake3Offset > 0)
        {
            uint crcEnd = crcOffset > 0 ? crcOffset + 4 : dataOffset + dataSize;
            if (!(crcEnd <= blake3Offset))
                return new IronCfgError(IronCfgErrorCode.BoundsViolation, 40);
        }

        // Step 11: File size match
        if (fileSize != expectedFileSize)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, 8);

        if (buffer.Length != fileSize)
            return new IronCfgError(IronCfgErrorCode.TruncatedFile, 0);

        // Create reserved2 from buffer
        byte[] res2 = new byte[16];
        buffer.Slice(48, 16).CopyTo(res2);

        header = new IronCfgHeader(
            magic, version, flags, reserved0, fileSize,
            schemaOffset, schemaSize, stringPoolOffset, stringPoolSize,
            dataOffset, dataSize, crcOffset, blake3Offset,
            reserved1, new ReadOnlyMemory<byte>(res2));

        return IronCfgError.Ok;
    }

    private static uint ReadUInt32LE(ReadOnlySpan<byte> buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    private static ushort ReadUInt16LE(ReadOnlySpan<byte> buf, int off)
    {
        return (ushort)(buf[off] | (buf[off + 1] << 8));
    }
}
