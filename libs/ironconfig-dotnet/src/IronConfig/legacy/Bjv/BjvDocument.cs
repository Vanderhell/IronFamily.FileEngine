using System;
using System.Collections.Generic;
using System.Text;

namespace IronConfig;

/// <summary>
/// Represents a parsed BJV document
/// </summary>
public class BjvDocument
{
    private readonly byte[] _data;
    private readonly bool _isBjv4;
    private readonly List<string> _dictionary = new();
    private readonly List<string> _vsp = new();

    public int Version { get; }
    public bool HasCrc { get; }
    public bool HasVsp { get; }
    public bool IsBjv4 => _isBjv4;
    public IReadOnlyList<string> Dictionary => _dictionary.AsReadOnly();
    public IReadOnlyList<string> Vsp => _vsp.AsReadOnly();
    public BjvValue Root { get; }

    private BjvDocument(byte[] data, bool isBjv4, bool hasCrc, bool hasVsp, List<string> dict, List<string> vsp)
    {
        _data = data;
        _isBjv4 = isBjv4;
        HasCrc = hasCrc;
        HasVsp = hasVsp;
        _dictionary.AddRange(dict);
        _vsp.AddRange(vsp);
        Version = isBjv4 ? 4 : 2;

        // Root value offset is at header[20:24]
        uint rootOff = BitConverter.ToUInt32(_data, 20);
        Root = new BjvValue(_data, rootOff);
    }

    public static BjvDocument Parse(byte[] data)
    {
        if (data == null || data.Length < 32)
            throw new ArgumentException("BJV file too small");

        // Check magic
        bool isBjv2 = data[0] == 'B' && data[1] == 'J' && data[2] == 'V' && data[3] == '2';
        bool isBjv4 = data[0] == 'B' && data[1] == 'J' && data[2] == 'V' && data[3] == '4';

        if (!isBjv2 && !isBjv4)
            throw new InvalidOperationException("Invalid BJV magic");

        // Check flags
        byte flags = data[4];
        if ((flags & 0x01) == 0)
            throw new InvalidOperationException("Little-endian flag not set");

        bool hasCrc = (flags & 0x02) != 0;
        bool hasVsp = (flags & 0x04) != 0;

        if ((flags & 0xf8) != 0)
            throw new InvalidOperationException("Unknown flags");

        // Check reserved
        if (data[5] != 0 || data[28] != 0 || data[29] != 0 || data[30] != 0 || data[31] != 0)
            throw new InvalidOperationException("Reserved fields not zero");

        // Validate file size
        uint totalSize = BitConverter.ToUInt32(data, 8);
        if (totalSize != (uint)data.Length)
            throw new InvalidOperationException("File size mismatch");

        // Read offsets
        uint dictOff = BitConverter.ToUInt32(data, 12);
        uint vspOff = BitConverter.ToUInt32(data, 16);
        uint crcOff = BitConverter.ToUInt32(data, 24);

        // Validate and check CRC if present
        if (hasCrc)
        {
            if (crcOff == 0)
                throw new InvalidOperationException("CRC flag set but crc_offset is 0");
            if (crcOff + 4 != (uint)data.Length)
                throw new InvalidOperationException("CRC offset invalid");

            // Read stored CRC (little-endian)
            uint storedCrc = (uint)(data[crcOff] | (data[crcOff + 1] << 8) | (data[crcOff + 2] << 16) | (data[crcOff + 3] << 24));

            // Compute CRC over data before the trailer
            byte[] dataToHash = new byte[crcOff];
            Array.Copy(data, 0, dataToHash, 0, (int)crcOff);
            uint computedCrc = Crc32Ieee.Compute(dataToHash);

            if (storedCrc != computedCrc)
                throw new InvalidOperationException($"CRC mismatch: expected {computedCrc:X8}, got {storedCrc:X8}");
        }
        else
        {
            if (crcOff != 0)
                throw new InvalidOperationException("CRC offset nonzero but flag not set");
        }

        // Parse dictionary
        var dict = ParseDictionary(data, dictOff);

        // Parse VSP if present
        var vsp = hasVsp ? ParseVsp(data, vspOff) : new List<string>();

        return new BjvDocument(data, isBjv4, hasCrc, hasVsp, dict, vsp);
    }

    private static List<string> ParseDictionary(byte[] data, uint offset)
    {
        var dict = new List<string>();
        int pos = (int)offset;

        // Read key count
        (int count, int consumed) = DecodeVarUInt(data, pos);
        pos += consumed;

        for (int i = 0; i < count; i++)
        {
            (int strlen, int lenConsumed) = DecodeVarUInt(data, pos);
            pos += lenConsumed;

            string key = Encoding.UTF8.GetString(data, pos, strlen);
            dict.Add(key);
            pos += strlen;
        }

        return dict;
    }

    private static List<string> ParseVsp(byte[] data, uint offset)
    {
        var vsp = new List<string>();
        int pos = (int)offset;

        (int count, int consumed) = DecodeVarUInt(data, pos);
        pos += consumed;

        for (int i = 0; i < count; i++)
        {
            (int strlen, int lenConsumed) = DecodeVarUInt(data, pos);
            pos += lenConsumed;

            string str = Encoding.UTF8.GetString(data, pos, strlen);
            vsp.Add(str);
            pos += strlen;
        }

        return vsp;
    }

    internal static (int value, int consumed) DecodeVarUInt(byte[] data, int offset)
    {
        int result = 0;
        int shift = 0;
        int consumed = 0;

        for (int i = 0; i < 5; i++)
        {
            if (offset + i >= data.Length)
                throw new InvalidOperationException("VarUInt read past EOF");

            byte b = data[offset + i];
            consumed++;

            result |= ((b & 0x7f) << shift);

            if ((b & 0x80) == 0)
                return (result, consumed);

            shift += 7;
        }

        throw new InvalidOperationException("VarUInt too long");
    }

    public string? GetKeyById(int keyId)
    {
        if (keyId < 0 || keyId >= _dictionary.Count)
            return null;
        return _dictionary[keyId];
    }

    public int? FindKeyId(string key)
    {
        return _dictionary.IndexOf(key) >= 0 ? _dictionary.IndexOf(key) : null;
    }
}

/// <summary>
/// Represents a BJV value
/// </summary>
public class BjvValue
{
    private readonly byte[] _data;
    private readonly uint _offset;

    public BjvValue(byte[] data, uint offset)
    {
        _data = data;
        _offset = offset;
    }

    public BjvType Type
    {
        get
        {
            if (_offset >= _data.Length)
                return BjvType.Invalid;
            return (BjvType)_data[_offset];
        }
    }

    public bool IsNull => Type == BjvType.Null;
    public bool IsTrue => Type == BjvType.True;
    public bool IsFalse => Type == BjvType.False;

    public long? AsInt64()
    {
        if (Type != BjvType.I64) return null;
        return BitConverter.ToInt64(_data, (int)_offset + 1);
    }

    public ulong? AsUInt64()
    {
        if (Type != BjvType.U64) return null;
        return BitConverter.ToUInt64(_data, (int)_offset + 1);
    }

    public double? AsFloat64()
    {
        if (Type != BjvType.F64) return null;
        return BitConverter.ToDouble(_data, (int)_offset + 1);
    }

    public string? AsString()
    {
        if (Type == BjvType.String)
        {
            (int len, int consumed) = BjvDocument.DecodeVarUInt(_data, (int)_offset + 1);
            return Encoding.UTF8.GetString(_data, (int)_offset + 1 + consumed, len);
        }
        return null;
    }

    public int? AsStringId()
    {
        if (Type == BjvType.StringId)
        {
            (int id, _) = BjvDocument.DecodeVarUInt(_data, (int)_offset + 1);
            return id;
        }
        return null;
    }

    public byte[]? AsBytes()
    {
        if (Type != BjvType.Bytes) return null;
        (int len, int consumed) = BjvDocument.DecodeVarUInt(_data, (int)_offset + 1);
        var result = new byte[len];
        Buffer.BlockCopy(_data, (int)_offset + 1 + consumed, result, 0, len);
        return result;
    }

    public int ArrayLength
    {
        get
        {
            if (Type != BjvType.Array) return 0;
            (int len, _) = BjvDocument.DecodeVarUInt(_data, (int)_offset + 1);
            return len;
        }
    }

    public BjvValue GetArrayElement(int index, bool isBjv4 = false)
    {
        if (Type != BjvType.Array) throw new InvalidOperationException("Not an array");
        (int count, int consumed) = BjvDocument.DecodeVarUInt(_data, (int)_offset + 1);
        if (index < 0 || index >= count) throw new IndexOutOfRangeException();

        // Parse through array elements to find the one at index
        uint pos = _offset + 1 + (uint)consumed;
        for (int i = 0; i < index; i++)
        {
            pos = SkipValue(pos, isBjv4);
        }
        return new BjvValue(_data, pos);
    }

    public int ObjectLength
    {
        get
        {
            if (Type != BjvType.Object) return 0;
            (int len, _) = BjvDocument.DecodeVarUInt(_data, (int)_offset + 1);
            return len;
        }
    }

    public string? GetObjectKey(int index, IReadOnlyList<string> dictionary, bool isBjv4 = false)
    {
        if (Type != BjvType.Object) return null;
        (int count, int consumed) = BjvDocument.DecodeVarUInt(_data, (int)_offset + 1);
        if (index < 0 || index >= count) return null;

        uint pos = _offset + 1 + (uint)consumed;
        int keyIdSize = isBjv4 ? 4 : 2;

        for (int i = 0; i < index; i++)
        {
            // Skip keyId
            pos += (uint)keyIdSize;
            // Skip value
            pos = SkipValue(pos, isBjv4);
        }

        // Read keyId at pos
        int keyId;
        if (isBjv4)
        {
            keyId = _data[pos] | (_data[pos + 1] << 8) | (_data[pos + 2] << 16) | (_data[pos + 3] << 24);
        }
        else
        {
            keyId = _data[pos] | (_data[pos + 1] << 8);
        }

        if (keyId < 0 || keyId >= dictionary.Count) return $"key_{keyId}";
        return dictionary[keyId];
    }

    public BjvValue GetObjectValue(int index, bool isBjv4 = false)
    {
        if (Type != BjvType.Object) throw new InvalidOperationException("Not an object");
        (int count, int consumed) = BjvDocument.DecodeVarUInt(_data, (int)_offset + 1);
        if (index < 0 || index >= count) throw new IndexOutOfRangeException();

        uint pos = _offset + 1 + (uint)consumed;
        int keyIdSize = isBjv4 ? 4 : 2;

        for (int i = 0; i < index; i++)
        {
            pos += (uint)keyIdSize; // Skip keyId
            pos = SkipValue(pos, isBjv4);
        }
        pos += (uint)keyIdSize; // Skip current keyId
        return new BjvValue(_data, pos);
    }

    private uint SkipValue(uint pos, bool isBjv4 = false)
    {
        if (pos >= _data.Length) return pos;

        byte type = _data[pos];
        pos++;

        switch ((BjvType)type)
        {
            case BjvType.Null:
            case BjvType.True:
            case BjvType.False:
                return pos;
            case BjvType.I64:
            case BjvType.U64:
            case BjvType.F64:
                return pos + 8;
            case BjvType.String:
            case BjvType.Bytes:
                (int len, int consumed) = BjvDocument.DecodeVarUInt(_data, (int)pos);
                return pos + (uint)consumed + (uint)len;
            case BjvType.StringId:
                // StringId is just a VarUInt index, no length field
                (int strIdLen, int strIdConsumed) = BjvDocument.DecodeVarUInt(_data, (int)pos);
                return pos + (uint)strIdConsumed;
            case BjvType.Array:
                (int arrLen, int arrConsumed) = BjvDocument.DecodeVarUInt(_data, (int)pos);
                pos += (uint)arrConsumed;
                for (int i = 0; i < arrLen; i++)
                    pos = SkipValue(pos, isBjv4);
                return pos;
            case BjvType.Object:
                (int objLen, int objConsumed) = BjvDocument.DecodeVarUInt(_data, (int)pos);
                pos += (uint)objConsumed;
                int keyIdSize = isBjv4 ? 4 : 2;
                for (int i = 0; i < objLen; i++)
                {
                    pos += (uint)keyIdSize; // keyId size depends on BJV format
                    pos = SkipValue(pos, isBjv4);
                }
                return pos;
            default:
                return pos;
        }
    }
}

public enum BjvType : byte
{
    Null = 0x00,
    False = 0x01,
    True = 0x02,
    I64 = 0x10,
    U64 = 0x11,
    F64 = 0x12,
    String = 0x20,
    Bytes = 0x21,
    StringId = 0x22,
    Array = 0x30,
    Object = 0x40,
    Invalid = 0xff
}
