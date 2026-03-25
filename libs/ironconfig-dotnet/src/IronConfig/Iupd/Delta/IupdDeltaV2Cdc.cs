using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Blake3;

namespace IronConfig.Iupd.Delta;

/// <summary>
/// IRONDEL2 - Delta v2 with Content-Defined Chunking (CDC) using Rabin fingerprints.
/// Deterministic COPY/LIT operations with full fail-closed validation.
/// Spec: specs/IRONDEL2_SPEC_MIN.md
/// </summary>
public static class IronDel2
{
    private const string MAGIC = "IRONDEL2";
    private const byte VERSION = 0x01;
    private const byte FLAGS = 0x00;
    private const ushort RESERVED = 0x0000;
    private const int HASH_SIZE = 32;  // BLAKE3-256
    private const byte OP_COPY = 0x01;
    private const byte OP_LIT = 0x02;

    // DoS limits (fail-closed)
    private const uint MAX_OPS = 20_000_000;
    private const long MAX_COPY_LEN = 1_073_741_824;  // 1GB

    // Header size (fixed)
    private const int HEADER_SIZE = 100;
    private static IronDel2CreateDiagnostics _lastCreateDiagnostics = IronDel2CreateDiagnostics.Empty;

    public static IronDel2CreateDiagnostics LastCreateDiagnostics => _lastCreateDiagnostics;

    /// <summary>
    /// Create IRONDEL2 patch using CDC.
    /// Algorithm: CDC chunk target, match against base, encode as COPY/LIT ops.
    /// DETERMINISTIC: same inputs produce identical patch bytes.
    /// </summary>
    public static byte[] Create(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> targetBytes)
    {
        var totalSw = Stopwatch.StartNew();
        var phaseSw = Stopwatch.StartNew();

        // Compute hashes for verification
        var baseHash = Hasher.Hash(baseBytes).AsSpan().Slice(0, HASH_SIZE).ToArray();
        var targetHash = Hasher.Hash(targetBytes).AsSpan().Slice(0, HASH_SIZE).ToArray();
        long hashUs = ElapsedUs(phaseSw);

        // CDC chunk target
        phaseSw.Restart();
        var targetChunks = CdcChunker.ComputeChunkBoundaries(targetBytes);
        long targetChunkingUs = ElapsedUs(phaseSw);

        // Build base chunk index for matching (hash → list of byte positions)
        phaseSw.Restart();
        var baseChunkIndex = new Dictionary<ulong, List<int>>();
        var baseChunks = CdcChunker.ComputeChunkBoundaries(baseBytes);
        long baseChunkingUs = ElapsedUs(phaseSw);

        phaseSw.Restart();
        foreach (var (offset, len) in baseChunks)
        {
            var chunkHash = ComputeChunkHash(baseBytes.Slice(offset, len));
            if (!baseChunkIndex.TryGetValue(chunkHash, out var offsets))
            {
                offsets = new List<int>();
                baseChunkIndex[chunkHash] = offsets;
            }
            offsets.Add(offset);
        }
        long baseIndexUs = ElapsedUs(phaseSw);

        // Generate ops
        phaseSw.Restart();
        var ops = new List<DeltaOp>();

        foreach (var (chunkOffset, chunkLen) in targetChunks)
        {
            var chunkData = targetBytes.Slice(chunkOffset, chunkLen);
            var chunkHash = ComputeChunkHash(chunkData);

            // Try to find matching chunk in base (deterministic: choose lowest offset)
            if (baseChunkIndex.TryGetValue(chunkHash, out var baseOffsets))
            {
                // Verify first match (actual byte comparison)
                int matchOffset = -1;
                foreach (var bOffset in baseOffsets)
                {
                    if (bOffset + chunkLen <= baseBytes.Length)
                    {
                        bool matches = true;
                        for (int i = 0; i < chunkLen; i++)
                        {
                            if (baseBytes[bOffset + i] != chunkData[i])
                            {
                                matches = false;
                                break;
                            }
                        }
                        if (matches)
                        {
                            matchOffset = bOffset;
                            break;
                        }
                    }
                }

                if (matchOffset >= 0)
                {
                    ops.Add(new DeltaOp { Type = OP_COPY, BaseOffset = (ulong)matchOffset, Length = (uint)chunkLen });
                    continue;
                }
            }

            // No match: LIT (literal data)
            ops.Add(new DeltaOp { Type = OP_LIT, Data = chunkData.ToArray(), Length = (uint)chunkLen });
        }

        long matchingUs = ElapsedUs(phaseSw);

        // Merge adjacent COPY ops with contiguous ranges (optional optimization)
        phaseSw.Restart();
        ops = MergeOps(ops);
        long mergeUs = ElapsedUs(phaseSw);

        // Encode IRONDEL2 patch file
        phaseSw.Restart();
        var patch = EncodePatch(baseBytes.Length, targetBytes.Length, baseHash, targetHash, ops);
        long encodeUs = ElapsedUs(phaseSw);

        _lastCreateDiagnostics = new IronDel2CreateDiagnostics(
            baseBytes.Length,
            targetBytes.Length,
            baseChunks.Count,
            targetChunks.Count,
            ops.Count,
            hashUs,
            baseChunkingUs,
            targetChunkingUs,
            baseIndexUs,
            matchingUs,
            mergeUs,
            encodeUs,
            ElapsedUs(totalSw));

        return patch;
    }

    /// <summary>
    /// Apply IRONDEL2 patch using CDC.
    /// DETERMINISTIC: identical input produces identical output.
    /// Fail-closed: validates header CRC32, base hash, target hash, bounds.
    /// </summary>
    public static byte[] Apply(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> patchBytes, out IupdError error)
    {
        error = IupdError.Ok;

        // Validate minimum header size
        if (patchBytes.Length < HEADER_SIZE)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 0, $"Patch too small for header ({patchBytes.Length} < {HEADER_SIZE})");
            return Array.Empty<byte>();
        }

        // Parse and validate header
        string magic = Encoding.ASCII.GetString(patchBytes.Slice(0, 8).ToArray());
        if (magic != MAGIC)
        {
            error = new IupdError(IupdErrorCode.DeltaMagicMismatch, 0, $"Magic mismatch: expected {MAGIC}, got {magic}");
            return Array.Empty<byte>();
        }

        byte version = patchBytes[8];
        if (version != VERSION)
        {
            error = new IupdError(IupdErrorCode.DeltaVersionUnsupported, 8, $"Version unsupported: 0x{version:X2}");
            return Array.Empty<byte>();
        }

        byte flags = patchBytes[9];
        if (flags != FLAGS)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 9, $"Flags invalid: expected 0x{FLAGS:X2}, got 0x{flags:X2}");
            return Array.Empty<byte>();
        }

        ushort reserved = BitConverter.ToUInt16(patchBytes.Slice(10, 2));
        if (reserved != RESERVED)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 10, $"Reserved invalid: expected 0x{RESERVED:X4}");
            return Array.Empty<byte>();
        }

        ulong baseLen = BitConverter.ToUInt64(patchBytes.Slice(12, 8));
        if (baseLen != (ulong)baseBytes.Length)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 12, $"Base length mismatch: expected {baseLen}, got {baseBytes.Length}");
            return Array.Empty<byte>();
        }

        ulong targetLen = BitConverter.ToUInt64(patchBytes.Slice(20, 8));

        var patchBaseHash = patchBytes.Slice(28, HASH_SIZE).ToArray();
        var patchTargetHash = patchBytes.Slice(60, HASH_SIZE).ToArray();

        uint opCount = BitConverter.ToUInt32(patchBytes.Slice(92, 4));
        if (opCount == 0 || opCount > MAX_OPS)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 92, $"Op count invalid: {opCount}");
            return Array.Empty<byte>();
        }

        uint headerCrc32 = BitConverter.ToUInt32(patchBytes.Slice(96, 4));

        // Validate header CRC32
        byte[] headerForCrc = patchBytes.Slice(0, 96).ToArray();
        uint computedCrc = ComputeCrc32(headerForCrc);
        if (computedCrc != headerCrc32)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, 96, $"Header CRC32 mismatch");
            return Array.Empty<byte>();
        }

        // Verify base hash
        var computedBaseHash = Hasher.Hash(baseBytes).AsSpan().Slice(0, HASH_SIZE).ToArray();
        if (!BytesEqual(patchBaseHash, computedBaseHash))
        {
            error = new IupdError(IupdErrorCode.DeltaBaseHashMismatch, 28, "Base hash mismatch");
            return Array.Empty<byte>();
        }

        // Decode and execute ops
        var result = new List<byte>((int)targetLen);
        int opOffset = HEADER_SIZE;
        uint opsProcessed = 0;

        while (opsProcessed < opCount && opOffset < patchBytes.Length)
        {
            if (opOffset + 1 > patchBytes.Length)
            {
                error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)opOffset, "Truncated opcode");
                return Array.Empty<byte>();
            }

            byte opCode = patchBytes[opOffset];
            opOffset++;

            if (opCode == OP_COPY)
            {
                if (opOffset + 12 > patchBytes.Length)  // 8 (baseOffset) + 4 (length)
                {
                    error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)opOffset, "Truncated COPY operation");
                    return Array.Empty<byte>();
                }

                ulong copyOffset = BitConverter.ToUInt64(patchBytes.Slice(opOffset, 8));
                opOffset += 8;
                uint copyLen = BitConverter.ToUInt32(patchBytes.Slice(opOffset, 4));
                opOffset += 4;

                // Validate bounds
                if (copyOffset + copyLen > baseLen || copyLen > MAX_COPY_LEN)
                {
                    error = new IupdError(IupdErrorCode.DeltaEntryOutOfRange, copyOffset, "COPY operation out of range");
                    return Array.Empty<byte>();
                }

                for (uint i = 0; i < copyLen; i++)
                    result.Add(baseBytes[(int)(copyOffset + i)]);
            }
            else if (opCode == OP_LIT)
            {
                if (opOffset + 4 > patchBytes.Length)
                {
                    error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)opOffset, "Truncated LIT length");
                    return Array.Empty<byte>();
                }

                uint litLen = BitConverter.ToUInt32(patchBytes.Slice(opOffset, 4));
                opOffset += 4;

                // Validate DoS limits
                if (litLen > targetLen || result.Count + litLen > (int)targetLen)
                {
                    error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)opOffset, "LIT length exceeds target");
                    return Array.Empty<byte>();
                }

                if (opOffset + litLen > patchBytes.Length)
                {
                    error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)opOffset, "Truncated LIT data");
                    return Array.Empty<byte>();
                }

                result.AddRange(patchBytes.Slice(opOffset, (int)litLen).ToArray());
                opOffset += (int)litLen;
            }
            else
            {
                error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)opOffset - 1, $"Invalid opcode: 0x{opCode:X2}");
                return Array.Empty<byte>();
            }

            opsProcessed++;
        }

        // Validate final output size
        if ((ulong)result.Count != targetLen)
        {
            error = new IupdError(IupdErrorCode.DeltaMalformed, (ulong)result.Count,
                $"Output size mismatch: expected {targetLen}, got {result.Count}");
            return Array.Empty<byte>();
        }

        var resultArray = result.ToArray();

        // Verify target hash
        var computedTargetHash = Hasher.Hash(resultArray).AsSpan().Slice(0, HASH_SIZE).ToArray();
        if (!BytesEqual(patchTargetHash, computedTargetHash))
        {
            error = new IupdError(IupdErrorCode.DeltaTargetHashMismatch, 60, "Target hash mismatch");
            return Array.Empty<byte>();
        }

        return resultArray;
    }

    /// <summary>
    /// Compute deterministic lightweight fingerprint for chunk matching.
    /// This is only an index key: matches are still verified byte-by-byte.
    /// Full BLAKE3 remains in the patch header for base/target validation.
    /// </summary>
    private static ulong ComputeChunkHash(ReadOnlySpan<byte> chunkData)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;

        ulong hash = fnvOffset ^ (ulong)chunkData.Length;
        for (int i = 0; i < chunkData.Length; i++)
        {
            hash ^= chunkData[i];
            hash *= fnvPrime;
        }

        // Mix length again to reduce collisions for similar prefixes.
        hash ^= (ulong)chunkData.Length * 0x9E3779B185EBCA87UL;
        return hash;
    }

    /// <summary>
    /// Merge adjacent operations for size optimization.
    /// Adjacent COPY with contiguous base offsets → merge
    /// Adjacent LIT → merge
    /// </summary>
    private static List<DeltaOp> MergeOps(List<DeltaOp> ops)
    {
        if (ops.Count <= 1)
            return ops;

        var merged = new List<DeltaOp>();
        var current = ops[0];

        for (int i = 1; i < ops.Count; i++)
        {
            var next = ops[i];

            // Merge adjacent COPY with contiguous base offsets
            if (current.Type == OP_COPY && next.Type == OP_COPY &&
                current.BaseOffset + current.Length == next.BaseOffset)
            {
                current.Length += next.Length;
                continue;
            }

            // Merge adjacent LIT
            if (current.Type == OP_LIT && next.Type == OP_LIT)
            {
                current.Data = ConcatBytes(current.Data, next.Data);
                current.Length += next.Length;
                continue;
            }

            // Cannot merge, save current and move to next
            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return merged;
    }

    private static byte[] ConcatBytes(byte[] left, byte[] right)
    {
        var combined = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, combined, 0, left.Length);
        Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);
        return combined;
    }

    private static long ElapsedUs(Stopwatch sw) => (long)(sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency);

    /// <summary>
    /// Encode IRONDEL2 patch file (deterministic binary format).
    /// Header: 100 bytes (fixed) with CRC32
    /// Ops: stream of COPY/LIT operations
    /// </summary>
    private static byte[] EncodePatch(int baseLenVal, int targetLenVal, byte[] baseHash, byte[] targetHash, List<DeltaOp> ops)
    {
        var result = new List<byte>();

        // Build header (100 bytes)
        var header = new byte[HEADER_SIZE];
        int offset = 0;

        // Magic (8 bytes)
        Encoding.ASCII.GetBytes(MAGIC).CopyTo(header, offset);
        offset += 8;

        // Version (1 byte)
        header[offset++] = VERSION;

        // Flags (1 byte)
        header[offset++] = FLAGS;

        // Reserved (2 bytes)
        BitConverter.GetBytes(RESERVED).CopyTo(header, offset);
        offset += 2;

        // Base length (8 bytes)
        BitConverter.GetBytes((ulong)baseLenVal).CopyTo(header, offset);
        offset += 8;

        // Target length (8 bytes)
        BitConverter.GetBytes((ulong)targetLenVal).CopyTo(header, offset);
        offset += 8;

        // Base hash (32 bytes)
        baseHash.CopyTo(header, offset);
        offset += 32;

        // Target hash (32 bytes)
        targetHash.CopyTo(header, offset);
        offset += 32;

        // Op count (4 bytes)
        BitConverter.GetBytes((uint)ops.Count).CopyTo(header, offset);
        offset += 4;

        // Header CRC32 (4 bytes) - computed after setting to 0
        // Leave offset 96-99 as 0 for CRC computation
        uint crc32 = ComputeCrc32(header.AsSpan().Slice(0, 96).ToArray());
        BitConverter.GetBytes(crc32).CopyTo(header, 96);

        result.AddRange(header);

        // Encode ops
        foreach (var op in ops)
        {
            result.Add(op.Type);

            if (op.Type == OP_COPY)
            {
                result.AddRange(BitConverter.GetBytes(op.BaseOffset));
                result.AddRange(BitConverter.GetBytes(op.Length));
            }
            else if (op.Type == OP_LIT)
            {
                result.AddRange(BitConverter.GetBytes(op.Length));
                result.AddRange(op.Data);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Compute CRC32 checksum (ISO polynomial 0x04C11DB7).
    /// </summary>
    private static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
        }
        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>
    /// Constant-time byte array comparison (prevents timing attacks).
    /// </summary>
    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;
        int result = 0;
        for (int i = 0; i < a.Length; i++)
            result |= a[i] ^ b[i];
        return result == 0;
    }

    /// <summary>
    /// Delta operation (internal).
    /// </summary>
    private class DeltaOp
    {
        public byte Type { get; set; }
        public ulong BaseOffset { get; set; }
        public uint Length { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}

public readonly record struct IronDel2CreateDiagnostics(
    int BaseBytes,
    int TargetBytes,
    int BaseChunkCount,
    int TargetChunkCount,
    int OperationCount,
    long HashUs,
    long BaseChunkingUs,
    long TargetChunkingUs,
    long BaseIndexUs,
    long MatchingUs,
    long MergeUs,
    long EncodeUs,
    long TotalUs)
{
    public static IronDel2CreateDiagnostics Empty => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Backwards compatibility wrapper for IronDel2 (old spike name: IupdDeltaV2Cdc).
/// Forwards calls to IronDel2 implementation.
/// </summary>
public static class IupdDeltaV2Cdc
{
    /// <summary>
    /// Create DELTA v2 patch (legacy name, forwards to IronDel2.Create).
    /// </summary>
    public static byte[] CreateDeltaV2(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> targetBytes)
        => IronDel2.Create(baseBytes, targetBytes);

    /// <summary>
    /// Apply DELTA v2 patch (legacy name, forwards to IronDel2.Apply).
    /// </summary>
    public static byte[] ApplyDeltaV2(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> deltaBytes, out IupdError error)
        => IronDel2.Apply(baseBytes, deltaBytes, out error);
}
