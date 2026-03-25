using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IronConfig.Icf2;

/// <summary>
/// ICF2 encoder: JSON array of objects -> ICF2 binary
/// </summary>
public class Icf2Encoder
{
    private readonly bool _useCrc32;
    private readonly bool _useBlake3;
    private readonly List<byte> _output = new();

    public Icf2Encoder(bool useCrc32 = false, bool useBlake3 = false)
    {
        _useCrc32 = useCrc32;
        _useBlake3 = useBlake3;
    }

    /// <summary>
    /// Encode JSON array to ICF2 bytes
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

    private byte[] EncodeRecords(List<JsonElement> records)
    {
        _output.Clear();

        // Reserve header
        _output.AddRange(new byte[Icf2Header.HEADER_SIZE]);

        // Collect all keys and sort them
        var allKeys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in record.EnumerateObject())
                {
                    allKeys.Add(prop.Name);
                }
            }
        }

        var keysList = allKeys.ToList();

        // Write prefix dictionary
        uint prefixDictOffset = (uint)_output.Count;
        var prefixDict = new Icf2PrefixDict(keysList.ToArray());
        byte[] prefixDictBytes = prefixDict.Encode();
        _output.AddRange(prefixDictBytes);
        uint prefixDictSize = (uint)prefixDictBytes.Length;

        // Write schema
        uint schemaOffset = (uint)_output.Count;
        byte[] schemaBytes = EncodeSchema(keysList, records);
        _output.AddRange(schemaBytes);
        uint schemaSize = (uint)schemaBytes.Length;

        // Write columns
        uint columnsOffset = (uint)_output.Count;
        byte[] columnsBytes = EncodeColumns(keysList, records);
        _output.AddRange(columnsBytes);
        uint columnsSize = (uint)columnsBytes.Length;

        // Calculate offsets before building header
        uint crcOffset = 0;
        uint blake3Offset = 0;

        if (_useCrc32)
        {
            crcOffset = (uint)_output.Count;
            // Reserve 4 bytes for CRC
            _output.AddRange(new byte[4]);
        }

        if (_useBlake3)
        {
            blake3Offset = (uint)_output.Count;
            // Simplified: just write 32 zero bytes (real implementation would compute BLAKE3)
            _output.AddRange(new byte[32]);
        }

        // Build header (before writing it, so we have FileSize)
        var header = new Icf2Header
        {
            FileSize = (uint)_output.Count,
            PrefixDictOffset = prefixDictOffset,
            PrefixDictSize = prefixDictSize,
            SchemaOffset = schemaOffset,
            SchemaSize = schemaSize,
            ColumnsOffset = columnsOffset,
            ColumnsSize = columnsSize,
            RowIndexOffset = 0,
            RowIndexSize = 0,
            PayloadOffset = 0,
            PayloadSize = 0,
            CrcOffset = crcOffset,
            Blake3Offset = blake3Offset,
            HasPrefixDict = true,
            HasColumns = true,
            HasCrc32 = _useCrc32,
            HasBlake3 = _useBlake3
        };

        // Write header at the beginning
        byte[] headerBytes = header.Serialize();
        for (int i = 0; i < Icf2Header.HEADER_SIZE; i++)
        {
            _output[i] = headerBytes[i];
        }

        // NOW compute and write CRC (after header is in place)
        if (_useCrc32)
        {
            uint crc = ComputeCrc32(_output.ToArray(), 0, (int)crcOffset);
            // Write CRC at the reserved position
            _output[(int)crcOffset] = (byte)(crc & 0xFF);
            _output[(int)crcOffset + 1] = (byte)((crc >> 8) & 0xFF);
            _output[(int)crcOffset + 2] = (byte)((crc >> 16) & 0xFF);
            _output[(int)crcOffset + 3] = (byte)((crc >> 24) & 0xFF);
        }

        return _output.ToArray();
    }

    private byte[] EncodeSchema(List<string> keys, List<JsonElement> records)
    {
        var output = new List<byte>();

        // Row count
        WriteVarUInt(output, (uint)records.Count);

        // Field count
        WriteVarUInt(output, (uint)keys.Count);

        // For each field
        for (uint i = 0; i < keys.Count; i++)
        {
            WriteVarUInt(output, i); // keyId
            DetermineBestType(keys[(int)i], records, out byte fieldType);
            output.Add(fieldType);
            output.Add(0); // storage: column_fixed
            WriteVarUInt(output, i); // colId
        }

        return output.ToArray();
    }

    private byte[] EncodeColumns(List<string> keys, List<JsonElement> records)
    {
        var output = new List<byte>();

        for (int colIdx = 0; colIdx < keys.Count; colIdx++)
        {
            string key = keys[colIdx];

            // Determine column type
            DetermineBestType(key, records, out byte fieldType);

            if (fieldType == 5) // str
            {
                // Variable-length column
                output.AddRange(EncodeStringColumn(key, records));
            }
            else
            {
                // Fixed-width column
                output.AddRange(EncodeFixedColumn(fieldType, key, records));
            }
        }

        return output.ToArray();
    }

    private byte[] EncodeStringColumn(string key, List<JsonElement> records)
    {
        var output = new List<byte>();

        // Row count
        WriteVarUInt(output, (uint)records.Count);

        // Collect all strings
        var strings = new List<string>();
        foreach (var record in records)
        {
            string val = "";
            if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty(key, out var prop))
            {
                val = prop.GetString() ?? "";
            }
            strings.Add(val);
        }

        // Write delta-encoded offsets
        uint currentOffset = 0;
        WriteVarUInt(output, currentOffset);

        var blob = new List<byte>();
        foreach (var str in strings)
        {
            blob.AddRange(Encoding.UTF8.GetBytes(str));
            WriteVarUInt(output, (uint)Encoding.UTF8.GetByteCount(str));
            currentOffset += (uint)Encoding.UTF8.GetByteCount(str);
        }

        output.AddRange(blob);
        return output.ToArray();
    }

    private byte[] EncodeFixedColumn(byte fieldType, string key, List<JsonElement> records)
    {
        var output = new List<byte>();

        // Row count
        WriteVarUInt(output, (uint)records.Count);

        foreach (var record in records)
        {
            long val = 0;
            if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty(key, out var prop))
            {
                if (fieldType == 1) // bool
                {
                    val = (prop.ValueKind == JsonValueKind.True) ? 1L : 0L;
                }
                else if (fieldType == 2 || fieldType == 3) // i64, u64
                {
                    // Try to get as int64, if it's a number
                    if (prop.ValueKind == JsonValueKind.Number)
                    {
                        try
                        {
                            val = prop.GetInt64();
                        }
                        catch
                        {
                            // If it's a float or too large, convert from double
                            val = (long)prop.GetDouble();
                        }
                    }
                }
            }

            WriteInt64LE(output, val);
        }

        return output.ToArray();
    }

    private void DetermineBestType(string key, List<JsonElement> records, out byte fieldType)
    {
        fieldType = 0; // null
        foreach (var record in records)
        {
            if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty(key, out var val))
            {
                if (val.ValueKind == JsonValueKind.String)
                {
                    fieldType = 5; // str
                    return;
                }
                else if (val.ValueKind == JsonValueKind.Number)
                {
                    fieldType = 2; // i64
                }
                else if (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False)
                {
                    fieldType = 1; // bool
                }
            }
        }
    }

    private static uint ComputeCrc32(byte[] data, int start, int length)
    {
        // Use proper IEEE CRC32 algorithm
        return Crc32Ieee.Compute(new ReadOnlySpan<byte>(data, start, length));
    }

    private static void WriteVarUInt(List<byte> output, uint value)
    {
        while (true)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value == 0)
            {
                output.Add(b);
                break;
            }
            output.Add((byte)(b | 0x80));
        }
    }

    private static void WriteInt64LE(List<byte> output, long value)
    {
        output.Add((byte)(value & 0xFF));
        output.Add((byte)((value >> 8) & 0xFF));
        output.Add((byte)((value >> 16) & 0xFF));
        output.Add((byte)((value >> 24) & 0xFF));
        output.Add((byte)((value >> 32) & 0xFF));
        output.Add((byte)((value >> 40) & 0xFF));
        output.Add((byte)((value >> 48) & 0xFF));
        output.Add((byte)((value >> 56) & 0xFF));
    }

    private void WriteUInt32LE(uint value)
    {
        _output.Add((byte)(value & 0xFF));
        _output.Add((byte)((value >> 8) & 0xFF));
        _output.Add((byte)((value >> 16) & 0xFF));
        _output.Add((byte)((value >> 24) & 0xFF));
    }
}
