// IronEdge Runtime - Unified Error Model
// Maps canonical codes (0x00-0x7F) to public categories (<=16)
// Designed for deterministic error reporting and stable CLI output

using System;
using IronConfig.Common;

namespace IronConfig;

/// <summary>
/// Public error category exposed to clients (&lt;=16 values).
/// Maps to canonical internal codes for stability.
/// </summary>
public enum IronEdgeErrorCategory : byte
{
    None = 0,                    // No error (success)
    InvalidArgument = 1,         // Precondition violated (input validation)
    Io = 2,                      // I/O operation failed
    UnsupportedVersion = 3,      // File version exceeds engine capability
    InvalidMagic = 4,            // File magic bytes don't match
    Truncated = 5,               // File ends prematurely
    CorruptData = 6,             // Data integrity check failed
    InvalidChecksum = 7,         // CRC32/BLAKE3 mismatch
    InvalidSignature = 8,        // Cryptographic signature invalid
    InvariantBroken = 9,         // Logic invariant violated (internal error)
    SchemaError = 10,            // Schema validation failed (IRONCFG)
    CompressionError = 11,       // Compression/decompression failed (ILOG)
    IndexError = 12,             // Index integrity issue (ILOG)
    ManifestError = 13,          // Manifest structure invalid (IUPD)
    DependencyError = 14,        // Dependency graph invalid (IUPD)
    PolicyViolation = 15,        // Update policy violation
    Unknown = 16                 // Unknown or unclassified error
}

/// <summary>
/// Engine identifier for error source tracking.
/// </summary>
public enum IronEdgeEngine : byte
{
    Runtime = 0,
    IronCfg = 1,
    ILog = 2,
    Iupd = 3
}

/// <summary>
/// Unified error information for all IronEdge engines.
/// Struct to enable deterministic copying and serialization.
/// All fields are nullable-aware for stable JSON output.
/// </summary>
public readonly struct IronEdgeError
{
    /// <summary>
    /// Public error category (stable for API contracts).
    /// </summary>
    public IronEdgeErrorCategory Category { get; }

    /// <summary>
    /// Internal canonical code (0x00-0x7F) for detailed diagnosis.
    /// Stable across runs but not part of public API.
    /// </summary>
    public byte Code { get; }

    /// <summary>
    /// Engine that produced this error.
    /// </summary>
    public IronEdgeEngine Engine { get; }

    /// <summary>
    /// Byte offset in file where error occurred (if known).
    /// Nullable for errors without positional context.
    /// </summary>
    public long? Offset { get; }

    /// <summary>
    /// Human-readable error message.
    /// Deterministic: no timestamps, machine paths, or random content.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Optional inner exception for debugging (not exposed in public API).
    /// </summary>
    public Exception? InnerException { get; }

    /// <summary>
    /// Create a unified error from component parts.
    /// </summary>
    public IronEdgeError(
        IronEdgeErrorCategory category,
        byte code,
        IronEdgeEngine engine,
        string message,
        long? offset = null,
        Exception? innerException = null)
    {
        Category = category;
        Code = code;
        Engine = engine;
        Message = message ?? "Unknown error";
        Offset = offset;
        InnerException = innerException;
    }

    /// <summary>
    /// Success marker (Category.None, Code 0x00).
    /// </summary>
    public static IronEdgeError Ok => new(
        IronEdgeErrorCategory.None,
        0x00,
        IronEdgeEngine.Runtime,
        "OK"
    );

    /// <summary>
    /// True if this represents success (Category == None).
    /// </summary>
    public bool IsOk => Category == IronEdgeErrorCategory.None;

    /// <summary>
    /// Deterministic string representation (no timestamps).
    /// Format: "Category Code (0xXX) Engine: Message [offset: N]"
    /// </summary>
    public override string ToString()
    {
        if (IsOk)
            return "OK";

        var offsetStr = Offset.HasValue ? $" [offset: {Offset}]" : "";
        return $"{Category} Code 0x{Code:X2} ({Engine}): {Message}{offsetStr}";
    }

    /// <summary>
    /// Create from IRONCFG error, mapping to canonical codes + categories.
    /// </summary>
    public static IronEdgeError FromIronCfgError(IronConfig.IronCfg.IronCfgError cfgErr)
    {
        if (cfgErr.IsOk)
            return Ok;

        // Map IronCfgErrorCode (0-24) to canonical (0x20-0x3F) and public category
        var (category, code, message) = MapIronCfgError(cfgErr.Code);

        return new IronEdgeError(
            category: category,
            code: code,
            engine: IronEdgeEngine.IronCfg,
            message: message,
            offset: cfgErr.Offset
        );
    }

    /// <summary>
    /// Create from IUPD error, mapping to canonical codes + categories.
    /// </summary>
    public static IronEdgeError FromIupdError(IronConfig.Iupd.IupdError updErr)
    {
        if (updErr.IsOk)
            return Ok;

        // Map IupdErrorCode (0x0000-0x0014) to canonical (0x60-0x7F) and public category
        var (category, code, messageSuffix) = MapIupdError(updErr.Code);

        var message = updErr.Message;
        if (!string.IsNullOrEmpty(messageSuffix))
        {
            message = string.IsNullOrEmpty(message) ? messageSuffix : $"{message}: {messageSuffix}";
        }

        return new IronEdgeError(
            category: category,
            code: code,
            engine: IronEdgeEngine.Iupd,
            message: message,
            offset: updErr.ChunkIndex.HasValue ? (long)updErr.ChunkIndex.Value : (long?)updErr.ByteOffset
        );
    }

    /// <summary>
    /// Create from ILOG error code, mapping to canonical codes + categories.
    /// Uses compile-time switch mapping (no reflection).
    /// </summary>
    public static IronEdgeError FromIlogError(IlogErrorCode code, ulong byteOffset = 0)
    {
        if ((ushort)code == 0)
            return Ok;

        // Map IlogErrorCode to category using compile-time switch
        var (category, canonicalCode, message) = MapIlogError(code);

        return new IronEdgeError(
            category: category,
            code: canonicalCode,
            engine: IronEdgeEngine.ILog,
            message: message,
            offset: (long)byteOffset
        );
    }

    /// <summary>
    /// Legacy overload for backward compatibility: accepts ushort code value directly.
    /// Maps to IlogErrorCode enum internally.
    /// </summary>
    public static IronEdgeError FromIlogError(ushort logCodeValue, ulong byteOffset = 0)
    {
        return FromIlogError((IlogErrorCode)logCodeValue, byteOffset);
    }

    /// <summary>
    /// Convenience overload: accepts object and extracts IlogError fields via reflection.
    /// Used for legacy code that passes IlogError instances directly.
    /// </summary>
    public static IronEdgeError FromIlogError(object logErr)
    {
        if (logErr == null)
            return Ok;

        // Extract Code and ByteOffset properties if this is an IlogError
        var codeProp = logErr.GetType().GetProperty("Code");
        var byteOffsetProp = logErr.GetType().GetProperty("ByteOffset");

        if (codeProp?.GetValue(logErr) is IlogErrorCode code)
        {
            ulong offset = 0;
            if (byteOffsetProp?.GetValue(logErr) is ulong byteOffset)
                offset = byteOffset;

            return FromIlogError(code, offset);
        }

        return Ok;
    }

    // =====================================================================
    // PRIVATE MAPPING FUNCTIONS
    // =====================================================================

    /// <summary>
    /// Maps IRONCFG error codes to canonical (0x20-0x3F) and public category.
    /// </summary>
    private static (IronEdgeErrorCategory category, byte code, string message) MapIronCfgError(
        IronConfig.IronCfg.IronCfgErrorCode cfgCode)
    {
        return cfgCode switch
        {
            // Shared errors (map to 0x00-0x1F range)
            IronConfig.IronCfg.IronCfgErrorCode.TruncatedFile =>
                (IronEdgeErrorCategory.Truncated, 0x01, "File truncated"),

            IronConfig.IronCfg.IronCfgErrorCode.InvalidMagic =>
                (IronEdgeErrorCategory.InvalidMagic, 0x02, "Invalid magic bytes"),

            IronConfig.IronCfg.IronCfgErrorCode.InvalidVersion =>
                (IronEdgeErrorCategory.UnsupportedVersion, 0x03, "Unsupported version"),

            IronConfig.IronCfg.IronCfgErrorCode.InvalidFlags =>
                (IronEdgeErrorCategory.CorruptData, 0x04, "Invalid flags"),

            IronConfig.IronCfg.IronCfgErrorCode.ReservedFieldNonzero =>
                (IronEdgeErrorCategory.CorruptData, 0x05, "Reserved field non-zero"),

            IronConfig.IronCfg.IronCfgErrorCode.FlagMismatch =>
                (IronEdgeErrorCategory.CorruptData, 0x06, "Flag mismatch"),

            IronConfig.IronCfg.IronCfgErrorCode.BoundsViolation =>
                (IronEdgeErrorCategory.Truncated, 0x07, "Bounds violation"),

            IronConfig.IronCfg.IronCfgErrorCode.ArithmeticOverflow =>
                (IronEdgeErrorCategory.CorruptData, 0x08, "Arithmetic overflow"),

            IronConfig.IronCfg.IronCfgErrorCode.TruncatedBlock =>
                (IronEdgeErrorCategory.Truncated, 0x09, "Block truncated"),

            IronConfig.IronCfg.IronCfgErrorCode.Crc32Mismatch =>
                (IronEdgeErrorCategory.InvalidChecksum, 0x07, "CRC32 mismatch"),

            IronConfig.IronCfg.IronCfgErrorCode.Blake3Mismatch =>
                (IronEdgeErrorCategory.InvalidChecksum, 0x08, "BLAKE3 mismatch"),

            // IRONCFG-specific errors (0x20-0x3F)
            IronConfig.IronCfg.IronCfgErrorCode.InvalidSchema =>
                (IronEdgeErrorCategory.SchemaError, 0x20, "Invalid schema"),

            IronConfig.IronCfg.IronCfgErrorCode.FieldOrderViolation =>
                (IronEdgeErrorCategory.SchemaError, 0x21, "Field order violation"),

            IronConfig.IronCfg.IronCfgErrorCode.InvalidString =>
                (IronEdgeErrorCategory.CorruptData, 0x22, "Invalid string (UTF-8)"),

            IronConfig.IronCfg.IronCfgErrorCode.InvalidTypeCode =>
                (IronEdgeErrorCategory.CorruptData, 0x23, "Invalid type code"),

            IronConfig.IronCfg.IronCfgErrorCode.FieldTypeMismatch =>
                (IronEdgeErrorCategory.SchemaError, 0x24, "Field type mismatch"),

            IronConfig.IronCfg.IronCfgErrorCode.MissingRequiredField =>
                (IronEdgeErrorCategory.SchemaError, 0x25, "Missing required field"),

            IronConfig.IronCfg.IronCfgErrorCode.UnknownField =>
                (IronEdgeErrorCategory.SchemaError, 0x26, "Unknown field"),

            IronConfig.IronCfg.IronCfgErrorCode.FieldCountMismatch =>
                (IronEdgeErrorCategory.SchemaError, 0x27, "Field count mismatch"),

            IronConfig.IronCfg.IronCfgErrorCode.ArrayTypeMismatch =>
                (IronEdgeErrorCategory.SchemaError, 0x28, "Array type mismatch"),

            IronConfig.IronCfg.IronCfgErrorCode.NonMinimalVarint =>
                (IronEdgeErrorCategory.CorruptData, 0x29, "Non-minimal varint"),

            IronConfig.IronCfg.IronCfgErrorCode.InvalidFloat =>
                (IronEdgeErrorCategory.CorruptData, 0x2A, "Invalid float"),

            IronConfig.IronCfg.IronCfgErrorCode.RecursionLimitExceeded =>
                (IronEdgeErrorCategory.CorruptData, 0x2B, "Recursion limit exceeded"),

            IronConfig.IronCfg.IronCfgErrorCode.LimitExceeded =>
                (IronEdgeErrorCategory.CorruptData, 0x2C, "Limit exceeded"),

            _ => (IronEdgeErrorCategory.Unknown, 0xFF, "Unknown IRONCFG error")
        };
    }

    /// <summary>
    /// Maps IUPD error codes to canonical (0x60-0x7F) and public category.
    /// </summary>
    private static (IronEdgeErrorCategory category, byte code, string message) MapIupdError(
        IronConfig.Iupd.IupdErrorCode updCode)
    {
        return updCode switch
        {
            // Shared errors (map to 0x00-0x1F range)
            IronConfig.Iupd.IupdErrorCode.InvalidMagic =>
                (IronEdgeErrorCategory.InvalidMagic, 0x01, "Invalid magic"),

            IronConfig.Iupd.IupdErrorCode.UnsupportedVersion =>
                (IronEdgeErrorCategory.UnsupportedVersion, 0x02, "Unsupported version"),

            IronConfig.Iupd.IupdErrorCode.InvalidFlags =>
                (IronEdgeErrorCategory.CorruptData, 0x03, "Invalid flags"),

            IronConfig.Iupd.IupdErrorCode.InvalidHeaderSize =>
                (IronEdgeErrorCategory.CorruptData, 0x04, "Invalid header size"),

            IronConfig.Iupd.IupdErrorCode.OffsetOutOfBounds =>
                (IronEdgeErrorCategory.Truncated, 0x05, "Offset out of bounds"),

            IronConfig.Iupd.IupdErrorCode.Crc32Mismatch =>
                (IronEdgeErrorCategory.InvalidChecksum, 0x06, "CRC32 mismatch"),

            IronConfig.Iupd.IupdErrorCode.Blake3Mismatch =>
                (IronEdgeErrorCategory.InvalidChecksum, 0x07, "BLAKE3 mismatch"),

            // IUPD-specific errors (0x60-0x7F)
            IronConfig.Iupd.IupdErrorCode.InvalidChunkTableSize =>
                (IronEdgeErrorCategory.ManifestError, 0x60, "Invalid chunk table size"),

            IronConfig.Iupd.IupdErrorCode.ChunkIndexError =>
                (IronEdgeErrorCategory.ManifestError, 0x61, "Chunk index error"),

            IronConfig.Iupd.IupdErrorCode.OverlappingPayloads =>
                (IronEdgeErrorCategory.ManifestError, 0x62, "Overlapping payloads"),

            IronConfig.Iupd.IupdErrorCode.EmptyChunk =>
                (IronEdgeErrorCategory.ManifestError, 0x63, "Empty chunk"),

            IronConfig.Iupd.IupdErrorCode.InvalidManifestVersion =>
                (IronEdgeErrorCategory.ManifestError, 0x64, "Invalid manifest version"),

            IronConfig.Iupd.IupdErrorCode.ManifestSizeMismatch =>
                (IronEdgeErrorCategory.ManifestError, 0x65, "Manifest size mismatch"),

            IronConfig.Iupd.IupdErrorCode.CyclicDependency =>
                (IronEdgeErrorCategory.DependencyError, 0x68, "Cyclic dependency"),

            IronConfig.Iupd.IupdErrorCode.InvalidDependency =>
                (IronEdgeErrorCategory.DependencyError, 0x69, "Invalid dependency"),

            IronConfig.Iupd.IupdErrorCode.MissingChunkInApplyOrder =>
                (IronEdgeErrorCategory.DependencyError, 0x6A, "Missing chunk in apply order"),

            IronConfig.Iupd.IupdErrorCode.DuplicateChunkInApplyOrder =>
                (IronEdgeErrorCategory.DependencyError, 0x6B, "Duplicate chunk in apply order"),

            IronConfig.Iupd.IupdErrorCode.MissingChunk =>
                (IronEdgeErrorCategory.ManifestError, 0x66, "Missing chunk"),

            IronConfig.Iupd.IupdErrorCode.ApplyError =>
                (IronEdgeErrorCategory.PolicyViolation, 0x6C, "Apply error"),

            IronConfig.Iupd.IupdErrorCode.UnknownError =>
                (IronEdgeErrorCategory.Unknown, 0x7F, "Unknown IUPD error"),

            _ => (IronEdgeErrorCategory.Unknown, 0xFF, "Unmapped IUPD error")
        };
    }

    /// <summary>
    /// Maps ILOG error codes to canonical (0x40-0x5F) and public category.
    /// Deterministic mapping with priority ordering for errors that can co-occur.
    /// Priority: InvalidMagic > Truncated > InvalidChecksum > CompressionError > CorruptData
    /// </summary>
    private static (IronEdgeErrorCategory category, byte code, string message) MapIlogError(
        IlogErrorCode logCode)
    {
        return logCode switch
        {
            IlogErrorCode.InvalidMagic =>
                (IronEdgeErrorCategory.InvalidMagic, 0x01, "Invalid ILOG magic bytes"),

            IlogErrorCode.UnsupportedVersion =>
                (IronEdgeErrorCategory.UnsupportedVersion, 0x02, "Unsupported ILOG version"),

            IlogErrorCode.CorruptedHeader =>
                (IronEdgeErrorCategory.CorruptData, 0x41, "ILOG header corrupted"),

            IlogErrorCode.MissingLayer =>
                (IronEdgeErrorCategory.CorruptData, 0x42, "ILOG layer missing"),

            IlogErrorCode.MalformedBlock =>
                (IronEdgeErrorCategory.CorruptData, 0x43, "ILOG block malformed"),

            IlogErrorCode.BlockOutOfBounds =>
                (IronEdgeErrorCategory.Truncated, 0x44, "ILOG block out of bounds"),

            IlogErrorCode.InvalidBlockType =>
                (IronEdgeErrorCategory.CorruptData, 0x45, "ILOG block type invalid"),

            IlogErrorCode.SchemaValidation =>
                (IronEdgeErrorCategory.SchemaError, 0x46, "ILOG schema validation failed"),

            IlogErrorCode.OutOfBoundsRef =>
                (IronEdgeErrorCategory.Truncated, 0x47, "ILOG reference out of bounds"),

            IlogErrorCode.DictLookup =>
                (IronEdgeErrorCategory.CorruptData, 0x48, "ILOG dictionary lookup failed"),

            IlogErrorCode.VarintDecode =>
                (IronEdgeErrorCategory.CorruptData, 0x49, "ILOG varint decode error"),

            IlogErrorCode.Crc32Mismatch =>
                (IronEdgeErrorCategory.InvalidChecksum, 0x06, "ILOG CRC32 mismatch"),

            IlogErrorCode.Blake3Mismatch =>
                (IronEdgeErrorCategory.InvalidChecksum, 0x07, "ILOG BLAKE3 mismatch"),

            IlogErrorCode.CompressionFailed =>
                (IronEdgeErrorCategory.CompressionError, 0x40, "ILOG compression/decompression failed"),

            IlogErrorCode.RecordTruncated =>
                (IronEdgeErrorCategory.Truncated, 0x05, "ILOG record truncated"),

            IlogErrorCode.DepthLimit =>
                (IronEdgeErrorCategory.CorruptData, 0x4A, "ILOG depth limit exceeded"),

            IlogErrorCode.FileSizeLimit =>
                (IronEdgeErrorCategory.Truncated, 0x4B, "ILOG file size limit exceeded"),

            IlogErrorCode.RecordCountLimit =>
                (IronEdgeErrorCategory.CorruptData, 0x4C, "ILOG record count limit exceeded"),

            IlogErrorCode.StringLengthLimit =>
                (IronEdgeErrorCategory.CorruptData, 0x4D, "ILOG string length limit exceeded"),

            IlogErrorCode.CriticalFlag =>
                (IronEdgeErrorCategory.CorruptData, 0x4E, "ILOG critical flag set"),

            _ => (IronEdgeErrorCategory.Unknown, 0xFF, "Unknown ILOG error")
        };
    }
}

/// <summary>
/// Exception wrapper for IronEdge errors when throwing is required.
/// Used for compatibility with existing exception-based error handling.
/// </summary>
public class IronEdgeException : Exception
{
    public IronEdgeError Error { get; }

    public IronEdgeException(IronEdgeError error)
        : base(error.ToString())
    {
        Error = error;
    }

    public IronEdgeException(IronEdgeError error, Exception? inner)
        : base(error.ToString(), inner)
    {
        Error = error;
    }
}
