namespace IronConfig.Common;

/// <summary>
/// Canonical error codes (0x00-0x7F) used internally across all IronEdge engines.
/// These are the internal diagnostic codes exposed in error structures.
/// Maps from engine-specific codes to unified canonical namespace.
///
/// Ranges:
/// 0x00-0x1F: Shared errors across all engines
/// 0x20-0x3F: IRONCFG-specific errors
/// 0x40-0x5F: ILOG-specific errors
/// 0x60-0x7F: IUPD-specific errors
/// </summary>
public static class IronEdgeErrorCode
{
    // Shared errors (0x00-0x1F)
    public const byte InvalidMagic = 0x01;
    public const byte UnsupportedVersion = 0x02;
    public const byte CorruptData_Base = 0x03;
    public const byte Truncated_Base = 0x05;
    public const byte Checksum_Base = 0x06;

    // ILOG-specific errors (0x40-0x5F)
    public const byte ILog_CompressionFailed = 0x40;
    public const byte ILog_HeaderCorrupted = 0x41;
    public const byte ILog_LayerMissing = 0x42;
    public const byte ILog_BlockMalformed = 0x43;
    public const byte ILog_BlockOutOfBounds = 0x44;
    public const byte ILog_BlockTypeInvalid = 0x45;
    public const byte ILog_SchemaValidationFailed = 0x46;
    public const byte ILog_RefOutOfBounds = 0x47;
    public const byte ILog_DictLookupFailed = 0x48;
    public const byte ILog_VarintDecodeFailed = 0x49;
    public const byte ILog_DepthLimitExceeded = 0x4A;
    public const byte ILog_FileSizeLimitExceeded = 0x4B;
    public const byte ILog_RecordCountLimitExceeded = 0x4C;
    public const byte ILog_StringLengthLimitExceeded = 0x4D;
    public const byte ILog_CriticalFlagSet = 0x4E;

    // Error code mappings for lookup (compilation-time constants)
    // All shared codes map to 0x00-0x1F by protocol
    public const byte UnknownError = 0xFF;
}
