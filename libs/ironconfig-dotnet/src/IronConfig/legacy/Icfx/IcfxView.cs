using System;

namespace IronConfig.Icfx;

/// <summary>
/// Zero-copy view of ICFX binary configuration file
/// </summary>
public class IcfxView
{
    private readonly byte[] _buffer;
    private readonly IcfxHeader _header;
    private readonly IcfxContext _context;
    private IcfxValueView? _rootValue;

    /// <summary>
    /// Create ICFX view from byte array (zero-copy)
    /// </summary>
    public IcfxView(byte[] buffer)
    {
        _buffer = buffer;

        // Parse and validate header
        ReadOnlySpan<byte> bufferSpan = new ReadOnlySpan<byte>(buffer);
        if (!IcfxHeader.TryParse(bufferSpan, out var header))
            throw new InvalidOperationException("Invalid ICFX header");

        _header = header;

        if (!_header.ValidateOffsets())
            throw new InvalidOperationException("Invalid ICFX offsets");

        // Create context (parses dictionary and VSP)
        _context = new IcfxContext(buffer, _header);

        // Validate CRC if present
        if (_header.HasCrc)
        {
            ValidateCrc();
        }
    }

    /// <summary>
    /// Get root value
    /// </summary>
    public IcfxValueView Root
    {
        get
        {
            if (_rootValue == null)
                _rootValue = new IcfxValueView(_buffer, _header.PayloadOffset, _context);
            return _rootValue.Value;
        }
    }

    /// <summary>
    /// Validate CRC32 (if present)
    /// </summary>
    private void ValidateCrc()
    {
        if (!_header.HasCrc || _header.CrcOffset == 0)
            return;

        // Read stored CRC from file
        uint storedCrc = ReadUInt32LE(_header.CrcOffset);

        // Compute CRC over [0 .. crc_offset)
        byte[] dataToHash = new byte[_header.CrcOffset];
        Array.Copy(_buffer, 0, dataToHash, 0, (int)_header.CrcOffset);

        uint computedCrc = Crc32Ieee.Compute(dataToHash);

        if (computedCrc != storedCrc)
            throw new InvalidOperationException("CRC32 mismatch: file is corrupted or invalid");
    }

    /// <summary>
    /// Read little-endian uint32 from offset
    /// </summary>
    private uint ReadUInt32LE(uint offset)
    {
        if (offset + 4 > (uint)_buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return (uint)(
            _buffer[(int)offset] |
            (_buffer[(int)(offset + 1)] << 8) |
            (_buffer[(int)(offset + 2)] << 16) |
            (_buffer[(int)(offset + 3)] << 24)
        );
    }

    /// <summary>
    /// Get header information
    /// </summary>
    public IcfxHeader Header => _header;

    /// <summary>
    /// Get context (dictionary, VSP, etc.)
    /// </summary>
    public IcfxContext Context => _context;

    /// <summary>
    /// Validate ICFX file (static method)
    /// </summary>
    public static bool Validate(byte[] buffer, out string? error)
    {
        error = null;

        try
        {
            var view = new IcfxView(buffer);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

}
