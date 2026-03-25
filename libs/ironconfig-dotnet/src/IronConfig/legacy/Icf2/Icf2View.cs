using System;
using System.Collections.Generic;
using System.Text;

namespace IronConfig.Icf2;

/// <summary>
/// ICF2 zero-copy reader (immutable view of ICF2 file)
/// </summary>
public class Icf2View
{
    private readonly byte[] _data;
    private readonly Icf2Header _header;
    private readonly Icf2PrefixDict _prefixDict;
    private readonly List<FieldMeta> _schema;

    public uint RowCount { get; private set; }

    public static Icf2View Open(byte[] data)
    {
        if (data.Length < Icf2Header.HEADER_SIZE)
            throw new InvalidOperationException("Data too small for ICF2 header");

        var header = Icf2Header.Parse(data);

        // Validate CRC if present
        if (header.HasCrc32)
        {
            uint stored = ReadUInt32LE(data, (int)header.CrcOffset);
            uint computed = ComputeCrc32(data, 0, (int)header.CrcOffset);
            if (stored != computed)
                throw new InvalidOperationException("CRC32 mismatch");
        }

        // Decode prefix dictionary
        var prefixDict = Icf2PrefixDict.Decode(data, header.PrefixDictOffset, header.PrefixDictSize);

        // Decode schema
        var (rowCount, schema) = DecodeSchema(data, header.SchemaOffset, header.SchemaSize);

        var view = new Icf2View(data, header, prefixDict, schema);
        view.RowCount = rowCount;
        return view;
    }

    public string GetString(uint row, uint keyId)
    {
        if (row >= RowCount)
            throw new IndexOutOfRangeException($"Row {row} out of range");

        var field = _schema[(int)keyId];
        if (field.FieldType != 5) // str
            throw new InvalidOperationException("Field is not a string");

        // Read from string column
        uint colOffset = GetColumnOffset(field.ColId);
        return DecodeStringValue(row, (int)field.ColId, colOffset);
    }

    public long GetInt64(uint row, uint keyId)
    {
        if (row >= RowCount)
            throw new IndexOutOfRangeException($"Row {row} out of range");

        var field = _schema[(int)keyId];
        if (field.FieldType != 2 && field.FieldType != 3) // i64, u64
            throw new InvalidOperationException("Field is not i64/u64");

        uint colOffset = GetColumnOffset(field.ColId);
        return DecodeInt64Value(row, colOffset);
    }

    public string GetFieldName(uint keyId)
    {
        if (keyId >= _prefixDict.Count)
            throw new IndexOutOfRangeException($"Key ID {keyId} out of range");
        return _prefixDict.GetKey(keyId);
    }

    private uint GetColumnOffset(uint colId)
    {
        uint offset = _header.ColumnsOffset;
        for (uint i = 0; i < colId; i++)
        {
            var field = _schema[(int)i];
            if (field.FieldType == 5) // str
            {
                offset += SkipStringColumn(_data, (int)offset);
            }
            else
            {
                offset += SkipFixedColumn(_data, (int)offset);
            }
        }
        return offset;
    }

    private string DecodeStringValue(uint row, int colId, uint colOffset)
    {
        uint pos = colOffset;
        uint rowCount = ReadVarUInt(_data, ref pos);

        if (row >= rowCount)
            throw new IndexOutOfRangeException($"Row {row} out of column {colId}");

        // Skip initial offset (always 0)
        ReadVarUInt(_data, ref pos);

        // Read all length values to calculate offset for requested row
        var lengths = new uint[rowCount];
        for (uint i = 0; i < rowCount; i++)
        {
            lengths[i] = ReadVarUInt(_data, ref pos);
        }

        // pos now points to the blob start
        uint blobStart = pos;

        // Calculate offset (sum of all previous row lengths)
        uint strOffset = 0;
        for (uint i = 0; i < row; i++)
        {
            strOffset += lengths[i];
        }

        uint strLength = lengths[row];
        return Encoding.UTF8.GetString(_data, (int)(blobStart + strOffset), (int)strLength);
    }

    private long DecodeInt64Value(uint row, uint colOffset)
    {
        uint pos = colOffset;
        uint rowCount = ReadVarUInt(_data, ref pos);

        if (row >= rowCount)
            throw new IndexOutOfRangeException($"Row {row} out of column");

        uint dataOffset = pos + (row * 8);
        return ReadInt64LE(_data, (int)dataOffset);
    }

    private static uint SkipStringColumn(byte[] data, int offset)
    {
        uint pos = (uint)offset;
        uint rowCount = ReadVarUInt(data, ref pos);

        // Read offset/length values (initial offset + rowCount length values)
        // and accumulate the blob size (sum of all lengths)
        uint totalBlobSize = 0;
        for (uint i = 0; i <= rowCount; i++)
        {
            uint val = ReadVarUInt(data, ref pos);
            // First value (i=0) is initial offset, skip it
            // Remaining values (i>0) are lengths, sum them
            if (i > 0)
                totalBlobSize += val;
        }

        // pos is now at the blob start
        // Column size = metadata size + blob size
        uint columnSize = (pos - (uint)offset) + totalBlobSize;
        return columnSize;
    }

    private static uint SkipFixedColumn(byte[] data, int offset)
    {
        uint pos = (uint)offset;
        uint rowCount = ReadVarUInt(data, ref pos);
        return (rowCount * 8) + (pos - (uint)offset);
    }

    private Icf2View(byte[] data, Icf2Header header, Icf2PrefixDict prefixDict, List<FieldMeta> schema)
    {
        _data = data;
        _header = header;
        _prefixDict = prefixDict;
        _schema = schema;
    }

    private static (uint rowCount, List<FieldMeta> schema) DecodeSchema(byte[] data, uint offset, uint size)
    {
        uint pos = offset;
        uint rowCount = ReadVarUInt(data, ref pos);
        uint fieldCount = ReadVarUInt(data, ref pos);

        var schema = new List<FieldMeta>();
        for (uint i = 0; i < fieldCount; i++)
        {
            uint keyId = ReadVarUInt(data, ref pos);
            byte fieldType = data[pos++];
            byte storage = data[pos++];
            uint colId = ReadVarUInt(data, ref pos);

            schema.Add(new FieldMeta { KeyId = keyId, FieldType = fieldType, Storage = storage, ColId = colId });
        }

        return (rowCount, schema);
    }

    private class FieldMeta
    {
        public uint KeyId { get; set; }
        public byte FieldType { get; set; }
        public byte Storage { get; set; }
        public uint ColId { get; set; }
    }

    private static uint ReadVarUInt(byte[] data, ref uint pos)
    {
        uint value = 0;
        int shift = 0;

        while (pos < data.Length && shift < 35)
        {
            byte b = data[pos++];
            value |= ((uint)(b & 0x7F)) << shift;
            if ((b & 0x80) == 0)
                return value;
            shift += 7;
        }

        throw new InvalidOperationException("VarUInt overflow");
    }

    private static uint ReadUInt32LE(byte[] data, int offset)
    {
        return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }

    private static long ReadInt64LE(byte[] data, int offset)
    {
        return (long)(
            (uint)data[offset] |
            ((uint)data[offset + 1] << 8) |
            ((uint)data[offset + 2] << 16) |
            ((uint)data[offset + 3] << 24) |
            ((long)(uint)data[offset + 4] << 32) |
            ((long)(uint)data[offset + 5] << 40) |
            ((long)(uint)data[offset + 6] << 48) |
            ((long)(uint)data[offset + 7] << 56)
        );
    }

    private static uint ComputeCrc32(byte[] data, int start, int length)
    {
        // Use proper IEEE CRC32 algorithm
        return Crc32Ieee.Compute(new ReadOnlySpan<byte>(data, start, length));
    }
}
