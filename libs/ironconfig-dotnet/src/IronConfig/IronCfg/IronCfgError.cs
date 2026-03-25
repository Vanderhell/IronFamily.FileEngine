using System;

namespace IronConfig.IronCfg;

/// <summary>
/// IRONCFG error code (0-24)
/// </summary>
public enum IronCfgErrorCode
{
    Ok = 0,
    TruncatedFile = 1,
    InvalidMagic = 2,
    InvalidVersion = 3,
    InvalidFlags = 4,
    ReservedFieldNonzero = 5,
    FlagMismatch = 6,
    BoundsViolation = 7,
    ArithmeticOverflow = 8,
    TruncatedBlock = 9,
    InvalidSchema = 10,
    FieldOrderViolation = 11,
    InvalidString = 12,
    InvalidTypeCode = 13,
    FieldTypeMismatch = 14,
    MissingRequiredField = 15,
    UnknownField = 16,
    FieldCountMismatch = 17,
    ArrayTypeMismatch = 18,
    NonMinimalVarint = 19,
    InvalidFloat = 20,
    RecursionLimitExceeded = 21,
    LimitExceeded = 22,
    Crc32Mismatch = 23,
    Blake3Mismatch = 24
}

/// <summary>
/// IRONCFG error with code and byte offset
/// </summary>
public readonly struct IronCfgError
{
    public IronCfgErrorCode Code { get; }
    public uint Offset { get; }

    public IronCfgError(IronCfgErrorCode code, uint offset)
    {
        Code = code;
        Offset = offset;
    }

    public static IronCfgError Ok => new(IronCfgErrorCode.Ok, 0);

    public bool IsOk => Code == IronCfgErrorCode.Ok;

    public override string ToString() => IsOk ? "OK" : $"{Code} at offset {Offset}";
}
