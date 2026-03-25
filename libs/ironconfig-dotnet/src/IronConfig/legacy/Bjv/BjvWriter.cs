using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IronConfig;

/// <summary>
/// BJV Writer - encodes values to binary BJV format
/// </summary>
public class BjvWriter
{
    private readonly List<byte> _data = new();
    private readonly List<string> _dictionary = new();
    private readonly List<string> _vsp = new();
    private bool _useVsp;
    private bool _useCrc;
    private bool _isBjv4;

    public BjvWriter(bool isBjv4 = false, bool useVsp = false, bool useCrc = false)
    {
        _isBjv4 = isBjv4;
        _useVsp = useVsp;
        _useCrc = useCrc;
    }

    /// <summary>
    /// Get the encoded BJV data
    /// </summary>
    public byte[] ToBytes()
    {
        return _data.ToArray();
    }

    /// <summary>
    /// Write header to stream
    /// </summary>
    public void WriteHeader(Stream output)
    {
        byte[] data = ToBytes();
        output.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Add key to dictionary (returns keyId)
    /// </summary>
    public int RegisterKey(string key)
    {
        int idx = _dictionary.IndexOf(key);
        if (idx >= 0) return idx;

        _dictionary.Add(key);
        return _dictionary.Count - 1;
    }

    /// <summary>
    /// Add string to VSP (returns strId)
    /// </summary>
    public int RegisterString(string str)
    {
        if (!_useVsp) return -1;

        int idx = _vsp.IndexOf(str);
        if (idx >= 0) return idx;

        _vsp.Add(str);
        return _vsp.Count - 1;
    }

    /// <summary>
    /// Encode a null value
    /// </summary>
    public void WriteNull()
    {
        _data.Add(0x00);
    }

    /// <summary>
    /// Encode a boolean
    /// </summary>
    public void WriteBool(bool value)
    {
        _data.Add(value ? (byte)0x02 : (byte)0x01);
    }

    /// <summary>
    /// Encode an int64
    /// </summary>
    public void WriteInt64(long value)
    {
        _data.Add(0x10);
        WriteInt64LE(value);
    }

    /// <summary>
    /// Encode a uint64
    /// </summary>
    public void WriteUInt64(ulong value)
    {
        _data.Add(0x11);
        WriteUInt64LE(value);
    }

    /// <summary>
    /// Encode a double
    /// </summary>
    public void WriteDouble(double value)
    {
        _data.Add(0x12);
        WriteDoubleLE(value);
    }

    /// <summary>
    /// Encode a UTF-8 string
    /// </summary>
    public void WriteString(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        _data.Add(0x20);
        WriteVarUInt((uint)utf8.Length);
        _data.AddRange(utf8);
    }

    /// <summary>
    /// Encode bytes
    /// </summary>
    public void WriteBytes(byte[] value)
    {
        _data.Add(0x21);
        WriteVarUInt((uint)value.Length);
        _data.AddRange(value);
    }

    private void WriteInt64LE(long value)
    {
        ulong u = (ulong)value;
        _data.Add((byte)(u & 0xFF));
        _data.Add((byte)((u >> 8) & 0xFF));
        _data.Add((byte)((u >> 16) & 0xFF));
        _data.Add((byte)((u >> 24) & 0xFF));
        _data.Add((byte)((u >> 32) & 0xFF));
        _data.Add((byte)((u >> 40) & 0xFF));
        _data.Add((byte)((u >> 48) & 0xFF));
        _data.Add((byte)((u >> 56) & 0xFF));
    }

    private void WriteUInt64LE(ulong value)
    {
        _data.Add((byte)(value & 0xFF));
        _data.Add((byte)((value >> 8) & 0xFF));
        _data.Add((byte)((value >> 16) & 0xFF));
        _data.Add((byte)((value >> 24) & 0xFF));
        _data.Add((byte)((value >> 32) & 0xFF));
        _data.Add((byte)((value >> 40) & 0xFF));
        _data.Add((byte)((value >> 48) & 0xFF));
        _data.Add((byte)((value >> 56) & 0xFF));
    }

    private void WriteDoubleLE(double value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        _data.AddRange(bytes);
    }

    private void WriteVarUInt(uint value)
    {
        while (value >= 0x80)
        {
            _data.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        _data.Add((byte)value);
    }
}
