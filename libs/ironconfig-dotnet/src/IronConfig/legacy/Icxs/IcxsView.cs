using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IronConfig.Icxs;

/// <summary>
/// Zero-copy view of ICXS binary file
/// </summary>
public class IcxsView
{
    private readonly byte[] _buffer;
    private readonly IcxsHeader _header;
    private readonly IcxsSchema _schema;
    private readonly uint _recordCount;
    private readonly uint _recordStride;
    private readonly uint _fixedRegionOffset;
    private readonly uint _variableRegionOffset;

    /// <summary>
    /// Create ICXS view from buffer with external schema (legacy mode)
    /// </summary>
    public IcxsView(byte[] buffer, IcxsSchema schema)
    {
        _buffer = buffer;
        _schema = schema;

        // Parse header
        if (!IcxsHeader.TryParse(buffer, out var header))
            throw new InvalidOperationException("Invalid ICXS header");

        _header = header;

        // Validate
        if (!_header.ValidateOffsets((uint)buffer.Length))
            throw new InvalidOperationException("Invalid ICXS offsets");

        // Verify schema hash
        var expectedHash = schema.ComputeHash();
        if (!expectedHash.SequenceEqual(_header.SchemaHash))
            throw new InvalidOperationException("Schema hash mismatch");

        // Validate CRC if present
        if (_header.HasCrc)
        {
            ValidateCrc();
        }

        // Parse data block header
        uint dataPos = _header.DataBlockOffset;
        _recordCount = ReadUInt32LE(dataPos);
        dataPos += 4;
        _recordStride = ReadUInt32LE(dataPos);
        dataPos += 4;

        _fixedRegionOffset = dataPos;
        _variableRegionOffset = dataPos + _recordCount * _recordStride;

        // Validate bounds
        if (_variableRegionOffset > (uint)buffer.Length)
            throw new InvalidOperationException("Data block exceeds file bounds");
    }

    /// <summary>
    /// Create ICXS view from buffer with embedded schema (self-contained mode)
    /// </summary>
    public IcxsView(byte[] buffer)
    {
        _buffer = buffer;

        // Parse header
        if (!IcxsHeader.TryParse(buffer, out var header))
            throw new InvalidOperationException("Invalid ICXS header");

        _header = header;

        // Validate
        if (!_header.ValidateOffsets((uint)buffer.Length))
            throw new InvalidOperationException("Invalid ICXS offsets");

        // Extract schema from embedded block
        _schema = IcxsSchema.ExtractFromEmbedded(buffer, _header.SchemaBlockOffset);

        // Validate CRC if present
        if (_header.HasCrc)
        {
            ValidateCrc();
        }

        // Parse data block header
        uint dataPos = _header.DataBlockOffset;
        _recordCount = ReadUInt32LE(dataPos);
        dataPos += 4;
        _recordStride = ReadUInt32LE(dataPos);
        dataPos += 4;

        _fixedRegionOffset = dataPos;
        _variableRegionOffset = dataPos + _recordCount * _recordStride;

        // Validate bounds
        if (_variableRegionOffset > (uint)buffer.Length)
            throw new InvalidOperationException("Data block exceeds file bounds");
    }

    /// <summary>
    /// Get record at index (zero-copy)
    /// </summary>
    public IcxsRecordView GetRecord(uint index)
    {
        if (index >= _recordCount)
            throw new IndexOutOfRangeException($"Record index {index} >= record count {_recordCount}");

        return new IcxsRecordView(_buffer, _schema, _fixedRegionOffset, _variableRegionOffset, _recordStride, index);
    }

    /// <summary>
    /// Get total record count
    /// </summary>
    public uint RecordCount => _recordCount;

    /// <summary>
    /// Get schema
    /// </summary>
    public IcxsSchema Schema => _schema;

    /// <summary>
    /// Validate CRC32 if present
    /// </summary>
    private void ValidateCrc()
    {
        if (!_header.HasCrc || _header.CrcOffset == 0)
            return;

        // Read stored CRC
        uint storedCrc = ReadUInt32LE(_header.CrcOffset);

        // Compute CRC over [0 .. crcOffset)
        byte[] dataToHash = new byte[_header.CrcOffset];
        Array.Copy(_buffer, 0, dataToHash, 0, (int)_header.CrcOffset);

        uint computedCrc = Crc32Ieee.Compute(dataToHash);

        if (computedCrc != storedCrc)
            throw new InvalidOperationException("CRC32 mismatch: file is corrupted");
    }

    private uint ReadUInt32LE(uint offset)
    {
        if (offset + 4 > _buffer.Length)
            throw new IndexOutOfRangeException($"Offset {offset} out of bounds");

        return (uint)(
            _buffer[(int)offset] |
            (_buffer[(int)(offset + 1)] << 8) |
            (_buffer[(int)(offset + 2)] << 16) |
            (_buffer[(int)(offset + 3)] << 24)
        );
    }

    /// <summary>
    /// Validate ICXS file with external schema (legacy mode)
    /// </summary>
    public static bool Validate(byte[] buffer, IcxsSchema schema, out string? error)
    {
        error = null;
        try
        {
            var view = new IcxsView(buffer, schema);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Validate ICXS file with embedded schema (self-contained mode)
    /// </summary>
    public static bool ValidateSelfContained(byte[] buffer, out string? error)
    {
        error = null;
        try
        {
            var view = new IcxsView(buffer);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Enumerate all records
    /// </summary>
    public IEnumerable<IcxsRecordView> EnumerateRecords()
    {
        for (uint i = 0; i < _recordCount; i++)
        {
            yield return GetRecord(i);
        }
    }
}

/// <summary>
/// Zero-copy view of a single ICXS record
/// </summary>
public struct IcxsRecordView
{
    private readonly byte[] _buffer;
    private readonly IcxsSchema _schema;
    private readonly uint _fixedRegionOffset;
    private readonly uint _variableRegionOffset;
    private readonly uint _recordStride;
    private readonly uint _recordIndex;

    public IcxsRecordView(
        byte[] buffer,
        IcxsSchema schema,
        uint fixedRegionOffset,
        uint variableRegionOffset,
        uint recordStride,
        uint recordIndex)
    {
        _buffer = buffer;
        _schema = schema;
        _fixedRegionOffset = fixedRegionOffset;
        _variableRegionOffset = variableRegionOffset;
        _recordStride = recordStride;
        _recordIndex = recordIndex;
    }

    /// <summary>
    /// Get int64 field (O(1))
    /// </summary>
    public bool TryGetInt64(uint fieldId, out long value)
    {
        value = 0;
        var field = _schema.GetFieldById(fieldId);
        if (field == null || field.Type != "i64")
            return false;

        var offset = GetFieldOffset(fieldId);
        value = ReadInt64LE(offset);
        return true;
    }

    /// <summary>
    /// Get uint64 field (O(1))
    /// </summary>
    public bool TryGetUInt64(uint fieldId, out ulong value)
    {
        value = 0;
        var field = _schema.GetFieldById(fieldId);
        if (field == null || field.Type != "u64")
            return false;

        var offset = GetFieldOffset(fieldId);
        value = ReadUInt64LE(offset);
        return true;
    }

    /// <summary>
    /// Get float64 field (O(1))
    /// </summary>
    public bool TryGetFloat64(uint fieldId, out double value)
    {
        value = 0;
        var field = _schema.GetFieldById(fieldId);
        if (field == null || field.Type != "f64")
            return false;

        var offset = GetFieldOffset(fieldId);
        value = ReadFloat64LE(offset);
        return true;
    }

    /// <summary>
    /// Get bool field (O(1))
    /// </summary>
    public bool TryGetBool(uint fieldId, out bool value)
    {
        value = false;
        var field = _schema.GetFieldById(fieldId);
        if (field == null || field.Type != "bool")
            return false;

        var offset = GetFieldOffset(fieldId);
        value = _buffer[(int)offset] != 0;
        return true;
    }

    /// <summary>
    /// Get string field (O(1) to get offset, O(n) to decode string)
    /// </summary>
    public bool TryGetString(uint fieldId, out string value)
    {
        value = "";
        var field = _schema.GetFieldById(fieldId);
        if (field == null || field.Type != "str")
            return false;

        var offset = GetFieldOffset(fieldId);
        uint stringOffset = ReadUInt32LE(offset);

        // Read string at variableRegionOffset + stringOffset
        uint stringPos = _variableRegionOffset + stringOffset;
        if (stringPos + 4 > _buffer.Length)
            return false;

        uint stringLen = ReadUInt32LE(stringPos);
        stringPos += 4;

        if (stringPos + stringLen > _buffer.Length)
            return false;

        value = Encoding.UTF8.GetString(_buffer, (int)stringPos, (int)stringLen);
        return true;
    }

    /// <summary>
    /// Calculate field offset in fixed region
    /// </summary>
    private uint GetFieldOffset(uint fieldId)
    {
        uint offset = 0;
        foreach (var field in _schema.GetFieldsSorted())
        {
            if (field.Id == fieldId)
                return _fixedRegionOffset + _recordIndex * _recordStride + offset;

            offset += (uint)field.GetFixedSize();
        }

        throw new InvalidOperationException($"Field {fieldId} not found");
    }

    private long ReadInt64LE(uint offset)
    {
        if (offset + 8 > _buffer.Length)
            throw new IndexOutOfRangeException();

        return (long)(
            _buffer[(int)offset] |
            (_buffer[(int)(offset + 1)] << 8) |
            (_buffer[(int)(offset + 2)] << 16) |
            (_buffer[(int)(offset + 3)] << 24) |
            ((long)_buffer[(int)(offset + 4)] << 32) |
            ((long)_buffer[(int)(offset + 5)] << 40) |
            ((long)_buffer[(int)(offset + 6)] << 48) |
            ((long)_buffer[(int)(offset + 7)] << 56)
        );
    }

    private ulong ReadUInt64LE(uint offset)
    {
        return (ulong)ReadInt64LE(offset);
    }

    private double ReadFloat64LE(uint offset)
    {
        if (offset + 8 > _buffer.Length)
            throw new IndexOutOfRangeException();

        Span<byte> bytes = stackalloc byte[8];
        for (int i = 0; i < 8; i++)
        {
            bytes[i] = _buffer[(int)(offset + i)];
        }
        return BitConverter.ToDouble(bytes);
    }

    private uint ReadUInt32LE(uint offset)
    {
        if (offset + 4 > _buffer.Length)
            throw new IndexOutOfRangeException();

        return (uint)(
            _buffer[(int)offset] |
            (_buffer[(int)(offset + 1)] << 8) |
            (_buffer[(int)(offset + 2)] << 16) |
            (_buffer[(int)(offset + 3)] << 24)
        );
    }
}
