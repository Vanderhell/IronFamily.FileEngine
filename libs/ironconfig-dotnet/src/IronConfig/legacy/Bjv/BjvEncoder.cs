using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IronConfig;

/// <summary>
/// Canonical BJV encoder
/// </summary>
public class BjvEncoder
{
    private readonly List<byte> _output = new();
    private readonly List<string> _dictionary = new();
    private readonly List<string> _vsp = new();
    private readonly bool _isBjv4;
    private readonly bool _useVsp;
    private readonly bool _useCrc;

    public BjvEncoder(bool isBjv4 = false, bool useVsp = false, bool useCrc = false)
    {
        _isBjv4 = isBjv4;
        _useVsp = useVsp;
        _useCrc = useCrc;
    }

    /// <summary>
    /// Encode root value to BJV bytes
    /// </summary>
    public byte[] Encode(BjvValueNode root)
    {
        var collector = new StringCollector(_useVsp);
        root.Accept(collector);

        _dictionary.AddRange(collector.Dictionary.OrderBy(x => x, StringComparer.Ordinal));
        _vsp.AddRange(collector.Vsp.OrderBy(x => x, StringComparer.Ordinal));

        // Reserve header (32 bytes)
        _output.AddRange(new byte[32]);

        // Write dictionary
        uint dictOffset = (uint)_output.Count;
        WriteDictionary();

        // Write VSP if present
        uint vspOffset = _useVsp ? (uint)_output.Count : 0;
        if (_useVsp)
            WriteVsp();

        // Write root value
        uint rootOffset = (uint)_output.Count;
        root.Accept(new EncoderVisitor(this));

        // Determine file size and crc offset
        uint crcOffset = 0;
        uint fileSize;

        if (_useCrc)
        {
            crcOffset = (uint)_output.Count;
            fileSize = crcOffset + 4; // CRC is 4 bytes
        }
        else
        {
            fileSize = (uint)_output.Count;
        }

        // Write header with correct crcOffset and fileSize
        WriteHeaderAndFinalize(dictOffset, vspOffset, rootOffset, crcOffset, fileSize);

        // Now compute and write CRC trailer if enabled
        if (_useCrc)
        {
            // Compute CRC over header + data (up to crcOffset)
            byte[] dataToHash = new byte[crcOffset];
            _output.CopyTo(0, dataToHash, 0, (int)crcOffset);
            uint crc = Crc32Ieee.Compute(dataToHash);

            // Write CRC as little-endian uint32
            _output.Add((byte)(crc & 0xFF));
            _output.Add((byte)((crc >> 8) & 0xFF));
            _output.Add((byte)((crc >> 16) & 0xFF));
            _output.Add((byte)((crc >> 24) & 0xFF));
        }

        return _output.ToArray();
    }

    public EncodeResult EncodeWithStats(BjvValueNode root)
    {
        byte[] data = Encode(root);
        return new EncodeResult
        {
            Data = data,
            DictionaryCount = _dictionary.Count,
            VspCount = _vsp.Count,
            FileSize = data.Length
        };
    }

    private void WriteDictionary()
    {
        WriteVarUInt((uint)_dictionary.Count);
        foreach (var key in _dictionary)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(key);
            WriteVarUInt((uint)utf8.Length);
            _output.AddRange(utf8);
        }
    }

    private void WriteVsp()
    {
        WriteVarUInt((uint)_vsp.Count);
        foreach (var str in _vsp)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(str);
            WriteVarUInt((uint)utf8.Length);
            _output.AddRange(utf8);
        }
    }

    private void WriteHeaderAndFinalize(uint dictOffset, uint vspOffset, uint rootOffset, uint crcOffset, uint fileSize)
    {
        byte[] header = new byte[32];
        header[0] = (byte)'B';
        header[1] = (byte)'J';
        header[2] = (byte)'V';
        header[3] = (byte)(_isBjv4 ? '4' : '2');

        byte flags = 0x01;
        if (_useCrc) flags |= 0x02;
        if (_useVsp) flags |= 0x04;
        header[4] = flags;

        Array.Copy(BitConverter.GetBytes((ushort)32), 0, header, 6, 2);
        Array.Copy(BitConverter.GetBytes(fileSize), 0, header, 8, 4);
        Array.Copy(BitConverter.GetBytes(dictOffset), 0, header, 12, 4);
        Array.Copy(BitConverter.GetBytes(vspOffset), 0, header, 16, 4);
        Array.Copy(BitConverter.GetBytes(rootOffset), 0, header, 20, 4);
        Array.Copy(BitConverter.GetBytes(crcOffset), 0, header, 24, 4);

        for (int i = 0; i < 32; i++)
            _output[i] = header[i];
    }

    public void WriteVarUInt(uint value)
    {
        while (value >= 0x80)
        {
            _output.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        _output.Add((byte)value);
    }

    public void WriteInt64LE(long value)
    {
        WriteUInt64LE((ulong)value);
    }

    public void WriteUInt64LE(ulong value)
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

    public void WriteKeyId(int keyId)
    {
        if (_isBjv4)
        {
            _output.Add((byte)(keyId & 0xFF));
            _output.Add((byte)((keyId >> 8) & 0xFF));
            _output.Add((byte)((keyId >> 16) & 0xFF));
            _output.Add((byte)((keyId >> 24) & 0xFF));
        }
        else
        {
            _output.Add((byte)(keyId & 0xFF));
            _output.Add((byte)((keyId >> 8) & 0xFF));
        }
    }

    public void WriteStringBytes(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        WriteVarUInt((uint)utf8.Length);
        _output.AddRange(utf8);
    }

    public List<string> Dictionary => _dictionary;
    public List<string> Vsp => _vsp;
    public List<byte> Output => _output;

    private class EncoderVisitor : IBjvValueVisitor
    {
        private readonly BjvEncoder _encoder;

        public EncoderVisitor(BjvEncoder encoder)
        {
            _encoder = encoder;
        }

        public void VisitNull(BjvNullValue value) => _encoder._output.Add(0x00);
        public void VisitBool(BjvBoolValue value) => _encoder._output.Add(value.Value ? (byte)0x02 : (byte)0x01);

        public void VisitInt64(BjvInt64Value value)
        {
            _encoder._output.Add(0x10);
            _encoder.WriteInt64LE(value.Value);
        }

        public void VisitUInt64(BjvUInt64Value value)
        {
            _encoder._output.Add(0x11);
            _encoder.WriteUInt64LE(value.Value);
        }

        public void VisitFloat64(BjvFloat64Value value)
        {
            if (double.IsNaN(value.Value))
                throw new InvalidOperationException("NaN not allowed");
            _encoder._output.Add(0x12);
            byte[] bytes = BitConverter.GetBytes(value.Value == 0 && double.IsNegativeInfinity(1.0 / value.Value) ? 0.0 : value.Value);
            _encoder._output.AddRange(bytes);
        }

        public void VisitString(BjvStringValue value)
        {
            if (_encoder._useVsp && _encoder._vsp.Contains(value.Value))
            {
                int strId = _encoder._vsp.IndexOf(value.Value);
                _encoder._output.Add(0x22);
                _encoder.WriteVarUInt((uint)strId);
            }
            else
            {
                _encoder._output.Add(0x20);
                _encoder.WriteStringBytes(value.Value);
            }
        }

        public void VisitBytes(BjvBytesValue value)
        {
            _encoder._output.Add(0x21);
            _encoder.WriteVarUInt((uint)value.Value.Length);
            _encoder._output.AddRange(value.Value);
        }

        public void VisitArray(BjvArrayValue value)
        {
            _encoder._output.Add(0x30);
            _encoder.WriteVarUInt((uint)value.Elements.Count);

            foreach (var element in value.Elements)
                element.Accept(this);
        }

        public void VisitObject(BjvObjectValue value)
        {
            _encoder._output.Add(0x40);

            var sortedKeys = value.Fields.Keys
                .Select(k => new { Key = k, KeyId = _encoder._dictionary.IndexOf(k) })
                .OrderBy(x => x.KeyId)
                .ToList();

            _encoder.WriteVarUInt((uint)sortedKeys.Count);

            foreach (var pair in sortedKeys)
            {
                _encoder.WriteKeyId(pair.KeyId);
                value.Fields[pair.Key].Accept(this);
            }
        }
    }

    private class StringCollector : IBjvValueVisitor
    {
        public HashSet<string> Dictionary { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Vsp { get; } = new(StringComparer.Ordinal);
        private readonly bool _collectVsp;

        public StringCollector(bool collectVsp) => _collectVsp = collectVsp;

        public void VisitNull(BjvNullValue value) { }
        public void VisitBool(BjvBoolValue value) { }
        public void VisitInt64(BjvInt64Value value) { }
        public void VisitUInt64(BjvUInt64Value value) { }
        public void VisitFloat64(BjvFloat64Value value) { }

        public void VisitString(BjvStringValue value)
        {
            if (_collectVsp) Vsp.Add(value.Value);
        }

        public void VisitBytes(BjvBytesValue value) { }

        public void VisitArray(BjvArrayValue value)
        {
            foreach (var element in value.Elements)
                element.Accept(this);
        }

        public void VisitObject(BjvObjectValue value)
        {
            foreach (var key in value.Fields.Keys)
                Dictionary.Add(key);
            foreach (var field in value.Fields.Values)
                field.Accept(this);
        }
    }
}

public class EncodeResult
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int DictionaryCount { get; set; }
    public int VspCount { get; set; }
    public int FileSize { get; set; }
}
