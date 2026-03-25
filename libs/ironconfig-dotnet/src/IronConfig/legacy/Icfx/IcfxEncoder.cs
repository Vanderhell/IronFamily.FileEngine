using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IronConfig.Icfx;

/// <summary>
/// Index mode for ICFX object encoding
/// </summary>
public enum IcfxIndexMode
{
    /// <summary>Always use linear scan objects (type 0x40)</summary>
    Off = 0,

    /// <summary>Always use hash-indexed objects (type 0x41)</summary>
    On = 1,

    /// <summary>Choose per-object: use 0x41 if pairCount >= 13, else 0x40</summary>
    Auto = 2
}

/// <summary>
/// ICFX encoder: converts BjvValueNode to ICFX binary format
/// </summary>
public class IcfxEncoder
{
    // Auto-index threshold: use 0x41 when pairCount >= this
    private const int AUTO_INDEX_THRESHOLD = 13;

    private readonly List<byte> _output = new();
    private readonly List<string> _dictionary = new();
    private readonly List<string> _vsp = new();
    private readonly bool _useVsp;
    private readonly bool _useCrc;
    private readonly IcfxIndexMode _indexMode;
    private bool _hasIndexedObjects = false;

    public IcfxEncoder(bool useVsp = false, bool useCrc = false, bool useIndex = false)
    {
        _useVsp = useVsp;
        _useCrc = useCrc;
        _indexMode = useIndex ? IcfxIndexMode.On : IcfxIndexMode.Off;
    }

    public IcfxEncoder(bool useVsp = false, bool useCrc = false, IcfxIndexMode indexMode = IcfxIndexMode.Off)
    {
        _useVsp = useVsp;
        _useCrc = useCrc;
        _indexMode = indexMode;
    }

    public IcfxEncoder(bool useVsp = false, bool useCrc = false, string indexMode = "off")
    {
        _useVsp = useVsp;
        _useCrc = useCrc;
        _indexMode = indexMode.ToLowerInvariant() switch
        {
            "off" => IcfxIndexMode.Off,
            "on" => IcfxIndexMode.On,
            "auto" => IcfxIndexMode.Auto,
            _ => throw new ArgumentException($"Invalid index mode: {indexMode}. Must be: off, on, or auto")
        };
    }

    /// <summary>
    /// Encode root value to ICFX bytes
    /// </summary>
    public byte[] Encode(BjvValueNode root)
    {
        // Collect all strings (keys and values for VSP)
        var collector = new StringCollector(_useVsp);
        root.Accept(collector);

        // Sort dictionary and VSP canonically
        _dictionary.AddRange(collector.Dictionary.OrderBy(x => x, StringComparer.Ordinal));
        _vsp.AddRange(collector.Vsp.OrderBy(x => x, StringComparer.Ordinal));

        // Reserve 48-byte header
        _output.AddRange(new byte[IcfxHeader.HEADER_SIZE]);

        // Write dictionary
        uint dictOffset = (uint)_output.Count;
        WriteDictionary();
        uint dictSize = (uint)_output.Count - dictOffset;

        // Write VSP if present
        uint vspOffset = 0;
        uint vspSize = 0;
        if (_useVsp)
        {
            vspOffset = (uint)_output.Count;
            WriteVsp();
            vspSize = (uint)_output.Count - vspOffset;
        }

        // Write payload (root value + all nested data)
        uint payloadOffset = (uint)_output.Count;
        root.Accept(new EncoderVisitor(this));
        uint payloadSize = (uint)_output.Count - payloadOffset;

        // Compute CRC offset and file size
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

        // Write header
        WriteHeader(dictOffset, dictSize, vspOffset, vspSize, payloadOffset, payloadSize, crcOffset, fileSize);

        // Compute and write CRC if enabled
        if (_useCrc)
        {
            byte[] dataToHash = new byte[crcOffset];
            _output.CopyTo(0, dataToHash, 0, (int)crcOffset);
            uint crc = Crc32Ieee.Compute(dataToHash);

            _output.Add((byte)(crc & 0xFF));
            _output.Add((byte)((crc >> 8) & 0xFF));
            _output.Add((byte)((crc >> 16) & 0xFF));
            _output.Add((byte)((crc >> 24) & 0xFF));
        }

        return _output.ToArray();
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

    private void WriteHeader(uint dictOffset, uint dictSize, uint vspOffset, uint vspSize,
                            uint payloadOffset, uint payloadSize, uint crcOffset, uint fileSize)
    {
        byte[] header = new byte[IcfxHeader.HEADER_SIZE];

        // Magic: ICFX
        header[0] = (byte)'I';
        header[1] = (byte)'C';
        header[2] = (byte)'F';
        header[3] = (byte)'X';

        // Flags
        byte flags = 0x01; // Little-endian (mandatory)
        if (_useVsp) flags |= 0x02; // VSP present
        if (_useCrc) flags |= 0x04; // CRC present
        if (_hasIndexedObjects) flags |= 0x08; // Index present
        header[4] = flags;

        header[5] = 0; // Reserved

        // Header size (always 48)
        BitConverter.GetBytes((ushort)IcfxHeader.HEADER_SIZE).CopyTo(header, 6);

        // Total file size
        BitConverter.GetBytes(fileSize).CopyTo(header, 8);

        // Dictionary offset
        BitConverter.GetBytes(dictOffset).CopyTo(header, 12);

        // VSP offset
        BitConverter.GetBytes(vspOffset).CopyTo(header, 16);

        // Index table offset (0 for v0)
        BitConverter.GetBytes(0U).CopyTo(header, 20);

        // Payload offset
        BitConverter.GetBytes(payloadOffset).CopyTo(header, 24);

        // CRC offset
        BitConverter.GetBytes(crcOffset).CopyTo(header, 28);

        // Payload size
        BitConverter.GetBytes(payloadSize).CopyTo(header, 32);

        // Dictionary size
        BitConverter.GetBytes(dictSize).CopyTo(header, 36);

        // VSP size
        BitConverter.GetBytes(vspSize).CopyTo(header, 40);

        // Reserved
        BitConverter.GetBytes(0U).CopyTo(header, 44);

        // Copy header to output
        for (int i = 0; i < IcfxHeader.HEADER_SIZE; i++)
            _output[i] = header[i];
    }

    /// <summary>
    /// Write VarUInt (ULEB128 encoding)
    /// </summary>
    public void WriteVarUInt(uint value)
    {
        while (value >= 0x80)
        {
            _output.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        _output.Add((byte)value);
    }

    /// <summary>
    /// Write int64 little-endian
    /// </summary>
    public void WriteInt64LE(long value)
    {
        WriteUInt64LE((ulong)value);
    }

    /// <summary>
    /// Write uint64 little-endian
    /// </summary>
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

    /// <summary>
    /// Write string (UTF-8 encoded with VarUInt length)
    /// </summary>
    public void WriteString(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        WriteVarUInt((uint)utf8.Length);
        _output.AddRange(utf8);
    }

    public List<string> Dictionary => _dictionary;
    public List<string> Vsp => _vsp;
    public List<byte> Output => _output;

    /// <summary>
    /// Compute deterministic hash table for IndexedObject
    /// </summary>
    private static uint[] ComputeHashTable(List<(uint keyId, int pairIndex)> sortedPairs)
    {
        if (sortedPairs.Count == 0)
            throw new InvalidOperationException("Cannot create indexed object with 0 pairs");

        // Calculate table size: nextPow2(pairCount * 2), minimum 8
        uint tableSize = NextPowerOfTwo((uint)sortedPairs.Count * 2);
        if (tableSize < 8) tableSize = 8;

        // Initialize table with EMPTY sentinel
        uint[] table = new uint[tableSize];
        for (int i = 0; i < tableSize; i++)
            table[i] = 0xFFFFFFFF; // EMPTY

        // Place each pair using hash + linear probing
        foreach (var (keyId, pairIndex) in sortedPairs)
        {
            uint hash = (keyId * 2654435761U) & (tableSize - 1);
            uint idx = hash;
            uint probes = 0;

            // Linear probing
            while (table[idx] != 0xFFFFFFFF)
            {
                idx = (idx + 1) & (tableSize - 1);
                probes++;
                if (probes >= tableSize)
                    throw new InvalidOperationException("Hash table exhausted (should not happen in valid implementation)");
            }

            table[idx] = (uint)pairIndex;
        }

        return table;
    }

    /// <summary>
    /// Calculate next power of two
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
    /// Visitor for encoding values to ICFX format
    /// </summary>
    private class EncoderVisitor : IBjvValueVisitor
    {
        private readonly IcfxEncoder _encoder;

        public EncoderVisitor(IcfxEncoder encoder)
        {
            _encoder = encoder;
        }

        public void VisitNull(BjvNullValue value)
        {
            _encoder._output.Add(0x00);
        }

        public void VisitBool(BjvBoolValue value)
        {
            _encoder._output.Add(value.Value ? (byte)0x02 : (byte)0x01);
        }

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
                throw new InvalidOperationException("NaN not allowed in ICFX");

            // Normalize -0.0 to +0.0
            double normalized = (value.Value == 0 && double.IsNegativeInfinity(1.0 / value.Value)) ? 0.0 : value.Value;

            _encoder._output.Add(0x12);
            byte[] bytes = BitConverter.GetBytes(normalized);
            _encoder._output.AddRange(bytes);
        }

        public void VisitString(BjvStringValue value)
        {
            if (_encoder._useVsp && _encoder._vsp.Contains(value.Value))
            {
                // String is in VSP, use reference
                int strId = _encoder._vsp.IndexOf(value.Value);
                _encoder._output.Add(0x22);
                _encoder.WriteVarUInt((uint)strId);
            }
            else
            {
                // Inline string
                _encoder._output.Add(0x20);
                _encoder.WriteString(value.Value);
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
            uint arrayTypeOffset = (uint)_encoder._output.Count - 1;

            _encoder.WriteVarUInt((uint)value.Elements.Count);

            // First, encode all elements into separate buffers to know their sizes
            List<byte[]> encodedElements = new();
            foreach (var element in value.Elements)
            {
                var elemEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
                // Copy the dictionary so nested objects can look up keys
                elemEncoder._dictionary.AddRange(_encoder._dictionary);
                elemEncoder._vsp.AddRange(_encoder._vsp);
                element.Accept(new EncoderVisitor(elemEncoder));
                encodedElements.Add(elemEncoder._output.ToArray());
            }

            // Get count size
            var countEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
            countEncoder.WriteVarUInt((uint)value.Elements.Count);
            uint countSize = (uint)countEncoder._output.Count;

            // Iteratively calculate offsets until offset table size stabilizes
            // The offset table size affects where elements start, which affects offset values
            uint offsetTableTotalSize = (uint)encodedElements.Count; // Start assuming 1 byte per offset
            uint prevOffsetTableSize;
            List<uint> finalOffsets = new();

            do
            {
                prevOffsetTableSize = offsetTableTotalSize;

                // Calculate element positions based on current offset table size estimate
                uint currentPos = 1 + countSize + offsetTableTotalSize;

                finalOffsets.Clear();
                uint elementPosition = currentPos;
                foreach (var encoded in encodedElements)
                {
                    finalOffsets.Add(elementPosition);
                    elementPosition += (uint)encoded.Length;
                }

                // Measure the actual size needed to encode these offsets
                var actualOffsetEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
                foreach (var offset in finalOffsets)
                {
                    actualOffsetEncoder.WriteVarUInt(offset);
                }
                offsetTableTotalSize = (uint)actualOffsetEncoder._output.Count;

                // Loop until stable (usually 1-2 iterations)
            } while (offsetTableTotalSize != prevOffsetTableSize);

            // Write offset table
            foreach (var offset in finalOffsets)
            {
                _encoder.WriteVarUInt(offset);
            }

            // Write actual elements
            foreach (var encoded in encodedElements)
            {
                _encoder._output.AddRange(encoded);
            }
        }

        public void VisitObject(BjvObjectValue value)
        {
            // Sort keys by keyId
            var sortedKeys = value.Fields.Keys
                .Select(k => (Key: k, KeyId: _encoder._dictionary.IndexOf(k)))
                .OrderBy(x => x.KeyId)
                .ToList();

            // Guard: all keys must be in dictionary (IndexOf must not return -1)
            foreach (var item in sortedKeys)
            {
                if (item.KeyId < 0)
                    throw new InvalidOperationException($"Key '{item.Key}' not found in dictionary. Dictionary has {_encoder._dictionary.Count} keys.");
            }

            // Encode all values into separate buffer to know their sizes
            List<byte[]> encodedValues = new();
            foreach (var pair in sortedKeys)
            {
                var valueEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
                // Copy the dictionary and VSP so nested structures can look up keys
                valueEncoder._dictionary.AddRange(_encoder._dictionary);
                valueEncoder._vsp.AddRange(_encoder._vsp);
                value.Fields[pair.Key].Accept(new EncoderVisitor(valueEncoder));
                encodedValues.Add(valueEncoder._output.ToArray());
            }

            // Decide: use indexed object or regular object
            bool useIndexed = false;
            if (sortedKeys.Count > 0)
            {
                useIndexed = _encoder._indexMode switch
                {
                    IcfxIndexMode.Off => false,
                    IcfxIndexMode.On => true,
                    IcfxIndexMode.Auto => sortedKeys.Count >= AUTO_INDEX_THRESHOLD,
                    _ => false
                };
            }

            if (useIndexed)
            {
                EncodeIndexedObject(sortedKeys, encodedValues);
            }
            else
            {
                EncodeRegularObject(sortedKeys, encodedValues);
            }
        }

        private void EncodeRegularObject(List<(string Key, int KeyId)> sortedKeys, List<byte[]> encodedValues)
        {
            _encoder._output.Add(0x40);
            uint objectTypeOffset = (uint)_encoder._output.Count - 1;

            _encoder.WriteVarUInt((uint)sortedKeys.Count);

            // Get count size
            var countEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
            countEncoder.WriteVarUInt((uint)sortedKeys.Count);
            uint countSize = (uint)countEncoder._output.Count;

            // Iteratively calculate offsets until metadata size stabilizes
            // The metadata size affects where values start, which affects offset values
            // Metadata = keyId varuint + offset varuint for each field
            uint metaTotalSize = (uint)sortedKeys.Count * 2; // Start assuming 1 byte per keyId and offset
            uint prevMetaSize;
            List<uint> finalOffsets = new();

            do
            {
                prevMetaSize = metaTotalSize;

                // Calculate value positions based on current metadata size estimate
                uint currentValuePos = 1 + countSize + metaTotalSize;

                finalOffsets.Clear();
                uint valuePosition = currentValuePos;
                foreach (var encoded in encodedValues)
                {
                    finalOffsets.Add(valuePosition);
                    valuePosition += (uint)encoded.Length;
                }

                // Measure the actual size needed to encode metadata
                var actualMetaEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
                for (int i = 0; i < sortedKeys.Count; i++)
                {
                    actualMetaEncoder.WriteVarUInt((uint)sortedKeys[i].KeyId);
                    actualMetaEncoder.WriteVarUInt(finalOffsets[i]);
                }
                metaTotalSize = (uint)actualMetaEncoder._output.Count;

                // Loop until stable (usually 1-2 iterations)
            } while (metaTotalSize != prevMetaSize);

            // Write keyId + offset pairs
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                _encoder.WriteVarUInt((uint)sortedKeys[i].KeyId);
                _encoder.WriteVarUInt(finalOffsets[i]);
            }

            // Write actual values
            foreach (var encoded in encodedValues)
            {
                _encoder._output.AddRange(encoded);
            }
        }

        private void EncodeIndexedObject(List<(string Key, int KeyId)> sortedKeys, List<byte[]> encodedValues)
        {
            _encoder._hasIndexedObjects = true;
            _encoder._output.Add(0x41); // INDEXED_OBJECT type

            // Prepare sortedPairs: (keyId, pairIndex)
            var sortedPairs = new List<(uint, int)>();
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                sortedPairs.Add(((uint)sortedKeys[i].KeyId, i));
            }

            // Compute hash table
            uint[] hashTable = ComputeHashTable(sortedPairs);
            uint tableSize = (uint)hashTable.Length;

            // Write pairCount
            _encoder.WriteVarUInt((uint)sortedKeys.Count);

            // Get count size and table size encoding size for offset calculation
            var countEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
            countEncoder.WriteVarUInt((uint)sortedKeys.Count);
            uint countSize = (uint)countEncoder._output.Count;

            // Iteratively calculate offsets until sizes stabilize
            // The offsets depend on the size of: count, pairs metadata, and table metadata
            uint metaTotalSize = (uint)sortedKeys.Count * 2; // Start estimate: keyId + offset per pair
            uint tableSizeEncoding = 1; // Start estimate: 1 byte for tableSize
            uint tableMetaSize = tableSize; // Start estimate: 1 byte per slot
            uint prevMetaSize;
            List<uint> finalOffsets = new();

            do
            {
                prevMetaSize = metaTotalSize + tableSizeEncoding + tableMetaSize;

                // Calculate value positions
                uint currentValuePos = 1 + countSize + metaTotalSize + tableSizeEncoding + tableMetaSize;

                finalOffsets.Clear();
                uint valuePosition = currentValuePos;
                foreach (var encoded in encodedValues)
                {
                    finalOffsets.Add(valuePosition);
                    valuePosition += (uint)encoded.Length;
                }

                // Measure actual sizes
                var metaEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
                for (int i = 0; i < sortedKeys.Count; i++)
                {
                    metaEncoder.WriteVarUInt((uint)sortedKeys[i].KeyId);
                    metaEncoder.WriteVarUInt(finalOffsets[i]);
                }
                metaTotalSize = (uint)metaEncoder._output.Count;

                var tableSizeEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
                tableSizeEncoder.WriteVarUInt(tableSize);
                tableSizeEncoding = (uint)tableSizeEncoder._output.Count;

                var tableEncoder = new IcfxEncoder(_encoder._useVsp, _encoder._useCrc, _encoder._indexMode);
                foreach (var slot in hashTable)
                {
                    tableEncoder.WriteVarUInt(slot);
                }
                tableMetaSize = (uint)tableEncoder._output.Count;

                // Loop until stable
            } while (metaTotalSize + tableSizeEncoding + tableMetaSize != prevMetaSize);

            // Write pairs: keyId + offset
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                _encoder.WriteVarUInt((uint)sortedKeys[i].KeyId);
                _encoder.WriteVarUInt(finalOffsets[i]);
            }

            // Write hash table size and slots
            _encoder.WriteVarUInt(tableSize);
            foreach (var slot in hashTable)
            {
                _encoder.WriteVarUInt(slot);
            }

            // Write actual values
            foreach (var encoded in encodedValues)
            {
                _encoder._output.AddRange(encoded);
            }
        }
    }

    /// <summary>
    /// Collector for extracting strings from value tree
    /// </summary>
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
