> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IronEdge Runtime - Unified Error Model

**Status**: PHASE 1 - Specification & Reference
**Version**: 1.0
**Date**: February 12, 2026

---

## Overview

The IronEdge Runtime uses a unified error model to provide consistent, actionable error information across three engines: IRONCFG, ILOG, and IUPD. Errors are categorized by severity and recovery strategy, enabling robust client-side handling.

## Error Code Ranges

| Range | Engine | Count | Purpose |
|-------|--------|-------|---------|
| 0x000-0x01F | SHARED | 32 | Universal errors (magic, version, truncation) |
| 0x020-0x03F | IRONCFG | 32 | Config-specific (schema, field validation) |
| 0x040-0x05F | ILOG | 32 | Logging-specific (compression, indexing) |
| 0x060-0x07F | IUPD | 32 | Update-specific (manifest, dependencies) |

**Total**: 128 canonical error codes (0x00-0x7F)

---

## Canonical Error Codes

### SHARED RANGE (0x00-0x1F)

These errors occur across all engines and represent fundamental format violations.

#### 0x00: OK
- **Description**: No error (success)
- **Retry**: No
- **Fatal**: No
- **Recovery**: N/A
- **Engines**: All

#### 0x01: ERR_TRUNCATED_FILE
- **Description**: File ends prematurely before reaching expected size
- **Indicators**: `file_size < header_size`, `payload_offset + payload_size > file_size`
- **Retry**: No (file corruption or incomplete download)
- **Fatal**: Yes
- **Recovery**: Re-download file
- **Engines**: All
- **Example**: "File truncated at offset 2048 (expected 4096)"

#### 0x02: ERR_INVALID_MAGIC
- **Description**: File magic bytes don't match engine signature
- **Indicators**: First 4 bytes != `[IRxC|IRxL|IRxU]`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Check file format, verify engine compatibility
- **Engines**: All
- **Example**: "Invalid magic: 0x12345678 (expected 0x49524... for IRONCFG)"

#### 0x03: ERR_UNSUPPORTED_VERSION
- **Description**: File version exceeds engine's maximum supported version
- **Indicators**: `file_version > engine_max_version`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Upgrade engine to newer version
- **Engines**: All
- **Example**: "File version 3, engine supports max version 2"

#### 0x04: ERR_INVALID_FLAGS
- **Description**: Reserved flag bits are set (should be 0)
- **Indicators**: Reserved bits != 0
- **Retry**: No
- **Fatal**: Yes (data integrity concern)
- **Recovery**: Verify file source, re-download
- **Engines**: All
- **Example**: "Invalid flags: 0xF0 (reserved bits set)"

#### 0x05: ERR_BOUNDS_VIOLATION
- **Description**: Offset or size field points outside file boundaries
- **Indicators**: `offset > file_size`, `offset + size > file_size`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: File corruption detected, re-download
- **Engines**: All
- **Example**: "Offset 10000 exceeds file size 8192"

#### 0x06: ERR_ARITHMETIC_OVERFLOW
- **Description**: Arithmetic operation (offset + size) overflowed
- **Indicators**: `u64::MAX < offset + size`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Invalid file format
- **Engines**: All
- **Example**: "Size calculation overflow: 0xFFF...FFF + 1"

#### 0x07: ERR_CRC32_MISMATCH
- **Description**: CRC32 checksum doesn't match stored value
- **Indicators**: `calculated_crc32 != stored_crc32`
- **Retry**: No
- **Fatal**: Yes (corruption)
- **Recovery**: File corrupted in transit/storage, re-download
- **Engines**: All
- **Example**: "CRC32 mismatch: calc=0x12345678 stored=0x87654321"

#### 0x08: ERR_BLAKE3_MISMATCH
- **Description**: BLAKE3-256 hash doesn't match stored value
- **Indicators**: `blake3_hash(data) != stored_hash`
- **Retry**: No
- **Fatal**: Yes (tampering concern)
- **Recovery**: Re-download or verify source
- **Engines**: All (if BLAKE3 enabled)
- **Example**: "BLAKE3 mismatch detected - possible tampering"

### IRONCFG RANGE (0x20-0x3F)

Configuration-specific errors for schema validation and field parsing.

#### 0x20: ERR_INVALID_SCHEMA
- **Description**: Schema definition is malformed or inconsistent
- **Indicators**: Duplicate field IDs, invalid field types, missing schema
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Verify schema file, update schema version
- **Example**: "Schema has duplicate field ID 5"

#### 0x21: ERR_FIELD_ORDER_VIOLATION
- **Description**: Fields not in ascending order by ID
- **Indicators**: `field[i].id >= field[i+1].id`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Field ordering requirement violated
- **Example**: "Fields out of order: ID 10 before ID 8"

#### 0x22: ERR_INVALID_STRING
- **Description**: String field contains invalid UTF-8
- **Indicators**: Non-UTF8 bytes in string field
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Re-download or re-encode
- **Example**: "Invalid UTF-8 at offset 2048"

#### 0x23: ERR_INVALID_TYPE_CODE
- **Description**: Type code not recognized (0x00-0x0D)
- **Indicators**: `type_code > 0x0D`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: File corruption or version mismatch
- **Example**: "Invalid type code: 0xFF"

#### 0x24: ERR_FIELD_TYPE_MISMATCH
- **Description**: Field value type doesn't match schema definition
- **Indicators**: `value_type != schema.fields[id].type`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Schema/data mismatch
- **Example**: "Field 3: expected type Int64, got Bool"

#### 0x25: ERR_MISSING_REQUIRED_FIELD
- **Description**: Required field (schema marked IsRequired=true) is missing
- **Indicators**: `required_field_id not in object.fields`
- **Retry**: No
- **Fatal**: Depends on field
- **Recovery**: Provide default or re-download
- **Example**: "Required field 'user_id' (field 0) missing"

### ILOG RANGE (0x40-0x5F)

Logging-specific errors for compression, indexing, and record parsing.

#### 0x40: ERR_COMPRESSION_FAILED
- **Description**: Compression/decompression operation failed
- **Indicators**: LZ4 error, corrupted compressed block
- **Retry**: No
- **Fatal**: Yes (if required profile)
- **Recovery**: Disable compression or re-compress
- **Example**: "LZ4 decompression failed at offset 1024"

#### 0x41: ERR_INDEX_CORRUPTED
- **Description**: Log index (L2 block) is invalid or corrupted
- **Indicators**: Invalid offsets, overlapping records, wrong sizes
- **Retry**: No
- **Fatal**: Yes (for indexed profiles)
- **Recovery**: Rebuild index or use unindexed data
- **Example**: "Index entry 5 offset overlaps with entry 4"

#### 0x42: ERR_RECORD_TRUNCATED
- **Description**: Individual log record ends prematurely
- **Indicators**: Record size > remaining block size
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Skip corrupted record
- **Example**: "Record at offset 2048 truncated (size 512, available 256)"

#### 0x43: ERR_INVALID_PROFILE
- **Description**: Requested profile not available in file
- **Indicators**: Profile bits not set in header
- **Retry**: No
- **Fatal**: Depends on profile requirement
- **Recovery**: Use lower profile or re-encode
- **Example**: "File doesn't have ARCHIVED profile (L3 block)"

### IUPD RANGE (0x60-0x7F)

Update-specific errors for manifest parsing, chunk validation, and apply operations.

#### 0x60: ERR_INVALID_CHUNK_TABLE
- **Description**: Chunk table header or structure is invalid
- **Indicators**: Bad chunk count, invalid offsets, size mismatch
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Re-download manifest
- **Example**: "Chunk table claims 1000 chunks but size only 512 bytes"

#### 0x61: ERR_CHUNK_INDEX_ERROR
- **Description**: Chunk index out of bounds or invalid
- **Indicators**: `chunk_index >= chunk_count` or negative
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Manifest corruption
- **Example**: "Chunk index 5 out of bounds (max 3)"

#### 0x62: ERR_OVERLAPPING_PAYLOADS
- **Description**: Chunk payloads overlap in update file
- **Indicators**: `offset[i] + size[i] > offset[i+1]`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: File corruption
- **Example**: "Chunk 2 overlaps with chunk 3"

#### 0x63: ERR_CYCLIC_DEPENDENCY
- **Description**: Chunk dependency graph contains cycle
- **Indicators**: Chunk A depends on B, B depends on A (transitive)
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Dependency graph malformed
- **Example**: "Cycle detected: chunk 1 → 3 → 1"

#### 0x64: ERR_MISSING_CHUNK_DEPENDENCY
- **Description**: Chunk depends on non-existent chunk
- **Indicators**: `depends_on_chunk_id >= chunk_count`
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Incomplete manifest
- **Example**: "Chunk 2 depends on non-existent chunk 5"

#### 0x65: ERR_INVALID_APPLY_ORDER
- **Description**: Apply order missing or duplicates chunks
- **Indicators**: `apply_order.len != chunk_count` or duplicates
- **Retry**: No
- **Fatal**: Yes
- **Recovery**: Manifest malformed
- **Example**: "Apply order has 3 chunks but manifest has 4"

---

## Error Classification

### By Recovery Strategy

| Strategy | Codes | Client Action |
|----------|-------|----------------|
| **Retry** | None | Internal protocol errors (impossible) |
| **Skip** | 0x40-0x42 (ILOG) | Continue with available data |
| **Downgrade** | 0x43 (profile), 0x23-0x24 | Use lower profile/version |
| **Abort** | All 0x00-0x08, most 0x20+ | Stop processing, report error |

### By Severity

| Level | Codes | Impact |
|-------|-------|--------|
| **FATAL** | 0x01-0x08, 0x60-0x65 | Data integrity/security concern |
| **ERROR** | 0x20-0x25, 0x40-0x42 | Operation cannot proceed |
| **WARNING** | 0x43 | Degraded functionality available |
| **OK** | 0x00 | Success |

### By Corruption Type

| Type | Codes | Indicator |
|------|-------|-----------|
| **Format** | 0x02-0x06 | File structure violated |
| **Integrity** | 0x07-0x08 | Checksum/hash mismatch |
| **Schema** | 0x20-0x25 | Data doesn't match schema |
| **Dependency** | 0x63-0x65 | Logical constraints violated |

---

## Client Error Handling Patterns

### Pattern 1: Fail-Fast (Strict Mode)
```csharp
var err = encoder.Encode(data, ...);
if (!err.IsOk)
{
    throw new IronEdgeException($"Error {err.Code}: {err.Message}");
}
```

### Pattern 2: Graceful Degradation (Lenient Mode)
```csharp
var err = decoder.Decode(data);
if (err.Code == 0x43) // ERR_INVALID_PROFILE
{
    // Use lower profile
    return decoder.DecodeWithProfile(IlogProfile.MINIMAL);
}
else if (!err.IsOk)
{
    throw new IronEdgeException($"Fatal error {err.Code}");
}
```

### Pattern 3: Logging with Context
```csharp
if (!err.IsOk)
{
    logger.Error($"Engine={engine}, Code=0x{(int)err.Code:X2}, " +
                  $"Offset={err.Offset}, Message={err.Message}");
}
```

---

## Implementation Notes

### Error Code Stability

- Error codes 0x00-0x7F are **stable** and won't change
- New engines use next available range (0x80+)
- Codes are **permanent** - never reused or deprecated
- Mapping documents are version-specific

### Error Serialization

Errors are returned as structured values:
```csharp
// IRONCFG
public struct IronCfgError
{
    public IronCfgErrorCode Code { get; }  // 0x20-0x3F
    public uint Offset { get; }             // Byte offset in file
}

// IUPD
public struct IupdError
{
    public IupdErrorCode Code { get; }      // 0x60-0x7F
    public ulong ByteOffset { get; }        // Byte offset
    public uint? ChunkIndex { get; }        // Optional chunk context
    public string Message { get; }          // Human-readable detail
}

// ILOG (NEW - unified)
public struct IlogError
{
    public IlogErrorCode Code { get; }      // 0x40-0x5F
    public uint BlockIndex { get; }         // Block/record context
    public uint Offset { get; }             // Byte offset within block
    public string Message { get; }          // Detail
}
```

### Error Documentation Requirements

Every error code MUST have:
- Clear, one-line description
- Root causes (how it can happen)
- Recovery strategy (what client does)
- Example message (human-readable)
- Stability guarantee (won't change)

---

## Testing Strategy

### Corruption Tests
- Truncate files at various offsets → ERR_TRUNCATED_FILE
- Corrupt magic bytes → ERR_INVALID_MAGIC
- Corrupt checksums → ERR_CRC32_MISMATCH, ERR_BLAKE3_MISMATCH
- Corrupt field data → ERR_FIELD_TYPE_MISMATCH, ERR_INVALID_STRING
- Create cyclic dependencies → ERR_CYCLIC_DEPENDENCY

### Threshold Tests
- Version mismatch → ERR_UNSUPPORTED_VERSION
- Profile mismatch → ERR_INVALID_PROFILE
- Out-of-bounds offsets → ERR_BOUNDS_VIOLATION

### Recovery Tests
- Verify error code is consistent across runs (deterministic)
- Verify error messages are informative
- Verify no panics/exceptions (all errors caught)

---

## Version & Compatibility

| Engine | Code Range | Version |
|--------|-----------|---------|
| IRONCFG | 0x20-0x3F | v1.0+ |
| ILOG | 0x40-0x5F | v1.0+ |
| IUPD | 0x60-0x7F | v2.0+ |

**Unified Model Version**: 1.0 (Feb 2026)
**Stability**: Stable (backward compatible)

---

## References

- `IRONCFG_ERROR_MAPPING.md` - Old → New code mappings
- `ILOG_ERROR_MAPPING.md` - ILOG error definitions
- `IUPD_ERROR_MAPPING.md` - IUPD error definitions
- Test file: `IronConfigErrorTests.cs`
