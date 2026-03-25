using System;
using System.Collections.Generic;
using System.IO.Hashing;
using IronConfig.IronCfg;

public abstract record IronCfgPath;
public record IronCfgKeyPath(string Key) : IronCfgPath;
public record IronCfgIndexPath(uint Index) : IronCfgPath;
public record IronCfgFieldIdPath(uint FieldId) : IronCfgPath;

public static class IronCfgValueReader
{
    private const int MAX_NESTING = 128;
    private readonly record struct SchemaCacheEntry(
        Dictionary<uint, (byte[] nameBytes, byte typeCode)> FieldMap,
        Dictionary<uint, Dictionary<uint, (byte[] nameBytes, byte typeCode)>> ElementFieldMapCache);

    private static Dictionary<uint, (byte[] nameBytes, byte typeCode)>? _lastSchemaMappings;
    private static ulong _lastSchemaCacheKey;

    private static Dictionary<uint, Dictionary<uint, (byte[] nameBytes, byte typeCode)>>? _elementFieldMapCache;

    public static void ResetCaches()
    {
        _lastSchemaMappings = null;
        _lastSchemaCacheKey = 0;
        _elementFieldMapCache = null;
    }

    public static IronCfgError GetBool(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out bool value)
    {
        value = false;
        var err = FindValueByPath(buffer, view, path, out var offset, out var typeCode);
        if (!err.IsOk) return err;

        if (typeCode == 0x01) { value = false; return IronCfgError.Ok; }
        if (typeCode == 0x02) { value = true; return IronCfgError.Ok; }
        return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, offset);
    }

    public static IronCfgError GetInt64(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out long value)
    {
        value = 0;
        var err = FindValueByPath(buffer, view, path, out var offset, out var typeCode);
        if (!err.IsOk) return err;

        if (typeCode != 0x10) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, offset);
        if (offset + 1 + 8 > buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);

        var span = buffer.Span.Slice((int)offset + 1, 8);
        value = BitConverter.ToInt64(span);
        return IronCfgError.Ok;
    }

    public static IronCfgError GetUInt64(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out ulong value)
    {
        value = 0;
        var err = FindValueByPath(buffer, view, path, out var offset, out var typeCode);
        if (!err.IsOk) return err;

        if (typeCode != 0x11) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, offset);
        if (offset + 1 + 8 > buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);

        var span = buffer.Span.Slice((int)offset + 1, 8);
        value = BitConverter.ToUInt64(span);
        return IronCfgError.Ok;
    }

    public static IronCfgError GetFloat64(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out double value)
    {
        value = 0.0;
        var err = FindValueByPath(buffer, view, path, out var offset, out var typeCode);
        if (!err.IsOk) return err;

        if (typeCode != 0x12) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, offset);
        if (offset + 1 + 8 > buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);

        var span = buffer.Span.Slice((int)offset + 1, 8);
        value = BitConverter.ToDouble(span);
        return IronCfgError.Ok;
    }

    public static IronCfgError GetString(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out ReadOnlyMemory<byte> value)
    {
        value = default;
        var err = FindValueByPath(buffer, view, path, out var offset, out var typeCode);
        if (!err.IsOk) return err;

        if (typeCode == IronCfgTypeSystem.StringInline)
        {
            var lenErr = DecodeVarUInt32(buffer.Span, offset + 1, out var strLen, out var lenBytes);
            if (!lenErr.IsOk) return lenErr;

            offset += 1 + lenBytes;
            if (offset + strLen > buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);

            value = buffer.Slice((int)offset, (int)strLen);
            return IronCfgError.Ok;
        }

        if (typeCode == IronCfgTypeSystem.StringId)
        {
            var poolErr = view.GetStringPool(out var poolBlock);
            if (!poolErr.IsOk) return poolErr;

            var idErr = DecodeVarUInt32(buffer.Span, offset + 1, out var stringIndex, out _);
            if (!idErr.IsOk) return idErr;

            var resolveErr = IronCfgTypeSystem.ResolveStringPoolEntry(
                poolBlock.Span,
                stringIndex,
                view.Header.StringPoolOffset,
                out var valueOffset,
                out var valueLength);
            if (!resolveErr.IsOk) return resolveErr;

            value = poolBlock.Slice((int)valueOffset, (int)valueLength);
            return IronCfgError.Ok;
        }

        return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, offset);
    }

    public static IronCfgError GetBytes(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out ReadOnlyMemory<byte> value)
    {
        value = default;
        var err = FindValueByPath(buffer, view, path, out var offset, out var typeCode);
        if (!err.IsOk) return err;

        if (typeCode != 0x22) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, offset);

        var lenErr = DecodeVarUInt32(buffer.Span, offset + 1, out var blobLen, out var lenBytes);
        if (!lenErr.IsOk) return lenErr;

        offset += 1 + lenBytes;
        if (offset + blobLen > buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);

        value = buffer.Slice((int)offset, (int)blobLen);
        return IronCfgError.Ok;
    }

    public static IronCfgError GetArrayLength(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out uint length)
    {
        length = 0;
        var err = FindValueByPath(buffer, view, path, out var offset, out var typeCode);
        if (!err.IsOk) return err;

        if (typeCode != 0x30) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, offset);

        var lenErr = DecodeVarUInt32(buffer.Span, offset + 1, out length, out _);
        return lenErr;
    }

    public static IronCfgError GetObjectFieldCount(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out uint count)
    {
        count = 0;
        var err = FindValueByPath(buffer, view, path, out var offset, out var typeCode);
        if (!err.IsOk) return err;

        if (typeCode != 0x40) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, offset);

        var countErr = DecodeVarUInt32(buffer.Span, offset + 1, out count, out _);
        return countErr;
    }

    private static IronCfgError FindValueByPath(ReadOnlyMemory<byte> buffer, IronCfgView view,
        IronCfgPath[] path, out uint finalOffset, out byte finalTypeCode)
    {
        finalOffset = 0;
        finalTypeCode = 0;

        // Tripwire: Log path navigation (set IRONCFG_TRACE_PATH=1)
        bool tracePathEnabled = System.Environment.GetEnvironmentVariable("IRONCFG_TRACE_PATH") == "1";
        string? tracePathFile = tracePathEnabled ? System.Environment.GetEnvironmentVariable("IRONCFG_TRACE_FILE") ?? "C:\\temp\\ironcfg_path_trace.log" : null;
        void LogPathEvent(string eventJson)
        {
            if (tracePathFile != null)
            {
                try { System.IO.File.AppendAllText(tracePathFile, eventJson + "\n"); }
                catch { }
            }
        }

        if (tracePathEnabled)
            LogPathEvent($"{{\"event\": \"FindValueByPath.START\", \"pathLength\": {path?.Length ?? 0}}}");

        // Parse schema to build fieldId -> (name bytes, type) map
        var schemaErr = view.GetSchema(out var schemaBlock);
        if (!schemaErr.IsOk) return schemaErr;

        ulong schemaCacheKey = ComputeSchemaCacheKey(schemaBlock.Span, view.Header.Version);

        Dictionary<uint, (byte[] nameBytes, byte typeCode)> fieldMap;
        if (_lastSchemaCacheKey == schemaCacheKey && _lastSchemaMappings != null && _elementFieldMapCache != null)
        {
            fieldMap = _lastSchemaMappings;
            if (tracePathEnabled)
                LogPathEvent($"{{\"event\": \"schema.cached\", \"fieldMapSize\": {fieldMap.Count}}}");
        }
        else
        {
            var parseErr = ParseSchema(buffer, view, out var cacheEntry);
            if (!parseErr.IsOk) return parseErr;
            fieldMap = cacheEntry.FieldMap;
            _lastSchemaMappings = cacheEntry.FieldMap;
            _elementFieldMapCache = cacheEntry.ElementFieldMapCache;
            _lastSchemaCacheKey = schemaCacheKey;
            if (tracePathEnabled)
                LogPathEvent($"{{\"event\": \"schema.parsed\", \"fieldMapSize\": {fieldMap.Count}}}");
        }

        if (path == null || path.Length == 0)
        {
            var rootErr = view.GetRoot(out var rootBlock);
            if (!rootErr.IsOk) return rootErr;
            if (rootBlock.Length == 0) return new IronCfgError(IronCfgErrorCode.BoundsViolation, view.Header.DataOffset);

            finalOffset = view.Header.DataOffset;
            finalTypeCode = rootBlock.Span[0];
            return IronCfgError.Ok;
        }

        var dataErr = view.GetRoot(out var dataBlock);
        if (!dataErr.IsOk) return dataErr;

        uint currentOffset = view.Header.DataOffset;
        byte currentType = dataBlock.Span[0];

        if (currentType != 0x40) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, currentOffset);

        // PHASE 3 FIX: Track the field ID of array fields for element fieldMap context switching
        uint currentArrayFieldId = 0;  // Stores fieldId of current array (0 if not in array)

        for (int i = 0; i < path.Length; i++)
        {
            if (i > MAX_NESTING) return new IronCfgError(IronCfgErrorCode.LimitExceeded, currentOffset);

            if (path[i] is IronCfgKeyPath keyPath)
            {
                if (currentType != 0x40) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, currentOffset);

                // Convert key to UTF8 bytes once for comparison
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(keyPath.Key);

                uint countOffset = currentOffset + 1;
                var countErr = DecodeVarUInt32(buffer.Span, countOffset, out var fieldCount, out var countBytes);
                if (!countErr.IsOk) return countErr;

                uint fieldDataOffset = countOffset + countBytes;
                bool found = false;

                for (uint f = 0; f < fieldCount; f++)
                {
                    if (fieldDataOffset >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldDataOffset);

                    var idErr = DecodeVarUInt32(buffer.Span, fieldDataOffset, out var fieldId, out var idBytes);
                    if (!idErr.IsOk) return idErr;
                    fieldDataOffset += idBytes;

                    if (fieldDataOffset >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldDataOffset);
                    byte fieldType = buffer.Span[(int)fieldDataOffset];
                    uint typeCodeOffset = fieldDataOffset;
                    fieldDataOffset++;

                    // Try to match field by name from schema using byte comparison
                    bool matches = false;
                    if (fieldMap.TryGetValue(fieldId, out var schemaField))
                    {
                        matches = ((ReadOnlySpan<byte>)schemaField.nameBytes).SequenceEqual(keyBytes);
                        if (tracePathEnabled)
                        {
                            string schemaFieldName = System.Text.Encoding.UTF8.GetString(schemaField.nameBytes);
                            LogPathEvent($"{{\"event\": \"field.lookup.keyPath\", \"fieldId\": {fieldId}, \"searchKey\": \"{keyPath.Key}\", \"schemaFieldName\": \"{schemaFieldName}\", \"matches\": {matches.ToString().ToLower()}}}");
                        }
                    }
                    else
                    {
                        if (tracePathEnabled)
                            LogPathEvent($"{{\"event\": \"field.lookup.keyPath.notInMap\", \"fieldId\": {fieldId}, \"searchKey\": \"{keyPath.Key}\", \"fieldMapSize\": {fieldMap.Count}}}");
                    }

                    if (matches)
                    {
                        currentOffset = typeCodeOffset;
                        currentType = fieldType;

                        // PHASE 3 FIX: Track array field ID for context switching
                        if (fieldType == 0x30)  // Array type
                            currentArrayFieldId = fieldId;
                        else
                            currentArrayFieldId = 0;  // Reset when leaving array context

                        found = true;
                        break;
                    }

                    var skipErr = SkipValue(buffer.Span, fieldDataOffset, fieldType, out var nextOffset);
                    if (!skipErr.IsOk) return skipErr;
                    fieldDataOffset = nextOffset;
                }

                if (!found)
                {
                    if (tracePathEnabled)
                        LogPathEvent($"{{\"event\": \"keyPath.notFound\", \"searchKey\": \"{keyPath.Key}\", \"fieldCount\": {fieldCount}}}");
                    return new IronCfgError(IronCfgErrorCode.UnknownField, currentOffset);
                }
            }
            else if (path[i] is IronCfgIndexPath indexPath)
            {
                if (currentType != 0x30) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, currentOffset);

                if (tracePathEnabled)
                    LogPathEvent($"{{\"event\": \"indexPath.start\", \"pathStep\": {i}, \"index\": {indexPath.Index}, \"currentTypeBeforeIndex\": \"0x{currentType:X2}\"}}");

                uint lenOffset = currentOffset + 1;
                var lenErr = DecodeVarUInt32(buffer.Span, lenOffset, out var arrayLen, out var lenBytes);
                if (!lenErr.IsOk) return lenErr;

                if (tracePathEnabled)
                    LogPathEvent($"{{\"event\": \"array.info\", \"arrayLen\": {arrayLen}, \"requestedIndex\": {indexPath.Index}}}");

                if (indexPath.Index >= arrayLen) return new IronCfgError(IronCfgErrorCode.BoundsViolation, currentOffset);

                uint elemOffset = lenOffset + lenBytes;

                for (uint e = 0; e < indexPath.Index; e++)
                {
                    if (elemOffset >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, elemOffset);
                    byte elemType = buffer.Span[(int)elemOffset];
                    elemOffset++;  // Skip the type code

                    var skipErr = SkipValue(buffer.Span, elemOffset, elemType, out elemOffset);
                    if (!skipErr.IsOk) return skipErr;
                }

                // elemOffset now points to the type code of the target element
                if (elemOffset >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, elemOffset);
                currentOffset = elemOffset;
                byte elementType = buffer.Span[(int)elemOffset];
                currentType = elementType;

                // PHASE 3 FIX: Switch fieldMap context when navigating into array element records
                bool fieldMapSwitched = false;
                if (currentType == 0x40 && _elementFieldMapCache != null && _elementFieldMapCache.TryGetValue(currentArrayFieldId, out var elemFieldMap))
                {
                    fieldMap = elemFieldMap;
                    fieldMapSwitched = true;
                    if (tracePathEnabled)
                        LogPathEvent($"{{\"event\": \"fieldMap.switched\", \"arrayFieldId\": {currentArrayFieldId}, \"newFieldMapSize\": {fieldMap.Count}}}");
                }
                else if (currentType == 0x40 && _elementFieldMapCache != null)
                {
                    if (tracePathEnabled)
                        LogPathEvent($"{{\"event\": \"fieldMap.notFound\", \"arrayFieldId\": {currentArrayFieldId}, \"cacheSize\": {_elementFieldMapCache.Count}}}");
                }

                if (tracePathEnabled)
                    LogPathEvent($"{{\"event\": \"indexPath.end\", \"elementTypeCode\": \"0x{elementType:X2}\", \"currentTypeAfterIndex\": \"0x{currentType:X2}\", \"fieldMapSwitched\": {fieldMapSwitched.ToString().ToLower()}, \"fieldMapSize\": {fieldMap.Count}}}");
            }
            else if (path[i] is IronCfgFieldIdPath fieldIdPath)
            {
                if (currentType != 0x40) return new IronCfgError(IronCfgErrorCode.FieldTypeMismatch, currentOffset);

                if (tracePathEnabled)
                    LogPathEvent($"{{\"event\": \"fieldIdPath.start\", \"pathStep\": {i}, \"searchFieldId\": {fieldIdPath.FieldId}, \"fieldMapSize\": {fieldMap.Count}}}");

                uint countOffset = currentOffset + 1;
                var countErr = DecodeVarUInt32(buffer.Span, countOffset, out var fieldCount, out var countBytes);
                if (!countErr.IsOk) return countErr;

                if (tracePathEnabled)
                    LogPathEvent($"{{\"event\": \"fieldIdPath.objectInfo\", \"fieldCount\": {fieldCount}}}");

                uint fieldDataOffset = countOffset + countBytes;
                bool found = false;

                for (uint f = 0; f < fieldCount; f++)
                {
                    if (fieldDataOffset >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldDataOffset);

                    var idErr = DecodeVarUInt32(buffer.Span, fieldDataOffset, out var fieldId, out var idBytes);
                    if (!idErr.IsOk) return idErr;
                    fieldDataOffset += idBytes;

                    if (fieldDataOffset >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, fieldDataOffset);
                    byte fieldType = buffer.Span[(int)fieldDataOffset];
                    uint typeCodeOffset = fieldDataOffset;
                    fieldDataOffset++;

                    if (tracePathEnabled)
                    {
                        string fieldName = fieldMap.TryGetValue(fieldId, out var sf) ? System.Text.Encoding.UTF8.GetString(sf.nameBytes) : "<?>";
                        LogPathEvent($"{{\"event\": \"fieldIdPath.candidate\", \"fieldId\": {fieldId}, \"fieldName\": \"{fieldName}\", \"fieldType\": \"0x{fieldType:X2}\", \"match\": {(fieldId == fieldIdPath.FieldId).ToString().ToLower()}}}");
                    }

                    if (fieldId == fieldIdPath.FieldId)
                    {
                        currentOffset = typeCodeOffset;
                        currentType = fieldType;

                        // PHASE 3 FIX: Track array field ID for context switching
                        if (fieldType == 0x30)  // Array type
                            currentArrayFieldId = fieldId;
                        else
                            currentArrayFieldId = 0;  // Reset when leaving array context

                        found = true;
                        if (tracePathEnabled)
                            LogPathEvent($"{{\"event\": \"fieldIdPath.found\", \"fieldId\": {fieldId}, \"fieldType\": \"0x{fieldType:X2}\"}}");
                        break;
                    }

                    var skipErr = SkipValue(buffer.Span, fieldDataOffset, fieldType, out var nextOffset);
                    if (!skipErr.IsOk) return skipErr;
                    fieldDataOffset = nextOffset;
                }

                if (!found)
                {
                    if (tracePathEnabled)
                        LogPathEvent($"{{\"event\": \"fieldIdPath.notFound\", \"searchFieldId\": {fieldIdPath.FieldId}, \"reason\": \"field_id_not_in_object\"}}");
                    return new IronCfgError(IronCfgErrorCode.UnknownField, currentOffset);
                }
            }
            else
            {
                return new IronCfgError(IronCfgErrorCode.InvalidTypeCode, currentOffset);
            }
        }

        finalOffset = currentOffset;
        finalTypeCode = currentType;
        if (tracePathEnabled)
            LogPathEvent($"{{\"event\": \"FindValueByPath.END\", \"success\": true, \"finalTypeCode\": \"0x{finalTypeCode:X2}\"}}");
        return IronCfgError.Ok;
    }

    private static IronCfgError ParseSchema(ReadOnlyMemory<byte> buffer, IronCfgView view,
        out SchemaCacheEntry cacheEntry)
    {
        cacheEntry = default;

        var elementFieldMapCache = new Dictionary<uint, Dictionary<uint, (byte[], byte)>>();

        var schemaErr = view.GetSchema(out var schemaBlock);
        if (!schemaErr.IsOk) return schemaErr;

        var schema = schemaBlock.Span;
        if (schema.Length == 0) return IronCfgError.Ok;

        uint offset = 0;
        byte schemaVersion = view.Header.Version;
        var parseErr = ParseSchemaInternal(
            schema,
            ref offset,
            out var fieldMap,
            elementFieldMapCache,
            schemaVersion,
            view.Header.SchemaOffset);
        if (!parseErr.IsOk) return parseErr;

        cacheEntry = new SchemaCacheEntry(fieldMap, elementFieldMapCache);
        return IronCfgError.Ok;
    }

    private static IronCfgError ParseSchemaInternal(ReadOnlySpan<byte> schema, ref uint offset,
        out Dictionary<uint, (byte[] nameBytes, byte typeCode)> fieldMap,
        Dictionary<uint, Dictionary<uint, (byte[] nameBytes, byte typeCode)>> elementFieldMapCache,
        byte schemaVersion = 2,
        uint schemaBaseOffset = 0)
    {
        fieldMap = new Dictionary<uint, (byte[], byte)>();

        if (schema.Length == 0) return IronCfgError.Ok;

        // Conditional tracing (set via environment variable IRONCFG_TRACE_SCHEMA=1)
        bool traceEnabled = System.Environment.GetEnvironmentVariable("IRONCFG_TRACE_SCHEMA") == "1";
        string? traceFile = traceEnabled ? System.Environment.GetEnvironmentVariable("IRONCFG_TRACE_FILE") ?? "/tmp/ironcfg_trace.log" : null;

        void WriteTrace(string msg)
        {
            if (traceFile != null)
            {
                try { System.IO.File.AppendAllText(traceFile, msg + "\n"); }
                catch { }
            }
        }

        uint initialOffset = offset;
        if (traceEnabled)
            WriteTrace($"[ParseSchema] ENTRY: offset={initialOffset}, schemaLen={schema.Length}, schemaVersion={schemaVersion}");

        var countErr = DecodeVarUInt32(schema, offset, out var fieldCount, out var countBytes);
        if (!countErr.IsOk) return RebaseSchemaError(countErr, schemaBaseOffset);
        offset += countBytes;

        if (traceEnabled)
            WriteTrace($"[ParseSchema] FieldCount: {fieldCount} (countBytes={countBytes}, offset now={offset})");

        for (uint i = 0; i < fieldCount; i++)
        {
            uint loopStartOffset = offset;
            if (traceEnabled)
                WriteTrace($"[ParseSchema] Field[{i}] START: offset={loopStartOffset}");

            var idErr = DecodeVarUInt32(schema, offset, out var fieldId, out var idBytes);
            if (!idErr.IsOk) return RebaseSchemaError(idErr, schemaBaseOffset);
            offset += idBytes;

            if (traceEnabled)
                WriteTrace($"[ParseSchema] Field[{i}] ID={fieldId} (idBytes={idBytes}, offset now={offset})");

            if (offset >= schema.Length) return new IronCfgError(IronCfgErrorCode.TruncatedBlock, schemaBaseOffset + offset);
            byte typeCode = schema[(int)offset];
            offset++;

            if (traceEnabled)
                WriteTrace($"[ParseSchema] Field[{i}] TypeCode=0x{typeCode:X2} (offset now={offset})");

            byte[] fieldName = Array.Empty<byte>();
            if (IronCfgTypeSystem.IsCompoundType(typeCode))
            {
                var nameErr = DecodeVarUInt32(schema, offset, out var nameLen, out var nameBytes);
                if (!nameErr.IsOk) return RebaseSchemaError(nameErr, schemaBaseOffset);
                offset += nameBytes;

                if (traceEnabled)
                    WriteTrace($"[ParseSchema] Field[{i}] NameLen={nameLen} (nameBytes={nameBytes}, offset now={offset})");

                if (offset + nameLen > schema.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, schemaBaseOffset + offset);
                fieldName = schema.Slice((int)offset, (int)nameLen).ToArray();
                offset += nameLen;

                if (traceEnabled)
                {
                    string fieldNameStr = System.Text.Encoding.UTF8.GetString(fieldName);
                    WriteTrace($"[ParseSchema] Field[{i}] Name='{fieldNameStr}' (offset now={offset})");
                }

                if (IronCfgTypeSystem.HasElementSchema(schemaVersion, typeCode))
                {
                    uint elemStartOffset = offset;
                    if (traceEnabled)
                        WriteTrace($"[ParseSchema] Field[{i}] ELEMENT_SCHEMA_START: offset={elemStartOffset}");

                    var elemErr = ParseSchemaInternal(
                        schema,
                        ref offset,
                        out var elemFieldMap,
                        elementFieldMapCache,
                        schemaVersion,
                        schemaBaseOffset);
                    if (!elemErr.IsOk) return elemErr;

                    if (traceEnabled)
                        WriteTrace($"[ParseSchema] Field[{i}] ELEMENT_SCHEMA_END: offset was {elemStartOffset}, now={offset} (advanced={offset - elemStartOffset})");

                    if (!elementFieldMapCache.ContainsKey(fieldId))
                    {
                        elementFieldMapCache[fieldId] = elemFieldMap;
                        if (traceEnabled)
                            WriteTrace($"[ParseSchema] CACHED elemFieldMap for fieldId={fieldId}, mapSize={elemFieldMap.Count}");
                    }
                }
            }

            fieldMap[fieldId] = (fieldName, typeCode);

            uint loopEndOffset = offset;
            if (traceEnabled)
                WriteTrace($"[ParseSchema] Field[{i}] END: offset was {loopStartOffset}, now={loopEndOffset} (advanced={loopEndOffset - loopStartOffset})");
        }

        if (traceEnabled)
            WriteTrace($"[ParseSchema] EXIT: offset was {initialOffset}, now={offset} (total advanced={offset - initialOffset})");

        return IronCfgError.Ok;
    }

    private static IronCfgError RebaseSchemaError(IronCfgError error, uint schemaBaseOffset)
    {
        if (error.IsOk)
            return error;

        return new IronCfgError(error.Code, schemaBaseOffset + error.Offset);
    }

    private static ulong ComputeSchemaCacheKey(ReadOnlySpan<byte> schema, byte version)
    {
        uint schemaCrc = IronConfig.Crc32Ieee.Compute(schema);
        ulong key = schemaCrc;
        key |= (ulong)(uint)schema.Length << 32;
        key ^= (ulong)version << 60;
        return key;
    }

    private static IronCfgError SkipValue(ReadOnlySpan<byte> buffer, uint offset, byte typeCode, out uint nextOffset)
    {
        nextOffset = offset;

        switch (typeCode)
        {
            case 0x00:
            case 0x01:
            case 0x02:
                nextOffset = offset;
                return IronCfgError.Ok;

            case 0x10:
            case 0x11:
            case 0x12:
                if (offset + 8 > buffer.Length)
                    return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
                nextOffset = offset + 8;
                return IronCfgError.Ok;

            case 0x20:
            case 0x22:
            {
                var lenErr = DecodeVarUInt32(buffer, offset, out var len, out var lenBytes);
                if (!lenErr.IsOk) return lenErr;
                if (offset + lenBytes + len > buffer.Length)
                    return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
                nextOffset = offset + lenBytes + len;
                return IronCfgError.Ok;
            }

            case 0x21:
            {
                var idErr = DecodeVarUInt32(buffer, offset, out _, out var idBytes);
                if (!idErr.IsOk) return idErr;
                nextOffset = offset + idBytes;
                return IronCfgError.Ok;
            }

            case 0x30:
            case 0x40:
            {
                var countErr = DecodeVarUInt32(buffer, offset, out var count, out var countBytes);
                if (!countErr.IsOk) return countErr;

                uint elemOffset = offset + countBytes;
                for (uint i = 0; i < count; i++)
                {
                    if (elemOffset >= buffer.Length)
                        return new IronCfgError(IronCfgErrorCode.BoundsViolation, elemOffset);

                    byte elemType = buffer[(int)elemOffset];
                    if (typeCode == 0x40)
                    {
                        var idErr = DecodeVarUInt32(buffer, elemOffset, out _, out var idBytes2);
                        if (!idErr.IsOk)
                            return idErr;
                        elemOffset += idBytes2;

                        if (elemOffset >= buffer.Length)
                            return new IronCfgError(IronCfgErrorCode.BoundsViolation, elemOffset);
                        elemType = buffer[(int)elemOffset];
                        elemOffset++;
                    }
                    else
                    {
                        elemOffset++;
                    }

                    var skipErr = SkipValue(buffer, elemOffset, elemType, out elemOffset);
                    if (!skipErr.IsOk)
                        return skipErr;
                }

                nextOffset = elemOffset;
                return IronCfgError.Ok;
            }

            default:
                return new IronCfgError(IronCfgErrorCode.InvalidTypeCode, offset);
        }
    }

    private static IronCfgError DecodeVarUInt32(ReadOnlySpan<byte> buffer, uint offset, out uint value, out uint bytes)
    {
        value = 0;
        bytes = 0;

        if (offset >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);

        byte b = buffer[(int)offset];
        if ((b & 0x80) == 0)
        {
            value = b;
            bytes = 1;
            return IronCfgError.Ok;
        }

        if (offset + 1 >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
        byte b1 = buffer[(int)offset + 1];
        if ((b1 & 0x80) == 0)
        {
            value = (uint)((b & 0x7F) | ((b1 & 0x7F) << 7));
            if (value < (1u << 7)) return new IronCfgError(IronCfgErrorCode.NonMinimalVarint, offset);
            bytes = 2;
            return IronCfgError.Ok;
        }

        if (offset + 2 >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
        byte b2 = buffer[(int)offset + 2];
        if ((b2 & 0x80) == 0)
        {
            value = (uint)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14));
            if (value < (1u << 14)) return new IronCfgError(IronCfgErrorCode.NonMinimalVarint, offset);
            bytes = 3;
            return IronCfgError.Ok;
        }

        if (offset + 3 >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
        byte b3 = buffer[(int)offset + 3];
        if ((b3 & 0x80) == 0)
        {
            value = (uint)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14) | ((b3 & 0x7F) << 21));
            if (value < (1u << 21)) return new IronCfgError(IronCfgErrorCode.NonMinimalVarint, offset);
            bytes = 4;
            return IronCfgError.Ok;
        }

        if (offset + 4 >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
        byte b4 = buffer[(int)offset + 4];
        if ((b4 & 0xF0) != 0) return new IronCfgError(IronCfgErrorCode.NonMinimalVarint, offset);
        value = (uint)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14) | ((b3 & 0x7F) << 21) | ((b4 & 0x0F) << 28));
        if (value < (1u << 28)) return new IronCfgError(IronCfgErrorCode.NonMinimalVarint, offset);
        bytes = 5;
        return IronCfgError.Ok;
    }
}
