using System;
using System.Collections.Generic;
using System.Text;

namespace IronConfig.Icf2;

/// <summary>
/// Front-coded prefix dictionary decoder for ICF2
/// </summary>
public class Icf2PrefixDict
{
    private readonly string[] _keys;

    public int Count => _keys.Length;

    public string GetKey(uint keyId)
    {
        if (keyId >= _keys.Length)
            throw new IndexOutOfRangeException($"Key ID {keyId} out of range");
        return _keys[keyId];
    }

    public static Icf2PrefixDict Decode(byte[] buffer, uint offset, uint size)
    {
        if (size == 0)
            return new Icf2PrefixDict(Array.Empty<string>());

        uint pos = offset;
        uint endPos = offset + size;

        // Read count
        uint count = ReadVarUInt(buffer, ref pos);
        var keys = new string[count];

        byte[] currentFullKey = Array.Empty<byte>();

        for (uint i = 0; i < count; i++)
        {
            uint commonPrefixLen = ReadVarUInt(buffer, ref pos);
            uint suffixLen = ReadVarUInt(buffer, ref pos);

            if (pos + suffixLen > endPos)
                throw new InvalidOperationException("Prefix dictionary truncated");

            // Reconstruct full key
            var newKey = new byte[commonPrefixLen + suffixLen];
            Array.Copy(currentFullKey, 0, newKey, 0, (int)commonPrefixLen);
            Array.Copy(buffer, (int)pos, newKey, (int)commonPrefixLen, (int)suffixLen);
            pos += suffixLen;

            keys[i] = Encoding.UTF8.GetString(newKey);
            currentFullKey = newKey;
        }

        return new Icf2PrefixDict(keys);
    }

    public byte[] Encode()
    {
        var output = new List<byte>();

        // Write count
        WriteVarUInt(output, (uint)_keys.Length);

        byte[] currentFullKey = Array.Empty<byte>();

        foreach (var key in _keys)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);

            // Find common prefix
            uint commonPrefixLen = 0;
            while (commonPrefixLen < currentFullKey.Length &&
                   commonPrefixLen < keyBytes.Length &&
                   currentFullKey[commonPrefixLen] == keyBytes[commonPrefixLen])
            {
                commonPrefixLen++;
            }

            uint suffixLen = (uint)(keyBytes.Length - commonPrefixLen);

            // Write commonPrefixLen, suffixLen, suffix
            WriteVarUInt(output, commonPrefixLen);
            WriteVarUInt(output, suffixLen);
            output.AddRange(keyBytes.AsSpan((int)commonPrefixLen, (int)suffixLen));

            currentFullKey = keyBytes;
        }

        return output.ToArray();
    }

    internal Icf2PrefixDict(string[] keys)
    {
        _keys = keys;
    }

    private static uint ReadVarUInt(byte[] buffer, ref uint pos)
    {
        uint value = 0;
        int shift = 0;

        while (pos < buffer.Length && shift < 35)
        {
            byte b = buffer[pos++];
            value |= ((uint)(b & 0x7F)) << shift;
            if ((b & 0x80) == 0)
                return value;
            shift += 7;
        }

        throw new InvalidOperationException("VarUInt overflow");
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
}
