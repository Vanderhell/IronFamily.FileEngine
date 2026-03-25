# ICFG Format Specification v1

**Date**: 2026-03-14
**Version**: 1 (v1 and v2 backward compatible)
**Status**: LOCKED for v1
**Source**: Live code analysis (IronCfgHeader.cs, IronCfgEncoder.cs, IronCfgValidator.cs, ironcfg_*.c)

---

## 1. Magic and Version

### Magic Number
- **Magic**: `0x47464349` (little-endian bytes: 0x49 0x43 0x46 0x47, ASCII: "ICFG")
- **Purpose**: File format identification
- **Validation**: Must match exactly or file is rejected with `InvalidMagic` error
- **Location**: Bytes 0-3 of header

### Version
- **Field**: 1 byte at offset 4
- **Current versions**: 1, 2 (backward compatible)
- **v1**: Basic ICFG format (magic, header, schema, data, optional CRC32/BLAKE3)
- **v2**: Added ElementSchema for Array fields (enhanced type definitions)
- **Future**: New versions with incremented byte value
- **Validation**: Only versions 1 and 2 accepted; others rejected with `InvalidVersion` error
- **Location**: Byte 4 of header

---

## 2. Header Layout (64 bytes, little-endian)

Fixed-size header follows magic/version:

| Offset | Size | Field | Type | Purpose |
|--------|------|-------|------|---------|
| 0 | 4 | magic | u32LE | "ICFG" identifier |
| 4 | 1 | version | u8 | File format version (1 or 2) |
| 5 | 1 | flags | u8 | Feature flags (bits 0-2 defined, 3-7 reserved) |
| 6 | 2 | reserved0 | u16LE | Must be 0x0000 |
| 8 | 4 | file_size | u32LE | Total file size in bytes |
| 12 | 4 | schema_offset | u32LE | Byte offset to schema block |
| 16 | 4 | schema_size | u32LE | Schema block size in bytes |
| 20 | 4 | string_pool_offset | u32LE | Byte offset to string pool (0 if absent) |
| 24 | 4 | string_pool_size | u32LE | String pool size in bytes |
| 28 | 4 | data_offset | u32LE | Byte offset to data block (root object) |
| 32 | 4 | data_size | u32LE | Data block size in bytes |
| 36 | 4 | crc_offset | u32LE | Byte offset to CRC32 (0 if absent) |
| 40 | 4 | blake3_offset | u32LE | Byte offset to BLAKE3 hash (0 if absent) |
| 44 | 4 | reserved1 | u32LE | Must be 0x00000000 |
| 48 | 16 | reserved2 | u8[16] | All bytes must be 0x00 |

**Total header size**: 64 bytes

---

## 3. Flags Byte (offset 5)

Bit layout (8 bits, little-endian):

| Bit | Name | Meaning | Constraint |
|-----|------|---------|-----------|
| 0 | CRC32 | If set: CRC32 checksum present at crc_offset | If set, crc_offset must be non-zero; if clear, crc_offset must be zero |
| 1 | BLAKE3 | If set: BLAKE3 hash present at blake3_offset | If set, blake3_offset must be non-zero; if clear, blake3_offset must be zero |
| 2 | EmbeddedSchema | Reserved for future use (currently unused) | Must be zero in v1; v2 may use for schema embedding |
| 3-7 | Reserved | Must be zero in all versions | Any set bit is validation error: `InvalidFlags` |

**Flag validation rules**:
- All reserved bits (3-7) must be zero, else error `InvalidFlags`
- CRC32 flag ↔ crc_offset consistency, else error `FlagMismatch`
- BLAKE3 flag ↔ blake3_offset consistency, else error `FlagMismatch`

---

## 4. Block Layout and Offsets

File structure (typical with all blocks):

```
[Header: 64 bytes]
[Schema block: schema_size bytes]
[String pool: string_pool_size bytes (optional)]
[Data block: data_size bytes]
[CRC32: 4 bytes (optional)]
[BLAKE3: 32 bytes (optional)]
```

### Offset Monotonicity

All offsets must follow strict ordering:
```
schema_offset < schema_offset + schema_size ≤ pool_start
pool_start ≤ pool_end ≤ data_offset
data_offset < data_offset + data_size ≤ crc_offset (if present)
crc_offset + 4 ≤ blake3_offset (if both present)
blake3_offset + 32 = file_size (if blake3 present)
```

**Where**:
- `pool_start = string_pool_offset > 0 ? string_pool_offset : data_offset`
- `pool_end = string_pool_offset > 0 ? string_pool_offset + string_pool_size : data_offset`

**Validation error**: `BoundsViolation` if ordering violated

### File Size Calculation

Expected file size = data_offset + data_size + (4 if CRC32 flag) + (32 if BLAKE3 flag)

Actual file size must match exactly, else error `BoundsViolation`

---

## 5. Schema Block

**Format**: Variable-length encoded schema definition

### Schema Encoding

```
[VarUInt: field_count]
For each field (in ascending fieldId order):
  [VarUInt: fieldId]
  [u8: typeCode]
  If typeCode >= 0x1C (compound type):
    [VarUInt: name_length]
    [name_length bytes: field name (UTF-8)]
    If typeCode == 0x30 (Array, v2+ only):
      [Recursive ElementSchema block]
```

### Schema Constraints

- Field count: 1 to MAX_FIELDS (65,536)
- Field IDs: Must be in strictly ascending order (no duplicates, no gaps allowed)
- Field IDs: Can be 0 to 2^32-1
- Type codes: 0x00 through 0x40, or reserved/compound
- Field names: Required only for compound types (typeCode >= 0x1C)
- Field names: Must be valid UTF-8
- Nesting: Max recursion depth 32 levels

**Validation errors**:
- `InvalidSchema`: Schema block missing or unparseable
- `FieldOrderViolation`: Field IDs not in ascending order
- `LimitExceeded`: Field count > MAX_FIELDS
- `InvalidTypeCode`: Type code not in valid range
- `InvalidString`: Field name not valid UTF-8

---

## 6. Type System

### Primitive Types

| Code | Type | Size | Encoding | Notes |
|------|------|------|----------|-------|
| 0x00 | Null | 1 byte | Type byte only | Represents null/empty value |
| 0x01 | Bool (false) | 1 byte | 0x01 | Boolean false |
| 0x02 | Bool (true) | 1 byte | 0x02 | Boolean true |
| 0x10 | Int64 | 9 bytes | 0x10 + 8 bytes LE | Signed 64-bit, 2's complement |
| 0x11 | UInt64 | 9 bytes | 0x11 + 8 bytes LE | Unsigned 64-bit |
| 0x12 | Float64 | 9 bytes | 0x12 + 8 bytes LE | IEEE 754 double, LE |

### Compound Types

| Code | Type | Structure | Notes |
|------|------|-----------|-------|
| 0x20 | String | 0x20 + VarUInt(length) + UTF-8 | Max 16 MB per string |
| 0x22 | Bytes | 0x22 + VarUInt(length) + binary data | Max 16 MB per binary |
| 0x30 | Array | 0x30 + VarUInt(count) + elements | Max 1M elements, homogeneous type |
| 0x40 | Object | 0x40 + VarUInt(field_count) + fields | Root must be Object |

### Compound Type Flags

Type codes >= 0x1C (0x20, 0x22, 0x30, 0x40, etc.) indicate compound types.
For compound types, the schema stores field names explicitly.

---

## 7. Data Block

### Root Type

**Constraint**: Root value must always be type 0x40 (Object)

**Encoding**: Data block starts with 0x40 byte, followed by root object fields

### Object Encoding

```
[u8: 0x40 (type)]
[VarUInt: field_count]
For each field (in ascending fieldId order):
  [VarUInt: fieldId]
  [u8: valueTypeCode]
  [Value data (type-specific)]
```

### Array Encoding

```
[u8: 0x30 (type)]
[VarUInt: element_count]
For each element:
  [u8: elementTypeCode]
  [Element data (type-specific)]
```

**Constraint**: All array elements must have same type code

### String Encoding

```
[u8: 0x20 (type)]
[VarUInt: byte_length]
[byte_length bytes: UTF-8 data]
```

**Constraint**: All bytes must be valid UTF-8

### Field Data Layout

For primitives: value is inline (e.g., 0x10 + 8 bytes for int64)

For compounds: type byte + VarUInt size/count + data

---

## 8. CRC32 Checksum (Optional)

### When Present

If CRC32 flag (bit 0) is set in header, 4-byte CRC32 follows data block.

### Algorithm

- **Polynomial**: IEEE 802.3 (0xEDB88320, reflected)
- **Initial value**: 0xFFFFFFFF
- **Final XOR**: 0xFFFFFFFF
- **Input**: All bytes from offset 0 to crc_offset (excluding CRC32 itself)
- **Output**: 4 bytes little-endian at crc_offset

### Validation

Recompute CRC32 over covered region and compare with stored value.
Mismatch → error `Crc32Mismatch`

---

## 9. BLAKE3 Hash (Optional)

### When Present

If BLAKE3 flag (bit 1) is set in header, 32-byte BLAKE3 hash follows CRC32 (or data, if no CRC32).

### Algorithm

- **Hash function**: BLAKE3 (32-byte output)
- **Input**: All bytes from offset 0 to blake3_offset (excluding BLAKE3 itself)
- **Output**: 32 bytes at blake3_offset

### Validation

Requires BLAKE3 library. Recompute hash and compare.
Mismatch → error `Blake3Mismatch`

**Note**: BLAKE3 validation currently checks structure only (not cryptographic hash) due to library integration status.

---

## 10. Deterministic Encoding

### Float Normalization

- **-0.0 normalization**: Negative zero (0x8000000000000000) is normalized to positive zero (0x0000000000000000)
- **NaN rejection**: Any NaN value (exponent all 1s, mantissa non-zero) is rejected with error `InvalidFloat`
- **Implementation**: Check signbit(), reject if NaN detected

### Field Ordering

Objects always encode fields in ascending fieldId order.
Encoder must use sorted data structure (e.g., SortedDictionary) to ensure determinism.

### VarUInt Encoding

- **Minimal encoding**: No unnecessary leading bytes
- **Max size**: 5 bytes for 32-bit values, 10 bytes for 64-bit
- **Example**: Value 127 → 1 byte (0x7F), value 128 → 2 bytes (0x80 0x01)

**Non-minimal encoding** (e.g., 0x80 0x00 for value 0) is detected and rejected with error `NonMinimalVarUint`

### String/Bytes Determinism

- String length is encoded as VarUInt (deterministic per length value)
- UTF-8 data must match exactly
- No string normalization (NFC/NFD) applied

---

## 11. Validation Modes

### Fast Validation (O(1))

Validates header only:
- Magic number matches
- Version is 1 or 2
- Flags bits 3-7 are zero
- Reserved fields are zero
- Offset monotonicity
- Flag-to-offset consistency
- File size matches calculated value

Returns immediately on first error.

### Strict Validation (O(n))

Validates all blocks:
- Fast validation (all header checks)
- Schema block parsing and field validation
- String pool UTF-8 validation
- Data block root type (must be Object)
- CRC32 verification (if present)
- BLAKE3 structure verification (if present)
- Field count limits
- String length limits
- Recursion depth limits
- VarUInt minimality checks

---

## 12. Error Codes and Handling

| Code | Name | Meaning |
|------|------|---------|
| 0 | OK | No error |
| 1 | TruncatedFile | File shorter than expected |
| 2 | InvalidMagic | Magic number does not match "ICFG" |
| 3 | InvalidVersion | Version not 1 or 2 |
| 4 | InvalidFlags | Bits 3-7 of flags are set |
| 5 | ReservedFieldNonzero | Reserved fields contain non-zero bytes |
| 6 | FlagMismatch | CRC/BLAKE3 flag inconsistent with offsets |
| 7 | BoundsViolation | Offset ordering violated or file size mismatch |
| 8 | ArithmeticOverflow | Offset arithmetic overflowed |
| 9 | TruncatedBlock | Block shorter than declared |
| 10 | InvalidSchema | Schema unparseable |
| 11 | FieldOrderViolation | Fields not in ascending fieldId order |
| 12 | InvalidString | String not valid UTF-8 |
| 13 | InvalidTypeCode | Type code not in 0x00-0x40 range |
| 14 | FieldTypeMismatch | Value type does not match schema type |
| 15 | MissingRequiredField | Required field absent |
| 16 | UnknownField | Field ID not in schema |
| 17 | FieldCountMismatch | Object field count != schema field count |
| 18 | ArrayTypeMismatch | Array element type inconsistent |
| 19 | NonMinimalVarUint | VarUInt uses unnecessary bytes |
| 20 | InvalidFloat | NaN detected in float field |
| 21 | RecursionLimitExceeded | Nesting depth > 32 |
| 22 | LimitExceeded | Field count, string length, or array size exceeds limit |
| 23 | Crc32Mismatch | CRC32 does not match |
| 24 | Blake3Mismatch | BLAKE3 hash does not match |

---

## 13. Hard Limits

| Limit | Value | Purpose |
|-------|-------|---------|
| MAX_FILE_SIZE | 256 MB | Prevents DoS from huge files |
| MAX_STRING_LENGTH | 16 MB | Prevents DoS from huge strings |
| MAX_FIELDS | 65,536 | Limits schema complexity |
| MAX_ARRAY_ELEMENTS | 1,000,000 | Limits array size |
| MAX_RECURSION_DEPTH | 32 | Prevents stack overflow from deep nesting |
| VarUInt max bytes | 5 (u32), 10 (u64) | Bounds variable-length encoding |
| Header size | 64 bytes | Fixed for fast parsing |
| CRC32 size | 4 bytes | IEEE standard |
| BLAKE3 size | 32 bytes | Standard hash output |

---

## Summary

ICFG is a binary format optimized for:
- **Deterministic encoding**: Same input → same bytes, always
- **Fast validation**: O(1) header check for most use cases
- **Strict validation**: O(n) full verification when needed
- **Zero-copy reading**: No deserialization required
- **Size efficiency**: Compact binary representation
- **Cryptographic verification**: Optional CRC32 and BLAKE3

Backward compatibility between v1 and v2 is maintained.
Format is ready for production use.

