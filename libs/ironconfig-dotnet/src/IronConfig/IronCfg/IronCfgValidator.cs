using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using IronConfig;

namespace IronConfig.IronCfg;

/// <summary>
/// IRONCFG validator with fast and strict modes
/// </summary>
public static class IronCfgValidator
{
    // Hard limits from spec
    private const uint MAX_FILE_SIZE = 256 * 1024 * 1024; // 256 MB
    private const uint MAX_FIELDS = 65536;
    private const uint MAX_ARRAY_ELEMENTS = 1_000_000;
    private const uint MAX_STRING_LENGTH = 16 * 1024 * 1024; // 16 MB
    private const int MAX_RECURSION_DEPTH = 32;
    private const int STRICT_METADATA_CACHE_MAX = 1024;
    private static readonly ConcurrentDictionary<StrictSchemaRefCacheKey, StrictMetadataCacheEntry> StrictSchemaRefCache = new();
    private static readonly ConcurrentDictionary<ulong, StrictMetadataCacheEntry> StrictSchemaContentCache = new();
    private static readonly ConcurrentDictionary<StrictPoolRefCacheKey, byte> StrictPoolRefCache = new();
    private static readonly ConcurrentDictionary<ulong, byte> StrictPoolContentCache = new();
    private readonly record struct SchemaFieldDef(byte TypeCode, Dictionary<uint, SchemaFieldDef>? ElementSchema);
    private readonly record struct StrictMetadataCacheEntry(Dictionary<uint, SchemaFieldDef> SchemaDefs);
    private readonly record struct StrictSchemaRefCacheKey(byte[] Buffer, int Offset, int Length, byte Version);
    private readonly record struct StrictPoolRefCacheKey(byte[] Buffer, int Offset, int Length);

    public static void ResetStrictMetadataCache()
    {
        StrictSchemaRefCache.Clear();
        StrictSchemaContentCache.Clear();
        StrictPoolRefCache.Clear();
        StrictPoolContentCache.Clear();
    }

    /// <summary>
    /// Open and validate header (Fast validation: O(1) header + offset checks only)
    /// Returns IronCfgError.Ok on success, error code otherwise
    /// </summary>
    public static IronCfgError Open(ReadOnlyMemory<byte> buffer, out IronCfgView view)
    {
        view = default;

        var headerError = IronCfgHeader.Parse(buffer.Span, out var header);
        if (!headerError.IsOk)
            return headerError;

        view = new IronCfgView(buffer, header);
        return IronCfgError.Ok;
    }

    /// <summary>
    /// Fast validation (O(1)): header and offset checks only
    /// </summary>
    public static IronCfgError ValidateFast(ReadOnlyMemory<byte> buffer)
    {
        var openError = Open(buffer, out _);
        return openError;
    }

  /// <summary>
  /// Strict validation (O(n)): full canonical validation including schema, data, limits
  /// </summary>
  public static IronCfgError ValidateStrict(ReadOnlyMemory<byte> buffer, IronCfgView view)
  {
      // Step 13: Validate file size limit
      if (view.Header.FileSize > MAX_FILE_SIZE)
          return new IronCfgError(IronCfgErrorCode.LimitExceeded, 8);

      // Step 14: Validate schema block exists and is parseable
      var schemaError = view.GetSchema(out var schemaBlock);
      if (!schemaError.IsOk)
          return schemaError;

      var schema = schemaBlock.Span;
      if (schema.Length == 0)
          return new IronCfgError(IronCfgErrorCode.InvalidSchema, view.Header.SchemaOffset);

      // Step 17: Validate string pool if present
      var poolError = view.GetStringPool(out var poolBlock);
      if (!poolError.IsOk)
          return poolError;

      var poolSpan = poolBlock.Span;

      Dictionary<uint, SchemaFieldDef> schemaDefs;
      if (!TryGetStrictSchemaCache(schemaBlock, view.Header.Version, out var schemaKey, out schemaDefs))
      {
          var schemaDefsError = ParseSchemaDefinitions(
              schema,
              view.Header.Version,
              view.Header.SchemaOffset,
              out schemaDefs);
          if (!schemaDefsError.IsOk)
              return schemaDefsError;

          RememberStrictSchemaCache(schemaKey, schemaDefs);
      }

      if (!TryGetStrictPoolCache(poolBlock, out var poolKey))
      {
          var poolValidateError = ValidateStringPool(poolSpan, view.Header.StringPoolOffset);
          if (!poolValidateError.IsOk)
              return poolValidateError;

          RememberStrictPoolCache(poolKey);
      }

      // Step 18: Validate data block
      var dataError = view.GetRoot(out var dataBlock);
      if (!dataError.IsOk)
          return dataError;

      if (dataBlock.Length == 0)
          return new IronCfgError(IronCfgErrorCode.InvalidSchema, view.Header.DataOffset);

      // Root must be object type (0x40)
      uint dataOffset = 0;
      var dataValidateErr = ValidateValue(
          dataBlock.Span,
          ref dataOffset,
          expectedType: 0x40,
          objectSchema: schemaDefs,
          stringPool: poolSpan,
          stringPoolOffsetBase: view.Header.StringPoolOffset,
          fieldOffsetBase: view.Header.DataOffset,
          stringPoolValidated: true,
          depth: 0);
      if (!dataValidateErr.IsOk)
          return dataValidateErr;

      if (dataOffset != dataBlock.Length)
          return new IronCfgError(IronCfgErrorCode.BoundsViolation, view.Header.DataOffset + dataOffset);

      // Step 19: Validate CRC32 if present
      if (view.Header.HasCrc32)
      {
          var crcError = view.GetCrc32(out var crcValue);
          if (!crcError.IsOk)
              return crcError;

          // Calculate CRC32 over header + schema + pool + data
          uint dataRegionSize = view.Header.DataOffset + view.Header.DataSize;
          var crcSpan = buffer.Span.Slice(0, (int)dataRegionSize);

          uint computed = Crc32Ieee.Compute(crcSpan);
          if (computed != crcValue)
              return new IronCfgError(IronCfgErrorCode.Crc32Mismatch, view.Header.CrcOffset);
      }

      // Step 20: Validate BLAKE3 if present
      if (view.Header.HasBlake3)
      {
          var blake3Error = view.GetBlake3(out var blake3Value);
          if (!blake3Error.IsOk)
              return blake3Error;

          var computed = Blake3Ieee.Compute(buffer.Span.Slice(0, (int)view.Header.Blake3Offset));
          if (!computed.AsSpan().SequenceEqual(blake3Value.Span))
              return new IronCfgError(IronCfgErrorCode.Blake3Mismatch, view.Header.Blake3Offset);
      }

      return IronCfgError.Ok;
  }

    /// <summary>
    /// Decode VarUInt32 from buffer
    /// </summary>
    internal static IronCfgError DecodeVarUInt32(ReadOnlySpan<byte> buffer, uint offset, out uint value, out uint bytesRead)
    {
        value = 0;
        bytesRead = 0;

        if (offset >= buffer.Length)
            return new IronCfgError(IronCfgErrorCode.TruncatedBlock, offset);

        uint result = 0;
        int shift = 0;

        for (int i = 0; i < 5; i++)
        {
            if (offset + i >= buffer.Length)
                return new IronCfgError(IronCfgErrorCode.TruncatedBlock, offset + (uint)i);

            byte b = buffer[(int)(offset + i)];
            if (i == 4 && (b & 0xF0) != 0)
                return new IronCfgError(IronCfgErrorCode.NonMinimalVarint, offset);

            result |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                uint minimalBytes = GetMinimalVarUInt32Bytes(result);
                if ((uint)(i + 1) != minimalBytes)
                    return new IronCfgError(IronCfgErrorCode.NonMinimalVarint, offset);

                value = result;
                bytesRead = (uint)(i + 1);
                return IronCfgError.Ok;
            }

            shift += 7;
        }

        // 5+ bytes = non-minimal or overflow
        return new IronCfgError(IronCfgErrorCode.NonMinimalVarint, offset);
    }

    private static uint GetMinimalVarUInt32Bytes(uint value)
    {
        if (value < (1u << 7)) return 1;
        if (value < (1u << 14)) return 2;
        if (value < (1u << 21)) return 3;
        if (value < (1u << 28)) return 4;
        return 5;
    }

    private static IronCfgError RebaseError(IronCfgError error, uint baseOffset)
    {
        if (error.IsOk)
            return error;

        return new IronCfgError(error.Code, baseOffset + error.Offset);
    }

    /// <summary>
    /// Validate UTF-8 encoding
    /// </summary>
    private static IronCfgError ValidateUtf8(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];

            if ((b & 0x80) == 0)
            {
                // Single byte ASCII
                i++;
            }
            else if ((b & 0xE0) == 0xC0)
            {
                // 2-byte sequence
                if (i + 2 > data.Length)
                    return new IronCfgError(IronCfgErrorCode.InvalidString, (uint)i);

                byte b2 = data[i + 1];
                if ((b2 & 0xC0) != 0x80)
                    return new IronCfgError(IronCfgErrorCode.InvalidString, (uint)i);

                i += 2;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                // 3-byte sequence
                if (i + 3 > data.Length)
                    return new IronCfgError(IronCfgErrorCode.InvalidString, (uint)i);

                byte b2 = data[i + 1];
                byte b3 = data[i + 2];
                if ((b2 & 0xC0) != 0x80 || (b3 & 0xC0) != 0x80)
                    return new IronCfgError(IronCfgErrorCode.InvalidString, (uint)i);

                i += 3;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                // 4-byte sequence
                if (i + 4 > data.Length)
                    return new IronCfgError(IronCfgErrorCode.InvalidString, (uint)i);

                byte b2 = data[i + 1];
                byte b3 = data[i + 2];
                byte b4 = data[i + 3];
                if ((b2 & 0xC0) != 0x80 || (b3 & 0xC0) != 0x80 || (b4 & 0xC0) != 0x80)
                    return new IronCfgError(IronCfgErrorCode.InvalidString, (uint)i);

                i += 4;
            }
            else
            {
                return new IronCfgError(IronCfgErrorCode.InvalidString, (uint)i);
            }
        }

        return IronCfgError.Ok;
    }
	
    private readonly record struct StrictSchemaCacheHandle(
        StrictSchemaRefCacheKey? RefKey,
        ulong? ContentKey);

    private readonly record struct StrictPoolCacheHandle(
        StrictPoolRefCacheKey? RefKey,
        ulong? ContentKey);

    private static bool TryGetStrictSchemaCache(
        ReadOnlyMemory<byte> schema,
        byte schemaVersion,
        out StrictSchemaCacheHandle key,
        out Dictionary<uint, SchemaFieldDef> schemaDefs)
    {
        if (MemoryMarshal.TryGetArray(schema, out ArraySegment<byte> segment) && segment.Array is not null)
        {
            var refKey = new StrictSchemaRefCacheKey(segment.Array, segment.Offset, segment.Count, schemaVersion);
            key = new StrictSchemaCacheHandle(refKey, null);
            if (StrictSchemaRefCache.TryGetValue(refKey, out var entry))
            {
                schemaDefs = entry.SchemaDefs;
                return true;
            }
        }
        else
        {
            ulong contentKey = ComputeStrictSchemaContentKey(schema.Span, schemaVersion);
            key = new StrictSchemaCacheHandle(null, contentKey);
            if (StrictSchemaContentCache.TryGetValue(contentKey, out var entry))
            {
                schemaDefs = entry.SchemaDefs;
                return true;
            }
        }

        schemaDefs = null!;
        return false;
    }

    private static void RememberStrictSchemaCache(StrictSchemaCacheHandle key, Dictionary<uint, SchemaFieldDef> schemaDefs)
    {
        if (StrictSchemaRefCache.Count + StrictSchemaContentCache.Count >= STRICT_METADATA_CACHE_MAX)
        {
            StrictSchemaRefCache.Clear();
            StrictSchemaContentCache.Clear();
        }

        var entry = new StrictMetadataCacheEntry(schemaDefs);
        if (key.RefKey is { } refKey)
            StrictSchemaRefCache[refKey] = entry;
        else if (key.ContentKey is { } contentKey)
            StrictSchemaContentCache[contentKey] = entry;
    }

    private static bool TryGetStrictPoolCache(ReadOnlyMemory<byte> pool, out StrictPoolCacheHandle key)
    {
        if (pool.Length == 0)
        {
            key = default;
            return true;
        }

        if (MemoryMarshal.TryGetArray(pool, out ArraySegment<byte> segment) && segment.Array is not null)
        {
            var refKey = new StrictPoolRefCacheKey(segment.Array, segment.Offset, segment.Count);
            key = new StrictPoolCacheHandle(refKey, null);
            return StrictPoolRefCache.ContainsKey(refKey);
        }

        ulong contentKey = ComputeStrictPoolContentKey(pool.Span);
        key = new StrictPoolCacheHandle(null, contentKey);
        return StrictPoolContentCache.ContainsKey(contentKey);
    }

    private static void RememberStrictPoolCache(StrictPoolCacheHandle key)
    {
        if (key == default)
            return;

        if (StrictPoolRefCache.Count + StrictPoolContentCache.Count >= STRICT_METADATA_CACHE_MAX)
        {
            StrictPoolRefCache.Clear();
            StrictPoolContentCache.Clear();
        }

        if (key.RefKey is { } refKey)
            StrictPoolRefCache[refKey] = 0;
        else if (key.ContentKey is { } contentKey)
            StrictPoolContentCache[contentKey] = 0;
    }

    private static ulong ComputeStrictSchemaContentKey(ReadOnlySpan<byte> schema, byte schemaVersion)
    {
        uint schemaCrc = Crc32Ieee.Compute(schema);
        ulong key = ((ulong)schemaCrc << 32) | (uint)schema.Length;
        key ^= ((ulong)schemaVersion << 57);
        return key;
    }

    private static ulong ComputeStrictPoolContentKey(ReadOnlySpan<byte> pool)
    {
        uint poolCrc = Crc32Ieee.Compute(pool);
        return ((ulong)poolCrc << 32) | (uint)pool.Length;
    }

    private static IronCfgError ParseSchemaDefinitions(
        ReadOnlySpan<byte> schema,
        byte schemaVersion,
        uint schemaBlockOffset,
        out Dictionary<uint, SchemaFieldDef> fields)
    {
        uint offset = 0;
        return ParseSchemaDefinitionsBlock(schema, ref offset, schemaVersion, schemaBlockOffset, out fields);
    }

    private static IronCfgError ParseSchemaDefinitionsBlock(
        ReadOnlySpan<byte> schema,
        ref uint offset,
        byte schemaVersion,
        uint schemaBlockOffset,
        out Dictionary<uint, SchemaFieldDef> fields)
    {
        fields = new Dictionary<uint, SchemaFieldDef>();

        var countError = DecodeVarUInt32(schema, offset, out var fieldCount, out var countBytes);
        if (!countError.IsOk)
            return RebaseError(countError, schemaBlockOffset);
        offset += countBytes;

        uint? prevFieldId = null;
        for (uint i = 0; i < fieldCount; i++)
        {
            var fieldIdError = DecodeVarUInt32(schema, offset, out var fieldId, out var fieldIdBytes);
            if (!fieldIdError.IsOk)
                return RebaseError(fieldIdError, schemaBlockOffset);
            offset += fieldIdBytes;

            if (prevFieldId.HasValue && fieldId <= prevFieldId.Value)
                return new IronCfgError(IronCfgErrorCode.FieldOrderViolation, schemaBlockOffset + offset);
            prevFieldId = fieldId;

            if (offset >= schema.Length)
                return new IronCfgError(IronCfgErrorCode.TruncatedBlock, schemaBlockOffset + offset);

            byte typeCode = schema[(int)offset++];
            if (!IronCfgTypeSystem.IsValidTypeCode(typeCode))
                return new IronCfgError(IronCfgErrorCode.InvalidTypeCode, schemaBlockOffset + offset - 1);

            Dictionary<uint, SchemaFieldDef>? elementSchema = null;
            if (IronCfgTypeSystem.IsCompoundType(typeCode))
            {
                var nameLenErr = DecodeVarUInt32(schema, offset, out var nameLen, out var nameLenBytes);
                if (!nameLenErr.IsOk)
                    return RebaseError(nameLenErr, schemaBlockOffset);
                offset += nameLenBytes;

                if (nameLen > MAX_STRING_LENGTH)
                    return new IronCfgError(IronCfgErrorCode.LimitExceeded, schemaBlockOffset + offset);

                if (offset + nameLen > schema.Length)
                    return new IronCfgError(IronCfgErrorCode.TruncatedBlock, schemaBlockOffset + offset);

                var utf8Err = ValidateUtf8(schema.Slice((int)offset, (int)nameLen));
                if (!utf8Err.IsOk)
                    return new IronCfgError(utf8Err.Code, schemaBlockOffset + offset + utf8Err.Offset);

                offset += nameLen;

                if (IronCfgTypeSystem.HasElementSchema(schemaVersion, typeCode))
                {
                    var elemErr = ParseSchemaDefinitionsBlock(schema, ref offset, schemaVersion, schemaBlockOffset, out var nestedFields);
                    if (!elemErr.IsOk)
                        return elemErr;
                    elementSchema = nestedFields;
                }
            }

            fields[fieldId] = new SchemaFieldDef(typeCode, elementSchema);
        }

        return IronCfgError.Ok;
    }

    private static IronCfgError ValidateValue(
        ReadOnlySpan<byte> data,
        ref uint offset,
        byte expectedType,
        Dictionary<uint, SchemaFieldDef>? objectSchema,
        ReadOnlySpan<byte> stringPool,
        uint stringPoolOffsetBase,
        uint fieldOffsetBase,
        bool stringPoolValidated,
        int depth)
    {
        if (depth > MAX_RECURSION_DEPTH)
            return new IronCfgError(IronCfgErrorCode.RecursionLimitExceeded, fieldOffsetBase + offset);

        if (offset >= data.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldOffsetBase + offset);

        byte actualType = data[(int)offset++];
        if (!IronCfgTypeSystem.IsValidTypeCode(actualType))
            return new IronCfgError(IronCfgErrorCode.InvalidTypeCode, fieldOffsetBase + offset - 1);

        if (!IronCfgTypeSystem.TypeMatchesExpected(expectedType, actualType))
        {
            var code = expectedType == 0x30 ? IronCfgErrorCode.ArrayTypeMismatch : IronCfgErrorCode.FieldTypeMismatch;
            return new IronCfgError(code, fieldOffsetBase + offset - 1);
        }

        switch (actualType)
        {
            case 0x00:
            case 0x01:
            case 0x02:
                return IronCfgError.Ok;

            case 0x10:
            case 0x11:
                if (offset + 8 > data.Length)
                    return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldOffsetBase + offset);
                offset += 8;
                return IronCfgError.Ok;

            case 0x12:
                if (offset + 8 > data.Length)
                    return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldOffsetBase + offset);
                double floatValue = BitConverter.ToDouble(data.Slice((int)offset, 8));
                if (double.IsNaN(floatValue))
                    return new IronCfgError(IronCfgErrorCode.InvalidFloat, fieldOffsetBase + offset);
                if (floatValue == 0.0 && BitConverter.DoubleToInt64Bits(floatValue) < 0)
                    return new IronCfgError(IronCfgErrorCode.InvalidFloat, fieldOffsetBase + offset);
                offset += 8;
                return IronCfgError.Ok;

            case 0x20:
                return ValidateUtf8Value(data, ref offset, fieldOffsetBase);

            case 0x21:
                return ValidateStringIdValue(data, ref offset, stringPool, stringPoolOffsetBase, fieldOffsetBase, stringPoolValidated);

            case 0x22:
                return ValidateBlobValue(data, ref offset, fieldOffsetBase);

            case 0x30:
                return ValidateArrayValue(data, ref offset, objectSchema, stringPool, stringPoolOffsetBase, fieldOffsetBase, stringPoolValidated, depth + 1);

            case 0x40:
                return ValidateObjectValue(data, ref offset, objectSchema, stringPool, stringPoolOffsetBase, fieldOffsetBase, stringPoolValidated, depth + 1);

            default:
                return new IronCfgError(IronCfgErrorCode.InvalidTypeCode, fieldOffsetBase + offset - 1);
        }
    }

    private static IronCfgError ValidateUtf8Value(ReadOnlySpan<byte> data, ref uint offset, uint fieldOffsetBase)
    {
        var lenErr = DecodeVarUInt32(data, offset, out var len, out var lenBytes);
        if (!lenErr.IsOk)
            return RebaseError(lenErr, fieldOffsetBase);
        offset += lenBytes;

        if (len > MAX_STRING_LENGTH)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, fieldOffsetBase + offset);
        if (offset + len > data.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldOffsetBase + offset);

        var utf8Err = ValidateUtf8(data.Slice((int)offset, (int)len));
        if (!utf8Err.IsOk)
            return new IronCfgError(utf8Err.Code, fieldOffsetBase + offset + utf8Err.Offset);

        offset += len;
        return IronCfgError.Ok;
    }

    private static IronCfgError ValidateBlobValue(ReadOnlySpan<byte> data, ref uint offset, uint fieldOffsetBase)
    {
        var lenErr = DecodeVarUInt32(data, offset, out var len, out var lenBytes);
        if (!lenErr.IsOk)
            return RebaseError(lenErr, fieldOffsetBase);
        offset += lenBytes;

        if (len > MAX_STRING_LENGTH)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, fieldOffsetBase + offset);
        if (offset + len > data.Length)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldOffsetBase + offset);

        offset += len;
        return IronCfgError.Ok;
    }

    private static IronCfgError ValidateArrayValue(
        ReadOnlySpan<byte> data,
        ref uint offset,
        Dictionary<uint, SchemaFieldDef>? elementSchema,
        ReadOnlySpan<byte> stringPool,
        uint stringPoolOffsetBase,
        uint fieldOffsetBase,
        bool stringPoolValidated,
        int depth)
    {
        var countErr = DecodeVarUInt32(data, offset, out var count, out var countBytes);
        if (!countErr.IsOk)
            return RebaseError(countErr, fieldOffsetBase);
        offset += countBytes;

        if (count > MAX_ARRAY_ELEMENTS)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, fieldOffsetBase + offset);

        for (uint i = 0; i < count; i++)
        {
            if (offset >= data.Length)
                return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldOffsetBase + offset);

            byte elementType = data.Slice((int)offset)[0];
            if (elementSchema != null && elementType != 0x00 && elementType != 0x40)
                return new IronCfgError(IronCfgErrorCode.ArrayTypeMismatch, fieldOffsetBase + offset);

            var elementErr = ValidateValue(
                data,
                ref offset,
                expectedType: 0x00,
                objectSchema: elementType == 0x40 ? elementSchema : null,
                stringPool: stringPool,
                stringPoolOffsetBase: stringPoolOffsetBase,
                fieldOffsetBase: fieldOffsetBase,
                stringPoolValidated: stringPoolValidated,
                depth: depth);
            if (!elementErr.IsOk)
                return elementErr;
        }

        return IronCfgError.Ok;
    }

    private static IronCfgError ValidateObjectValue(
        ReadOnlySpan<byte> data,
        ref uint offset,
        Dictionary<uint, SchemaFieldDef>? schemaFields,
        ReadOnlySpan<byte> stringPool,
        uint stringPoolOffsetBase,
        uint fieldOffsetBase,
        bool stringPoolValidated,
        int depth)
    {
        var countErr = DecodeVarUInt32(data, offset, out var fieldCount, out var countBytes);
        if (!countErr.IsOk)
            return RebaseError(countErr, fieldOffsetBase);
        offset += countBytes;

        if (fieldCount > MAX_FIELDS)
            return new IronCfgError(IronCfgErrorCode.LimitExceeded, fieldOffsetBase + offset);

        uint? prevFieldId = null;
        for (uint i = 0; i < fieldCount; i++)
        {
            var fieldIdErr = DecodeVarUInt32(data, offset, out var fieldId, out var fieldIdBytes);
            if (!fieldIdErr.IsOk)
                return RebaseError(fieldIdErr, fieldOffsetBase);
            offset += fieldIdBytes;

            if (prevFieldId.HasValue && fieldId <= prevFieldId.Value)
                return new IronCfgError(IronCfgErrorCode.FieldOrderViolation, fieldOffsetBase + offset);
            prevFieldId = fieldId;

            SchemaFieldDef? schemaField = null;
            if (schemaFields != null)
            {
                if (!schemaFields.TryGetValue(fieldId, out var foundField))
                    return new IronCfgError(IronCfgErrorCode.UnknownField, fieldOffsetBase + offset - fieldIdBytes);
                schemaField = foundField;
            }

            var valueErr = ValidateValue(
                data,
                ref offset,
                expectedType: schemaField?.TypeCode ?? (byte)0x00,
                objectSchema: schemaField?.ElementSchema,
                stringPool: stringPool,
                stringPoolOffsetBase: stringPoolOffsetBase,
                fieldOffsetBase: fieldOffsetBase,
                stringPoolValidated: stringPoolValidated,
                depth: depth);
            if (!valueErr.IsOk)
                return valueErr;
        }

        return IronCfgError.Ok;
    }

    private static IronCfgError ValidateStringIdValue(
        ReadOnlySpan<byte> data,
        ref uint offset,
        ReadOnlySpan<byte> stringPool,
        uint stringPoolOffsetBase,
        uint fieldOffsetBase,
        bool stringPoolValidated)
    {
        if (stringPool.Length == 0)
            return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldOffsetBase + offset);

        var idErr = DecodeVarUInt32(data, offset, out var stringIndex, out var idBytes);
        if (!idErr.IsOk)
            return RebaseError(idErr, fieldOffsetBase);

        offset += idBytes;

        var resolveErr = IronCfgTypeSystem.ResolveStringPoolEntry(
            stringPool,
            stringIndex,
            stringPoolOffsetBase,
            out var entryOffset,
            out var entryLength);
        if (!resolveErr.IsOk)
            return resolveErr;

        if (!stringPoolValidated)
        {
            var utf8Err = ValidateUtf8(stringPool.Slice((int)entryOffset, (int)entryLength));
            if (!utf8Err.IsOk)
                return new IronCfgError(utf8Err.Code, stringPoolOffsetBase + entryOffset + utf8Err.Offset);
        }

        return IronCfgError.Ok;
    }

    private static IronCfgError ValidateStringPool(ReadOnlySpan<byte> poolSpan, uint stringPoolOffsetBase)
    {
        if (poolSpan.Length == 0)
            return IronCfgError.Ok;

        uint poolOffset = 0;
        while (poolOffset < poolSpan.Length)
        {
            var strLenError = DecodeVarUInt32(poolSpan, poolOffset, out var strLen, out var strLenBytes);
            if (!strLenError.IsOk)
                return RebaseError(strLenError, stringPoolOffsetBase);

            poolOffset += strLenBytes;

            if (strLen > MAX_STRING_LENGTH)
                return new IronCfgError(IronCfgErrorCode.LimitExceeded, stringPoolOffsetBase + poolOffset);

            if (poolOffset + strLen > poolSpan.Length)
                return new IronCfgError(IronCfgErrorCode.TruncatedBlock, stringPoolOffsetBase + poolOffset);

            var utf8Error = ValidateUtf8(poolSpan.Slice((int)poolOffset, (int)strLen));
            if (!utf8Error.IsOk)
                return new IronCfgError(utf8Error.Code, stringPoolOffsetBase + poolOffset + utf8Error.Offset);

            poolOffset += strLen;
        }

        return IronCfgError.Ok;
    }
}
