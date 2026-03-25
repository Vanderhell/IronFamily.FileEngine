> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONCFG Canonicalization and Determinism Rules

**Version**: 1.0
**Date**: 2026-01-16
**Status**: Normative
**References**: spec/IRONCFG.md (mandatory reading)

---

## 1. Canonical Form Definition

**Canonical Form**: An IRONCFG file is in canonical form if and only if it obeys ALL rules in sections 2–6 of this document.

**Logical Equivalence**: Two sets of configuration data are logically equivalent if and only if their canonical IRONCFG encodings are **byte-for-byte identical**.

**Determinism Property**: Encoding the same logical configuration multiple times MUST produce identical bytes across all encode operations.

---

## 2. Canonical Header Rules

### 2.1 Reserved Fields (MUST)

- Byte 6-7 (`reserved0`): MUST be `0x00 0x00`
- Byte 44-47 (`reserved1`): MUST be `0x00000000` (little-endian)
- Bytes 48-63 (`reserved2`): MUST be all `0x00` (16 bytes)

**Violation**: Any non-zero reserved field → parser rejects with error RESERVED_FIELD_NONZERO

### 2.2 Flag Bits (MUST)

- Bit 3-7 (reserved flag bits): MUST be 0
- Bit 0 (CRC32_PRESENT): MUST match crcOffset (0 ⟺ flag 0 = 0)
- Bit 1 (BLAKE3_PRESENT): MUST match blake3Offset (0 ⟺ flag 1 = 0)

**Encoder Rule (MUST)**:
- If CRC32 will be computed, set flag bit 0 = 1 before computation
- If BLAKE3 will be computed, set flag bit 1 = 1 before computation
- All other flag bits MUST be 0

**Parser Rule (MUST)**:
- Unknown flags (bits 3-7 set) → reject immediately with error INVALID_FLAGS
- Mismatched flags (e.g., flag 0 = 1 but crcOffset = 0) → reject with error FLAG_MISMATCH

### 2.3 Offset Invariants (MUST)

Define strict ordering:
```
0 < schemaOffset ≤ schemaOffset + schemaSize
schemaOffset + schemaSize ≤ stringPoolOffset  (if stringPoolOffset > 0, else dataOffset)
stringPoolOffset + stringPoolSize ≤ dataOffset  (if stringPoolOffset > 0, else dataOffset)
dataOffset ≤ dataOffset + dataSize
dataOffset + dataSize ≤ crcOffset  (if crcOffset > 0, else blake3Offset or fileSize)
crcOffset + 4 ≤ blake3Offset  (if blake3Offset > 0, else fileSize)
blake3Offset + 32 = fileSize  (if blake3Offset > 0)
crcOffset + 4 = fileSize  (if crcOffset > 0 and blake3Offset = 0)
dataOffset + dataSize = fileSize  (if crcOffset = 0 and blake3Offset = 0)
```

**Violation**: Any offset violation → parser rejects with error BOUNDS_VIOLATION

### 2.4 Size Fields (MUST)

- `schemaSize > 0` (schema block is mandatory)
- `dataSize > 0` (data block is mandatory)
- `stringPoolSize = 0` ⟺ `stringPoolOffset = 0` (both zero or both non-zero)
- `crcOffset = 0` ⟺ flag bit 0 = 0
- `blake3Offset = 0` ⟺ flag bit 1 = 0
- `fileSize = 64 + schemaSize + stringPoolSize + dataSize + (4 if CRC) + (32 if BLAKE3)`

**Encoder Rule (MUST)**:
- Compute all offsets before writing header
- Compute fileSize as sum of all blocks
- Write fileSize correctly before any integrity computation

**Parser Rule (MUST)**:
- Verify fileSize matches actual file size; if mismatch → error TRUNCATED_FILE

---

## 3. Canonical Schema Rules

### 3.1 Field Ordering (MUST)

Within the schema block, fields MUST be sorted by `fieldId` in strictly ascending order.

```
fieldId[0] < fieldId[1] < ... < fieldId[n-1]
```

**Violation**: Non-ascending fieldId → parser rejects with error FIELD_ORDER_VIOLATION

### 3.2 Field Name Lexicographic Ordering (MUST)

Within the schema block, field names MUST be sorted lexicographically by UTF-8 byte order:

```
UTF8_BYTES(fieldName[0]) < UTF8_BYTES(fieldName[1]) < ... (lexicographically)
```

Where `<` is defined by:
- Compare byte-by-byte from left to right
- First differing byte determines order (unsigned comparison)
- Shorter string comes before longer string if one is prefix of other

**Violation**: Field names not sorted lexicographically → parser rejects with error INVALID_SCHEMA

### 3.3 Field Name Uniqueness (MUST)

No two fields in schema MUST have identical field names (case-sensitive byte comparison).

**Violation**: Duplicate field name → parser rejects with error INVALID_SCHEMA

### 3.4 Field ID Uniqueness (MUST)

No two fields in schema MUST have identical fieldId.

**Violation**: Duplicate fieldId → parser rejects with error INVALID_SCHEMA

### 3.5 VarUInt Encoding in Schema (MUST)

All VarUInt values in schema block (fieldCount, fieldId, fieldNameLen) MUST use minimal byte encoding (see section 4.1).

**Violation**: Non-minimal VarUInt → parser rejects with error NON_MINIMAL_VARUINT

### 3.6 Field Type and Storage Validity (MUST)

- `fieldType` MUST be one of: 0 (null), 1 (bool), 2 (i64), 3 (u64), 4 (f64), 5 (string), 6 (bytes)
- `isRequired` MUST be 0x00 or 0x01

**Violation**: Invalid fieldType or isRequired → parser rejects with error INVALID_SCHEMA

---

## 4. Canonical VarUInt Rules

### 4.1 Minimal Byte Encoding (MUST)

VarUInt encoding MUST use the minimal number of bytes to represent the value:

```
Value 0-127:           1 byte:  0xxxxxxx
Value 128-16383:       2 bytes: 10xxxxxx 0xxxxxxx
Value 16384-2097151:   3 bytes: 10xxxxxx 10xxxxxx 0xxxxxxx
Value 2097152-268435455:  4 bytes: 10xxxxxx ... 0xxxxxxx
Value 268435456+:      5 bytes: 10xxxxxx ... 10xxxxxx 0xxxxxxx  (max u32)
(and up to 10 bytes for u64)
```

**Forbidden Non-Minimal Encodings**:
- `0x00` MUST NOT be encoded as `0x80 0x00` or any longer sequence
- `0x7F` MUST NOT be encoded as `0xFF 0x00`
- `0x80` MUST NOT be encoded with more than 2 bytes
- Any other redundant encoding → rejected with error NON_MINIMAL_VARUINT

**Encoder Algorithm (MUST)**:
```
value ← input unsigned integer
while value ≥ 128:
  output (value & 0x7F) | 0x80
  value ← value >> 7
output (value & 0x7F)
```

**Parser Algorithm (MUST)**:
```
result ← 0
shift ← 0
byte_count ← 0
loop:
  read_byte ← next byte from buffer
  byte_count ← byte_count + 1
  if byte_count > max_bytes_for_type:  # 5 for u32, 10 for u64
    reject with NON_MINIMAL_VARUINT
  result ← result | ((read_byte & 0x7F) << shift)
  if (read_byte & 0x80) = 0:
    break
  shift ← shift + 7
return result
```

**Detectability (MUST)**:
- Non-minimal encoding is always detectable: if value fits in N bytes, it MUST NOT be encoded in N+1 bytes
- After parsing, recompute encoding; if recomputed differs → reject as non-canonical

---

## 5. Canonical Data Type Rules

### 5.1 Null Type (MUST)

**Encoding**: Single byte `0x00`, no payload.

**Canonical Rule**: Null is always represented as `0x00`.

**Usage**:
- MUST be used only when schema field allows null (isRequired = 0x00)
- Required fields (isRequired = 0x01) MUST NOT encode as null

### 5.2 Boolean Type (MUST)

**Encoding**:
- Type byte `0x01` → false
- Type byte `0x02` → true
- No payload

**Canonical Rule (MUST)**:
- false MUST be encoded as `0x01` (never `0x00` or any other value)
- true MUST be encoded as `0x02` (never `0x01`, `0x03`, or any other value)

**Forbidden**:
- Any encoding other than `0x01` or `0x02` → parser rejects with error INVALID_FLOAT

### 5.3 Integer Types (i64, u64) (MUST)

**Encoding**:
- Type byte: `0x10` (i64) or `0x11` (u64)
- Payload: 8 bytes, little-endian

**Canonical Rule (MUST)**:
- Integers MUST be encoded in IEEE 754 little-endian format (8 bytes exactly)
- No VarUInt encoding for integers (fixed 8 bytes always)
- No padding or truncation

**Forbidden**:
- VarUInt-encoded integers
- Different byte counts
- Big-endian or mixed-endian

**Signed Integer Rules (i64)**:
- Range: -2^63 to 2^63 - 1
- Negative numbers: two's complement representation
- No sign extension alternatives

**Unsigned Integer Rules (u64)**:
- Range: 0 to 2^64 - 1
- No sign bit

### 5.4 Floating-Point Type (f64) (MUST)

**Encoding**:
- Type byte: `0x12`
- Payload: 8 bytes, little-endian IEEE 754 double

**Canonical Rules (MUST)**:

#### 5.4.1 NaN Prohibition (MUST)
- NaN (any payload with exponent = all 1s and mantissa ≠ 0) is FORBIDDEN
- Encoder MUST NOT emit NaN
- Parser MUST reject any NaN with error INVALID_FLOAT
- This includes: quiet NaN, signaling NaN, all NaN variants

**Detection (MUST)**:
- Read 8 bytes as IEEE 754 double
- Exponent field (bits 52-62) = all 1s AND mantissa field (bits 0-51) ≠ 0 → NaN detected
- On NaN detection: reject with error INVALID_FLOAT

#### 5.4.2 Negative Zero Canonicalization (MUST)
- -0.0 (sign bit = 1, exponent = 0, mantissa = 0) MUST be converted to +0.0 (sign bit = 0, exponent = 0, mantissa = 0)
- Encoder MUST emit +0.0 for any -0.0 input (before final encoding)
- Parser SHOULD treat -0.0 as +0.0 (accept but normalize)

**Byte Representation**:
- +0.0: `0x00 0x00 0x00 0x00 0x00 0x00 0x00 0x00` (little-endian)
- -0.0: `0x00 0x00 0x00 0x00 0x00 0x00 0x00 0x80` (little-endian)
- Canonical form: +0.0 bytes only

#### 5.4.3 Infinity Canonicalization (MUST)
- +Infinity (exponent = all 1s, mantissa = 0, sign bit = 0) is allowed
- -Infinity (exponent = all 1s, mantissa = 0, sign bit = 1) is allowed
- MUST be encoded in canonical IEEE 754 form (no alternatives)

**Byte Representations**:
- +Inf: `0x00 0x00 0x00 0x00 0x00 0x00 0xF0 0x7F` (little-endian)
- -Inf: `0x00 0x00 0x00 0x00 0x00 0x00 0xF0 0xFF` (little-endian)

#### 5.4.4 Subnormal and Normal Numbers (MUST)
- Subnormal numbers (exponent = 0, mantissa ≠ 0) are allowed
- Normal numbers (exponent ∉ {0, all 1s}) are allowed
- MUST be encoded in canonical IEEE 754 form (no truncation, no rounding alternatives)

**Non-Ambiguity (MUST)**:
- Each logical floating-point value has exactly one canonical byte representation
- No alternative encodings (e.g., different bit patterns for same value)

### 5.5 String Type (MUST)

**Encoding**:
- Type byte: `0x20` (inline string) or `0x21` (pooled string reference)
- If inline (`0x20`): VarUInt length + UTF-8 bytes
- If pooled (`0x21`): VarUInt stringId (index into string pool)

#### 5.5.1 UTF-8 Validity (MUST)

All UTF-8 strings MUST be valid UTF-8:

**Valid UTF-8 Sequences**:
- Single-byte: `0xxxxxxx` (U+0000 to U+007F)
- Two-byte: `110xxxxx 10xxxxxx` (U+0080 to U+07FF)
- Three-byte: `1110xxxx 10xxxxxx 10xxxxxx` (U+0800 to U+FFFF)
- Four-byte: `11110xxx 10xxxxxx 10xxxxxx 10xxxxxx` (U+10000 to U+10FFFF)

**Forbidden UTF-8 Sequences**:
- Invalid continuation bytes (not `10xxxxxx` after multi-byte start)
- Overlong encodings (e.g., U+0041 as `0xC1 0x81` instead of `0x41`)
- Surrogate pairs (U+D800 to U+DFFF, which are invalid in UTF-8)
- Invalid start bytes (`11111xxx` or greater)

**Violation**: Invalid UTF-8 → parser rejects with error INVALID_STRING

#### 5.5.2 No Unicode Normalization (MUST)

Strings MUST NOT be Unicode-normalized (NFC, NFD, NFKC, NFKD).

- Input string as-is (no transformation)
- Byte-for-byte comparison in string pool ordering

**Implication**: Two different Unicode representations of same character are DIFFERENT strings.

#### 5.5.3 String Pool Ordering (MUST)

If string pool is present, strings MUST be sorted lexicographically by UTF-8 byte order:

```
UTF8_BYTES(string[0]) < UTF8_BYTES(string[1]) < ... (lexicographically)
```

**Violation**: Strings not sorted → parser rejects with error INVALID_SCHEMA

#### 5.5.4 String Pool Uniqueness (MUST)

No duplicate strings in string pool (case-sensitive byte comparison).

**Violation**: Duplicate string → parser rejects with error INVALID_SCHEMA

#### 5.5.5 Inline vs Pooled Decision (ENCODER RULE)

Encoder MUST follow this rule:
- If string appears only once in data: encode inline (`0x20`)
- If string appears 2+ times in data: intern in pool and use pooled reference (`0x21`)
- If string is long (heuristic: >100 bytes) or frequently repeated: intern in pool

**Effect**: Multiple encoder implementations MAY produce different pool memberships, but MUST produce identical output for same input (determinism).

**Parser Rule (MUST)**:
- Accept both inline and pooled strings
- No requirement to optimize pooling

#### 5.5.6 String Length Limits (MUST)

- Max string length: 16 MB (as per spec/IRONCFG.md section 10.1)
- VarUInt length encoding MUST be minimal

**Violation**:
- String length > 16 MB → reject with error LIMIT_EXCEEDED
- Non-minimal VarUInt length → reject with error NON_MINIMAL_VARUINT

### 5.6 Bytes Type (MUST)

**Encoding**:
- Type byte: `0x22`
- Payload: VarUInt length + raw bytes (no interpretation)

**Canonical Rules (MUST)**:
- Raw bytes are opaque (no UTF-8 validation required)
- VarUInt length MUST be minimal
- Max length: 16 MB (same as strings)
- Byte order: exactly as provided (no endianness transformation)

**Violation**:
- Length > 16 MB → reject with error LIMIT_EXCEEDED
- Non-minimal VarUInt length → reject with error NON_MINIMAL_VARUINT

### 5.7 Array Type (MUST)

**Encoding**:
- Type byte: `0x30`
- Payload: VarUInt element count + TLV values (all same type)

**Canonical Rules (MUST)**:
- All array elements MUST have the same type (homogeneous)
- Element type MUST match schema array element type
- Element count MUST match number of elements in payload
- VarUInt count MUST be minimal
- Elements MUST be in input order (no sorting)

**Forbidden**:
- Heterogeneous arrays (mixed types)
- Duplicate elements removed or reordered
- Element type mismatch with schema

**Violation**:
- Type mismatch → error ARRAY_TYPE_MISMATCH
- Non-minimal count → error NON_MINIMAL_VARUINT
- Count > 1,000,000 → error LIMIT_EXCEEDED

### 5.8 Object Type (MUST)

**Encoding**:
- Type byte: `0x40`
- Payload: VarUInt field count + field pairs
- Each pair: fieldId (VarUInt) + TLV value

**Canonical Rules (MUST)**:
- All fields declared in schema MUST be present in data object
- fieldId values MUST be in strictly ascending order
- fieldId MUST match schema fieldId exactly
- Field values MUST match schema type declarations
- Required fields (isRequired = 0x01) MUST NOT be null
- Optional fields (isRequired = 0x00) MAY be null
- VarUInt fieldId and field count MUST be minimal

**Forbidden**:
- Omitted fields
- Undeclared fields (fields not in schema)
- Field type mismatch
- Non-ascending fieldId order
- Duplicate fieldId
- Required field as null

**Violation**:
- Missing field → error MISSING_REQUIRED_FIELD or FIELD_COUNT_MISMATCH
- Unknown field → error UNKNOWN_FIELD
- Type mismatch → error FIELD_TYPE_MISMATCH
- Non-ascending fieldId → error FIELD_ORDER_VIOLATION
- Non-minimal VarUInt → error NON_MINIMAL_VARUINT

---

## 6. Canonical Integrity Mechanisms

### 6.1 CRC32 Canonicalization (MUST)

If CRC32 is present (flag bit 0 = 1):

**Computation (MUST)**:
- Algorithm: IEEE 802.3 (polynomial 0xEDB88320)
- Initialization: 0xFFFFFFFF
- Data: bytes [0 .. crcOffset - 1]
- Final XOR: 0xFFFFFFFF
- Result: stored as 4 bytes, little-endian, at crcOffset

**Encoder Algorithm (MUST)**:
```
1. Set header flag bit 0 = 0 initially
2. Set all offset fields
3. Compute file size (up to crcOffset - 4)
4. Set crcOffset = fileSize - 4
5. Write all blocks (schema, string pool, data)
6. Compute CRC32 over [0 .. crcOffset - 1]
7. Set flag bit 0 = 1 in header
8. Write CRC32 at crcOffset
9. Update header with correct flag
```

**Parser Validation (MUST)**:
- Read header flag bit 0
- If bit 0 = 1:
  - Read CRC32 from crcOffset
  - Compute CRC32 over [0 .. crcOffset - 1]
  - Compare: if mismatch → reject with error CRC32_MISMATCH
- If bit 0 = 0:
  - crcOffset MUST be 0
  - No CRC validation

**Non-Ambiguity (MUST)**:
- Exactly one CRC32 value per file content
- CRC32 cannot be spoofed or bypassed

### 6.2 BLAKE3 Canonicalization (SHOULD)

If BLAKE3 is present (flag bit 1 = 1):

**Computation (SHOULD)**:
- Algorithm: BLAKE3
- Data: bytes [0 .. blake3Offset - 1]
- Output: 32 bytes (first 256 bits of BLAKE3 hash)
- Result: stored as raw bytes at blake3Offset

**Encoder Algorithm (SHOULD)**:
```
1. Set flag bit 1 = 0 initially
2. Write schema, string pool, data blocks
3. Compute BLAKE3 hash over [0 .. fileSize - 32]
4. Set flag bit 1 = 1
5. Set blake3Offset = fileSize - 32
6. Write first 32 bytes of BLAKE3 at blake3Offset
```

**Parser Validation (SHOULD)**:
- Read header flag bit 1
- If bit 1 = 1:
  - Read BLAKE3 from blake3Offset (32 bytes)
  - Compute BLAKE3 over [0 .. blake3Offset - 1]
  - Compare: if mismatch → may reject with error BLAKE3_MISMATCH (advisory)
- If bit 1 = 0:
  - blake3Offset MUST be 0
  - No BLAKE3 validation

---

## 7. Forbidden Encoding Patterns

The following MUST be rejected by all parsers:

| Pattern | Error Code | Reason |
|---------|-----------|--------|
| Non-minimal VarUInt | NON_MINIMAL_VARUINT | Ambiguous encoding |
| NaN in f64 | INVALID_FLOAT | Undefined behavior |
| -0.0 in f64 (parser may fix) | (none, auto-normalize) | Duplicate representation |
| Unknown type code | INVALID_TYPE_CODE | Undefined semantics |
| Unknown flag bits | INVALID_FLAGS | Forward-compat issue |
| Non-zero reserved field | RESERVED_FIELD_NONZERO | Schema violation |
| Non-ascending fieldId in object | FIELD_ORDER_VIOLATION | Ambiguity |
| Duplicate fieldId in object | FIELD_ORDER_VIOLATION | Semantic error |
| Field type ≠ schema type | FIELD_TYPE_MISMATCH | Schema violation |
| Required field as null | MISSING_REQUIRED_FIELD | Configuration error |
| Omitted field | FIELD_COUNT_MISMATCH | Schema violation |
| Invalid UTF-8 | INVALID_STRING | Encoding error |
| Heterogeneous array | ARRAY_TYPE_MISMATCH | Type error |
| Unsorted string pool | INVALID_SCHEMA | Determinism violation |
| Unsorted schema fields | INVALID_SCHEMA | Determinism violation |
| Flag mismatch (CRC flag 1 but crcOffset 0) | FLAG_MISMATCH | Consistency error |

---

## 8. Determinism Proof Strategy

### 8.1 Logical Equivalence Definition

Two configurations **C1** and **C2** are logically equivalent if:
- Same schema
- Same field values (including order for arrays, null vs present, etc.)
- Same string content
- Same numeric values (after float canonicalization)

### 8.2 Determinism Invariant

For any configuration C:

```
encode(C) = E1
encode(C) = E2
encode(C) = E3

MUST hold: E1 = E2 = E3 (byte-for-byte identical)
```

### 8.3 Determinism Test Cases

Encoder MUST pass these tests:

#### Test: Numeric Canonicalization
- Input: -0.0 in f64 field
- Expected output: +0.0 bytes

#### Test: VarUInt Minimality
- Input: schema with 255 fields
- Expected output: fieldCount encoded as 2 bytes (0xFF 0x01), not longer

#### Test: String Pool Ordering
- Input: strings {"zoo", "apple", "banana"}
- Expected output: pool ordered as ["apple", "banana", "zoo"]

#### Test: Field ID Ordering
- Input: object with fields {fieldId=2, fieldId=0, fieldId=1} (in any input order)
- Expected output: encoded as {fieldId=0, fieldId=1, fieldId=2}

#### Test: Round-Trip Identity
- Input: valid IRONCFG file F
- Encode: parse(F) → config → encode(config) → F'
- Expected: F' = F (byte-for-byte identical)

### 8.4 Permutation Rules

**Input Permutations That MUST Produce Identical Output**:
- Reorder schema fields by fieldId (encoder sorts by fieldId)
- Reorder object fields by fieldId during input (encoder sorts by fieldId)
- Provide -0.0 instead of +0.0 (encoder canonicalizes to +0.0)

**Input Permutations That MUST NOT Produce Identical Output** (different logical data):
- Reorder array elements (arrays are ordered by input)
- Add/remove fields (schema is fixed)
- Change field values (different data)
- Different string content (even if same character)

---

## 9. Cross-Language Determinism (C99 vs .NET)

### 9.1 Floating-Point Equivalence

Both C99 and .NET MUST:
- Use IEEE 754 double precision (8 bytes)
- Use little-endian byte order for all multi-byte integers
- Canonicalize -0.0 to +0.0 before encoding
- Reject NaN on parsing
- Store and retrieve floating-point values without loss of precision

**Potential Pitfall (MUST AVOID)**:
- C99 `fmod()` may behave differently from .NET `%` operator
- Use integer arithmetic or verify float results are canonicalized

### 9.2 String Encoding Equivalence

Both C99 and .NET MUST:
- Validate UTF-8 using same rules
- Perform lexicographic comparison by UTF-8 byte value (not Unicode codepoint)
- NOT apply any locale-dependent collation
- NOT apply Unicode normalization

**Potential Pitfall (MUST AVOID)**:
- C99 `strcoll()` uses locale; MUST NOT use for IRONCFG
- .NET `StringComparer.Ordinal` is correct; case-sensitive, locale-independent
- C99 correct approach: byte-by-byte comparison of UTF-8 encoded bytes

### 9.3 VarUInt Encoding Equivalence

Both C99 and .NET MUST:
- Use same encoding algorithm (section 4.1)
- Enforce minimal byte constraint
- Detect non-minimal encoding and reject

**Potential Pitfall (MUST AVOID)**:
- Signed vs unsigned integer handling in bit shift operations
- Both must use unsigned types (u32, u64) for VarUInt

### 9.4 Memory Layout Equivalence

Both C99 and .NET MUST:
- Use little-endian byte order explicitly
- Not rely on platform endianness
- Use fixed-size types (u8, u32, u64, f64)

**Potential Pitfall (MUST AVOID)**:
- C99 `struct` padding (use explicit byte alignment or byte-by-byte access)
- .NET `Encoding.GetBytes()` on strings (ensures UTF-8 byte accuracy)

---

## 10. Canonical Validation Algorithm

### 10.1 Strict Parser Validation Pseudocode

```
function validateStrict(file):
  // Header checks (section 2)
  if file.length < 64:
    return TRUNCATED_FILE
  if file[0:4] != "ICFG":
    return INVALID_MAGIC
  if file[4] != 1:
    return INVALID_VERSION
  if file[5] & 0xF8:  // check bits 3-7
    return INVALID_FLAGS
  if file[6:8] != 0x00:
    return RESERVED_FIELD_NONZERO
  if file[44:48] != 0x00000000:
    return RESERVED_FIELD_NONZERO
  if file[48:64] != [0x00]*16:
    return RESERVED_FIELD_NONZERO

  // Offset validation
  schema_off = LEu32(file[12:16])
  schema_size = LEu32(file[16:20])
  pool_off = LEu32(file[20:24])
  pool_size = LEu32(file[24:28])
  data_off = LEu32(file[28:32])
  data_size = LEu32(file[32:36])
  crc_off = LEu32(file[36:40])
  blake3_off = LEu32(file[40:44])
  file_size = LEu32(file[8:12])

  if validateOffsets(schema_off, schema_size, pool_off, pool_size,
                      data_off, data_size, crc_off, blake3_off, file_size):
    return BOUNDS_VIOLATION

  // Schema validation (section 3)
  schema = parseSchema(file[schema_off:schema_off+schema_size])
  if schema.errors:
    return schema.error_code
  if not isAscendingFieldId(schema):
    return FIELD_ORDER_VIOLATION
  if not isLexicographicFieldNames(schema):
    return INVALID_SCHEMA
  if not isUniqueFieldIds(schema):
    return INVALID_SCHEMA
  if not isUniqueFieldNames(schema):
    return INVALID_SCHEMA

  // String pool validation (section 5.5.3)
  if pool_off > 0:
    pool = parseStringPool(file[pool_off:pool_off+pool_size])
    if pool.errors:
      return pool.error_code
    if not isLexicographicStrings(pool):
      return INVALID_SCHEMA
    if not isUniqueStrings(pool):
      return INVALID_SCHEMA

  // Data block validation (section 5.8)
  data_block = parseObject(file[data_off:data_off+data_size])
  if data_block.errors:
    return data_block.error_code
  if not conformsToSchema(data_block, schema):
    return FIELD_TYPE_MISMATCH or FIELD_COUNT_MISMATCH

  // Integrity checks (section 6)
  if file[5] & 0x01:  // CRC32 present flag
    if crc_off == 0:
      return FLAG_MISMATCH
    computed_crc = crc32(file[0:crc_off])
    stored_crc = LEu32(file[crc_off:crc_off+4])
    if computed_crc != stored_crc:
      return CRC32_MISMATCH
  else:
    if crc_off != 0:
      return FLAG_MISMATCH

  if file[5] & 0x02:  // BLAKE3 present flag
    if blake3_off == 0:
      return FLAG_MISMATCH
    // BLAKE3 validation (advisory)
  else:
    if blake3_off != 0:
      return FLAG_MISMATCH

  return OK
```

### 10.2 Determinism Check Pseudocode

```
function checkDeterminism(config, iterations=3):
  encoded_outputs = []
  for i in 1 to iterations:
    encoded = encode(config)
    encoded_outputs.append(encoded)

  for i in 1 to iterations-1:
    if encoded_outputs[i] != encoded_outputs[i+1]:
      return DETERMINISM_FAILED

  return OK

function checkRoundTrip(file):
  config = parse(file)
  re_encoded = encode(config)
  if re_encoded != file:
    return ROUND_TRIP_MISMATCH
  return OK
```

---

## 11. Canonicalization Checklist

An encoder is canonical if it:

- [ ] Rejects NaN in f64 input
- [ ] Converts -0.0 to +0.0 in f64
- [ ] Sorts schema fields by fieldId (ascending)
- [ ] Sorts schema field names lexicographically by UTF-8 bytes
- [ ] Sorts object fields by fieldId (ascending) in output
- [ ] Sorts string pool lexicographically by UTF-8 bytes
- [ ] Uses minimal VarUInt encoding for all variable integers
- [ ] Sets reserved header fields to 0x00
- [ ] Sets unknown flag bits to 0
- [ ] Verifies CRC32 flag bit matches crcOffset presence
- [ ] Verifies BLAKE3 flag bit matches blake3Offset presence
- [ ] Computes CRC32 correctly (IEEE 802.3)
- [ ] Computes BLAKE3 correctly (if present)
- [ ] Encodes all integers (i64, u64) as fixed 8-byte little-endian
- [ ] Validates UTF-8 in all strings
- [ ] Does NOT apply Unicode normalization
- [ ] Includes all required fields in output object
- [ ] Rejects omitted fields
- [ ] Rejects null in required fields
- [ ] Encodes arrays in input order (no sorting)

A parser is strict if it:

- [ ] Rejects all fields listed in section 7 "Forbidden Encoding Patterns"
- [ ] Rejects NaN in f64 with INVALID_FLOAT
- [ ] Treats -0.0 as +0.0 (or auto-fixes)
- [ ] Verifies fieldId ascending order
- [ ] Verifies field name lexicographic order (in schema)
- [ ] Verifies string pool lexicographic order
- [ ] Verifies VarUInt minimal encoding
- [ ] Verifies reserved fields are zero
- [ ] Verifies CRC32 match (if present)
- [ ] Verifies flag bit consistency
- [ ] Verifies field types match schema
- [ ] Verifies required fields not null
- [ ] Verifies no unknown fields
- [ ] Verifies recursion depth < 32
- [ ] Verifies file size <= 256 MB
- [ ] Verifies string length <= 16 MB
- [ ] Verifies array count <= 1,000,000
- [ ] Verifies schema field count <= 65536

