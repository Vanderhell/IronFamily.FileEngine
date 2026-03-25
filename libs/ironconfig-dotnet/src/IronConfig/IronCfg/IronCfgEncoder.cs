using System;
using System.Collections.Generic;
using System.IO.Hashing;

namespace IronConfig.IronCfg;

/// <summary>
/// IRONCFG deterministic encoder
/// </summary>
public static class IronCfgEncoder
{
    private const uint MAX_FILE_SIZE = 256 * 1024 * 1024;
    private const uint MAX_FIELDS = 65536;
    private const uint MAX_ARRAY_ELEMENTS = 1_000_000;
    private const uint MAX_STRING_LENGTH = 16 * 1024 * 1024;

    /// <summary>
    /// Encode value with CRC32 and optional BLAKE3
    /// </summary>
    public static IronCfgError Encode(
        IronCfgValue root,
        IronCfgSchema schema,
        bool computeCrc32,
        bool computeBlake3,
        Span<byte> buffer,
        out int encodedSize)
    {
        encodedSize = 0;

        if (buffer.Length < 64)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, 0);

        var ctx = new EncodeContext(buffer);

        // Skip header (write at end)
        ctx.Offset = 64;

        // Encode schema block
        uint schemaOffset = ctx.Offset;
        var schemaErr = EncodeSchema(ref ctx, schema);
        if (!schemaErr.IsOk)
            return schemaErr;
        uint schemaSize = ctx.Offset - schemaOffset;

        // Encode data block
        uint dataOffset = ctx.Offset;
        var dataErr = EncodeValue(ref ctx, root);
        if (!dataErr.IsOk)
            return dataErr;
        uint dataSize = ctx.Offset - dataOffset;

        // Calculate CRC and BLAKE3 offsets
        uint crcOffset = 0;
        uint blake3Offset = 0;
        byte flags = 0;

        if (computeCrc32)
        {
            flags |= 0x01;
            crcOffset = ctx.Offset;
            ctx.Offset += 4;
        }

        if (computeBlake3)
        {
            flags |= 0x02;
            blake3Offset = ctx.Offset;
            ctx.Offset += 32;
        }

        uint fileSize = ctx.Offset;

        if (fileSize > MAX_FILE_SIZE)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, 8);

        if (fileSize > buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, 0);

        // Write header
        WriteHeader(buffer, schemaOffset, schemaSize, dataOffset, dataSize, crcOffset, blake3Offset, fileSize, flags);

        // Compute and write CRC32
        if (computeCrc32)
        {
            var crcData = buffer.Slice(0, (int)crcOffset);
            uint crc = Crc32Ieee.Compute(crcData);
            WriteUInt32LE(buffer, crcOffset, crc);
        }

        if (computeBlake3)
        {
            var blake3Data = buffer.Slice(0, (int)blake3Offset);
            Blake3Ieee.Compute(blake3Data, buffer.Slice((int)blake3Offset, 32));
        }

        encodedSize = (int)fileSize;
        return IronCfgError.Ok;
    }

    private static void WriteHeader(
        Span<byte> buffer,
        uint schemaOffset,
        uint schemaSize,
        uint dataOffset,
        uint dataSize,
        uint crcOffset,
        uint blake3Offset,
        uint fileSize,
        byte flags)
    {
        buffer.Slice(0, 64).Clear();

        WriteUInt32LE(buffer, 0, IronCfgHeader.MAGIC);
        buffer[4] = IronCfgHeader.VERSION;
        buffer[5] = flags;
        WriteUInt16LE(buffer, 6, 0);
        WriteUInt32LE(buffer, 8, fileSize);
        WriteUInt32LE(buffer, 12, schemaOffset);
        WriteUInt32LE(buffer, 16, schemaSize);
        WriteUInt32LE(buffer, 20, 0); // stringPoolOffset
        WriteUInt32LE(buffer, 24, 0); // stringPoolSize
        WriteUInt32LE(buffer, 28, dataOffset);
        WriteUInt32LE(buffer, 32, dataSize);
        WriteUInt32LE(buffer, 36, crcOffset);
        WriteUInt32LE(buffer, 40, blake3Offset);
        WriteUInt32LE(buffer, 44, 0); // reserved1
        buffer.Slice(48, 16).Clear(); // reserved2
    }

    private static IronCfgError EncodeSchema(ref EncodeContext ctx, IronCfgSchema schema)
    {
        if ((uint)schema.Fields.Count > MAX_FIELDS)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, ctx.Offset);

        // Write field count
        var countSize = EncodeVarUInt(ctx.Buffer, ctx.Offset, (uint)schema.Fields.Count);
        ctx.Offset += countSize;

        foreach (var field in schema.Fields)
        {
            // Write fieldId
            var idSize = EncodeVarUInt(ctx.Buffer, ctx.Offset, field.FieldId);
            ctx.Offset += idSize;

            // Write fieldType (typeCode)
            if (ctx.Offset >= ctx.Buffer.Length)
                return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);
            if (!IronCfgTypeSystem.IsValidTypeCode(field.FieldType))
                return new IronCfgError(IronCfgErrorCode.InvalidTypeCode, ctx.Offset);
            ctx.Buffer[(int)ctx.Offset++] = field.FieldType;

            if (IronCfgTypeSystem.IsCompoundType(field.FieldType))
            {
                var nameBytes = System.Text.Encoding.UTF8.GetBytes(field.FieldName);
                uint nameLen = (uint)nameBytes.Length;
                if (nameLen > MAX_STRING_LENGTH)
                    return new IronCfgError(IronCfgErrorCode.LimitExceeded, ctx.Offset);
                var nameLenSize = EncodeVarUInt(ctx.Buffer, ctx.Offset, nameLen);
                ctx.Offset += nameLenSize;

                if (ctx.Offset + nameLen > ctx.Buffer.Length)
                    return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);

                nameBytes.AsSpan().CopyTo(ctx.Buffer.Slice((int)ctx.Offset, (int)nameLen));
                ctx.Offset += nameLen;

                if (IronCfgTypeSystem.HasElementSchema(IronCfgHeader.VERSION, field.FieldType))
                {
                    if (field.ElementSchema == null)
                        return new IronCfgError(IronCfgErrorCode.InvalidSchema, ctx.Offset);

                    var elemErr = EncodeSchema(ref ctx, field.ElementSchema);
                    if (!elemErr.IsOk)
                        return elemErr;
                }
            }
        }

        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeValue(ref EncodeContext ctx, IronCfgValue? value)
    {
        return value switch
        {
            null => EncodeNull(ref ctx),
            IronCfgBool b => EncodeBool(ref ctx, b),
            IronCfgInt64 i => EncodeInt64(ref ctx, i),
            IronCfgUInt64 u => EncodeUInt64(ref ctx, u),
            IronCfgFloat64 f => EncodeFloat64(ref ctx, f),
            IronCfgString s => EncodeString(ref ctx, s),
            IronCfgBytes b => EncodeBytes(ref ctx, b),
            IronCfgArray a => EncodeArray(ref ctx, a),
            IronCfgObject o => EncodeObject(ref ctx, o),
            _ => new IronCfgError(IronCfgErrorCode.InvalidSchema, ctx.Offset)
        };
    }

    private static IronCfgError EncodeNull(ref EncodeContext ctx)
    {
        if (ctx.Offset >= ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);
        ctx.Buffer[(int)ctx.Offset++] = 0x00;
        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeBool(ref EncodeContext ctx, IronCfgBool value)
    {
        if (ctx.Offset >= ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);
        ctx.Buffer[(int)ctx.Offset++] = value.Value ? (byte)0x02 : (byte)0x01;
        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeInt64(ref EncodeContext ctx, IronCfgInt64 value)
    {
        if (ctx.Offset + 9 > ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);
        ctx.Buffer[(int)ctx.Offset++] = 0x10;
        WriteInt64LE(ctx.Buffer, ctx.Offset, value.Value);
        ctx.Offset += 8;
        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeUInt64(ref EncodeContext ctx, IronCfgUInt64 value)
    {
        if (ctx.Offset + 9 > ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);
        ctx.Buffer[(int)ctx.Offset++] = 0x11;
        WriteUInt64LE(ctx.Buffer, ctx.Offset, value.Value);
        ctx.Offset += 8;
        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeFloat64(ref EncodeContext ctx, IronCfgFloat64 value)
    {
        if (ctx.Offset + 9 > ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);

        double normalizedValue = value.Value;

        // Check for NaN
        if (double.IsNaN(normalizedValue))
            return new IronCfgError(IronCfgErrorCode.InvalidFloat, ctx.Offset);

        // Normalize -0.0 to +0.0
        if (normalizedValue == 0.0 && BitConverter.DoubleToInt64Bits(normalizedValue) < 0)
            normalizedValue = 0.0;

        ctx.Buffer[(int)ctx.Offset++] = 0x12;
        WriteFloat64LE(ctx.Buffer, ctx.Offset, normalizedValue);
        ctx.Offset += 8;
        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeString(ref EncodeContext ctx, IronCfgString value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value.Value);
        uint len = (uint)bytes.Length;
        if (len > MAX_STRING_LENGTH)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, ctx.Offset);

        if (ctx.Offset + 1 + 5 + len > ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);

        ctx.Buffer[(int)ctx.Offset++] = 0x20;
        var lenSize = EncodeVarUInt(ctx.Buffer, ctx.Offset, len);
        ctx.Offset += lenSize;

        bytes.AsSpan().CopyTo(ctx.Buffer.Slice((int)ctx.Offset, bytes.Length));
        ctx.Offset += (uint)bytes.Length;
        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeBytes(ref EncodeContext ctx, IronCfgBytes value)
    {
        uint len = (uint)value.Data.Length;
        if (len > MAX_STRING_LENGTH)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, ctx.Offset);

        if (ctx.Offset + 1 + 5 + len > ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);

        ctx.Buffer[(int)ctx.Offset++] = 0x22;
        var lenSize = EncodeVarUInt(ctx.Buffer, ctx.Offset, len);
        ctx.Offset += lenSize;

        value.Data.AsSpan().CopyTo(ctx.Buffer.Slice((int)ctx.Offset, (int)len));
        ctx.Offset += len;
        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeArray(ref EncodeContext ctx, IronCfgArray value)
    {
        uint count = (uint)value.Elements.Count;
        if (count > MAX_ARRAY_ELEMENTS)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, ctx.Offset);

        if (ctx.Offset + 1 + 5 > ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);

        ctx.Buffer[(int)ctx.Offset++] = 0x30;
        var countSize = EncodeVarUInt(ctx.Buffer, ctx.Offset, count);
        ctx.Offset += countSize;

        foreach (var elem in value.Elements)
        {
            var err = EncodeValue(ref ctx, elem);
            if (!err.IsOk)
                return err;
        }

        return IronCfgError.Ok;
    }

    private static IronCfgError EncodeObject(ref EncodeContext ctx, IronCfgObject value)
    {
        uint fieldCount = (uint)value.Fields.Count;
        if (fieldCount > MAX_FIELDS)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, ctx.Offset);

        if (ctx.Offset + 1 + 5 > ctx.Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);

        ctx.Buffer[(int)ctx.Offset++] = 0x40;
        var countSize = EncodeVarUInt(ctx.Buffer, ctx.Offset, fieldCount);
        ctx.Offset += countSize;

        // Fields must be in ascending fieldId order
        foreach (var kvp in value.Fields)
        {
            uint fieldId = kvp.Key;

            if (ctx.Offset + 5 > ctx.Buffer.Length)
                return new IronCfgError(IronCfgErrorCode.BoundsViolation, ctx.Offset);

            var idSize = EncodeVarUInt(ctx.Buffer, ctx.Offset, fieldId);
            ctx.Offset += idSize;

            var err = EncodeValue(ref ctx, kvp.Value);
            if (!err.IsOk)
                return err;
        }

        return IronCfgError.Ok;
    }

    private static uint EncodeVarUInt(Span<byte> buffer, uint offset, uint value)
    {
        uint size = 0;
        while (value >= 128)
        {
            buffer[(int)(offset + size)] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
            size++;
        }
        buffer[(int)(offset + size)] = (byte)(value & 0x7F);
        return size + 1;
    }

    private static void WriteUInt32LE(Span<byte> buffer, uint offset, uint value)
    {
        buffer[(int)offset] = (byte)(value & 0xFF);
        buffer[(int)offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[(int)offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[(int)offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteUInt16LE(Span<byte> buffer, uint offset, ushort value)
    {
        buffer[(int)offset] = (byte)(value & 0xFF);
        buffer[(int)offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt64LE(Span<byte> buffer, uint offset, long value)
    {
        ulong uvalue = (ulong)value;
        buffer[(int)offset] = (byte)(uvalue & 0xFF);
        buffer[(int)offset + 1] = (byte)((uvalue >> 8) & 0xFF);
        buffer[(int)offset + 2] = (byte)((uvalue >> 16) & 0xFF);
        buffer[(int)offset + 3] = (byte)((uvalue >> 24) & 0xFF);
        buffer[(int)offset + 4] = (byte)((uvalue >> 32) & 0xFF);
        buffer[(int)offset + 5] = (byte)((uvalue >> 40) & 0xFF);
        buffer[(int)offset + 6] = (byte)((uvalue >> 48) & 0xFF);
        buffer[(int)offset + 7] = (byte)((uvalue >> 56) & 0xFF);
    }

    private static void WriteUInt64LE(Span<byte> buffer, uint offset, ulong value)
    {
        buffer[(int)offset] = (byte)(value & 0xFF);
        buffer[(int)offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[(int)offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[(int)offset + 3] = (byte)((value >> 24) & 0xFF);
        buffer[(int)offset + 4] = (byte)((value >> 32) & 0xFF);
        buffer[(int)offset + 5] = (byte)((value >> 40) & 0xFF);
        buffer[(int)offset + 6] = (byte)((value >> 48) & 0xFF);
        buffer[(int)offset + 7] = (byte)((value >> 56) & 0xFF);
    }

    private static void WriteFloat64LE(Span<byte> buffer, uint offset, double value)
    {
        var bytes = BitConverter.GetBytes(value);
        bytes.AsSpan().CopyTo(buffer.Slice((int)offset, 8));
    }

    private ref struct EncodeContext
    {
        public Span<byte> Buffer;
        public uint Offset;

        public EncodeContext(Span<byte> buffer)
        {
            Buffer = buffer;
            Offset = 0;
        }
    }
}

/* Value types for encoding */

public class IronCfgValue { }

public class IronCfgBool : IronCfgValue
{
    public bool Value { get; set; }
}

public class IronCfgInt64 : IronCfgValue
{
    public long Value { get; set; }
}

public class IronCfgUInt64 : IronCfgValue
{
    public ulong Value { get; set; }
}

public class IronCfgFloat64 : IronCfgValue
{
    public double Value { get; set; }
}

public class IronCfgString : IronCfgValue
{
    public string Value { get; set; } = string.Empty;
}

public class IronCfgBytes : IronCfgValue
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public class IronCfgArray : IronCfgValue
{
    public List<IronCfgValue?> Elements { get; set; } = new();
}

public class IronCfgObject : IronCfgValue
{
    public SortedDictionary<uint, IronCfgValue?> Fields { get; set; } = new();
}

public class IronCfgSchema
{
    public List<IronCfgField> Fields { get; set; } = new();
}

public class IronCfgField
{
    public uint FieldId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public byte FieldType { get; set; }
    public bool IsRequired { get; set; }
    public IronCfgSchema? ElementSchema { get; set; }

    // CRITICAL FIX: Cache element field maps for array fields (FieldType == 0x30)
    // Must be set during schema parsing when element schema is known
    public IReadOnlyDictionary<string, (byte[] nameBytes, byte typeCode)>? ElementFieldMapByName { get; set; }
    public IReadOnlyDictionary<uint, (byte[] nameBytes, byte typeCode)>? ElementFieldMapById { get; set; }
}
