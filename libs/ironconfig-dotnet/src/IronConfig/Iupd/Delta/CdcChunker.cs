using System;
using System.Collections.Generic;

namespace IronConfig.Iupd.Delta;

/// <summary>
/// Content-Defined Chunking (CDC) using deterministic Rabin fingerprint rolling hash.
/// SPIKE implementation for DELTA v2.
/// </summary>
public static class CdcChunker
{
    // CDC Parameters (FROZEN for SPIKE - DO NOT CHANGE)
    // These values ensure deterministic chunk boundaries across all platforms

    public const int MIN_CHUNK = 2048;      // Minimum chunk size (bytes)
    public const int AVG_CHUNK = 4096;      // Target average chunk size
    public const int MAX_CHUNK = 8192;      // Maximum chunk size (hard cut)
    public const int WINDOW_SIZE = 48;      // Rabin fingerprint window (bytes)

    // Rabin fingerprint constants (deterministic polynomial)
    private const ulong POLY = 0xC15D213AA4D7A795UL;  // Fixed irreducible polynomial
    private const ulong TARGET = 0x0000F00000000000UL; // Cut target (48 bits set, deterministic)
    private const ulong MASK = 0x0000FFFFFFFFFFFFUL;   // Mask for fingerprint comparison

    /// <summary>
    /// CDC chunk boundary: when (fingerprint &amp; MASK) == TARGET, cut chunk.
    /// Ensures deterministic boundaries regardless of CPU/OS.
    /// </summary>
    private const ulong CUT_THRESHOLD = (TARGET & MASK);
    private static readonly ulong[] OutByteContributions = BuildOutByteContributions();

    /// <summary>
    /// Compute deterministic Rabin fingerprint for a byte window.
    /// Uses fixed polynomial to ensure reproducibility.
    /// </summary>
    private static ulong ComputeRabinHash(ReadOnlySpan<byte> data, int offset, int len)
    {
        if (len == 0)
            return 0;

        ulong hash = 0;
        int maxIdx = Math.Min(offset + len, data.Length);

        for (int i = offset; i < maxIdx; i++)
        {
            hash = ((hash << 1) | (hash >> 63));  // Rotate left 1
            hash ^= (ulong)data[i];               // XOR with byte
            hash = RabinUpdate(hash);
        }

        return hash;
    }

    /// <summary>
    /// Update Rabin hash with polynomial reduction (deterministic).
    /// </summary>
    private static ulong RabinUpdate(ulong hash)
    {
        if ((hash & 0x8000000000000000UL) != 0)
        {
            hash ^= POLY;
        }
        return hash;
    }

    /// <summary>
    /// Rolling window Rabin fingerprint update (deterministic).
    /// Adds new byte, removes old byte from window.
    /// </summary>
    private static ulong RollingHashUpdate(ulong currentHash, byte outByte, byte inByte)
    {
        // Update hash
        ulong newHash = currentHash ^ OutByteContributions[outByte];
        newHash = (newHash << 1) | (newHash >> 63);
        if ((newHash & 0x8000000000000000UL) != 0)
            newHash ^= POLY;
        newHash ^= (ulong)inByte;

        return newHash;
    }

    private static ulong[] BuildOutByteContributions()
    {
        var table = new ulong[256];
        for (int value = 0; value < table.Length; value++)
        {
            ulong contribution = (byte)value;
            for (int i = 0; i < WINDOW_SIZE - 1; i++)
            {
                contribution <<= 1;
                if ((contribution & 0x8000000000000000UL) != 0)
                    contribution ^= POLY;
            }
            table[value] = contribution;
        }

        return table;
    }

    /// <summary>
    /// Compute CDC chunk boundaries for data.
    /// Returns list of (offset, length) tuples representing chunks.
    /// DETERMINISTIC: same input always produces same chunks.
    /// </summary>
    public static List<(int Offset, int Length)> ComputeChunkBoundaries(ReadOnlySpan<byte> data)
    {
        var chunks = new List<(int, int)>();

        if (data.Length == 0)
            return chunks;

        int offset = 0;
        int dataLen = data.Length;

        while (offset < dataLen)
        {
            int chunkStart = offset;
            int chunkLen = 0;
            ulong hash = 0;
            bool foundBoundary = false;

            // Build window for initial hash
            int windowEnd = Math.Min(chunkStart + WINDOW_SIZE, dataLen);
            for (int i = chunkStart; i < windowEnd; i++)
            {
                hash = ((hash << 1) | (hash >> 63));
                hash ^= (ulong)data[i];
                if ((hash & 0x8000000000000000UL) != 0)
                    hash ^= POLY;
                chunkLen++;
            }

            // Rolling hash through data
            while (offset + chunkLen < dataLen && chunkLen < MAX_CHUNK)
            {
                // Check for cut boundary
                if (chunkLen >= MIN_CHUNK && (hash & MASK) == CUT_THRESHOLD)
                {
                    foundBoundary = true;
                    break;
                }

                // Roll hash window
                byte outByte = data[chunkStart + chunkLen - WINDOW_SIZE];
                byte inByte = data[chunkStart + chunkLen];

                hash = RollingHashUpdate(hash, outByte, inByte);
                chunkLen++;
            }

            // Force cut at MAX_CHUNK if no boundary found
            if (!foundBoundary && chunkLen >= MAX_CHUNK)
            {
                chunkLen = MAX_CHUNK;
            }

            chunks.Add((chunkStart, chunkLen));
            offset += chunkLen;
        }

        return chunks;
    }

    /// <summary>
    /// Extract chunk data by boundaries (deterministic).
    /// </summary>
    public static byte[] GetChunkData(ReadOnlySpan<byte> data, int offset, int length)
    {
        var chunk = new byte[length];
        data.Slice(offset, length).CopyTo(chunk);
        return chunk;
    }
}
