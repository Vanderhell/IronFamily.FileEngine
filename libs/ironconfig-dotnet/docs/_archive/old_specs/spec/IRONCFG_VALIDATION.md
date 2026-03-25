> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONCFG Validation Model and Error Semantics

**Version**: 1.0
**Date**: 2026-01-16
**Status**: Normative
**References**: spec/IRONCFG.md, spec/IRONCFG_CANONICAL.md

---

## 1. Validation Overview

IRONCFG validation enforces:
- **Safety**: No out-of-bounds memory access, no integer overflow
- **Correctness**: All canonical rules obeyed, schema conformance
- **Determinism**: Same input always produces same error (or success) result
- **Speed**: Two validation modes trade off speed vs guarantees

**Fundamental Principle (MUST)**:
Every invalid IRONCFG file MUST result in:
1. Deterministic error detection (same error on all platforms/languages)
2. Specific error code with byte offset
3. No undefined behavior
4. Immediate failure (fail-fast, no partial processing)

---

## 2. Validation Modes

### 2.1 validate_fast (MUST)

**Purpose**: Quick structural validation suitable for hot paths (e.g., on-open checks).

**Guarantees**:
- File size >= 64 bytes (header can be read safely)
- Magic = "ICFG"
- Version = 1
- Reserved header fields = 0x00
- No unknown flag bits set
- Offset ordering is monotonic (no backward jumps)
- No offset/size causes integer overflow
- CRC32 flag presence matches crcOffset presence
- BLAKE3 flag presence matches blake3Offset presence

**Time Complexity**: O(1) â€” constant time, independent of file size

**What is NOT checked**:
- Schema validity
- String pool validity
- Data block structure
- Type correctness
- CRC32/BLAKE3 values (not computed)
- Required field presence
- Recursion depth (not traversed)
- Canonical ordering (fields, VarUInt, etc.)

**Exit Behavior (MUST)**:
- On first validation failure: return specific error code + byte offset
- On success: allow proceed to strict validation or data access

**Implementation Guideline**:
```
function validate_fast(buffer, size):
  // 1. File size
  if size < 64:
    return (TRUNCATED_FILE, offset=0)

  // 2. Header reads (bounds-safe, first 64 bytes available)
  magic = buffer[0:4]
  version = buffer[4]
  flags = buffer[5]
  reserved0 = buffer[6:8]
  file_size = LEu32(buffer[8:12])
  schema_off = LEu32(buffer[12:16])
  schema_size = LEu32(buffer[16:20])
  pool_off = LEu32(buffer[20:24])
  pool_size = LEu32(buffer[24:28])
  data_off = LEu32(buffer[28:32])
  data_size = LEu32(buffer[32:36])
  crc_off = LEu32(buffer[36:40])
  blake3_off = LEu32(buffer[40:44])
  reserved1 = LEu32(buffer[44:48])
  reserved2 = buffer[48:64]

  // 3. Magic and version
  if magic != "ICFG":
    return (INVALID_MAGIC, offset=0)
  if version != 1:
    return (INVALID_VERSION, offset=4)

  // 4. Flags
  if flags & 0xF8:  // bits 3-7 reserved
    return (INVALID_FLAGS, offset=5)

  // 5. Reserved fields
  if reserved0 != 0x0000:
    return (RESERVED_FIELD_NONZERO, offset=6)
  if reserved1 != 0x00000000:
    return (RESERVED_FIELD_NONZERO, offset=44)
  if reserved2 != [0x00]*16:
    return (RESERVED_FIELD_NONZERO, offset=48)

  // 6. Flag consistency
  crc_flag = (flags & 0x01) != 0
  blake3_flag = (flags & 0x02) != 0
  if crc_flag && crc_off == 0:
    return (FLAG_MISMATCH, offset=5)
  if !crc_flag && crc_off != 0:
    return (FLAG_MISMATCH, offset=5)
  if blake3_flag && blake3_off == 0:
    return (FLAG_MISMATCH, offset=5)
  if !blake3_flag && blake3_off != 0:
    return (FLAG_MISMATCH, offset=5)

  // 7. Offset monotonicity and overflow
  if !is_ascending([schema_off, schema_off+schema_size,
                    pool_off, pool_off+pool_size,
                    data_off, data_off+data_size,
                    crc_off+(crc_flag?4:0),
                    blake3_off+(blake3_flag?32:0)]):
    return (BOUNDS_VIOLATION, offset=12)

  // 8. Offset overflow detection
  offsets = [schema_off, schema_size, pool_off, pool_size,
             data_off, data_size, crc_off, blake3_off]
  for each off in offsets:
    if off > 2^32-1:
      return (ARITHMETIC_OVERFLOW, offset=12)
    if off + size_component > 2^32:
      return (ARITHMETIC_OVERFLOW, offset=12)

  // 9. File size consistency
  expected_size = data_off + data_size
  if crc_flag:
    expected_size += 4
  if blake3_flag:
    expected_size += 32
  if file_size != expected_size:
    return (BOUNDS_VIOLATION, offset=8)
  if size != file_size:
    return (TRUNCATED_FILE, offset=0)

  // 10. Header size field check
  if LEu16(buffer[6:8]) != 32:
    return (INVALID_SCHEMA, offset=6)

  return (OK, offset=0)
```

### 2.2 validate_strict (MUST)

**Purpose**: Full validation with all canonical rules enforced. Use before data access.

**Guarantees** (includes all validate_fast guarantees, plus):
- Schema block is valid and parseable
- All schema fields have ascending fieldId
- Field names are sorted lexicographically by UTF-8 bytes
- No duplicate fieldId or fieldName
- String pool (if present) is valid and parseable
- All strings in pool are sorted lexicographically
- No duplicate strings in pool
- Data block conforms to schema
- All field types match schema declarations
- All required fields present (not null)
- No unknown fields
- Object fields are in ascending fieldId order
- Array elements are homogeneous (same type)
- All VarUInt values are minimal-encoded
- All float values are canonical (-0.0 normalized, NaN rejected)
- CRC32 matches file (if present)
- BLAKE3 matches file (if present, advisory)
- Recursion depth never exceeds 32
- No field/array count exceeds limits
- All string lengths within limits
- File size within limits
- No syntax errors in any block

**Time Complexity**: O(n) where n = file size (full traversal required for completeness)

**Exit Behavior (MUST)**:
- On first validation failure: return specific error code + byte offset
- On success: file is guaranteed safe and canonical
- If CRC32 fails: return CRC32_MISMATCH, do not proceed
- If BLAKE3 fails (advisory): may return BLAKE3_MISMATCH or continue

**Implementation Guideline** (pseudocode outline):
```
function validate_strict(buffer, size):
  // 1. Fast validation
  result = validate_fast(buffer, size)
  if result.error != OK:
    return result

  // 2. Parse header (now guaranteed safe)
  // [use offsets from fast validation]

  // 3. Schema validation
  schema_result = parse_and_validate_schema(buffer[schema_off:schema_off+schema_size])
  if schema_result.error != OK:
    return schema_result

  // 4. String pool validation (if present)
  if pool_off > 0:
    pool_result = parse_and_validate_string_pool(buffer[pool_off:pool_off+pool_size])
    if pool_result.error != OK:
      return pool_result

  // 5. Data block validation
  data_result = parse_and_validate_data(buffer[data_off:data_off+data_size], schema, pool)
  if data_result.error != OK:
    return data_result

  // 6. CRC32 validation
  if crc_flag:
    computed_crc = crc32_ieee(buffer[0:crc_off])
    stored_crc = LEu32(buffer[crc_off:crc_off+4])
    if computed_crc != stored_crc:
      return (CRC32_MISMATCH, offset=crc_off)

  // 7. BLAKE3 validation (advisory)
  if blake3_flag:
    computed_blake3 = blake3(buffer[0:blake3_off])
    stored_blake3 = buffer[blake3_off:blake3_off+32]
    if computed_blake3[:32] != stored_blake3:
      // advisory, may log but not fail
      return (BLAKE3_MISMATCH, offset=blake3_off)

  return (OK, offset=0)
```

### 2.3 open_unsafe (OPTIONAL)

**Purpose**: Internal use only after strict validation. No checks performed.

**Guarantees**:
- File MUST have passed validate_strict before open_unsafe is used
- All offsets, sizes, and data structures are trusted valid
- No error checking during data access

**Restrictions (MUST)**:
- Caller MUST have proof that validate_strict passed
- Caller MUST be same language (C99 or .NET)
- Caller MUST not share buffer across validation contexts
- Use only in inner loops after validation gate

**Implementation (MUST NOT)**:
- Add any validation logic
- Check bounds
- Verify offsets
- Validate types

---

## 3. Validation Order (STRICT)

Validation MUST proceed in this exact order. Deviation causes non-determinism or missed errors.

| Step | Check | Returns On Failure | Byte Offset | Reason |
|------|-------|-------------------|------------|--------|
| 1 | File size â‰Ą 64 | TRUNCATED_FILE | 0 | Header cannot be read |
| 2 | Magic = "ICFG" | INVALID_MAGIC | 0 | Wrong format |
| 3 | Version = 1 | INVALID_VERSION | 4 | Unsupported version |
| 4 | Flags bits 3-7 = 0 | INVALID_FLAGS | 5 | Unknown flags |
| 5 | reserved0 = 0x0000 | RESERVED_FIELD_NONZERO | 6 | Non-canonical header |
| 6 | reserved1 = 0x00000000 | RESERVED_FIELD_NONZERO | 44 | Non-canonical header |
| 7 | reserved2 = all 0x00 | RESERVED_FIELD_NONZERO | 48 | Non-canonical header |
| 8 | CRC flag â†” crcOffset | FLAG_MISMATCH | 5 | Inconsistent flags |
| 9 | BLAKE3 flag â†” blake3Offset | FLAG_MISMATCH | 5 | Inconsistent flags |
| 10 | Offsets ascending | BOUNDS_VIOLATION | 12 | Invalid layout |
| 11 | No offset overflow | ARITHMETIC_OVERFLOW | 12 | Integer overflow |
| 12 | File size match | BOUNDS_VIOLATION | 8 | Size mismatch |
| 13 | Schema block bounds | BOUNDS_VIOLATION | 16 | Schema extends beyond file |
| 14 | String pool bounds (if present) | BOUNDS_VIOLATION | 20 | Pool extends beyond file |
| 15 | Data block bounds | BOUNDS_VIOLATION | 28 | Data extends beyond file |
| 16 | Schema parseable | INVALID_SCHEMA | (calculated) | Schema syntax error |
| 17 | fieldId ascending | FIELD_ORDER_VIOLATION | (calculated) | Non-canonical ordering |
| 18 | fieldName sorted lex | INVALID_SCHEMA | (calculated) | Non-canonical ordering |
| 19 | No duplicate fieldId | INVALID_SCHEMA | (calculated) | Duplicate field |
| 20 | No duplicate fieldName | INVALID_SCHEMA | (calculated) | Duplicate name |
| 21 | String pool parseable | INVALID_SCHEMA | (calculated) | Pool syntax error |
| 22 | Pool strings sorted lex | INVALID_SCHEMA | (calculated) | Non-canonical ordering |
| 23 | No duplicate strings | INVALID_SCHEMA | (calculated) | Duplicate string |
| 24 | Data block root is object | INVALID_TYPE_CODE | (calculated) | Root must be object |
| 25 | fieldCount matches schema | FIELD_COUNT_MISMATCH | (calculated) | Field count mismatch |
| 26 | fieldId in object â‰¤ max | BOUNDS_VIOLATION | (calculated) | fieldId out of range |
| 27 | fieldId ascending in object | FIELD_ORDER_VIOLATION | (calculated) | Non-canonical ordering |
| 28 | No duplicate fieldId in object | FIELD_ORDER_VIOLATION | (calculated) | Duplicate field |
| 29 | Field types match schema | FIELD_TYPE_MISMATCH | (calculated) | Type mismatch |
| 30 | Required fields not null | MISSING_REQUIRED_FIELD | (calculated) | Required field null |
| 31 | No unknown fields | UNKNOWN_FIELD | (calculated) | Undeclared field |
| 32 | Array elements same type | ARRAY_TYPE_MISMATCH | (calculated) | Heterogeneous array |
| 33 | All VarUInt minimal | NON_MINIMAL_VARUINT | (calculated) | Non-minimal encoding |
| 34 | Float values canonical | INVALID_FLOAT | (calculated) | NaN or invalid float |
| 35 | Recursion depth â‰¤ 32 | RECURSION_LIMIT_EXCEEDED | (calculated) | Too deeply nested |
| 36 | String length â‰¤ 16 MB | LIMIT_EXCEEDED | (calculated) | String too long |
| 37 | Array count â‰¤ 1,000,000 | LIMIT_EXCEEDED | (calculated) | Array too large |
| 38 | Field count â‰¤ 65,536 | LIMIT_EXCEEDED | (calculated) | Too many fields |
| 39 | Field count = schema count | FIELD_COUNT_MISMATCH | (calculated) | Mismatch |
| 40 | CRC32 matches (if present) | CRC32_MISMATCH | crcOffset | Corruption detected |
| 41 | BLAKE3 matches (if present, advisory) | BLAKE3_MISMATCH | blake3Offset | Advisory hash fail |

**First-Error Rule (MUST)**:
- On first validation failure, stop immediately
- Return error code + byte offset
- Do not continue validation
- Do not attempt recovery

---

## 4. Error Model and Codes

### 4.1 Error Code Definitions

All error codes MUST map to deterministic outcomes. Codes are numbered 1-100 (0 = OK).

| Code | Name | Meaning | Byte Offset | Recovery | C99 | .NET |
|------|------|---------|------------|----------|-----|------|
| 0 | OK | File is valid | 0 | Proceed | 0 | OK |
| 1 | TRUNCATED_FILE | File < 64 bytes | 0 | No | TRUNCATED | TruncatedFile |
| 2 | INVALID_MAGIC | Magic â‰  "ICFG" | 0 | No | INVALID_MAGIC | InvalidMagic |
| 3 | INVALID_VERSION | Version â‰  1 | 4 | No | INVALID_VERSION | InvalidVersion |
| 4 | INVALID_FLAGS | Bits 3-7 set | 5 | No | INVALID_FLAGS | InvalidFlags |
| 5 | RESERVED_FIELD_NONZERO | Reserved field â‰  0 | 6/44/48 | No | RESERVED_NONZERO | ReservedNonZero |
| 6 | FLAG_MISMATCH | Flag â†” offset mismatch | 5 | No | FLAG_MISMATCH | FlagMismatch |
| 7 | BOUNDS_VIOLATION | Offset/size invalid | calculated | No | BOUNDS_VIOLATION | BoundsViolation |
| 8 | ARITHMETIC_OVERFLOW | Overflow in math | 12 | No | OVERFLOW | ArithmeticOverflow |
| 9 | TRUNCATED_BLOCK | Block extends beyond file | calculated | No | TRUNCATED_BLOCK | TruncatedBlock |
| 10 | INVALID_SCHEMA | Schema parse error | calculated | No | INVALID_SCHEMA | InvalidSchema |
| 11 | FIELD_ORDER_VIOLATION | fieldId not ascending | calculated | No | FIELD_ORDER | FieldOrderViolation |
| 12 | INVALID_STRING | Invalid UTF-8 | calculated | No | INVALID_UTF8 | InvalidString |
| 13 | INVALID_TYPE_CODE | Unknown type byte | calculated | No | INVALID_TYPE | InvalidTypeCode |
| 14 | FIELD_TYPE_MISMATCH | Type â‰  schema | calculated | No | TYPE_MISMATCH | FieldTypeMismatch |
| 15 | MISSING_REQUIRED_FIELD | Required field null | calculated | No | MISSING_FIELD | MissingRequiredField |
| 16 | UNKNOWN_FIELD | Undeclared field | calculated | No | UNKNOWN_FIELD | UnknownField |
| 17 | FIELD_COUNT_MISMATCH | Count â‰  schema | calculated | No | FIELD_COUNT | FieldCountMismatch |
| 18 | ARRAY_TYPE_MISMATCH | Array elements mixed type | calculated | No | ARRAY_TYPE | ArrayTypeMismatch |
| 19 | NON_MINIMAL_VARUINT | VarUInt not minimal | calculated | No | NON_MINIMAL | NonMinimalVarUint |
| 20 | INVALID_FLOAT | NaN or invalid f64 | calculated | No | INVALID_FLOAT | InvalidFloat |
| 21 | RECURSION_LIMIT_EXCEEDED | Nesting > 32 | calculated | No | RECURSION_LIMIT | RecursionLimitExceeded |
| 22 | LIMIT_EXCEEDED | String/array/field limit | calculated | No | LIMIT_EXCEEDED | LimitExceeded |
| 23 | CRC32_MISMATCH | CRC32 computation â‰  stored | crcOffset | No | CRC_MISMATCH | Crc32Mismatch |
| 24 | BLAKE3_MISMATCH | BLAKE3 computation â‰  stored | blake3Offset | Advisory | BLAKE3_MISMATCH | Blake3Mismatch |

### 4.2 Byte Offset Reporting (MUST)

Every error MUST report the byte offset where error was detected:

**Rules**:
- Offset MUST point to first byte of problematic element
- Offset MUST be within file bounds (â‰¤ fileSize)
- Offset MUST be exact (not approximate)
- If multiple errors in sequence, report first only
- If calculated offset unavailable, report nearest block offset

**Examples**:
- Invalid magic: offset = 0 (first byte of magic field)
- Invalid version: offset = 4 (version byte)
- Invalid fieldId order: offset = byte position of out-of-order fieldId in schema
- Invalid string: offset = byte position of invalid UTF-8 sequence
- Type mismatch: offset = byte position of type byte in data
- CRC mismatch: offset = crcOffset (location of stored CRC)

**Calculation Rules**:
- Header offsets: fixed (0, 4, 5, 6, etc.)
- Block offsets: calculated from header
- Schema field offset: schemaOffset + cumulative bytes up to field
- Data element offset: dataOffset + cumulative bytes up to element

---

## 5. Limits and DoS Policy

### 5.1 Hard Limits (MUST be enforced)

| Limit | Value | Enforcement Point | Error Code |
|-------|-------|-------------------|-----------|
| Max file size | 256 MB (268,435,456 bytes) | Step 12 (file size match) | LIMIT_EXCEEDED |
| Max recursion depth | 32 levels | During data block parsing | RECURSION_LIMIT_EXCEEDED |
| Max field count in schema | 65,536 | During schema parsing | LIMIT_EXCEEDED |
| Max array element count | 1,000,000 | During array parsing | LIMIT_EXCEEDED |
| Max string length | 16 MB (16,777,216 bytes) | During string parsing | LIMIT_EXCEEDED |
| Max dictionary size | 1,000,000 bytes total | During schema/pool parsing | LIMIT_EXCEEDED |

**Enforcement Rules (MUST)**:
- Check limit BEFORE allocation (if applicable)
- Check limit BEFORE traversal (if applicable)
- Return specific error on limit violation
- Do not attempt to process beyond limit
- Include expected limit and actual value in error message (if possible)

### 5.2 DoS Policy (MUST)

**Principle**: Prevent resource exhaustion while reading untrusted files.

**Work Limits**:
- Max bytes scanned: equal to fileSize (no quadratic scans)
- Max field lookups: O(n) per field, no nested searches
- Max VarUInt reads: bounded by offset field size
- Max string comparisons: O(n log n) for sorting validation (or O(nÂ˛) if checking all pairs)

**Memory Limits**:
- No allocation for untrusted input size (precompute from header)
- String pool: max 1 MB in single allocation
- Schema: max 1 MB in single allocation
- Data block: max 256 MB (entire file size)
- Stack depth: iterative parsing preferred, recursion â‰¤ 32

**Time Limits**:
- validate_fast: â‰¤ 1 ms for any file size
- validate_strict: â‰¤ 100 ms for 1 MB file (linear scanning allowed)
- No exponential operations

**Specific DoS Vectors Blocked**:
| Vector | Prevention | Check |
|--------|-----------|-------|
| Zip bomb (high compression ratio) | No compression required by format | N/A |
| Deep nesting | Max depth = 32 | RECURSION_LIMIT_EXCEEDED |
| Large array count | Max = 1,000,000 | LIMIT_EXCEEDED |
| Large string length | Max = 16 MB | LIMIT_EXCEEDED |
| Large field count | Max = 65,536 | LIMIT_EXCEEDED |
| Offset pointing backward | Monotonic offset check | BOUNDS_VIOLATION |
| Overlapping blocks | Offset ordering enforced | BOUNDS_VIOLATION |
| Invalid VarUInt loops | Max bytes per type enforced | NON_MINIMAL_VARUINT or BOUNDS_VIOLATION |
| Slowloris (tiny reads) | Linear scan only, no retries | N/A |

---

## 6. Corruption and Malformation Handling

### 6.1 Single-Bit Flip Detection

**Scenario**: Exactly 1 bit is flipped in data or header.

**Detection Guarantee (MUST)**:
- **In header**: 100% detection (validation checks every field)
- **In data with CRC32**: 99.9999% detection (polynomial 0xEDB88320, undetected ~1 in 4 billion)
- **In data without CRC32**: No detection (only structure validation)

**Handling (MUST)**:
- CRC32 failure: return CRC32_MISMATCH immediately
- Structural failure: return appropriate error code (type, bounds, etc.)
- No recovery: file is rejected, not repaired

### 6.2 Truncation Detection

**Scenario**: File is incomplete (header or block truncated).

**Detection (MUST)**:
- File < 64 bytes: TRUNCATED_FILE at step 1
- Header declares size > actual: TRUNCATED_FILE at step 12
- Block extends beyond file: BOUNDS_VIOLATION or TRUNCATED_BLOCK at block parsing

**Handling (MUST)**:
- Return specific error with byte offset of truncation point
- Partial data is never used
- File is rejected

### 6.3 Invalid Offset and Overlap Detection

**Scenarios**:
- Offset points outside file bounds
- Blocks overlap (schemaOffset + schemaSize > dataOffset)
- Offset arithmetic overflows (offset + size > 2^32)
- Offsets not in ascending order

**Detection (MUST)**:
- Step 10: offset monotonicity check
- Step 11: overflow check
- Step 12: file size consistency check

**Handling (MUST)**:
- Return BOUNDS_VIOLATION or ARITHMETIC_OVERFLOW
- Calculate and report exact byte offset of violation
- File is rejected

### 6.4 Checksum Mismatch Handling

**CRC32 Mismatch (MUST)**:
- Detected at step 40
- Return CRC32_MISMATCH immediately
- Byte offset = crcOffset (location of stored checksum)
- File is rejected, not repaired
- No recovery or "best effort"

**BLAKE3 Mismatch (SHOULD)**:
- Detected at step 41
- May return BLAKE3_MISMATCH or continue (advisory only)
- Parser implementation may log but not fail
- Use case: post-validation detection (not core validation)

---

## 7. Specific Validation Rules by Block

### 7.1 Header Validation

**Mandatory (validate_fast)**:
- Magic, version, flags
- Reserved field values
- Flag-offset consistency
- Offset monotonicity
- File size match
- No integer overflow in offset arithmetic

**Optional (validate_strict)**:
- Recompute expected offsets (redundant check)
- Verify header size field = 32

### 7.2 Schema Validation

**Mandatory (validate_strict)**:
- Schema block bounds (offset + size â‰¤ fileSize)
- Schema parseable (VarUInt counts, field structures)
- fieldId values in ascending order
- fieldName values sorted lexicographically by UTF-8
- No duplicate fieldId
- No duplicate fieldName
- All fieldType values valid (0-6)
- All isRequired values valid (0x00 or 0x01)

**Detectability**:
- Non-minimal VarUInt: reject with NON_MINIMAL_VARUINT
- Invalid field type: reject with INVALID_SCHEMA
- Non-ascending fieldId: reject with FIELD_ORDER_VIOLATION

### 7.3 String Pool Validation (if present)

**Mandatory (validate_strict)**:
- Pool block bounds (offset + size â‰¤ fileSize)
- Pool parseable (VarUInt counts, string lengths)
- All strings valid UTF-8
- Strings sorted lexicographically by UTF-8 byte order
- No duplicate strings

**Detectability**:
- Non-minimal VarUInt: reject with NON_MINIMAL_VARUINT
- Invalid UTF-8: reject with INVALID_STRING
- Non-sorted strings: reject with INVALID_SCHEMA
- String ID out of range: reject with BOUNDS_VIOLATION

### 7.4 Data Block Validation

**Mandatory (validate_strict)**:
- Data block bounds (offset + size â‰¤ fileSize)
- Root value is object type (0x40)
- Object fieldCount = schema fieldCount
- Object fieldId values in ascending order
- Each object fieldId matches schema fieldId
- Each field value type matches schema type
- Required fields not null
- No unknown fields
- Array elements homogeneous
- Array type matches schema

**Type-Specific Rules**:
- Null: type 0x00, no payload
- Boolean: type 0x01 or 0x02 only
- Integer: fixed 8 bytes, little-endian
- Float: IEEE 754, no NaN, -0.0 normalized
- String: valid UTF-8 or valid pool reference
- Bytes: any bytes allowed, VarUInt length
- Array: homogeneous, count matches element count
- Object: fieldId ascending, all fields present

**Detectability**:
- Non-minimal VarUInt: reject with NON_MINIMAL_VARUINT
- Invalid type code: reject with INVALID_TYPE_CODE
- Type mismatch: reject with FIELD_TYPE_MISMATCH
- NaN in float: reject with INVALID_FLOAT
- Recursion depth > 32: reject with RECURSION_LIMIT_EXCEEDED

---

## 8. C99 vs .NET Determinism

### 8.1 Header Validation Equivalence

**MUST**:
- Both parse header identically (little-endian byte order explicit)
- Both enforce same reserved field values
- Both check same flag bits
- Both use same offset ordering rules

**Pitfall Prevention**:
- C99: use explicit byte-by-byte reads or `memcpy` + endian conversion (not struct casting)
- .NET: use `BinaryReader` with `LittleEndian` encoding

### 8.2 VarUInt Parsing Equivalence

**MUST**:
- Both use same algorithm (shift, mask, accumulate)
- Both reject non-minimal encodings
- Both detect overflow (reading > max_bytes)

**Pitfall Prevention**:
- C99: use unsigned types (`uint32_t`, `uint64_t`), not signed
- .NET: use `uint` or `ulong`, not `int` or `long`

### 8.3 Float Handling Equivalence

**MUST**:
- Both use IEEE 754 double (8 bytes)
- Both reject NaN (exponent all 1s, mantissa â‰  0)
- Both normalize -0.0 to +0.0

**Pitfall Prevention**:
- C99: read 8 bytes, use `memcpy` to IEEE 754 double, not pointer cast
- .NET: use `BinaryReader.ReadDouble()` directly
- Both: explicit bit-level NaN check (not `isnan()` which may vary)

### 8.4 UTF-8 Validation Equivalence

**MUST**:
- Both validate same UTF-8 rules
- Both use byte-order comparison (not locale or Unicode codepoint)
- Both reject same invalid sequences

**Pitfall Prevention**:
- C99: iterate bytes, check continuation bytes (10xxxxxx), not `iswctype()`
- .NET: use `Encoding.UTF8.GetString()` which validates, or manual byte checks
- Both: explicit byte-level comparison for sorting

### 8.5 Recursion Depth Tracking Equivalence

**MUST**:
- Both track depth identically (start at 0, increment for object/array, decrement on exit)
- Both reject at depth > 32
- Both use same recursion (or both use iteration with explicit stack)

**Pitfall Prevention**:
- C99: explicit depth counter if iterative, or recursive function with depth limit
- .NET: same (depth counter or stack unwinding)

---

## 9. Error Reporting Format

### 9.1 Error Structure (MUST)

All parsers MUST return error in this format:

```
{
  error_code: u32,           // 0 = OK, 1-100 = specific error
  byte_offset: u32,          // Exact byte offset of error (within file)
  message: string (optional) // Human-readable message
}
```

**In C99**:
```c
struct IronCfgError {
  uint32_t code;
  uint32_t offset;
  const char *message;  // May be NULL
};
```

**In .NET**:
```csharp
public class IronCfgError {
  public uint Code { get; set; }
  public uint Offset { get; set; }
  public string Message { get; set; }  // May be null
}
```

### 9.2 Message Guidelines (SHOULD)

Messages are optional but helpful:

- Include error name (e.g., "INVALID_MAGIC")
- Include byte offset (e.g., "at offset 0")
- Include expected vs actual (if brief), e.g., "expected version 1, got 2"
- Do not include file path (security/privacy)
- Do not include full binary dump

**Example**: "INVALID_MAGIC at offset 0: expected 'ICFG', got 'XXXX'"

### 9.3 Exit Codes (MUST, CLI only)

Command-line validation tools MUST use:
- Exit code 0: validation passed
- Exit code 1: validation failed (generic error)
- Exit code 2: usage error (invalid arguments)

**The specific error code (1-100) MUST be available in error structure.**

---

## 10. Validation Testing Requirements

### 10.1 Determinism Test (MUST)

**Procedure**:
```
for each test file:
  result1 = validate_strict(file)
  result2 = validate_strict(file)
  result3 = validate_strict(file)
  assert result1 == result2 == result3 (error code + offset identical)
```

**Expected**:
- Repeated validation produces identical error (or success)
- No random or platform-dependent results

### 10.2 Cross-Language Equivalence Test (MUST)

**Procedure**:
```
for each test file:
  c_result = ironcfg_validate(file)
  net_result = IronConfigValidator.Validate(file)
  assert c_result.code == net_result.code
  assert c_result.offset == net_result.offset
```

**Expected**:
- C99 and .NET report identical error codes and offsets
- No deviation in detection logic

### 10.3 Malformed File Coverage Test (MUST)

Test that all error codes are detectable:
- [ ] TRUNCATED_FILE: file < 64 bytes
- [ ] INVALID_MAGIC: magic â‰  "ICFG"
- [ ] INVALID_VERSION: version â‰  1
- [ ] INVALID_FLAGS: unknown flag bits
- [ ] RESERVED_FIELD_NONZERO: reserved â‰  0
- [ ] FLAG_MISMATCH: CRC flag â†” offset mismatch
- [ ] BOUNDS_VIOLATION: offset out of bounds
- [ ] ARITHMETIC_OVERFLOW: offset arithmetic overflow
- [ ] TRUNCATED_BLOCK: block extends beyond file
- [ ] INVALID_SCHEMA: schema parse error
- [ ] FIELD_ORDER_VIOLATION: fieldId not ascending
- [ ] INVALID_STRING: invalid UTF-8
- [ ] INVALID_TYPE_CODE: unknown type byte
- [ ] FIELD_TYPE_MISMATCH: type â‰  schema
- [ ] MISSING_REQUIRED_FIELD: required field null
- [ ] UNKNOWN_FIELD: undeclared field
- [ ] FIELD_COUNT_MISMATCH: count â‰  schema
- [ ] ARRAY_TYPE_MISMATCH: array elements mixed
- [ ] NON_MINIMAL_VARUINT: non-minimal encoding
- [ ] INVALID_FLOAT: NaN or invalid f64
- [ ] RECURSION_LIMIT_EXCEEDED: depth > 32
- [ ] LIMIT_EXCEEDED: field/array/string limit
- [ ] CRC32_MISMATCH: CRC32 mismatch
- [ ] BLAKE3_MISMATCH: BLAKE3 mismatch

### 10.4 Corruption Detection Test (MUST)

**Procedure**:
```
for each golden vector with CRC32:
  file = read_binary(vector)
  for offset in 0 to len(file)-5:  // skip CRC bytes
    corrupted = file.copy()
    corrupted[offset] ^= 0x01  // flip 1 bit
    result = validate_strict(corrupted)
    // Result MUST be CRC32_MISMATCH or structure error
    assert result.code != OK
```

**Expected**:
- Single-bit flip is detected >99.99% of the time
- Either CRC32_MISMATCH or structural error
- No silent corruption

---

## 11. Validation Checklist

Use this checklist to verify validation implementation:

- [ ] validate_fast completes in O(1) time
- [ ] validate_strict completes in O(n) time (n = file size)
- [ ] First error is reported immediately
- [ ] Byte offset is calculated and reported for every error
- [ ] All 24 error codes are defined and distinct
- [ ] Reserved fields checked = 0x00
- [ ] Offsets are monotonic (no backward jumps)
- [ ] File size is validated at header
- [ ] Schema fieldId ascending enforced
- [ ] Schema fieldName lexicographic sorting enforced
- [ ] String pool (if present) is sorted and validated
- [ ] Data block root is object type
- [ ] Object fieldCount matches schema
- [ ] All required fields present (not null)
- [ ] No unknown fields allowed
- [ ] Array elements homogeneous
- [ ] VarUInt minimal encoding enforced
- [ ] Float values canonical (no NaN, -0.0 â†’ +0.0)
- [ ] Recursion depth â‰¤ 32
- [ ] String length â‰¤ 16 MB
- [ ] Array count â‰¤ 1,000,000
- [ ] Field count â‰¤ 65,536
- [ ] CRC32 computed and validated (if present)
- [ ] BLAKE3 computed and validated (if present, advisory)
- [ ] C99 and .NET implementations match error codes and offsets
- [ ] Determinism test: validate 3Ă—, all identical
- [ ] Corruption test: single-bit flip detected >99%
- [ ] All error messages do not include file path or binary dump
- [ ] No undefined behavior on invalid input
- [ ] No memory leaks on validation failure

---

## 12. References

- spec/IRONCFG.md â€” Binary format specification
- spec/IRONCFG_CANONICAL.md â€” Canonicalization rules
- FAMILY_STANDARD.md â€” Family-wide requirements
- tools/ironcert/ â€” Certification tool (enforcement reference)
- vectors/small/ironcfg/README.md â€” Test vectors and procedures
