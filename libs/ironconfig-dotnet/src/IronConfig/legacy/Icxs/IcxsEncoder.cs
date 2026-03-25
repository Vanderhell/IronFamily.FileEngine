using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IronConfig.Icxs;

/// <summary>
/// ICXS encoder: converts JSON array of objects to ICXS binary format
/// </summary>
public class IcxsEncoder
{
    private readonly IcxsSchema _schema;
    private readonly bool _useCrc;
    private readonly List<byte> _output = new();

    public IcxsEncoder(IcxsSchema schema, bool useCrc = false)
    {
        _schema = schema;
        _useCrc = useCrc;
    }

    /// <summary>
    /// Encode JSON array to ICXS bytes
    /// </summary>
    public byte[] Encode(JsonElement jsonArray)
    {
        if (jsonArray.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Input must be a JSON array");

        var records = new List<JsonElement>();
        foreach (var item in jsonArray.EnumerateArray())
        {
            records.Add(item);
        }

        return EncodeRecords(records);
    }

    /// <summary>
    /// Encode records to ICXS
    /// </summary>
    private byte[] EncodeRecords(List<JsonElement> records)
    {
        _output.Clear();

        // Reserve space for header
        _output.AddRange(new byte[IcxsHeader.HEADER_SIZE]);

        // Write schema block
        uint schemaBlockOffset = (uint)_output.Count;
        WriteSchemaBlock();

        // Write data block
        uint dataBlockOffset = (uint)_output.Count;
        WriteDataBlock(records);

        // Prepare header info (but don't write it yet)
        uint crcOffset = 0;
        if (_useCrc)
        {
            crcOffset = (uint)_output.Count; // CRC position will be right after data
        }

        // Build header content (we'll write it to the buffer later)
        var header = new IcxsHeader
        {
            Magic = Encoding.ASCII.GetBytes("ICXS"),
            Version = 0,
            Flags = (byte)(_useCrc ? 0x01 : 0x00),
            SchemaHash = _schema.ComputeHash(),
            SchemaBlockOffset = schemaBlockOffset,
            DataBlockOffset = dataBlockOffset,
            CrcOffset = crcOffset
        };

        // Write header content to buffer (first 64 bytes)
        var headerBytes = header.Serialize();
        for (int i = 0; i < IcxsHeader.HEADER_SIZE; i++)
        {
            _output[i] = headerBytes[i];
        }

        // Now compute and write CRC (this needs to be done after header is finalized)
        if (_useCrc)
        {
            // Compute CRC over [0 .. crcOffset) - this is the portion before the CRC itself
            byte[] dataToHash = new byte[crcOffset];
            for (int i = 0; i < crcOffset; i++)
            {
                dataToHash[i] = _output[i];
            }
            uint crc = Crc32Ieee.Compute(dataToHash);
            WriteUInt32LE(crc);
        }

        return _output.ToArray();
    }

    private void WriteSchemaBlock()
    {
        WriteVarUInt((uint)_schema.Fields.Count);

        foreach (var field in _schema.GetFieldsSorted())
        {
            WriteUInt32LE(field.Id);
            _output.Add((byte)field.ToFieldType());
        }
    }

    private void WriteDataBlock(List<JsonElement> records)
    {
        WriteUInt32LE((uint)records.Count);
        uint recordStride = _schema.CalculateRecordStride();
        WriteUInt32LE(recordStride);

        // First pass: collect all variable data and calculate offsets
        var variableBlocks = new List<byte[]>(); // All string data (length-prefixed)
        var recordVariableOffsets = new List<Dictionary<uint, uint>>(); // For each record, fieldId -> offset in var region

        uint varRegionOffset = 0;
        foreach (var record in records)
        {
            var recordOffsets = new Dictionary<uint, uint>();

            foreach (var field in _schema.GetFieldsSorted())
            {
                if (field.IsVariable)
                {
                    string val = "";
                    if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty(field.Name, out var value))
                    {
                        val = value.GetString() ?? "";
                    }

                    // Create variable block for this string
                    var strBytes = Encoding.UTF8.GetBytes(val);
                    var varBlock = new List<byte>();
                    WriteUInt32ToList(varBlock, (uint)strBytes.Length);
                    varBlock.AddRange(strBytes);

                    recordOffsets[field.Id] = varRegionOffset;
                    varRegionOffset += (uint)varBlock.Count;
                    variableBlocks.Add(varBlock.ToArray());
                }
            }

            recordVariableOffsets.Add(recordOffsets);
        }

        // Second pass: write fixed region
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];

            foreach (var field in _schema.GetFieldsSorted())
            {
                if (record.ValueKind != JsonValueKind.Object)
                {
                    WriteFieldDefault(field);
                    continue;
                }

                if (!record.TryGetProperty(field.Name, out var value))
                {
                    WriteFieldDefault(field);
                    continue;
                }

                switch (field.Type)
                {
                    case "i64":
                        WriteInt64LE(value.GetInt64());
                        break;
                    case "u64":
                        WriteUInt64LE(value.GetUInt64());
                        break;
                    case "f64":
                        WriteFloat64LE(value.GetDouble());
                        break;
                    case "bool":
                        _output.Add(value.GetBoolean() ? (byte)0x01 : (byte)0x00);
                        break;
                    case "str":
                        {
                            uint offset = recordVariableOffsets[i][field.Id];
                            WriteUInt32LE(offset);
                        }
                        break;
                }
            }
        }

        // Third pass: write variable region
        foreach (var varBlock in variableBlocks)
        {
            _output.AddRange(varBlock);
        }
    }


    private void WriteFieldDefault(IcxsField field)
    {
        switch (field.Type)
        {
            case "i64":
            case "u64":
            case "f64":
                _output.AddRange(new byte[8]);
                break;
            case "bool":
                _output.Add(0x00);
                break;
            case "str":
                WriteUInt32LE(0);
                break;
        }
    }

    private void WriteVarUInt(uint value)
    {
        while (true)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value == 0)
            {
                _output.Add(b);
                break;
            }
            _output.Add((byte)(b | 0x80));
        }
    }

    private void WriteInt64LE(long value)
    {
        _output.Add((byte)(value & 0xFF));
        _output.Add((byte)((value >> 8) & 0xFF));
        _output.Add((byte)((value >> 16) & 0xFF));
        _output.Add((byte)((value >> 24) & 0xFF));
        _output.Add((byte)((value >> 32) & 0xFF));
        _output.Add((byte)((value >> 40) & 0xFF));
        _output.Add((byte)((value >> 48) & 0xFF));
        _output.Add((byte)((value >> 56) & 0xFF));
    }

    private void WriteUInt64LE(ulong value)
    {
        WriteInt64LE((long)value);
    }

    private void WriteFloat64LE(double value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        _output.AddRange(bytes);
    }

    private void WriteUInt32LE(uint value)
    {
        _output.Add((byte)(value & 0xFF));
        _output.Add((byte)((value >> 8) & 0xFF));
        _output.Add((byte)((value >> 16) & 0xFF));
        _output.Add((byte)((value >> 24) & 0xFF));
    }

    private void WriteUInt32ToList(List<byte> list, uint value)
    {
        list.Add((byte)(value & 0xFF));
        list.Add((byte)((value >> 8) & 0xFF));
        list.Add((byte)((value >> 16) & 0xFF));
        list.Add((byte)((value >> 24) & 0xFF));
    }
}
