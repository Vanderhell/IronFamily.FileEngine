using System;

namespace IronConfig.Icfx;

/// <summary>
/// ICFX header structure (48 bytes, little-endian)
/// </summary>
public struct IcfxHeader
{
    public const int HEADER_SIZE = 48;
    public const uint MAGIC_LE = 0x5846_4349; // "ICFX" in little-endian (0x49='I', 0x43='C', 0x46='F', 0x58='X')

    public uint Magic { get; set; }
    public byte Flags { get; set; }
    public byte Reserved1 { get; set; }
    public ushort HeaderSize { get; set; }
    public uint TotalFileSize { get; set; }
    public uint DictionaryOffset { get; set; }
    public uint VspOffset { get; set; }
    public uint IndexTableOffset { get; set; }
    public uint PayloadOffset { get; set; }
    public uint CrcOffset { get; set; }
    public uint PayloadSize { get; set; }
    public uint DictionarySize { get; set; }
    public uint VspSize { get; set; }
    public uint Reserved2 { get; set; }

    /// <summary>
    /// Parse ICFX header from buffer
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out IcfxHeader header)
    {
        header = default;

        // Minimum header size check
        if (buffer.Length < HEADER_SIZE)
            return false;

        // Parse magic (4 bytes, little-endian)
        header.Magic = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));

        // Verify magic is "ICFX"
        if (header.Magic != MAGIC_LE)
            return false;

        // Parse flags
        header.Flags = buffer[4];

        // Check mandatory flag bits
        if ((header.Flags & 0x01) == 0) // Little-endian flag must be set
            return false;

        // Check reserved flag bits (4-7) must be 0
        if ((header.Flags & 0xF0) != 0)
            return false;

        header.Reserved1 = buffer[5];
        if (header.Reserved1 != 0)
            return false;

        // Parse header size (2 bytes, LE)
        header.HeaderSize = (ushort)(buffer[6] | (buffer[7] << 8));
        if (header.HeaderSize != HEADER_SIZE)
            return false;

        // Parse file size (4 bytes, LE)
        header.TotalFileSize = (uint)(buffer[8] | (buffer[9] << 8) | (buffer[10] << 16) | (buffer[11] << 24));

        // Parse offsets
        header.DictionaryOffset = (uint)(buffer[12] | (buffer[13] << 8) | (buffer[14] << 16) | (buffer[15] << 24));
        header.VspOffset = (uint)(buffer[16] | (buffer[17] << 8) | (buffer[18] << 16) | (buffer[19] << 24));
        header.IndexTableOffset = (uint)(buffer[20] | (buffer[21] << 8) | (buffer[22] << 16) | (buffer[23] << 24));
        header.PayloadOffset = (uint)(buffer[24] | (buffer[25] << 8) | (buffer[26] << 16) | (buffer[27] << 24));
        header.CrcOffset = (uint)(buffer[28] | (buffer[29] << 8) | (buffer[30] << 16) | (buffer[31] << 24));
        header.PayloadSize = (uint)(buffer[32] | (buffer[33] << 8) | (buffer[34] << 16) | (buffer[35] << 24));
        header.DictionarySize = (uint)(buffer[36] | (buffer[37] << 8) | (buffer[38] << 16) | (buffer[39] << 24));
        header.VspSize = (uint)(buffer[40] | (buffer[41] << 8) | (buffer[42] << 16) | (buffer[43] << 24));
        header.Reserved2 = (uint)(buffer[44] | (buffer[45] << 8) | (buffer[46] << 16) | (buffer[47] << 24));

        if (header.Reserved2 != 0)
            return false;

        // Bounds check: file size must match buffer
        if (header.TotalFileSize > (uint)buffer.Length)
            return false;

        // Basic offset sanity checks
        if (header.DictionaryOffset < HEADER_SIZE || header.DictionaryOffset > header.TotalFileSize)
            return false;
        if (header.PayloadOffset < HEADER_SIZE || header.PayloadOffset > header.TotalFileSize)
            return false;

        // VSP offset sanity check (0 if not present)
        if (header.VspOffset != 0 && (header.VspOffset < HEADER_SIZE || header.VspOffset > header.TotalFileSize))
            return false;

        // Index table offset sanity check (0 if not present)
        if (header.IndexTableOffset != 0 && (header.IndexTableOffset < HEADER_SIZE || header.IndexTableOffset > header.TotalFileSize))
            return false;

        // CRC offset sanity check (0 if not present)
        if (header.CrcOffset != 0 && (header.CrcOffset < HEADER_SIZE || header.CrcOffset > header.TotalFileSize))
            return false;

        return true;
    }

    /// <summary>
    /// Check if VSP is present
    /// </summary>
    public bool HasVsp => (Flags & 0x02) != 0;

    /// <summary>
    /// Check if CRC is present
    /// </summary>
    public bool HasCrc => (Flags & 0x04) != 0;

    /// <summary>
    /// Check if index table is present
    /// </summary>
    public bool HasIndexTable => (Flags & 0x08) != 0;

    /// <summary>
    /// Verify offsets don't overlap and are within bounds
    /// </summary>
    public bool ValidateOffsets()
    {
        if (DictionaryOffset + DictionarySize > TotalFileSize)
            return false;
        if (VspOffset != 0 && VspOffset + VspSize > TotalFileSize)
            return false;
        if (PayloadOffset + PayloadSize > TotalFileSize)
            return false;
        if (CrcOffset != 0 && CrcOffset + 4 > TotalFileSize)
            return false;
        return true;
    }
}
