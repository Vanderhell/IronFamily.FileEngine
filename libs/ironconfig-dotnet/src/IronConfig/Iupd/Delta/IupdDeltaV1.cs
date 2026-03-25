using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronConfig.Crypto;

namespace IronConfig.Iupd.Delta;

/// <summary>
/// IUPD DELTA v1: Fixed-chunk deterministic delta compression
///
/// Algorithm: Compares baseBytes and targetBytes in fixed 4096-byte chunks.
/// Generates compact delta that contains only changed chunks.
/// Completely deterministic (same inputs → identical byte output).
///
/// Format: Magic + Header + Sorted Entries
/// - Magic: "IUPDDEL1" (8 bytes)
/// - Header: Version, ChunkSize, Lengths, Hashes, EntryCount (96 bytes fixed)
/// - Entries: Sorted by ChunkIndex ascending (each contains index, length, data)
///
/// Fail-Closed Design:
/// - Base hash verified before applying
/// - Target hash verified after applying
/// - No silent corruption possible
/// </summary>
public static class IupdDeltaV1
{
    private const string MAGIC = "IUPDDEL1";
    private const int HEADER_SIZE = 96;
    private const uint VERSION = 1;
    private const uint CHUNK_SIZE = 4096;

    /// <summary>
    /// Create deterministic delta from baseBytes to targetBytes
    /// </summary>
    public static byte[] CreateDeltaV1(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> targetBytes)
    {
        // Compute hashes
        byte[] baseHash = new byte[32];
        Blake3Ieee.Compute(baseBytes, baseHash);

        byte[] targetHash = new byte[32];
        Blake3Ieee.Compute(targetBytes, targetHash);

        // Find changed chunks
        var changedChunks = new List<(uint ChunkIndex, int DataLen, byte[] Data)>();

        int baseChunkCount = (int)Math.Ceiling(baseBytes.Length / (double)CHUNK_SIZE);
        int targetChunkCount = (int)Math.Ceiling(targetBytes.Length / (double)CHUNK_SIZE);
        int maxChunkCount = Math.Max(baseChunkCount, targetChunkCount);

        for (uint chunkIdx = 0; chunkIdx < maxChunkCount; chunkIdx++)
        {
            ReadOnlySpan<byte> baseChunk = GetChunk(baseBytes, chunkIdx, padWithZeros: true);
            ReadOnlySpan<byte> targetChunk = GetChunk(targetBytes, chunkIdx, padWithZeros: true);

            if (!baseChunk.SequenceEqual(targetChunk))
            {
                int actualLen = GetActualChunkLength(targetBytes, chunkIdx);
                byte[] chunkData = new byte[actualLen];
                ReadOnlySpan<byte> targetChunkData = GetChunk(targetBytes, chunkIdx, padWithZeros: false);
                targetChunkData.Slice(0, actualLen).CopyTo(chunkData);
                changedChunks.Add((chunkIdx, actualLen, chunkData));
            }
        }

        // Sort by chunk index (deterministic)
        changedChunks.Sort((a, b) => a.ChunkIndex.CompareTo(b.ChunkIndex));

        // Build delta file
        using var ms = new MemoryStream();

        // Write header
        byte[] headerData = new byte[HEADER_SIZE];
        int offset = 0;

        // Magic "IUPDDEL1"
        var magicBytes = System.Text.Encoding.ASCII.GetBytes(MAGIC);
        magicBytes.CopyTo(headerData, offset);
        offset += 8;

        // Version (u32 LE)
        WriteUInt32LE(headerData, offset, VERSION);
        offset += 4;

        // ChunkSize (u32 LE)
        WriteUInt32LE(headerData, offset, CHUNK_SIZE);
        offset += 4;

        // TargetLength (u64 LE)
        WriteUInt64LE(headerData, offset, (ulong)targetBytes.Length);
        offset += 8;

        // BaseHash (32 bytes)
        baseHash.CopyTo(headerData, offset);
        offset += 32;

        // TargetHash (32 bytes)
        targetHash.CopyTo(headerData, offset);
        offset += 32;

        // EntryCount (u32 LE)
        WriteUInt32LE(headerData, offset, (uint)changedChunks.Count);
        offset += 4;

        // Reserved (u32 LE = 0)
        WriteUInt32LE(headerData, offset, 0);
        offset += 4;

        ms.Write(headerData);

        // Write entries
        foreach (var (chunkIdx, dataLen, data) in changedChunks)
        {
            byte[] entryHeader = new byte[8];
            WriteUInt32LE(entryHeader, 0, chunkIdx);
            WriteUInt32LE(entryHeader, 4, (uint)dataLen);
            ms.Write(entryHeader);
            ms.Write(data, 0, dataLen);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Apply delta to baseBytes, returning targetBytes
    /// Fail-closed: verifies base hash and target hash
    /// </summary>
    public static byte[] ApplyDeltaV1(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> deltaBytes, out IupdError error)
    {
        error = IupdError.Ok;

        // Validate minimum size
        if (deltaBytes.Length < HEADER_SIZE)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 0, "Delta file too small for header");
            return null;
        }

        // Parse header
        int offset = 0;

        // Magic
        string magic = System.Text.Encoding.ASCII.GetString(deltaBytes.Slice(offset, 8));
        offset += 8;
        if (magic != MAGIC)
        {
            error = new IupdError(IupdErrorCode.DeltaMagicMismatch, 0, $"Magic mismatch: expected {MAGIC}, got {magic}");
            return null;
        }

        // Version
        uint version = ReadUInt32LE(deltaBytes, offset);
        offset += 4;
        if (version != VERSION)
        {
            error = new IupdError(IupdErrorCode.DeltaVersionUnsupported, 8, $"Version unsupported: {version}");
            return null;
        }

        // ChunkSize (must be CHUNK_SIZE)
        uint chunkSize = ReadUInt32LE(deltaBytes, offset);
        offset += 4;
        if (chunkSize != CHUNK_SIZE)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 12, $"ChunkSize mismatch: expected {CHUNK_SIZE}, got {chunkSize}");
            return null;
        }

        // TargetLength
        ulong targetLength = ReadUInt64LE(deltaBytes, offset);
        offset += 8;

        // BaseHash
        byte[] baseHash = new byte[32];
        deltaBytes.Slice(offset, 32).CopyTo(baseHash);
        byte[] computedBaseHash = new byte[32];
        Blake3Ieee.Compute(baseBytes, computedBaseHash);
        if (!baseHash.SequenceEqual(computedBaseHash))
        {
            error = new IupdError(IupdErrorCode.DeltaBaseHashMismatch, 24, "Base hash mismatch (base data differs)");
            return null;
        }
        offset += 32;

        // TargetHash
        byte[] targetHash = new byte[32];
        deltaBytes.Slice(offset, 32).CopyTo(targetHash);
        offset += 32;

        // EntryCount
        uint entryCount = ReadUInt32LE(deltaBytes, offset);
        offset += 4;

        // Reserved
        uint reserved = ReadUInt32LE(deltaBytes, offset);
        offset += 4;
        if (reserved != 0)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 92, "Reserved field must be zero");
            return null;
        }

        // Create result buffer
        byte[] result = new byte[targetLength];

        // Copy base to result initially
        int copyLen = Math.Min((int)targetLength, baseBytes.Length);
        baseBytes.Slice(0, copyLen).CopyTo(result.AsSpan(0, copyLen));

        // Parse and apply entries
        var changedIndices = new HashSet<uint>();
        offset = HEADER_SIZE;

        for (uint i = 0; i < entryCount; i++)
        {
            if (offset + 8 > deltaBytes.Length)
            {
                error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)offset, "Truncated entry header");
                return null;
            }

            uint chunkIdx = ReadUInt32LE(deltaBytes, offset);
            offset += 4;
            uint dataLen = ReadUInt32LE(deltaBytes, offset);
            offset += 4;

            // Validate
            if (dataLen == 0 || dataLen > CHUNK_SIZE)
            {
                error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)(offset - 4), $"Invalid DataLen: {dataLen}");
                return null;
            }

            ulong resultOffset = chunkIdx * CHUNK_SIZE;
            if (resultOffset + dataLen > targetLength)
            {
                error = new IupdError(IupdErrorCode.DeltaEntryOutOfRange, (ulong)(offset - 8),
                    $"Entry chunk {chunkIdx} DataLen {dataLen} exceeds target length");
                return null;
            }

            // Copy entry data
            if (offset + dataLen > deltaBytes.Length)
            {
                error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)offset, "Truncated entry data");
                return null;
            }

            deltaBytes.Slice(offset, (int)dataLen).CopyTo(result.AsSpan((int)resultOffset));
            offset += (int)dataLen;

            changedIndices.Add(chunkIdx);
        }

        // Verify result hash
        byte[] resultHash = new byte[32];
        Blake3Ieee.Compute(result, resultHash);
        if (!resultHash.SequenceEqual(targetHash))
        {
            error = new IupdError(IupdErrorCode.DeltaTargetHashMismatch, (ulong)HEADER_SIZE + 32,
                "Result hash mismatch (data corruption or incorrect delta)");
            return null;
        }

        return result;
    }

    // Helper: Get chunk at index, optionally padded with zeros
    private static ReadOnlySpan<byte> GetChunk(ReadOnlySpan<byte> data, uint chunkIdx, bool padWithZeros)
    {
        ulong offset = chunkIdx * CHUNK_SIZE;
        if (offset >= (ulong)data.Length)
        {
            // Chunk entirely beyond end
            if (padWithZeros)
            {
                return new byte[CHUNK_SIZE]; // Implicitly zeros
            }
            return ReadOnlySpan<byte>.Empty;
        }

        int available = (int)Math.Min((ulong)data.Length - offset, CHUNK_SIZE);
        var chunk = data.Slice((int)offset, available);

        if (!padWithZeros || available == CHUNK_SIZE)
            return chunk;

        // Need to pad
        byte[] padded = new byte[CHUNK_SIZE];
        chunk.CopyTo(padded);
        return padded;
    }

    // Helper: Get actual unpadded chunk length
    private static int GetActualChunkLength(ReadOnlySpan<byte> data, uint chunkIdx)
    {
        ulong offset = chunkIdx * CHUNK_SIZE;
        if (offset >= (ulong)data.Length)
            return 0;

        ulong remaining = (ulong)data.Length - offset;
        return (int)Math.Min(remaining, CHUNK_SIZE);
    }

    // Little-endian helpers
    private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteUInt64LE(byte[] buffer, int offset, ulong value)
    {
        WriteUInt32LE(buffer, offset, (uint)(value & 0xFFFFFFFF));
        WriteUInt32LE(buffer, offset + 4, (uint)((value >> 32) & 0xFFFFFFFF));
    }

    private static uint ReadUInt32LE(ReadOnlySpan<byte> buffer, int offset)
    {
        return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
    }

    private static ulong ReadUInt64LE(ReadOnlySpan<byte> buffer, int offset)
    {
        ulong lo = ReadUInt32LE(buffer, offset);
        ulong hi = ReadUInt32LE(buffer, offset + 4);
        return lo | (hi << 32);
    }
}
