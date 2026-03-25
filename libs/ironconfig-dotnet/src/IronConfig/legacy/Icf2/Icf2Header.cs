using System;
using System.Text;

namespace IronConfig.Icf2;

/// <summary>
/// ICF2 file header parser and validator (64 bytes fixed)
/// </summary>
public class Icf2Header
{
    public const uint MAGIC = 0x32464349; // "ICF2" in little-endian
    public const int HEADER_SIZE = 64;

    public uint FileSize { get; set; }
    public uint PrefixDictOffset { get; set; }
    public uint PrefixDictSize { get; set; }
    public uint SchemaOffset { get; set; }
    public uint SchemaSize { get; set; }
    public uint ColumnsOffset { get; set; }
    public uint ColumnsSize { get; set; }
    public uint RowIndexOffset { get; set; }
    public uint RowIndexSize { get; set; }
    public uint PayloadOffset { get; set; }
    public uint PayloadSize { get; set; }
    public uint CrcOffset { get; set; }
    public uint Blake3Offset { get; set; }

    public bool HasCrc32 { get; set; }
    public bool HasBlake3 { get; set; }
    public bool HasPrefixDict { get; set; }
    public bool HasColumns { get; set; }

    public static Icf2Header Parse(byte[] buffer, int offset = 0)
    {
        if (buffer.Length < offset + HEADER_SIZE)
            throw new InvalidOperationException("Buffer too small for ICF2 header");

        // Validate magic
        uint magic = ReadUInt32LE(buffer, offset + 0);
        if (magic != MAGIC)
            throw new InvalidOperationException($"Invalid ICF2 magic: 0x{magic:X8}");

        // Validate version
        byte version = buffer[offset + 4];
        if (version != 0)
            throw new InvalidOperationException($"Unsupported ICF2 version: {version}");

        // Parse flags
        byte flags = buffer[offset + 5];
        bool hasCrc32 = (flags & 0x01) != 0;
        bool hasBlake3 = (flags & 0x02) != 0;
        bool hasPrefixDict = (flags & 0x04) != 0;
        bool hasColumns = (flags & 0x08) != 0;

        if ((flags & 0xF0) != 0)
            throw new InvalidOperationException($"Invalid ICF2 flags: 0x{flags:X2}");

        var header = new Icf2Header
        {
            FileSize = ReadUInt32LE(buffer, offset + 8),
            PrefixDictOffset = ReadUInt32LE(buffer, offset + 12),
            PrefixDictSize = ReadUInt32LE(buffer, offset + 16),
            SchemaOffset = ReadUInt32LE(buffer, offset + 20),
            SchemaSize = ReadUInt32LE(buffer, offset + 24),
            ColumnsOffset = ReadUInt32LE(buffer, offset + 28),
            ColumnsSize = ReadUInt32LE(buffer, offset + 32),
            RowIndexOffset = ReadUInt32LE(buffer, offset + 36),
            RowIndexSize = ReadUInt32LE(buffer, offset + 40),
            PayloadOffset = ReadUInt32LE(buffer, offset + 44),
            PayloadSize = ReadUInt32LE(buffer, offset + 48),
            CrcOffset = ReadUInt32LE(buffer, offset + 52),
            Blake3Offset = ReadUInt32LE(buffer, offset + 56),
            HasCrc32 = hasCrc32,
            HasBlake3 = hasBlake3,
            HasPrefixDict = hasPrefixDict,
            HasColumns = hasColumns
        };

        // Validate offsets are monotonic
        ValidateOffsets(header, buffer.Length);

        return header;
    }

    private static void ValidateOffsets(Icf2Header h, int bufferLen)
    {
        uint prevEnd = (uint)HEADER_SIZE;

        if (h.HasPrefixDict)
        {
            if (h.PrefixDictOffset < prevEnd || h.PrefixDictOffset + h.PrefixDictSize > bufferLen)
                throw new InvalidOperationException("Prefix dictionary offset out of bounds");
            prevEnd = h.PrefixDictOffset + h.PrefixDictSize;
        }

        if (h.SchemaOffset < prevEnd || h.SchemaOffset + h.SchemaSize > bufferLen)
            throw new InvalidOperationException("Schema offset out of bounds");
        prevEnd = h.SchemaOffset + h.SchemaSize;

        if (h.HasColumns)
        {
            if (h.ColumnsOffset < prevEnd || h.ColumnsOffset + h.ColumnsSize > bufferLen)
                throw new InvalidOperationException("Columns offset out of bounds");
            prevEnd = h.ColumnsOffset + h.ColumnsSize;
        }

        if (h.RowIndexSize > 0)
        {
            if (h.RowIndexOffset < prevEnd || h.RowIndexOffset + h.RowIndexSize > bufferLen)
                throw new InvalidOperationException("Row index offset out of bounds");
            prevEnd = h.RowIndexOffset + h.RowIndexSize;
        }

        if (h.PayloadSize > 0)
        {
            if (h.PayloadOffset < prevEnd || h.PayloadOffset + h.PayloadSize > bufferLen)
                throw new InvalidOperationException("Payload offset out of bounds");
            prevEnd = h.PayloadOffset + h.PayloadSize;
        }

        if (h.HasCrc32)
        {
            if (h.CrcOffset < prevEnd || h.CrcOffset + 4 > bufferLen)
                throw new InvalidOperationException("CRC offset out of bounds");
            prevEnd = h.CrcOffset + 4;
        }

        if (h.HasBlake3)
        {
            if (h.Blake3Offset < prevEnd || h.Blake3Offset + 32 > bufferLen)
                throw new InvalidOperationException("BLAKE3 offset out of bounds");
        }
    }

    public byte[] Serialize()
    {
        var buf = new byte[HEADER_SIZE];
        uint flags = 0;
        if (HasCrc32) flags |= 0x01;
        if (HasBlake3) flags |= 0x02;
        if (HasPrefixDict) flags |= 0x04;
        if (HasColumns) flags |= 0x08;

        WriteUInt32LE(buf, 0, MAGIC);
        buf[4] = 0; // version
        buf[5] = (byte)flags;
        WriteUInt32LE(buf, 8, FileSize);
        WriteUInt32LE(buf, 12, PrefixDictOffset);
        WriteUInt32LE(buf, 16, PrefixDictSize);
        WriteUInt32LE(buf, 20, SchemaOffset);
        WriteUInt32LE(buf, 24, SchemaSize);
        WriteUInt32LE(buf, 28, ColumnsOffset);
        WriteUInt32LE(buf, 32, ColumnsSize);
        WriteUInt32LE(buf, 36, RowIndexOffset);
        WriteUInt32LE(buf, 40, RowIndexSize);
        WriteUInt32LE(buf, 44, PayloadOffset);
        WriteUInt32LE(buf, 48, PayloadSize);
        WriteUInt32LE(buf, 52, CrcOffset);
        WriteUInt32LE(buf, 56, Blake3Offset);

        return buf;
    }

    private static uint ReadUInt32LE(byte[] buf, int off)
    {
        return (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
    }

    private static void WriteUInt32LE(byte[] buf, int off, uint val)
    {
        buf[off] = (byte)(val & 0xFF);
        buf[off + 1] = (byte)((val >> 8) & 0xFF);
        buf[off + 2] = (byte)((val >> 16) & 0xFF);
        buf[off + 3] = (byte)((val >> 24) & 0xFF);
    }
}
