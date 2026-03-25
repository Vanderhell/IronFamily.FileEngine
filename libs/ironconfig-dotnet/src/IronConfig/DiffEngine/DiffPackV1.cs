using System;
using System.Collections.Generic;
using System.Text;
using Blake3;

namespace IronConfig.DiffEngine;

/// <summary>
/// DiffPack v1 format: Rsync-style diff with fail-closed semantics.
/// Format: [Magic "IFDIFF01"] [Version 1] [BaseHash32] [TargetHash32] [Ops...]
/// DoS limits: MAX_OPS, MAX_INSERT_BYTES, MAX_PATCH_BYTES guardrails.
/// </summary>
public static class DiffPackV1
{
    private const string MAGIC = "IFDIFF01";
    private const uint VERSION = 1;
    private const int HASH_SIZE = 32;  // BLAKE3-256
    private const byte OP_COPY = 0;
    private const byte OP_INSERT = 1;

    // DoS mitigation limits
    private const int MAX_OPS = 1_000_000;           // Max operations
    private const long MAX_INSERT_BYTES = 256_000_000;  // 256MB max inserted data
    private const long MAX_PATCH_BYTES = 512_000_000;   // 512MB max patch file size

    /// <summary>
    /// Create diff pack (fail-closed: base and target hashes verified).
    /// </summary>
    public static byte[] CreateDiffPackV1(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> targetBytes)
    {
        var baseHash = Hasher.Hash(baseBytes.ToArray()).AsSpan().Slice(0, HASH_SIZE).ToArray();
        var targetHash = Hasher.Hash(targetBytes.ToArray()).AsSpan().Slice(0, HASH_SIZE).ToArray();

        var result = new List<byte>();

        // Header
        result.AddRange(Encoding.ASCII.GetBytes(MAGIC));
        result.AddRange(BitConverter.GetBytes(VERSION));
        result.AddRange(baseHash);
        result.AddRange(targetHash);

        // For foundation spike: simple COPY all (will be replaced with matching algorithm in PHASE 2)
        // Op: INSERT entire target
        result.Add(OP_INSERT);
        result.AddRange(BitConverter.GetBytes((uint)targetBytes.Length));
        result.AddRange(targetBytes.ToArray());

        return result.ToArray();
    }

    /// <summary>
    /// Apply diff pack (fail-closed: verifies both base and target hashes).
    /// </summary>
    public static byte[] ApplyDiffPackV1(ReadOnlySpan<byte> baseBytes, ReadOnlySpan<byte> diffBytes, out DiffEngineError error)
    {
        error = DiffEngineError.Ok;

        // DoS check: patch file size limit
        if (diffBytes.Length > MAX_PATCH_BYTES)
        {
            error = new DiffEngineError(DiffEngineErrorCode.DiffLimitsExceeded, 0, $"Patch size {diffBytes.Length} exceeds limit {MAX_PATCH_BYTES}");
            return Array.Empty<byte>();
        }

        // Validate header
        if (diffBytes.Length < 76)
        {
            error = new DiffEngineError(DiffEngineErrorCode.DiffMalformed, 0, "Diff too small for header");
            return Array.Empty<byte>();
        }

        string magic = Encoding.ASCII.GetString(diffBytes.Slice(0, 8).ToArray());
        if (magic != MAGIC)
        {
            error = new DiffEngineError(DiffEngineErrorCode.DiffMagicMismatch, 0, $"Magic mismatch: {magic}");
            return Array.Empty<byte>();
        }

        uint version = BitConverter.ToUInt32(diffBytes.Slice(8, 4));
        if (version != VERSION)
        {
            error = new DiffEngineError(DiffEngineErrorCode.DiffVersionUnsupported, 8, $"Version {version}");
            return Array.Empty<byte>();
        }

        var baseHash = diffBytes.Slice(12, HASH_SIZE).ToArray();
        var targetHash = diffBytes.Slice(12 + HASH_SIZE, HASH_SIZE).ToArray();

        // Fail-closed: verify base hash BEFORE applying ops
        var computedBaseHash = Hasher.Hash(baseBytes.ToArray()).AsSpan().Slice(0, HASH_SIZE).ToArray();
        if (!ConstantTimeEqual(baseHash, computedBaseHash))
        {
            error = new DiffEngineError(DiffEngineErrorCode.DiffBaseHashMismatch, 12, "Base hash mismatch");
            return Array.Empty<byte>();
        }

        // Execute ops
        var result = new List<byte>();
        int opOffset = 76;
        int opCount = 0;
        long totalInsertBytes = 0;

        while (opOffset < diffBytes.Length)
        {
            if (opOffset + 1 > diffBytes.Length)
            {
                error = new DiffEngineError(DiffEngineErrorCode.DiffMalformed, (ulong)opOffset, "Truncated opcode");
                return Array.Empty<byte>();
            }

            byte opCode = diffBytes[opOffset];
            opOffset++;
            opCount++;

            // DoS check: operation count limit
            if (opCount > MAX_OPS)
            {
                error = new DiffEngineError(DiffEngineErrorCode.DiffLimitsExceeded, (ulong)opOffset, $"Operation count {opCount} exceeds limit {MAX_OPS}");
                return Array.Empty<byte>();
            }

            if (opCode == OP_COPY)
            {
                if (opOffset + 12 > diffBytes.Length)
                {
                    error = new DiffEngineError(DiffEngineErrorCode.DiffMalformed, (ulong)opOffset, "Truncated COPY");
                    return Array.Empty<byte>();
                }

                ulong baseOffset = BitConverter.ToUInt64(diffBytes.Slice(opOffset, 8));
                opOffset += 8;
                uint len = BitConverter.ToUInt32(diffBytes.Slice(opOffset, 4));
                opOffset += 4;

                if (baseOffset + len > (ulong)baseBytes.Length)
                {
                    error = new DiffEngineError(DiffEngineErrorCode.DiffEntryOutOfRange, baseOffset, "COPY out of range");
                    return Array.Empty<byte>();
                }

                for (int i = 0; i < len; i++)
                    result.Add(baseBytes[(int)(baseOffset + (ulong)i)]);
            }
            else if (opCode == OP_INSERT)
            {
                if (opOffset + 4 > diffBytes.Length)
                {
                    error = new DiffEngineError(DiffEngineErrorCode.DiffMalformed, (ulong)opOffset, "Truncated INSERT len");
                    return Array.Empty<byte>();
                }

                uint len = BitConverter.ToUInt32(diffBytes.Slice(opOffset, 4));
                opOffset += 4;

                // DoS check: insert bytes limit
                totalInsertBytes += len;
                if (totalInsertBytes > MAX_INSERT_BYTES)
                {
                    error = new DiffEngineError(DiffEngineErrorCode.DiffLimitsExceeded, (ulong)opOffset, $"Insert bytes {totalInsertBytes} exceeds limit {MAX_INSERT_BYTES}");
                    return Array.Empty<byte>();
                }

                if (opOffset + len > diffBytes.Length)
                {
                    error = new DiffEngineError(DiffEngineErrorCode.DiffMalformed, (ulong)opOffset, "Truncated INSERT data");
                    return Array.Empty<byte>();
                }

                for (int i = 0; i < len; i++)
                    result.Add(diffBytes[opOffset + i]);
                opOffset += (int)len;
            }
            else
            {
                error = new DiffEngineError(DiffEngineErrorCode.DiffMalformed, (ulong)opOffset, $"Invalid opcode {opCode}");
                return Array.Empty<byte>();
            }
        }

        var resultArray = result.ToArray();

        // Fail-closed: verify target hash AFTER ops
        var computedTargetHash = Hasher.Hash(resultArray).AsSpan().Slice(0, HASH_SIZE).ToArray();
        if (!ConstantTimeEqual(targetHash, computedTargetHash))
        {
            error = new DiffEngineError(DiffEngineErrorCode.DiffTargetHashMismatch, 44, "Target hash mismatch");
            return Array.Empty<byte>();
        }

        return resultArray;
    }

    /// <summary>
    /// Constant-time byte array comparison.
    /// </summary>
    private static bool ConstantTimeEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;
        int result = 0;
        for (int i = 0; i < a.Length; i++)
            result |= a[i] ^ b[i];
        return result == 0;
    }
}

/// <summary>
/// Diff engine error codes.
/// </summary>
public enum DiffEngineErrorCode
{
    Ok = 0,
    DiffMagicMismatch = 0x0030,
    DiffVersionUnsupported = 0x0031,
    DiffBaseHashMismatch = 0x0032,
    DiffTargetHashMismatch = 0x0033,
    DiffEntryOutOfRange = 0x0034,
    DiffMalformed = 0x0035,
    DiffLimitsExceeded = 0x0036,  // DoS prevention: ops count, insert bytes, or patch size exceeded
}

/// <summary>
/// Diff engine error with offset context.
/// </summary>
public struct DiffEngineError
{
    public static DiffEngineError Ok => new DiffEngineError(DiffEngineErrorCode.Ok, 0, "");

    public DiffEngineErrorCode Code { get; }
    public ulong Offset { get; }
    public string Message { get; }

    public bool IsOk => Code == DiffEngineErrorCode.Ok;

    public DiffEngineError(DiffEngineErrorCode code, ulong offset, string message)
    {
        Code = code;
        Offset = offset;
        Message = message;
    }

    public override string ToString() => IsOk ? "Ok" : $"{Code} @ {Offset}: {Message}";
}
