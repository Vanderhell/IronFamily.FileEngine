using System;

namespace IronConfig.Icxs;

/// <summary>
/// ICXS file header (64 bytes fixed)
/// </summary>
public struct IcxsHeader
{
    public const uint HEADER_SIZE = 64;
    public const string MAGIC = "ICXS";

    public byte[] Magic { get; set; } // 4 bytes: "ICXS"
    public byte Version { get; set; }
    public byte Flags { get; set; }
    public byte[] SchemaHash { get; set; } // 16 bytes (SHA-256 first half)
    public uint SchemaBlockOffset { get; set; }
    public uint DataBlockOffset { get; set; }
    public uint CrcOffset { get; set; }

    public bool HasCrc => (Flags & 0x01) != 0;

    /// <summary>
    /// Try to parse ICXS header from buffer
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out IcxsHeader header)
    {
        header = default;

        if (buffer.Length < HEADER_SIZE)
            return false;

        // Check magic
        var magic = buffer.Slice(0, 4);
        if (magic[0] != 'I' || magic[1] != 'C' || magic[2] != 'X' || magic[3] != 'S')
            return false;

        // Read header
        header.Magic = new byte[4];
        magic.CopyTo(header.Magic);

        header.Version = buffer[4];
        header.Flags = buffer[5];

        header.SchemaHash = new byte[16];
        buffer.Slice(8, 16).CopyTo(header.SchemaHash);

        header.SchemaBlockOffset = ReadUInt32LE(buffer, 24);
        header.DataBlockOffset = ReadUInt32LE(buffer, 28);
        header.CrcOffset = ReadUInt32LE(buffer, 32);

        return true;
    }

    /// <summary>
    /// Validate header offsets
    /// </summary>
    public bool ValidateOffsets(uint fileSize)
    {
        if (SchemaBlockOffset >= fileSize)
            return false;
        if (DataBlockOffset >= fileSize)
            return false;
        if (HasCrc && (CrcOffset == 0 || CrcOffset >= fileSize))
            return false;
        if (SchemaBlockOffset >= DataBlockOffset)
            return false;
        return true;
    }

    private static uint ReadUInt32LE(ReadOnlySpan<byte> buffer, int offset)
    {
        return (uint)(
            buffer[offset] |
            (buffer[offset + 1] << 8) |
            (buffer[offset + 2] << 16) |
            (buffer[offset + 3] << 24)
        );
    }

    /// <summary>
    /// Serialize header to bytes (64 bytes)
    /// </summary>
    public byte[] Serialize()
    {
        var data = new byte[HEADER_SIZE];
        Array.Copy(Magic, 0, data, 0, 4);
        data[4] = Version;
        data[5] = Flags;
        Array.Copy(SchemaHash, 0, data, 8, 16);
        WriteUInt32LE(data, 24, SchemaBlockOffset);
        WriteUInt32LE(data, 28, DataBlockOffset);
        WriteUInt32LE(data, 32, CrcOffset);
        return data;
    }

    private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
