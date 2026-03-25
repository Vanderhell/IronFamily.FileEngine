using System;
using System.Collections.Generic;
using System.Text;

namespace IronConfig.Icfx;

/// <summary>
/// Zero-copy view of ICFX values in a binary buffer
/// </summary>
public struct IcfxValueView
{
    private readonly byte[] _buffer;
    private readonly uint _offset; // Offset to the type byte of this value
    private readonly IcfxContext _context; // Shared context: dictionary, VSP, etc.

    public IcfxValueView(byte[] buffer, uint offset, IcfxContext context)
    {
        _buffer = buffer;
        _offset = offset;
        _context = context;
    }

    /// <summary>
    /// Get the type byte of this value
    /// </summary>
    public byte TypeByte
    {
        get
        {
            if (_offset >= _buffer.Length)
                throw new ArgumentOutOfRangeException("Value offset out of bounds");
            return _buffer[(int)_offset];
        }
    }

    /// <summary>
    /// Get value as null
    /// </summary>
    public bool IsNull => TypeByte == 0x00;

    /// <summary>
    /// Get value as bool
    /// </summary>
    public bool? GetBool()
    {
        byte type = TypeByte;
        if (type == 0x01) return false;
        if (type == 0x02) return true;
        return null;
    }

    /// <summary>
    /// Get value as int64
    /// </summary>
    public long? GetInt64()
    {
        if (TypeByte != 0x10)
            return null;

        uint pos = _offset + 1;
        if (pos + 8 > _buffer.Length)
            return null;

        long value = (long)(
            _buffer[(int)pos] |
            (_buffer[(int)(pos + 1)] << 8) |
            (_buffer[(int)(pos + 2)] << 16) |
            (_buffer[(int)(pos + 3)] << 24) |
            ((long)_buffer[(int)(pos + 4)] << 32) |
            ((long)_buffer[(int)(pos + 5)] << 40) |
            ((long)_buffer[(int)(pos + 6)] << 48) |
            ((long)_buffer[(int)(pos + 7)] << 56)
        );

        return value;
    }

    /// <summary>
    /// Get value as uint64
    /// </summary>
    public ulong? GetUInt64()
    {
        if (TypeByte != 0x11)
            return null;

        uint pos = _offset + 1;
        if (pos + 8 > _buffer.Length)
            return null;

        ulong value = (ulong)(
            _buffer[(int)pos] |
            (_buffer[(int)(pos + 1)] << 8) |
            (_buffer[(int)(pos + 2)] << 16) |
            (_buffer[(int)(pos + 3)] << 24) |
            ((long)_buffer[(int)(pos + 4)] << 32) |
            ((long)_buffer[(int)(pos + 5)] << 40) |
            ((long)_buffer[(int)(pos + 6)] << 48) |
            ((long)_buffer[(int)(pos + 7)] << 56)
        );

        return value;
    }

    /// <summary>
    /// Get value as float64
    /// </summary>
    public double? GetFloat64()
    {
        if (TypeByte != 0x12)
            return null;

        uint pos = _offset + 1;
        if (pos + 8 > _buffer.Length)
            return null;

        Span<byte> bytes = stackalloc byte[8];
        Array.Copy(_buffer, (int)pos, bytes.ToArray(), 0, 8);
        return BitConverter.ToDouble(bytes);
    }

    /// <summary>
    /// Get value as string bytes (zero-copy)
    /// </summary>
    public byte[] GetStringBytes()
    {
        if (TypeByte == 0x20) // Inline string
        {
            uint pos = _offset + 1;
            uint len;
            uint consumed;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out len, out consumed))
                throw new InvalidOperationException("Invalid string length encoding");

            pos += consumed;
            if (pos + len > _buffer.Length)
                throw new InvalidOperationException("String data out of bounds");

            var result = new byte[(int)len];
            Array.Copy(_buffer, (int)pos, result, 0, (int)len);
            return result;
        }

        if (TypeByte == 0x22) // String ID (VSP reference)
        {
            uint pos = _offset + 1;
            uint strId;
            uint consumed;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out strId, out consumed))
                throw new InvalidOperationException("Invalid string ID encoding");

            return _context.GetVspString((int)strId);
        }

        throw new InvalidOperationException("Not a string value");
    }

    /// <summary>
    /// Get value as string (decoded)
    /// </summary>
    public string GetString()
    {
        var bytes = GetStringBytes();
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Get value as array view
    /// </summary>
    public IcfxArrayView? GetArray()
    {
        if (TypeByte != 0x30)
            return null;

        uint pos = _offset + 1;
        uint count;
        uint consumed;

        if (!VarUIntHelper.TryRead(_buffer, (int)pos, out count, out consumed))
            return null;

        return new IcfxArrayView(_buffer, _offset, count, _context);
    }

    /// <summary>
    /// Get value as object view (handles both regular 0x40 and indexed 0x41)
    /// </summary>
    public IcfxObjectView? GetObject()
    {
        byte type = TypeByte;
        if (type == 0x40 || type == 0x41)
        {
            return new IcfxObjectView(_buffer, _offset, _context, isIndexed: type == 0x41);
        }
        return null;
    }
}

/// <summary>
/// Zero-copy view of ICFX array
/// </summary>
public struct IcfxArrayView
{
    private readonly byte[] _buffer;
    private readonly uint _arrayOffset; // Offset to array type byte
    private readonly uint _count;
    private readonly IcfxContext _context;

    public IcfxArrayView(byte[] buffer, uint arrayOffset, uint count, IcfxContext context)
    {
        _buffer = buffer;
        _arrayOffset = arrayOffset;
        _count = count;
        _context = context;
    }

    public uint Count => _count;

    /// <summary>
    /// Get element at index (zero-copy)
    /// </summary>
    public IcfxValueView GetElement(uint index)
    {
        if (index >= _count)
            throw new IndexOutOfRangeException();

        uint pos = _arrayOffset + 1;
        uint countRead;
        uint consumed;

        // Read count
        if (!VarUIntHelper.TryRead(_buffer, (int)pos, out countRead, out consumed))
            throw new InvalidOperationException("Cannot read array count");

        pos += consumed;

        // Read offsets to find element
        for (uint i = 0; i <= index; i++)
        {
            uint offset;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out offset, out consumed))
                throw new InvalidOperationException("Cannot read element offset");

            if (i == index)
            {
                uint elementValueOffset = _arrayOffset + offset;
                // Guard: offset must be within buffer bounds
                if (elementValueOffset >= _buffer.Length)
                    throw new InvalidOperationException($"Element offset {elementValueOffset} exceeds buffer length {_buffer.Length}");
                return new IcfxValueView(_buffer, elementValueOffset, _context);
            }

            pos += consumed;
        }

        throw new InvalidOperationException("Element not found");
    }

    /// <summary>
    /// Enumerate array elements (zero-copy)
    /// </summary>
    public ArrayEnumerator GetEnumerator() => new(_buffer, _arrayOffset, _count, _context);

    public struct ArrayEnumerator
    {
        private readonly byte[] _buffer;
        private readonly uint _arrayOffset;
        private readonly uint _count;
        private readonly IcfxContext _context;
        private uint _index;
        private uint _currentPos;
        private bool _initialized;

        public ArrayEnumerator(byte[] buffer, uint arrayOffset, uint count, IcfxContext context)
        {
            _buffer = buffer;
            _arrayOffset = arrayOffset;
            _count = count;
            _context = context;
            _index = 0;
            _currentPos = arrayOffset + 1;
            _initialized = false;
        }

        public IcfxValueView Current { get; private set; }

        public bool MoveNext()
        {
            if (_index >= _count)
                return false;

            if (!_initialized)
            {
                // Skip count
                uint countVal;
                uint consumed;
                if (!VarUIntHelper.TryRead(_buffer, (int)_currentPos, out countVal, out consumed))
                    return false;
                _currentPos += consumed;
                _initialized = true;
            }

            if (_index < _count)
            {
                // Read offset
                uint offset;
                uint consumed;
                if (!VarUIntHelper.TryRead(_buffer, (int)_currentPos, out offset, out consumed))
                    return false;

                _currentPos += consumed;
                uint elementOffset = _arrayOffset + offset;
                Current = new IcfxValueView(_buffer, elementOffset, _context);
                _index++;
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// Zero-copy view of ICFX object (supports both regular 0x40 and indexed 0x41)
/// </summary>
public struct IcfxObjectView
{
    private readonly byte[] _buffer;
    private readonly uint _objectOffset; // Offset to object type byte
    private readonly IcfxContext _context;
    private readonly bool _isIndexed; // true for 0x41, false for 0x40

    public IcfxObjectView(byte[] buffer, uint objectOffset, IcfxContext context, bool isIndexed = false)
    {
        _buffer = buffer;
        _objectOffset = objectOffset;
        _context = context;
        _isIndexed = isIndexed;
    }

    /// <summary>
    /// Get value by key (zero-copy scan)
    /// </summary>
    public bool TryGetValue(string key, out IcfxValueView value)
    {
        value = default;

        int keyId = _context.GetKeyId(key);
        if (keyId < 0)
            return false;

        return TryGetValueByKeyId((uint)keyId, out value);
    }

    /// <summary>
    /// Get value by key ID (O(n) for regular, O(1) avg for indexed)
    /// </summary>
    public bool TryGetValueByKeyId(uint keyId, out IcfxValueView value)
    {
        value = default;

        if (_isIndexed)
        {
            return TryGetValueByKeyIdIndexed(keyId, out value);
        }
        else
        {
            return TryGetValueByKeyIdLinear(keyId, out value);
        }
    }

    /// <summary>
    /// Get value by key ID using linear scan (regular object, 0x40)
    /// Uses fast-path optimization: scan pairs without materializing values until match
    /// </summary>
    private bool TryGetValueByKeyIdLinear(uint keyId, out IcfxValueView value)
    {
        value = default;

        uint pos = _objectOffset + 1;
        uint count;
        uint consumed;

        if (!VarUIntHelper.TryRead(_buffer, (int)pos, out count, out consumed))
            return false;

        pos += consumed;

        // Fast-path: scan pairs, match keyId, then return value
        // Avoids materializing IcfxValueView objects for non-matching pairs
        for (uint i = 0; i < count; i++)
        {
            uint currentKeyId;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out currentKeyId, out consumed))
                return false;

            uint keyIdPos = pos;
            pos += consumed;

            uint valueOffset;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out valueOffset, out consumed))
                return false;

            pos += consumed;

            // Found matching keyId - return value
            if (currentKeyId == keyId)
            {
                uint valuePos = _objectOffset + valueOffset;
                // Guard: offset must be within buffer bounds
                if (valuePos >= _buffer.Length)
                    throw new InvalidOperationException($"Value offset {valuePos} exceeds buffer length {_buffer.Length}");
                value = new IcfxValueView(_buffer, valuePos, _context);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fast linear lookup with early exit - does not create IcfxValueView until match
    /// Returns true/false only, used for existence checks without materializing value
    /// </summary>
    public bool ContainsKey(string key)
    {
        int keyId = _context.GetKeyId(key);
        if (keyId < 0)
            return false;

        return ContainsKeyId((uint)keyId);
    }

    /// <summary>
    /// Check if object contains a key ID (fast-path for linear objects)
    /// </summary>
    private bool ContainsKeyId(uint keyId)
    {
        if (_isIndexed)
        {
            // For indexed objects, use hash table for fast lookup
            uint pos = _objectOffset + 1;
            uint count;
            uint consumed;

            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out count, out consumed))
                return false;

            pos += consumed;

            // Read all pairs
            var pairs = new (uint KeyId, uint Offset)[count];
            for (uint i = 0; i < count; i++)
            {
                uint pairKeyId;
                if (!VarUIntHelper.TryRead(_buffer, (int)pos, out pairKeyId, out consumed))
                    return false;
                pos += consumed;

                uint pairOffset;
                if (!VarUIntHelper.TryRead(_buffer, (int)pos, out pairOffset, out consumed))
                    return false;
                pos += consumed;

                pairs[i] = (pairKeyId, pairOffset);
            }

            // Read hash table size
            uint tableSize;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out tableSize, out consumed))
                return false;

            pos += consumed;

            if (tableSize < 8 || (tableSize & (tableSize - 1)) != 0)
                return false;

            // Use hash table to find the pair
            uint hash = (keyId * 2654435761U) & (tableSize - 1);
            uint idx = hash;
            uint probes = 0;
            const uint MAX_PROBES = 10000;

            while (probes < MAX_PROBES && probes < tableSize)
            {
                uint tablePos = pos;
                for (uint i = 0; i < idx; i++)
                {
                    uint slotValue;
                    if (!VarUIntHelper.TryRead(_buffer, (int)tablePos, out slotValue, out consumed))
                        return false;
                    tablePos += consumed;
                }

                uint slotEntry;
                if (!VarUIntHelper.TryRead(_buffer, (int)tablePos, out slotEntry, out consumed))
                    return false;

                if (slotEntry == 0xFFFFFFFF)
                    return false;

                if (slotEntry >= count)
                    return false;

                var (pairKeyId, _) = pairs[slotEntry];

                if (pairKeyId == keyId)
                    return true;

                idx = (idx + 1) & (tableSize - 1);
                probes++;
            }

            return false;
        }
        else
        {
            // For linear objects, simple scan
            uint pos = _objectOffset + 1;
            uint count;
            uint consumed;

            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out count, out consumed))
                return false;

            pos += consumed;

            for (uint i = 0; i < count; i++)
            {
                uint currentKeyId;
                if (!VarUIntHelper.TryRead(_buffer, (int)pos, out currentKeyId, out consumed))
                    return false;

                pos += consumed;

                uint valueOffset;
                if (!VarUIntHelper.TryRead(_buffer, (int)pos, out valueOffset, out consumed))
                    return false;

                pos += consumed;

                if (currentKeyId == keyId)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Get value by key ID using hash table lookup (indexed object, 0x41)
    /// </summary>
    private bool TryGetValueByKeyIdIndexed(uint keyId, out IcfxValueView value)
    {
        value = default;

        uint pos = _objectOffset + 1;
        uint count;
        uint consumed;

        // Read pair count
        if (!VarUIntHelper.TryRead(_buffer, (int)pos, out count, out consumed))
            return false;

        pos += consumed;

        // Read all pairs (keyId, offset)
        var pairs = new (uint KeyId, uint Offset)[count];
        for (uint i = 0; i < count; i++)
        {
            uint pairKeyId;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out pairKeyId, out consumed))
                return false;
            pos += consumed;

            uint pairOffset;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out pairOffset, out consumed))
                return false;
            pos += consumed;

            pairs[i] = (pairKeyId, pairOffset);
        }

        // Read hash table size
        uint tableSize;
        if (!VarUIntHelper.TryRead(_buffer, (int)pos, out tableSize, out consumed))
            return false;

        pos += consumed;

        // Validate table size is power of 2
        if (tableSize < 8 || (tableSize & (tableSize - 1)) != 0)
            return false;

        // Use hash table to find the pair
        uint hash = (keyId * 2654435761U) & (tableSize - 1);
        uint idx = hash;
        uint probes = 0;
        const uint MAX_PROBES = 10000; // Safety guard to prevent infinite loops

        while (probes < MAX_PROBES && probes < tableSize)
        {
            // Read slot at idx
            uint slotPos = pos + idx;
            if (slotPos >= _buffer.Length)
                return false;

            // We need to read the slot value, but we need to know where we are in the table
            // We need to recalculate the position for slot idx
            uint tablePos = pos;
            for (uint i = 0; i < idx; i++)
            {
                uint slotValue;
                if (!VarUIntHelper.TryRead(_buffer, (int)tablePos, out slotValue, out consumed))
                    return false;
                tablePos += consumed;
            }

            uint slotEntry;
            if (!VarUIntHelper.TryRead(_buffer, (int)tablePos, out slotEntry, out consumed))
                return false;

            // Check if empty
            if (slotEntry == 0xFFFFFFFF)
                return false; // Not found

            // Check if it's a valid pair index
            if (slotEntry >= count)
                return false; // Invalid

            // Get the pair
            var (pairKeyId, pairOffset) = pairs[slotEntry];

            if (pairKeyId == keyId)
            {
                // Found it!
                uint valuePos = _objectOffset + pairOffset;
                // Guard: offset must be within buffer bounds
                if (valuePos >= _buffer.Length)
                    throw new InvalidOperationException($"Value offset {valuePos} exceeds buffer length {_buffer.Length}");
                value = new IcfxValueView(_buffer, valuePos, _context);
                return true;
            }

            // Linear probe
            idx = (idx + 1) & (tableSize - 1);
            probes++;
        }

        return false; // Not found or hash table corrupted
    }

    /// <summary>
    /// Validate indexed object structure (ensures hash table consistency)
    /// </summary>
    public bool ValidateIfIndexed()
    {
        if (!_isIndexed)
            return true; // Regular objects are validated by decoder

        uint pos = _objectOffset + 1;
        uint count;
        uint consumed;

        // Read pair count
        if (!VarUIntHelper.TryRead(_buffer, (int)pos, out count, out consumed))
            return false;

        pos += consumed;

        // Read all pairs and collect keyIds
        var keyIds = new uint[count];
        var offsets = new uint[count];
        for (uint i = 0; i < count; i++)
        {
            uint keyId;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out keyId, out consumed))
                return false;
            pos += consumed;
            keyIds[i] = keyId;

            uint offset;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out offset, out consumed))
                return false;
            pos += consumed;
            offsets[i] = offset;
        }

        // Validate keyIds are sorted ascending
        for (uint i = 1; i < count; i++)
        {
            if (keyIds[i] <= keyIds[i - 1])
                return false; // Not strictly ascending
        }

        // Read hash table size
        uint tableSize;
        if (!VarUIntHelper.TryRead(_buffer, (int)pos, out tableSize, out consumed))
            return false;

        pos += consumed;

        // Validate table size is power of 2 and correct
        if (tableSize < 8 || (tableSize & (tableSize - 1)) != 0)
            return false;

        uint expectedTableSize = NextPowerOfTwo(count * 2);
        if (tableSize != expectedTableSize)
            return false;

        // Read hash table and validate
        var foundIndices = new bool[count];
        var slotEntries = new uint[tableSize];

        for (uint i = 0; i < tableSize; i++)
        {
            uint slotEntry;
            if (!VarUIntHelper.TryRead(_buffer, (int)pos, out slotEntry, out consumed))
                return false;
            pos += consumed;
            slotEntries[i] = slotEntry;

            // Validate slot entry
            if (slotEntry != 0xFFFFFFFF && slotEntry >= count)
                return false; // Invalid pair index

            if (slotEntry != 0xFFFFFFFF)
            {
                if (foundIndices[slotEntry])
                    return false; // Duplicate pair index
                foundIndices[slotEntry] = true;
            }
        }

        // Validate all pairs are reachable via hash + probe
        for (uint i = 0; i < count; i++)
        {
            if (!foundIndices[i])
                return false; // Unreachable pair
        }

        // Validate each pair is findable via hash function
        for (uint i = 0; i < count; i++)
        {
            uint keyId = keyIds[i];
            uint hash = (keyId * 2654435761U) & (tableSize - 1);
            uint idx = hash;
            uint probes = 0;

            while (probes < tableSize)
            {
                uint slotEntry = slotEntries[idx];

                if (slotEntry == 0xFFFFFFFF)
                    return false; // Empty slot before finding pair

                if (slotEntry == i)
                    break; // Found this pair

                idx = (idx + 1) & (tableSize - 1);
                probes++;
            }

            if (probes >= tableSize)
                return false; // Pair not findable via hash + probe
        }

        return true;
    }

    /// <summary>
    /// Calculate next power of two (helper for validation)
    /// </summary>
    private static uint NextPowerOfTwo(uint value)
    {
        if (value <= 1) return 1;
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }

    /// <summary>
    /// Enumerate object key-value pairs (zero-copy, works for both regular and indexed objects)
    /// </summary>
    public ObjectEnumerator GetEnumerator() => new(_buffer, _objectOffset, _context, _isIndexed);

    public struct ObjectEnumerator
    {
        private readonly byte[] _buffer;
        private readonly uint _objectOffset;
        private readonly IcfxContext _context;
        private readonly bool _isIndexed;
        private uint _count;
        private uint _index;
        private uint _currentPos;
        private bool _initialized;
        private (uint KeyId, uint Offset)[]? _pairs; // For indexed objects only

        public ObjectEnumerator(byte[] buffer, uint objectOffset, IcfxContext context, bool isIndexed)
        {
            _buffer = buffer;
            _objectOffset = objectOffset;
            _context = context;
            _isIndexed = isIndexed;
            _count = 0;
            _index = 0;
            _currentPos = objectOffset + 1;
            _initialized = false;
            _pairs = null;
            Current = default;
        }

        public (string Key, IcfxValueView Value) Current { get; private set; }

        public bool MoveNext()
        {
            if (!_initialized)
            {
                uint consumed;
                if (!VarUIntHelper.TryRead(_buffer, (int)_currentPos, out _count, out consumed))
                    return false;
                _currentPos += consumed;

                // For indexed objects, read all pairs first
                if (_isIndexed)
                {
                    _pairs = new (uint, uint)[_count];
                    for (uint i = 0; i < _count; i++)
                    {
                        uint keyId;
                        if (!VarUIntHelper.TryRead(_buffer, (int)_currentPos, out keyId, out consumed))
                            return false;
                        _currentPos += consumed;

                        uint offset;
                        if (!VarUIntHelper.TryRead(_buffer, (int)_currentPos, out offset, out consumed))
                            return false;
                        _currentPos += consumed;

                        _pairs[i] = (keyId, offset);
                    }
                }

                _initialized = true;
            }

            if (_index >= _count)
                return false;

            uint currentKeyId;
            uint currentOffset;
            uint consumed2;

            if (_isIndexed)
            {
                // For indexed objects, use the pairs we already read
                if (_pairs == null)
                    return false;
                currentKeyId = _pairs[_index].KeyId;
                currentOffset = _pairs[_index].Offset;
            }
            else
            {
                // For regular objects, read pairs from buffer
                if (!VarUIntHelper.TryRead(_buffer, (int)_currentPos, out currentKeyId, out consumed2))
                    return false;

                _currentPos += consumed2;

                if (!VarUIntHelper.TryRead(_buffer, (int)_currentPos, out currentOffset, out consumed2))
                    return false;

                _currentPos += consumed2;
            }

            string? key = _context.GetKeyString((int)currentKeyId);
            if (key == null)
                return false;

            uint valuePos = _objectOffset + currentOffset;
            // Guard: offset must be within buffer bounds
            if (valuePos >= _buffer.Length)
                throw new InvalidOperationException($"Value offset {valuePos} exceeds buffer length {_buffer.Length}");
            var value = new IcfxValueView(_buffer, valuePos, _context);

            Current = (key, value);
            _index++;
            return true;
        }
    }
}

/// <summary>
/// Helper for reading VarUInt from buffer
/// </summary>
public static class VarUIntHelper
{
    public static bool TryRead(byte[] buffer, int offset, out uint value, out uint bytesRead)
    {
        value = 0;
        bytesRead = 0;

        for (int i = 0; i < 5 && offset + i < buffer.Length; i++)
        {
            byte b = buffer[offset + i];
            value |= (uint)(b & 0x7F) << (7 * i);
            bytesRead++;

            if ((b & 0x80) == 0)
                return true;
        }

        return false;
    }
}
