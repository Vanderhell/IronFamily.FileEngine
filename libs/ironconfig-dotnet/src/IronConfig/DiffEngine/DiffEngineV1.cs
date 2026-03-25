using System;
using System.Collections.Generic;
using System.Linq;

namespace IronConfig.DiffEngine;

/// <summary>
/// DiffEngine v1: Rsync-style block matching for robust diffing.
/// Handles header/middle inserts better than fixed-chunk DELTA.
/// </summary>
public static class DiffEngineV1
{
    public const int BLOCK_SIZE = 4096;
    public const int MIN_MATCH = 32;
    public const int MAX_OPS_COUNT = 1_000_000;
    public const long MAX_INSERT_TOTAL = 10_000_000_000;

    /// <summary>
    /// Create diff: base -> target using rsync-style block matching.
    /// SPIKE: simple implementation focusing on correctness over speed.
    /// </summary>
    public static byte[] CreateDiffV1(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> targetBytes)
    {
        // Build base index
        var baseIndex = BuildBaseIndex(baseBytes);

        // Scan target for matches
        var ops = new List<DiffOp>();
        int targetPos = 0;
        long totalInsert = 0;

        while (targetPos < targetBytes.Length)
        {
            // Try to find a match at current position
            var match = FindBestMatch(baseBytes, baseIndex, targetBytes, targetPos);

            if (match.Length >= MIN_MATCH)
            {
                // Emit COPY
                ops.Add(new DiffOp { Type = DiffOp.OpType.Copy, BaseOffset = (ulong)match.BaseOffset, Length = (uint)match.Length });
                targetPos += match.Length;
            }
            else
            {
                // Emit INSERT for next byte or run
                int runLen = FindInsertRun(baseBytes, baseIndex, targetBytes, targetPos);
                ops.Add(new DiffOp { Type = DiffOp.OpType.Insert, Data = targetBytes.Slice(targetPos, runLen).ToArray(), Length = (uint)runLen });
                totalInsert += runLen;
                targetPos += runLen;

                // DoS check
                if (totalInsert > MAX_INSERT_TOTAL)
                    throw new InvalidOperationException($"Insert total exceeds limit");
            }

            // Sanity check
            if (ops.Count > MAX_OPS_COUNT)
                throw new InvalidOperationException($"Operation count exceeds limit");
        }

        // Encode to diff format
        return EncodeDiffPack(baseBytes, targetBytes, ops);
    }

    /// <summary>
    /// Apply diff: base + diff -> target.
    /// </summary>
    public static byte[] ApplyDiffV1(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> diffBytes, out DiffEngineError error)
    {
        return DiffPackV1.ApplyDiffPackV1(baseBytes, diffBytes, out error);
    }

    /// <summary>
    /// Build index of base blocks by strong hash.
    /// </summary>
    private static Dictionary<string, List<int>> BuildBaseIndex(ReadOnlySpan<byte> baseBytes)
    {
        var index = new Dictionary<string, List<int>>();

        for (int offset = 0; offset < baseBytes.Length; offset += BLOCK_SIZE)
        {
            int len = Math.Min(BLOCK_SIZE, baseBytes.Length - offset);
            var blockData = baseBytes.Slice(offset, len).ToArray();
            var blockHash = ComputeBlockHash(blockData);

            if (!index.ContainsKey(blockHash))
                index[blockHash] = new List<int>();

            index[blockHash].Add(offset);
        }

        return index;
    }

    /// <summary>
    /// Find best matching block at targetPos.
    /// </summary>
    private static (int BaseOffset, int Length) FindBestMatch(ReadOnlySpan<byte> baseBytes, Dictionary<string, List<int>> baseIndex, ReadOnlySpan<byte> targetBytes, int targetPos)
    {
        int remainingTarget = targetBytes.Length - targetPos;
        if (remainingTarget < MIN_MATCH)
            return (-1, 0);

        // Compute hash of candidate block
        int blockLen = Math.Min(BLOCK_SIZE, remainingTarget);
        var blockData = targetBytes.Slice(targetPos, blockLen).ToArray();
        var blockHash = ComputeBlockHash(blockData);

        // Look up in base index
        if (!baseIndex.TryGetValue(blockHash, out var baseOffsets))
            return (-1, 0);

        // Try each candidate (deterministic: lowest offset first)
        var sorted = baseOffsets.OrderBy(o => o).ToList();
        foreach (int baseOffset in sorted)
        {
            // Verify actual bytes and extend match
            int matchLen = 0;
            while (baseOffset + matchLen < baseBytes.Length &&
                   targetPos + matchLen < targetBytes.Length &&
                   baseBytes[baseOffset + matchLen] == targetBytes[targetPos + matchLen])
            {
                matchLen++;
            }

            if (matchLen >= MIN_MATCH)
                return (baseOffset, matchLen);
        }

        return (-1, 0);
    }

    /// <summary>
    /// Find extent of bytes to INSERT before next potential match.
    /// Lookahead to find next match boundary (greedy approach).
    /// </summary>
    private static int FindInsertRun(ReadOnlySpan<byte> baseBytes, Dictionary<string, List<int>> baseIndex, ReadOnlySpan<byte> targetBytes, int targetPos)
    {
        // Lookahead: scan forward looking for next match
        // This avoids creating massive INSERT ops for unmatched regions
        int lookaheadPos = targetPos + 1;
        while (lookaheadPos < targetBytes.Length)
        {
            var match = FindBestMatch(baseBytes, baseIndex, targetBytes, lookaheadPos);
            if (match.Length >= MIN_MATCH)
            {
                // Found a match, insert up to this position
                return lookaheadPos - targetPos;
            }
            lookaheadPos++;

            // Limit lookahead to avoid scanning entire file for single byte
            if (lookaheadPos - targetPos > Math.Min(BLOCK_SIZE * 2, targetBytes.Length / 4))
                break;
        }

        // No match found in lookahead: insert remainder of target
        return targetBytes.Length - targetPos;
    }

    /// <summary>
    /// Compute strong hash of block data (BLAKE3-256, first 32 bytes, hex).
    /// </summary>
    private static string ComputeBlockHash(byte[] blockData)
    {
        using var hasher = Blake3.Hasher.New();
        hasher.Update(blockData);
        var hash = hasher.Finalize();
        return Convert.ToHexString(hash.AsSpan().Slice(0, 16).ToArray());  // Use first 16 bytes
    }

    /// <summary>
    /// Encode diff pack (COPY/INSERT ops).
    /// </summary>
    private static byte[] EncodeDiffPack(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> targetBytes, List<DiffOp> ops)
    {
        var baseHash = Blake3.Hasher.Hash(baseBytes.ToArray()).AsSpan().Slice(0, 32).ToArray();
        var targetHash = Blake3.Hasher.Hash(targetBytes.ToArray()).AsSpan().Slice(0, 32).ToArray();

        var result = new List<byte>();

        // Header: IFDIFF01 magic
        result.AddRange(System.Text.Encoding.ASCII.GetBytes("IFDIFF01"));

        // Version: u32 = 1
        result.AddRange(System.BitConverter.GetBytes(1u));

        // Base hash: 32 bytes
        result.AddRange(baseHash);

        // Target hash: 32 bytes
        result.AddRange(targetHash);

        // Encode operations
        const byte OP_COPY = 0;
        const byte OP_INSERT = 1;

        foreach (var op in ops)
        {
            if (op.Type == DiffOp.OpType.Copy)
            {
                result.Add(OP_COPY);
                result.AddRange(System.BitConverter.GetBytes(op.BaseOffset));
                result.AddRange(System.BitConverter.GetBytes(op.Length));
            }
            else if (op.Type == DiffOp.OpType.Insert)
            {
                result.Add(OP_INSERT);
                result.AddRange(System.BitConverter.GetBytes(op.Length));
                result.AddRange(op.Data);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Diff operation (internal).
    /// </summary>
    private class DiffOp
    {
        public enum OpType { Copy, Insert }
        public OpType Type { get; set; }
        public ulong BaseOffset { get; set; }
        public uint Length { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}
