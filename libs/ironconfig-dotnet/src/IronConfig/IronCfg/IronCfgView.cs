using System;

namespace IronConfig.IronCfg;

/// <summary>
/// Zero-copy view of IRONCFG file
/// </summary>
public readonly struct IronCfgView
{
    public ReadOnlyMemory<byte> Buffer { get; }
    public IronCfgHeader Header { get; }

    public IronCfgView(ReadOnlyMemory<byte> buffer, IronCfgHeader header)
    {
        Buffer = buffer;
        Header = header;
    }

    /// <summary>
    /// Get root object data (zero-copy pointer)
    /// </summary>
    public IronCfgError GetRoot(out ReadOnlyMemory<byte> data)
    {
        data = default;

        uint rootOffset = Header.DataOffset;
        uint rootSize = Header.DataSize;

        if (rootOffset > Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, rootOffset);

        if (rootSize > Buffer.Length - rootOffset)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, rootOffset);

        data = Buffer.Slice((int)rootOffset, (int)rootSize);
        return IronCfgError.Ok;
    }

    /// <summary>
    /// Get schema block (zero-copy pointer)
    /// </summary>
    public IronCfgError GetSchema(out ReadOnlyMemory<byte> data)
    {
        data = default;

        uint schemaOffset = Header.SchemaOffset;
        uint schemaSize = Header.SchemaSize;

        if (schemaOffset > Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, schemaOffset);

        if (schemaSize > Buffer.Length - schemaOffset)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, schemaOffset);

        data = Buffer.Slice((int)schemaOffset, (int)schemaSize);
        return IronCfgError.Ok;
    }

    /// <summary>
    /// Get string pool (zero-copy pointer, may be empty if not present)
    /// </summary>
    public IronCfgError GetStringPool(out ReadOnlyMemory<byte> data)
    {
        data = default;

        uint poolOffset = Header.StringPoolOffset;
        uint poolSize = Header.StringPoolSize;

        // String pool is optional
        if (poolOffset == 0)
        {
            data = ReadOnlyMemory<byte>.Empty;
            return IronCfgError.Ok;
        }

        if (poolOffset > Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, poolOffset);

        if (poolSize > Buffer.Length - poolOffset)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, poolOffset);

        data = Buffer.Slice((int)poolOffset, (int)poolSize);
        return IronCfgError.Ok;
    }

    /// <summary>
    /// Get CRC32 value (4 bytes at crc_offset)
    /// </summary>
    public IronCfgError GetCrc32(out uint crc)
    {
        crc = 0;

        if (!Header.HasCrc32)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, Header.CrcOffset);

        uint crcOffset = Header.CrcOffset;
        if (crcOffset + 4 > Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, crcOffset);

        var crcSpan = Buffer.Span.Slice((int)crcOffset, 4);
        crc = (uint)(crcSpan[0] | (crcSpan[1] << 8) | (crcSpan[2] << 16) | (crcSpan[3] << 24));
        return IronCfgError.Ok;
    }

    /// <summary>
    /// Get BLAKE3 hash (32 bytes at blake3_offset)
    /// </summary>
    public IronCfgError GetBlake3(out ReadOnlyMemory<byte> hash)
    {
        hash = default;

        if (!Header.HasBlake3)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, Header.Blake3Offset);

        uint blake3Offset = Header.Blake3Offset;
        if (blake3Offset + 32 > Buffer.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, blake3Offset);

        hash = Buffer.Slice((int)blake3Offset, 32);
        return IronCfgError.Ok;
    }

    /// <summary>
    /// Get file size
    /// </summary>
    public uint GetFileSize() => Header.FileSize;

    /// <summary>
    /// Check if file has CRC32
    /// </summary>
    public bool HasCrc32() => Header.HasCrc32;

    /// <summary>
    /// Check if file has BLAKE3
    /// </summary>
    public bool HasBlake3() => Header.HasBlake3;

    /// <summary>
    /// Check if schema is embedded
    /// </summary>
    public bool HasEmbeddedSchema() => Header.HasEmbeddedSchema;
}
