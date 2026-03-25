using System;

namespace IronConfig.Iupd;

/// <summary>
/// IUPD error codes (per spec section 25)
/// </summary>
public enum IupdErrorCode : ushort
{
    Ok = 0x0000,
    InvalidMagic = 0x0001,
    UnsupportedVersion = 0x0002,
    InvalidFlags = 0x0003,
    InvalidHeaderSize = 0x0004,
    OffsetOutOfBounds = 0x0005,
    InvalidChunkTableSize = 0x0006,
    ChunkIndexError = 0x0007,
    OverlappingPayloads = 0x0008,
    EmptyChunk = 0x0009,
    InvalidManifestVersion = 0x000A,
    ManifestSizeMismatch = 0x000B,
    Crc32Mismatch = 0x000C,
    Blake3Mismatch = 0x000D,
    CyclicDependency = 0x000E,
    InvalidDependency = 0x000F,
    MissingChunkInApplyOrder = 0x0010,
    DuplicateChunkInApplyOrder = 0x0011,
    MissingChunk = 0x0012,
    ApplyError = 0x0013,
    SignatureMissing = 0x0015,
    SignatureInvalid = 0x0016,
    UnknownError = 0x0014,
    WitnessMissing = 0x0017,
    WitnessMismatch = 0x0018,
    ReplayDetected = 0x0019,
    KeyRevoked = 0x001A,
    ProfileNotAllowed = 0x001B,
    UpdateSequenceMissing = 0x001C,
    // Delta v1 error codes
    DeltaMagicMismatch = 0x0020,
    DeltaVersionUnsupported = 0x0021,
    DeltaBaseHashMismatch = 0x0022,
    DeltaTargetHashMismatch = 0x0023,
    DeltaEntryOutOfRange = 0x0024,
    DeltaMalformed = 0x0025
}

/// <summary>
/// IUPD error with code, byte offset, and chunk index
/// </summary>
public readonly struct IupdError
{
    public IupdErrorCode Code { get; }
    public ulong ByteOffset { get; }
    public uint? ChunkIndex { get; }
    public string Message { get; }

    public IupdError(IupdErrorCode code, ulong byteOffset, string message = "")
    {
        Code = code;
        ByteOffset = byteOffset;
        ChunkIndex = null;
        Message = message;
    }

    public IupdError(IupdErrorCode code, uint chunkIndex, string message = "")
    {
        Code = code;
        ByteOffset = 0;
        ChunkIndex = chunkIndex;
        Message = message;
    }

    public static IupdError Ok => new(IupdErrorCode.Ok, 0);

    public bool IsOk => Code == IupdErrorCode.Ok;

    public override string ToString()
    {
        if (IsOk) return "OK";

        if (ChunkIndex.HasValue)
            return $"{Code} (chunk {ChunkIndex}): {Message}";
        else
            return $"{Code} at offset {ByteOffset}: {Message}";
    }
}
