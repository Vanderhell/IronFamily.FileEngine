namespace IronConfig.Common;

/// <summary>
/// ILOG error codes (ushort) - shared across assemblies.
/// Used for unified error mapping without circular dependencies.
/// </summary>
public enum IlogErrorCode : ushort
{
    InvalidMagic = 0x0001,
    UnsupportedVersion = 0x0002,
    CorruptedHeader = 0x0003,
    MissingLayer = 0x0004,
    MalformedBlock = 0x0005,
    BlockOutOfBounds = 0x0006,
    InvalidBlockType = 0x0007,
    SchemaValidation = 0x0008,
    OutOfBoundsRef = 0x0009,
    DictLookup = 0x000A,
    VarintDecode = 0x000B,
    Crc32Mismatch = 0x000C,
    Blake3Mismatch = 0x000D,
    CompressionFailed = 0x000E,
    RecordTruncated = 0x000F,
    DepthLimit = 0x0010,
    FileSizeLimit = 0x0011,
    RecordCountLimit = 0x0012,
    StringLengthLimit = 0x0013,
    CriticalFlag = 0x0014,
}
