using System;

namespace IronConfig.IronCfg;

internal static class IronCfgTypeSystem
{
    internal const byte Null = 0x00;
    internal const byte BoolFalse = 0x01;
    internal const byte BoolTrue = 0x02;
    internal const byte Int64 = 0x10;
    internal const byte UInt64 = 0x11;
    internal const byte Float64 = 0x12;
    internal const byte StringInline = 0x20;
    internal const byte StringId = 0x21;
    internal const byte Bytes = 0x22;
    internal const byte Array = 0x30;
    internal const byte Object = 0x40;

    internal static bool IsValidTypeCode(byte typeCode)
    {
        return typeCode switch
        {
            Null => true,
            BoolFalse => true,
            BoolTrue => true,
            Int64 => true,
            UInt64 => true,
            Float64 => true,
            StringInline => true,
            StringId => true,
            Bytes => true,
            Array => true,
            Object => true,
            _ => false
        };
    }

    internal static bool IsCompoundType(byte typeCode) => typeCode >= 0x1C;

    internal static bool HasElementSchema(byte schemaVersion, byte typeCode)
    {
        return typeCode == Array && schemaVersion >= 2;
    }

    internal static bool TypeMatchesExpected(byte expectedType, byte actualType)
    {
        if (expectedType == Null || actualType == Null)
            return true;

        if (expectedType == BoolFalse)
            return actualType == BoolFalse || actualType == BoolTrue;

        if ((expectedType == StringInline || expectedType == StringId) &&
            (actualType == StringInline || actualType == StringId))
            return true;

        return actualType == expectedType;
    }

    internal static IronCfgError ResolveStringPoolEntry(
        ReadOnlySpan<byte> pool,
        uint stringIndex,
        uint poolBaseOffset,
        out uint valueOffset,
        out uint valueLength)
    {
        valueOffset = 0;
        valueLength = 0;

        uint entryIndex = 0;
        uint offset = 0;
        while (offset < pool.Length)
        {
            uint entryStart = offset;
            var lenErr = IronCfgValidator.DecodeVarUInt32(pool, offset, out var len, out var lenBytes);
            if (!lenErr.IsOk)
                return new IronCfgError(lenErr.Code, poolBaseOffset + lenErr.Offset);

            offset += lenBytes;
            if (offset + len > pool.Length)
                return new IronCfgError(IronCfgErrorCode.BoundsViolation, poolBaseOffset + offset);

            if (entryIndex == stringIndex)
            {
                valueOffset = entryStart + lenBytes;
                valueLength = len;
                return IronCfgError.Ok;
            }

            offset += len;
            entryIndex++;
        }

        return new IronCfgError(IronCfgErrorCode.BoundsViolation, poolBaseOffset + (uint)pool.Length);
    }
}
