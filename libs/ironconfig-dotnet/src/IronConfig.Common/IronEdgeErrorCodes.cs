namespace IronConfig.Common;

/// <summary>
/// Canonical error codes for IronEdge unified error model.
/// Used for stable diagnostics across engines.
/// Range: 0x00-0x7F
/// </summary>
public static class IronEdgeErrorCodes
{
    // Shared codes (0x00-0x1F)
    public const byte Truncated = 0x01;
    public const byte InvalidMagic = 0x02;
    public const byte UnsupportedVersion = 0x03;
    public const byte CorruptData = 0x04;
    public const byte Truncated2 = 0x05;  // Additional truncation
    public const byte Crc32Checksum = 0x06;
    public const byte Blake3Checksum = 0x07;

    // ILOG-specific codes (0x40-0x5F)
    public const byte IlogCompressionError = 0x40;
    public const byte IlogCorruptedHeader = 0x41;
    public const byte IlogMissingLayer = 0x42;
    public const byte IlogMalformedBlock = 0x43;
    public const byte IlogBlockOutOfBounds = 0x44;
    public const byte IlogInvalidBlockType = 0x45;
    public const byte IlogSchemaValidation = 0x46;
    public const byte IlogOutOfBoundsRef = 0x47;
    public const byte IlogDictLookup = 0x48;
    public const byte IlogVarintDecode = 0x49;
    public const byte IlogDepthLimit = 0x4A;
    public const byte IlogFileSizeLimit = 0x4B;
    public const byte IlogRecordCountLimit = 0x4C;
    public const byte IlogStringLengthLimit = 0x4D;
    public const byte IlogCriticalFlag = 0x4E;
}
