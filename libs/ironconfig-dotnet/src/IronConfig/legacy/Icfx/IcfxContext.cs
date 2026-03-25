using System;
using System.Collections.Generic;
using System.Text;

namespace IronConfig.Icfx;

/// <summary>
/// Shared context for ICFX parsing (dictionary, VSP, etc.)
/// </summary>
public class IcfxContext
{
    private readonly byte[] _buffer;
    private readonly IcfxHeader _header;
    private readonly List<string> _dictionary = new();
    private readonly List<byte[]> _vspBytes = new();
    private readonly Dictionary<string, int> _keyIdCache = new(StringComparer.Ordinal);

    public IcfxContext(byte[] buffer, IcfxHeader header)
    {
        _buffer = buffer;
        _header = header;

        // Parse dictionary
        ParseDictionary();

        // Parse VSP if present
        if (header.HasVsp)
            ParseVsp();
    }

    private void ParseDictionary()
    {
        uint pos = _header.DictionaryOffset;
        if (pos >= _buffer.Length)
            throw new InvalidOperationException("Dictionary offset out of bounds");

        // Read key count
        uint keyCount;
        uint consumed;
        if (!IronConfig.Icfx.VarUIntHelper.TryRead(_buffer, (int)pos, out keyCount, out consumed))
            throw new InvalidOperationException("Cannot read dictionary key count");

        pos += consumed;

        // Read all keys
        for (uint i = 0; i < keyCount; i++)
        {
            if (pos >= _buffer.Length)
                throw new InvalidOperationException("Dictionary data out of bounds");

            uint keyLen;
            if (!IronConfig.Icfx.VarUIntHelper.TryRead(_buffer, (int)pos, out keyLen, out consumed))
                throw new InvalidOperationException("Cannot read key length");

            pos += consumed;

            if (pos + keyLen > _buffer.Length)
                throw new InvalidOperationException("Key data out of bounds");

            var keyBytes = new byte[(int)keyLen];
            Array.Copy(_buffer, (int)pos, keyBytes, 0, (int)keyLen);
            string key = Encoding.UTF8.GetString(keyBytes);
            _dictionary.Add(key);
            _keyIdCache[key] = (int)i;

            pos += keyLen;
        }
    }

    private void ParseVsp()
    {
        uint pos = _header.VspOffset;
        if (pos == 0 || pos >= _buffer.Length)
            return;

        // Read string count
        uint stringCount;
        uint consumed;
        if (!IronConfig.Icfx.VarUIntHelper.TryRead(_buffer, (int)pos, out stringCount, out consumed))
            throw new InvalidOperationException("Cannot read VSP string count");

        pos += consumed;

        // Read all strings
        for (uint i = 0; i < stringCount; i++)
        {
            if (pos >= _buffer.Length)
                throw new InvalidOperationException("VSP data out of bounds");

            uint strLen;
            if (!IronConfig.Icfx.VarUIntHelper.TryRead(_buffer, (int)pos, out strLen, out consumed))
                throw new InvalidOperationException("Cannot read string length");

            pos += consumed;

            if (pos + strLen > _buffer.Length)
                throw new InvalidOperationException("String data out of bounds");

            var strBytes = new byte[(int)strLen];
            Array.Copy(_buffer, (int)pos, strBytes, 0, (int)strLen);
            _vspBytes.Add(strBytes);

            pos += strLen;
        }
    }

    /// <summary>
    /// Get key ID by key name (-1 if not found)
    /// </summary>
    public int GetKeyId(string key)
    {
        if (_keyIdCache.TryGetValue(key, out int id))
            return id;
        return -1;
    }

    /// <summary>
    /// Get key name by ID (null if not found)
    /// </summary>
    public string? GetKeyString(int keyId)
    {
        if (keyId < 0 || keyId >= _dictionary.Count)
            return null;
        return _dictionary[keyId];
    }

    /// <summary>
    /// Get VSP string bytes by ID
    /// </summary>
    public byte[] GetVspString(int strId)
    {
        if (strId < 0 || strId >= _vspBytes.Count)
            throw new ArgumentOutOfRangeException(nameof(strId));
        return _vspBytes[strId];
    }

    /// <summary>
    /// Get VSP string (decoded) by ID
    /// </summary>
    public string GetVspStringDecoded(int strId)
    {
        var bytes = GetVspString(strId);
        return Encoding.UTF8.GetString(bytes);
    }

    public int DictionaryCount => _dictionary.Count;
    public int VspCount => _vspBytes.Count;
}
