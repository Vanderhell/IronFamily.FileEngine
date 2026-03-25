> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONCFG: IronConfig Flagship Format Specification

**Version**: 1.0 (Normative Specification)
**Status**: FROZEN
**Date**: 2026-01-16
**Magic**: `ICFG` (0x49 0x43 0x46 0x47)
**Extension**: `.icfg`

> **FROZEN**: This specification is locked. Any incompatible change requires a new magic number and version bump. See FAMILY_STANDARD.md for certification process.

---

## 1. Overview

IRONCFG (IronConfig Flagship Format) is a deterministic, schema-driven binary format optimized for configuration files in PLC, IoT, backend, and industrial control systems. Unlike schemaless formats (BJV), IRONCFG mandates explicit schema definition and enforces strict validation to prevent configuration errors.

**Design Goals**:
1. **Determinism**: Identical input → identical bytes (reproducible builds, git diff-ability)
2. **Schema-driven**: All fields explicitly declared; unknown fields rejected
3. **Safety**: Strict bounds checking, no ambiguity, fail-fast on corruption
4. **Zero-copy reading**: C99 implementation without heap allocation during read
5. **Integrity**: Mandatory CRC32, optional BLAKE3 for critical systems

**Not For**: Game data, asset pipelines, streaming logs, dynamic schemas, or schemaless JSON.

---

## 2. File Structure

### 2.1 Overall Layout

```
[Header (64 bytes, fixed)]
[Schema Block]
[String Pool (optional)]
[Data Block]
[CRC32 (optional, 4 bytes)]
[BLAKE3 (optional, 32 bytes)]
```

**Offset Order Requirement (MUST)**: Schema < String Pool < Data < CRC32 < BLAKE3. No gaps, no overlap.

### 2.2 Header (64 bytes)

All multi-byte integers use **little-endian** byte order.

| Offset | Size | Field | Type | Description |
|--------|------|-------|------|-------------|
| 0-3 | 4 | magic | [u8; 4] | `"ICFG"` (0x49 0x43 0x46 0x47) |
| 4 | 1 | version | u8 | Format version (1 for this spec) |
| 5 | 1 | flags | u8 | Feature flags (see below) |
| 6-7 | 2 | reserved0 | [u8; 2] | MUST be 0x00 0x00 |
| 8-11 | 4 | fileSize | u32 | Total file size in bytes (includes CRC/BLAKE3) |
| 12-15 | 4 | schemaOffset | u32 | Byte offset to schema block start |
| 16-19 | 4 | schemaSize | u32 | Size of schema block in bytes |
| 20-23 | 4 | stringPoolOffset | u32 | Byte offset to string pool (0 if not present) |
| 24-27 | 4 | stringPoolSize | u32 | Size of string pool in bytes (0 if not present) |
| 28-31 | 4 | dataOffset | u32 | Byte offset to data block start |
| 32-35 | 4 | dataSize | u32 | Size of data block in bytes |
| 36-39 | 4 | crcOffset | u32 | Byte offset to CRC32 (0 if not present) |
| 40-43 | 4 | blake3Offset | u32 | Byte offset to BLAKE3 (0 if not present) |
| 44-47 | 4 | reserved1 | u32 | MUST be 0x00000000 |
| 48-63 | 16 | reserved2 | [u8; 16] | MUST be all zeros |

### 2.3 Flags (byte 5)

| Bit | Name | Meaning |
|-----|------|---------|
| 0 | CRC32_PRESENT | File contains CRC32 checksum (SHOULD be 1) |
| 1 | BLAKE3_PRESENT | File contains BLAKE3 hash (optional) |
| 2 | EMBEDDED_SCHEMA | Schema is embedded (1) or external (0) |
| 3-7 | reserved | MUST be 0 |

**Strict Parsing Rule (MUST)**: Unknown flags (bits 3-7 set) MUST cause immediate rejection with error INVALID_FLAGS.

---

## 3. Data Model

### 3.1 Supported Types

IRONCFG supports the following primitive and composite types:

**Primitive Types**:
- `null` — Explicit null value (no payload)
- `bool` — Boolean (false=0x00, true=0x01)
- `i64` — Signed 64-bit integer (little-endian)
- `u64` — Unsigned 64-bit integer (little-endian)
- `f64` — IEEE 754 double (little-endian)
- `string` — UTF-8 string (variable-length)
- `bytes` — Raw binary data (variable-length)

**Composite Types**:
- `object` — Key-value map with declared fields (MUST conform to schema)
- `array` — Homogeneous array of one declared type

**Forbidden Types**:
- Schemaless objects (objects with undeclared keys)
- Heterogeneous arrays (mixed types)
- Recursive types
- Union types
- Optional fields (all fields MUST be declared)

### 3.2 Canonical Numeric Representation

**Integers (i64, u64)**:
- Stored in little-endian binary form
- No variable-length encoding (fixed 8 bytes)
- Overflow detection: parser MUST reject files claiming values outside type range

**Floating Point (f64)**:
- IEEE 754 double precision
- **NaN is forbidden**: Encoders MUST NOT emit NaN; parsers MUST reject files containing NaN
- **-0.0 canonicalization**: Encoders MUST convert -0.0 to +0.0; parsers MUST treat all -0.0 as +0.0
- **Infinity**: Allowed (±Inf)

### 3.3 String Representation

**Encoding (MUST)**:
- UTF-8, no BOM, no null terminator
- Valid UTF-8 sequences only; invalid UTF-8 MUST cause rejection with error INVALID_STRING
- String length in bytes (not characters)

**Canonicalization**:
- No Unicode normalization (NFC, NFD, etc.) applied
- Sorted lexicographically by raw UTF-8 byte value (not Unicode codepoint)
- No case folding

---

## 4. Schema Model

### 4.1 Schema Embedding

The schema defines all fields and their types. IRONCFG supports two modes:

**Embedded Schema (flag bit 2 = 1)**:
- Schema stored in schemaOffset within the file
- Enables standalone files without external references
- MUST be used for most configuration files

**External Schema (flag bit 2 = 0)**:
- Schema defined in companion `.icfg.schema` file (same base name)
- Reduces file size for repeated configurations
- Parser MUST locate and validate external schema before parsing data

### 4.2 Schema Block Format (Embedded)

```
schemaOffset:
  fieldCount: VarUInt                    # Number of fields (MUST match data)

  for i in 0..fieldCount-1:
    fieldId: VarUInt                     # Field index (0-based, MUST be ascending)
    fieldNameLen: VarUInt                # Length of field name in bytes
    fieldName: [u8; fieldNameLen]        # Field name (UTF-8)
    fieldType: u8                        # Type code (see 3.1)
    isRequired: u8                       # 0x01 = required, 0x00 = may be null
```

**Field Requirements (MUST)**:
- `fieldId` values MUST be in strictly ascending order
- `fieldName` strings MUST be sorted lexicographically by UTF-8 byte order
- `fieldCount` MUST match the number of fields in the data block
- `isRequired` = 0x01 means field MUST NOT be null; 0x00 means field MAY be null
- No duplicate `fieldId` or `fieldName`

### 4.3 External Schema Format

External schema file (`config.icfg.schema`):

```
[Same as embedded schema block, but standalone]
[Optional CRC32 of schema]
```

**Validation (MUST)**:
- Parser loads external schema before parsing data
- Hash or version of external schema MUST be validated against data file metadata (if available)
- Mismatch MUST cause error SCHEMA_MISMATCH

---

## 5. String Pool (Optional)

If string pool is present (checked by attempting to read at stringPoolOffset):

```
stringPoolOffset:
  stringCount: VarUInt                   # Number of interned strings

  for i in 0..stringCount-1:
    stringLen: VarUInt                   # Length of string in bytes
    stringData: [u8; stringLen]          # UTF-8 string data
```

**Rules (MUST)**:
- Strings MUST be sorted lexicographically by UTF-8 byte order
- No duplicate strings
- Used when multiple string values repeat in the data block
- If present, string references use stringId (index into pool)

---

## 6. Data Block Format

### 6.1 Root Data Structure

```
dataOffset:
  rootValue: [TLV-encoded value]
```

The root value MUST be an object (type 0x40) matching the schema. It contains all configuration data.

### 6.2 Type-Length-Value (TLV) Encoding

All values are encoded as:

```
typeByte: u8
[payload specific to type]
```

**Type Codes**:

| Type | Code | Payload |
|------|------|---------|
| null | 0x00 | (none) |
| false | 0x01 | (none) |
| true | 0x02 | (none) |
| i64 | 0x10 | 8 bytes LE |
| u64 | 0x11 | 8 bytes LE |
| f64 | 0x12 | 8 bytes LE |
| string (inline) | 0x20 | VarUInt len + bytes |
| string (pooled) | 0x21 | VarUInt stringId |
| bytes | 0x22 | VarUInt len + bytes |
| array | 0x30 | VarUInt count + TLV values |
| object | 0x40 | VarUInt count + field pairs |

### 6.3 Object Encoding

```
0x40
VarUInt fieldCount
repeat fieldCount:
  fieldId: VarUInt                       # MUST match schema fieldId
  [TLV value]
```

**Rules (MUST)**:
- `fieldCount` MUST equal number of fields declared in schema
- `fieldId` values MUST be in strictly ascending order (canonical)
- All schema fields MUST be present (no omitted fields)
- Field types MUST match schema declaration exactly
- If schema says field is required, value MUST NOT be null
- If schema says field may be null, null is allowed (type 0x00)

### 6.4 Array Encoding

```
0x30
VarUInt elementCount
repeat elementCount:
  [TLV value, all same type]
```

**Rules (MUST)**:
- All elements MUST have the same type (homogeneous)
- Element type MUST match schema array type
- Empty arrays (elementCount=0) allowed
- No element is allowed to be a nested object (unless explicitly array-of-objects in schema)

---

## 7. Variable-Length Integer Encoding (VarUInt)

VarUInt encodes unsigned 32-bit and 64-bit integers with minimal bytes (ULEB128):

**Format**:
- Bytes 0-126: Single byte `0xxxxxxx`
- Bytes 127-16383: Two bytes `1xxxxxxx 0xxxxxxx`
- Bytes 16384+: Up to 5 bytes (u32) or 10 bytes (u64)

**Canonical Rule (MUST)**:
- Minimal byte encoding only; non-minimal encodings (e.g., `0x80 0x00` for 0) MUST be rejected with error NON_MINIMAL_VARUINT
- Parsers MUST enforce this during all VarUInt reads

---

## 8. Integrity Mechanisms

### 8.1 CRC32 (SHOULD be present)

If flag bit 0 = 1:

```
crcOffset:
  [4 bytes, u32 LE]
```

**Computation (MUST)**:
- Algorithm: IEEE 802.3 (polynomial 0xEDB88320, CRC-32)
- Data covered: bytes [0 .. crcOffset - 1]
- Standard initialization: 0xFFFFFFFF
- Final result: inverted (XOR 0xFFFFFFFF)
- Stored little-endian at crcOffset

**Validation (MUST)**:
- Parsers with CRC32 present MUST compute and verify
- Mismatch MUST cause error CRC32_MISMATCH
- Abort parsing if CRC fails (fail-fast)

### 8.2 BLAKE3 (OPTIONAL, for high-security systems)

If flag bit 1 = 1:

```
blake3Offset:
  [32 bytes, first 256 bits of BLAKE3 hash]
```

**Computation (MUST)**:
- Algorithm: BLAKE3 (blake3.io)
- Data covered: bytes [0 .. blake3Offset - 1]
- Output: 32 bytes (truncated from full 64-byte hash)
- Stored as raw bytes

**Validation (SHOULD)**:
- Parsers with BLAKE3 present SHOULD verify
- Mismatch SHOULD cause error BLAKE3_MISMATCH
- BLAKE3 verification does NOT block parsing (advisory only)

---

## 9. Determinism Rules

### 9.1 Byte-for-Byte Reproducibility

Two logical configurations are equivalent if and only if their IRONCFG encodings are **byte-for-byte identical**.

### 9.2 Canonical Encoding Rules (ALL MUST)

1. **VarUInt**: Minimal encoding only (see section 7)
2. **Numeric**: Little-endian byte order, no padding, no alternatives
3. **Float**: NaN forbidden; -0.0 normalized to +0.0 (section 3.2)
4. **Schema**: Fields sorted by fieldId ascending (section 4.2)
5. **Object fields**: fieldId values sorted ascending (section 6.3)
6. **String Pool**: Strings sorted lexicographically by UTF-8 bytes (section 5)
7. **Header Reserved**: All reserved fields MUST be 0x00
8. **No Rearrangement**: Array elements in input order, no sorting or dedup

### 9.3 Determinism Proof

Encoder determinism is provable via:
1. Golden vector sets (encoded form fixed)
2. Multiple encode cycles: encode → decode → encode → bit-identical
3. C99 and .NET implementations producing identical output on same input

---

## 10. Limits and Safety

### 10.1 Hard Limits (MUST be enforced)

| Limit | Value | Reason |
|-------|-------|--------|
| Max file size | 256 MB | Memory safety, DoS prevention |
| Max recursion depth | 32 | Stack overflow prevention (nested objects/arrays) |
| Max fieldCount per schema | 65536 | VarUInt practical limit, memory exhaustion prevention |
| Max array elements | 1,000,000 | Memory exhaustion prevention |
| Max string length | 16 MB | Memory safety, buffer overflow prevention |
| Max VarUInt value | 2^64-1 (u64) | Numeric overflow prevention |

**Enforcement (MUST)**:
- Check during schema parsing (fieldCount)
- Check during value parsing (string length, array count, recursion depth)
- Return specific error code on violation (see section 11)
- Do not attempt recovery; fail immediately

### 10.2 Bounds Checking (MUST)

Before every read:
- Verify: `current_offset + bytes_to_read ≤ file_size`
- Verify offsets do not overflow: `offset + size < 2^32`
- For VarUInt: detect overflow (reading > max bytes without proper termination)

**Failure Mode (MUST)**:
- Any bounds violation → error BOUNDS_VIOLATION
- No partial reads
- No out-of-bounds memory access

### 10.3 Overflow Guards (MUST)

All arithmetic operations:
- Detect overflow in addition: `offset + length`
- Detect overflow in multiplication: `count * element_size`
- Use 64-bit intermediate values where necessary
- Return error ARITHMETIC_OVERFLOW on detection

---

## 11. Validation Modes

### 11.1 Fast Validation (SHOULD)

Quick structural checks:
- Magic number correct
- Version matches (1)
- Header size = 64
- Offsets are non-zero and ascending
- CRC32 present flag matches crcOffset
- fileSize matches actual file size
- No bounds violations in header

**Exit on first failure; return error code**.

### 11.2 Strict Validation (MUST)

Full canonical enforcement:
- All fast validation checks
- Schema parsing:
  - fieldId ascending
  - fieldName sorted lexicographically
  - No duplicates
- String pool (if present):
  - Strings sorted lexicographically
  - No duplicates
- Data block:
  - Root is object (type 0x40)
  - fieldCount matches schema
  - fieldId values ascending
  - All required fields present
  - Field types match schema
  - All VarUInt minimal encoding
  - All float values canonicalized
  - No unknown type codes
- CRC32 match (if present)
- All reserved fields = 0

**Exit on first failure; return specific error code**.

---

## 12. Error Codes

Parsers MUST return these error codes (or equivalent mapping):

| Code | Name | Condition |
|------|------|-----------|
| 0 | OK | File valid |
| 1 | INVALID_MAGIC | Magic ≠ "ICFG" |
| 2 | INVALID_VERSION | Version ≠ 1 |
| 3 | INVALID_FLAGS | Reserved flag bits set |
| 4 | TRUNCATED_FILE | File shorter than 64-byte header |
| 5 | TRUNCATED_BLOCK | Block extends beyond file |
| 6 | BOUNDS_VIOLATION | Read beyond buffer bounds |
| 7 | ARITHMETIC_OVERFLOW | Integer overflow during calculation |
| 8 | CRC32_MISMATCH | CRC32 validation failed |
| 9 | BLAKE3_MISMATCH | BLAKE3 validation failed |
| 10 | INVALID_SCHEMA | Schema parsing failure (fieldId unsorted, duplicate, etc.) |
| 11 | SCHEMA_MISMATCH | External schema mismatch or missing |
| 12 | INVALID_VARUINT | VarUInt non-minimal or overflow |
| 13 | INVALID_STRING | Invalid UTF-8 sequence |
| 14 | INVALID_FLOAT | NaN detected or other float violation |
| 15 | INVALID_TYPE_CODE | Unknown type byte |
| 16 | FIELD_TYPE_MISMATCH | Field value type ≠ schema type |
| 17 | MISSING_REQUIRED_FIELD | Required field is null or absent |
| 18 | UNKNOWN_FIELD | Field present not in schema |
| 19 | ARRAY_TYPE_MISMATCH | Array element type ≠ schema |
| 20 | RECURSION_LIMIT_EXCEEDED | Nesting too deep |
| 21 | LIMIT_EXCEEDED | Max string length, array size, field count, etc. |
| 22 | RESERVED_FIELD_NONZERO | Header reserved field not zero |
| 23 | FIELD_COUNT_MISMATCH | fieldCount in data ≠ fieldCount in schema |
| 24 | FIELD_ORDER_VIOLATION | fieldId values not strictly ascending |
| 25 | NON_MINIMAL_VARUINT | VarUInt non-minimal encoding |

**Format (MUST)**:
- All errors MUST include error code
- All errors SHOULD include file offset where error detected
- All errors SHOULD include human-readable message
- Parsers MUST NOT throw exceptions; return error code + context

---

## 13. Compatibility Rules

### 13.1 Version and Magic

- **Magic "ICFG"** identifies this format exclusively
- **Version byte = 1** for this specification
- Any format change incompatible with this spec MUST use:
  - New magic number (e.g., "ICF2" is columnar format, distinct)
  - OR new version byte (version 2) with forward/backward compatibility matrix

### 13.2 Unknown Elements Policy

- **Unknown flags (bits 3-7)**: MUST cause immediate rejection
- **Unknown type codes**: MUST cause immediate rejection with INVALID_TYPE_CODE
- **Unknown schema fields**: MUST cause rejection (schema declares all fields)

### 13.3 Forward Compatibility

IRONCFG v1 does NOT support forward compatibility. All future changes require:
- New magic number (e.g., "ICG2", not "ICFG")
- OR version field increment + compatibility matrix

---

## 14. C99 Zero-Copy Implementation

### 14.1 Memory Model (MUST)

Parsers SHOULD implement zero-copy reading where possible:
- No heap allocation during header/schema parsing
- No copying of string/bytes data (return pointers into file buffer)
- Read-only access to file buffer (memory-mapped or in-memory)

### 14.2 Constraints

- Field access via fieldId → O(1) object field lookup (indexed schema)
- Array element access via index → O(1)
- String access → pointer into string pool or inline string
- No decompression or transformation of data

### 14.3 Stack Safety

- Max recursion depth = 32 (section 10.1)
- Schema parsing uses iteration (not recursion)
- Data block parsing may use recursion for nested objects/arrays; depth tracking MUST prevent overflow

---

## 15. Testing and Certification

### 15.1 Golden Vectors (MUST)

Each certified implementation MUST provide:
- **small_config.icfg** — Minimal configuration (1-5 fields, basic types)
- **medium_config.icfg** — Typical configuration (10-50 fields, mixed types)
- **large_config.icfg** — Large configuration (100-1000 fields, arrays)
- **edge_cases.icfg** — Null values, empty arrays, unicode strings, limits
- All variants: with CRC32, with BLAKE3, with external schema

### 15.2 Determinism Test (MUST)

```
encode(input) → bytes1
encode(input) → bytes2
encode(input) → bytes3
assert: bytes1 == bytes2 == bytes3
```

### 15.3 Parity Test (MUST)

.NET implementation MUST produce byte-for-byte identical output to C99 implementation on same input.

### 15.4 Corruption Detection (MUST)

```
encode(input) → file
corrupt_one_byte(file) → corrupted_file
parse(corrupted_file) → FAIL (CRC mismatch or data error)
```

### 15.5 Round-Trip Test (MUST)

```
parse(golden.icfg) → config
encode(config) → golden_reencoded.icfg
assert: golden_reencoded.icfg == golden.icfg (byte-for-byte)
```

---

## 16. Security Considerations

### 16.1 Denial of Service

Parsers MUST enforce all limits (section 10) to prevent:
- Stack exhaustion (deep nesting)
- Heap exhaustion (large allocations)
- CPU exhaustion (excessive loops)

### 16.2 Integer Overflow

All arithmetic MUST be overflow-safe:
- Detect addition overflow
- Detect multiplication overflow
- Use 64-bit intermediate values

### 16.3 Malformed Data

Parsers MUST validate all invariants:
- Canonical form (no non-minimal VarUInt, canonical float)
- Type matching (field types match schema)
- Ordering (fieldId ascending, strings sorted)
- Bounds (all offsets and lengths within file)

### 16.4 Canonical Form Verification

Non-canonical encodings MUST be rejected:
- Non-minimal VarUInt → error NON_MINIMAL_VARUINT
- NaN in float → error INVALID_FLOAT
- -0.0 (parser correction; encoder must produce +0.0)

---

## 17. Example: Minimal IRONCFG File

**Configuration**:
```
{
  "enabled": true,
  "port": 8080,
  "host": "localhost"
}
```

**Schema**:
```
3 fields:
  0: "enabled" (type=bool, required)
  1: "host" (type=string, required)
  2: "port" (type=u64, required)
```

**Encoded (hex dump)**:
```
Header (64 bytes):
49 43 46 47        "ICFG" magic
01                 version 1
05                 flags: CRC32_PRESENT | EMBEDDED_SCHEMA
00 00              reserved
64 00 00 00        fileSize = 100
40 00 00 00        schemaOffset = 64
12 00 00 00        schemaSize = 18
00 00 00 00        stringPoolOffset = 0 (no string pool)
00 00 00 00        stringPoolSize = 0
70 00 00 00        dataOffset = 112
15 00 00 00        dataSize = 21
85 00 00 00        crcOffset = 133
00 00 00 00        blake3Offset = 0
[16 reserved bytes]

Schema Block (18 bytes, offset 64):
03                 fieldCount = 3
00                 fieldId = 0
07 65 6e 61 62 6c 65 64  "enabled"
01                 type = bool
01                 isRequired = true
01                 fieldId = 1
04 68 6f 73 74    "host"
20                 type = string
01                 isRequired = true
02                 fieldId = 2
04 70 6f 72 74    "port"
11                 type = u64
01                 isRequired = true

Data Block (21 bytes, offset 112):
40                 object type
03                 fieldCount = 3
00 02              fieldId=0, value follows
02                 true (boolean)
01 09 6c 6f 63 61 6c 68 6f 73 74  fieldId=1, string "localhost" (9 bytes)
02 11 88 1F 00 00 00 00 00 00  fieldId=2, u64=8080

CRC32 (4 bytes, offset 133):
[computed over bytes 0-132]
```

---

## 18. Glossary

| Term | Definition |
|------|-----------|
| **ICFG** | IronConfig Flagship Format (this spec) |
| **Embedded Schema** | Schema stored within the file |
| **External Schema** | Schema stored in companion `.icfg.schema` file |
| **TLV** | Type-Length-Value: type byte + payload |
| **VarUInt** | Variable-length unsigned integer (ULEB128) |
| **Canonical Form** | Standard encoding with no alternatives (minimal VarUInt, sorted fields, etc.) |
| **Zero-Copy** | Reading without copying data; returning pointers into buffer |
| **Determinism** | Same input always produces identical bytes |
| **CRC32** | 32-bit cyclic redundancy check (IEEE 802.3) |
| **BLAKE3** | Cryptographic hash function (256 bits) |

---

## References

- IEEE 754: Floating-point arithmetic
- ULEB128: Variable-length integer encoding
- UTF-8: Text encoding standard
- CRC-32: IEEE 802.3 polynomial
- BLAKE3: blake3.io
- FAMILY_STANDARD.md: Family-wide requirements
- ENGINE_TEMPLATE.md: Engine specification template
