using System;
using System.Collections.Generic;
using System.Linq;

namespace IronConfig.Iupd;

/// <summary>
/// DEPRECATED: Legacy binary delta compression for DELTA profile.
/// Uses simplified binary diff algorithm for incremental updates.
///
/// NOTE: This is the old DELTA_V1 algorithm (0x01).
/// NEW CODE SHOULD USE IupdDeltaV2Cdc (IRONDEL2/0x02) instead, which provides
/// better compression via content-defined chunking.
///
/// This class is retained for backward compatibility only.
///
/// Delta format:
/// - Match: [0xFF][offset:2B LE][length:2B LE] - copy from old
/// - Insert: [0xFE][length:1B][data:N] - insert new bytes
/// - End: [0x00]
/// </summary>
public static class IupdDelta
{
    private const int MIN_MATCH = 8;
    private const int HASH_BITS = 16;
    private const int HASH_SIZE = 1 << HASH_BITS;

    /// <summary>
    /// Create delta from old data to new data
    /// Returns compressed delta (should be significantly smaller for similar data)
    /// </summary>
    public static byte[] CreateDelta(ReadOnlySpan<byte> oldData, ReadOnlySpan<byte> newData)
    {
        if (newData.Length == 0)
            return new byte[] { 0x00 };  // Empty delta

        var output = new System.IO.MemoryStream();
        var hashTable = BuildHashTable(oldData);

        int newPos = 0;
        while (newPos < newData.Length)
        {
            // Try to find a match in old data
            int bestMatchPos = -1;
            int bestMatchLen = 0;

            if (newPos + MIN_MATCH <= newData.Length && oldData.Length >= MIN_MATCH)
            {
                int hash = Hash(newData, newPos);
                if (hashTable.TryGetValue(hash, out var matches))
                {
                    foreach (var oldPos in matches.OrderByDescending(x => x))
                    {
                        int matchLen = GetMatchLength(oldData, newData, oldPos, newPos, 65535);
                        if (matchLen > bestMatchLen && matchLen >= MIN_MATCH)
                        {
                            bestMatchLen = matchLen;
                            bestMatchPos = oldPos;
                        }
                    }
                }
            }

            if (bestMatchLen >= MIN_MATCH)
            {
                // Encode match: [0xFF][offset:2B][length:2B]
                output.WriteByte(0xFF);
                output.WriteByte((byte)(bestMatchPos & 0xFF));
                output.WriteByte((byte)((bestMatchPos >> 8) & 0xFF));
                output.WriteByte((byte)(bestMatchLen & 0xFF));
                output.WriteByte((byte)((bestMatchLen >> 8) & 0xFF));
                newPos += bestMatchLen;
            }
            else
            {
                // Literal - find run of non-matching bytes
                int litStart = newPos;
                while (newPos < newData.Length && newPos - litStart < 254)
                {
                    // Early exit if we find a good match ahead
                    if (newPos + MIN_MATCH <= newData.Length)
                    {
                        int hash = Hash(newData, newPos);
                        if (hashTable.TryGetValue(hash, out var matches) && matches.Count > 0)
                        {
                            break;
                        }
                    }
                    newPos++;
                }

                int litLen = newPos - litStart;
                // Encode literal: [0xFE][length:1B][data:N]
                output.WriteByte(0xFE);
                output.WriteByte((byte)litLen);
                output.Write(newData.Slice(litStart, litLen));
            }
        }

        // End marker
        output.WriteByte(0x00);

        return output.ToArray();
    }

    /// <summary>
    /// Apply delta to reconstruct new data from old data
    /// </summary>
    public static bool TryApplyDelta(ReadOnlySpan<byte> oldData, ReadOnlySpan<byte> delta, out byte[] newData, out string? error)
    {
        newData = Array.Empty<byte>();
        error = null;

        try
        {
            var output = new System.IO.MemoryStream();
            int deltaPos = 0;

            while (deltaPos < delta.Length)
            {
                byte opcode = delta[deltaPos++];

                if (opcode == 0x00)
                {
                    // End marker
                    break;
                }
                else if (opcode == 0xFF)
                {
                    // Match: copy from old data
                    if (deltaPos + 4 > delta.Length)
                    {
                        error = "Unexpected end of delta data (match)";
                        return false;
                    }

                    int offset = delta[deltaPos] | (delta[deltaPos + 1] << 8);
                    int length = delta[deltaPos + 2] | (delta[deltaPos + 3] << 8);
                    deltaPos += 4;

                    if (offset + length > oldData.Length)
                    {
                        error = $"Delta match out of bounds: offset={offset}, length={length}";
                        return false;
                    }

                    output.Write(oldData.Slice(offset, length));
                }
                else if (opcode == 0xFE)
                {
                    // Literal: insert new bytes
                    if (deltaPos + 1 > delta.Length)
                    {
                        error = "Unexpected end of delta data (literal)";
                        return false;
                    }

                    int litLen = delta[deltaPos++];
                    if (deltaPos + litLen > delta.Length)
                    {
                        error = $"Unexpected end of delta data (literal data, need {litLen} bytes)";
                        return false;
                    }

                    output.Write(delta.Slice(deltaPos, litLen));
                    deltaPos += litLen;
                }
                else
                {
                    error = $"Unknown delta opcode: {opcode:X2}";
                    return false;
                }
            }

            newData = output.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Delta application failed: {ex.Message}";
            return false;
        }
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private static Dictionary<int, List<int>> BuildHashTable(ReadOnlySpan<byte> data)
    {
        var table = new Dictionary<int, List<int>>();

        for (int i = 0; i + MIN_MATCH <= data.Length; i++)
        {
            int hash = Hash(data, i);
            if (!table.ContainsKey(hash))
                table[hash] = new List<int>();
            table[hash].Add(i);
        }

        return table;
    }

    private static int Hash(ReadOnlySpan<byte> data, int pos)
    {
        if (pos + 4 > data.Length)
            return 0;

        uint hash = ((uint)data[pos] << 24) |
                    ((uint)data[pos + 1] << 16) |
                    ((uint)data[pos + 2] << 8) |
                    data[pos + 3];

        return (int)((hash * 2654435761U) >> (32 - HASH_BITS)) & (HASH_SIZE - 1);
    }

    private static int GetMatchLength(ReadOnlySpan<byte> oldData, ReadOnlySpan<byte> newData, int oldPos, int newPos, int maxLen)
    {
        int len = 0;
        while (len < maxLen && oldPos + len < oldData.Length && newPos + len < newData.Length && oldData[oldPos + len] == newData[newPos + len])
        {
            len++;
        }
        return len;
    }
}

/// <summary>
/// Helper for working with delta updates
/// </summary>
public class DeltaUpdate
{
    public byte[] OldDataHash { get; set; } = new byte[32];  // BLAKE3
    public byte[] NewDataHash { get; set; } = new byte[32];  // BLAKE3
    public byte[] DeltaData { get; set; } = Array.Empty<byte>();
    public ulong CompressedSize { get; set; }
    public ulong UncompressedSize { get; set; }

    public double CompressionRatio => UncompressedSize > 0 ? (double)CompressedSize / UncompressedSize : 1.0;
}
