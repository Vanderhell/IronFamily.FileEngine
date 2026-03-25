namespace IronConfig.Iupd;

using System;
using System.Buffers.Binary;

/// <summary>
/// INCREMENTAL-specific metadata trailer for patch-bound packages.
///
/// Structure (outside signature range, after UpdateSequence trailer):
/// [0-7]    Magic: "IUPDINC1" (8 bytes)
/// [8-11]   Length: total trailer length (4 bytes, little-endian)
/// [12]     Version: trailer format version (1 byte, =1)
/// [13]     AlgorithmId: patch algorithm (1 byte)
/// [14]     BaseHashLength: length of base hash (1 byte, typically 0x20 for BLAKE3-256)
/// [15-46]  BaseHash: base image BLAKE3-256 hash (32 bytes)
/// [47]     TargetHashLength: length of target hash (1 byte, typically 0x20)
/// [48-79]  TargetHash: target image BLAKE3-256 hash (32 bytes)
/// [80-83]  CRC32: integrity check (4 bytes, little-endian)
///
/// Total: ~84 bytes with BLAKE3-256 hashes
/// </summary>
public class IupdIncrementalMetadata
{
    private const string MAGIC_STRING = "IUPDINC1";
    private static readonly byte[] MAGIC = System.Text.Encoding.ASCII.GetBytes(MAGIC_STRING);
    private const uint MAGIC_LE = 0x314E4349;  // "INC1" in little-endian
    private const uint MAGIC_LE_2 = 0x50555049; // "IUPD" in little-endian (first 4 bytes reversed)

    private const int TRAILER_MIN_SIZE = 21;  // Magic(8) + Length(4) + Version(1) + AlgorithmId(1) + BaseHashLength(1) + BaseHash(≥1) + TargetHashLength(1) + CRC32(4)
    private const int BLAKE3_HASH_LENGTH = 32;

    // Algorithm IDs
    // NOTE: IRONDEL2 (0x02) is the active production path.
    // DELTA_V1 (0x01) is supported for backward compatibility only.
    public const byte ALGORITHM_UNSPECIFIED = 0x00;
    public const byte ALGORITHM_DELTA_V1 = 0x01;      // Legacy: for backward compatibility
    public const byte ALGORITHM_IRONDEL2 = 0x02;      // Active: Content-defined chunking delta algorithm

    public byte AlgorithmId { get; set; }
    public byte[]? BaseHash { get; set; }
    public byte[]? TargetHash { get; set; }

    /// <summary>
    /// Serialize metadata to trailer bytes.
    /// </summary>
    public byte[] Serialize()
    {
        int baseHashLen = BaseHash?.Length ?? 0;
        int targetHashLen = TargetHash?.Length ?? 0;

        if (baseHashLen == 0)
            throw new InvalidOperationException("BaseHash is required for INCREMENTAL");

        // Calculate total length
        int trailerLength = 8 + 4 + 1 + 1 + 1 + baseHashLen + 1 + targetHashLen + 4;
        byte[] trailer = new byte[trailerLength];

        // Write magic
        MAGIC.CopyTo(trailer, 0);

        // Write length (including this field and CRC32)
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(8, 4), (uint)trailerLength);

        // Write version
        trailer[12] = 1;

        // Write algorithm ID
        trailer[13] = AlgorithmId;

        // Write base hash
        trailer[14] = (byte)baseHashLen;
        if (baseHashLen > 0)
            BaseHash!.CopyTo(trailer, 15);

        int targetHashOffset = 15 + baseHashLen;
        trailer[targetHashOffset] = (byte)targetHashLen;

        if (targetHashLen > 0)
            TargetHash!.CopyTo(trailer, targetHashOffset + 1);

        // Compute CRC32 over everything except the CRC32 field itself
        int crc32Offset = targetHashOffset + 1 + targetHashLen;
        uint crc32Value = Crc32Ieee.Compute(trailer.AsSpan(0, crc32Offset));
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(crc32Offset, 4), crc32Value);

        return trailer;
    }

    /// <summary>
    /// Deserialize metadata from trailer bytes.
    /// Returns (success, metadata, errorMessage).
    /// </summary>
    public static (bool success, IupdIncrementalMetadata? metadata, string? error) TryDeserialize(byte[] trailerData)
    {
        if (trailerData.Length < TRAILER_MIN_SIZE)
            return (false, null, $"Trailer too short: {trailerData.Length} < {TRAILER_MIN_SIZE}");

        // Verify magic
        if (!IsValidMagic(trailerData))
            return (false, null, "Invalid INCREMENTAL metadata magic");

        // Read length
        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(trailerData.AsSpan(8, 4));
        if (declaredLength != trailerData.Length)
            return (false, null, $"Length mismatch: declared {declaredLength}, actual {trailerData.Length}");

        // Read version
        byte version = trailerData[12];
        if (version != 1)
            return (false, null, $"Unsupported trailer version: {version}");

        // Read algorithm ID
        byte algorithmId = trailerData[13];
        if (algorithmId == ALGORITHM_UNSPECIFIED)
            return (false, null, "Algorithm ID unspecified");

        // Read base hash
        byte baseHashLength = trailerData[14];
        if (baseHashLength == 0)
            return (false, null, "Base hash length is 0");

        if (15 + baseHashLength > trailerData.Length)
            return (false, null, "Trailer truncated (base hash)");

        byte[] baseHash = new byte[baseHashLength];
        Array.Copy(trailerData, 15, baseHash, 0, baseHashLength);

        int targetHashOffset = 15 + baseHashLength;
        if (targetHashOffset >= trailerData.Length)
            return (false, null, "Trailer truncated (target hash length field)");

        byte targetHashLength = trailerData[targetHashOffset];
        int crc32Offset = targetHashOffset + 1 + targetHashLength;

        if (crc32Offset + 4 > trailerData.Length)
            return (false, null, "Trailer truncated (CRC32)");

        byte[]? targetHash = null;
        if (targetHashLength > 0)
        {
            targetHash = new byte[targetHashLength];
            Array.Copy(trailerData, targetHashOffset + 1, targetHash, 0, targetHashLength);
        }

        // Verify CRC32
        uint storedCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(trailerData.AsSpan(crc32Offset, 4));
        uint computedCrc32 = Crc32Ieee.Compute(trailerData.AsSpan(0, crc32Offset));

        if (storedCrc32 != computedCrc32)
            return (false, null, $"CRC32 mismatch: stored {storedCrc32:X8}, computed {computedCrc32:X8}");

        var metadata = new IupdIncrementalMetadata
        {
            AlgorithmId = algorithmId,
            BaseHash = baseHash,
            TargetHash = targetHash
        };

        return (true, metadata, null);
    }

    /// <summary>
    /// Check if trailer data has valid INCREMENTAL magic.
    /// </summary>
    private static bool IsValidMagic(byte[] data)
    {
        if (data.Length < 8)
            return false;

        for (int i = 0; i < 8; i++)
        {
            if (data[i] != MAGIC[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Get algorithm name for display.
    /// </summary>
    public string GetAlgorithmName() => AlgorithmId switch
    {
        ALGORITHM_DELTA_V1 => "DELTA_V1",
        ALGORITHM_IRONDEL2 => "IRONDEL2",
        _ => $"UNKNOWN(0x{AlgorithmId:X2})"
    };

    /// <summary>
    /// Check if algorithm ID is known/supported.
    /// </summary>
    public bool IsKnownAlgorithm() => AlgorithmId is ALGORITHM_DELTA_V1 or ALGORITHM_IRONDEL2;
}
