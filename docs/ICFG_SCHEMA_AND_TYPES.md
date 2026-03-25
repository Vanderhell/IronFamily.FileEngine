# ICFG Schema and Type System

**Date**: 2026-03-14
**Version**: 1 (v1, v2 compatible)
**Status**: LOCKED for v1
**Source**: Live code analysis (IronCfgValueReader.cs, IronCfgEncoder.cs, ironcfg_*.c)

---

## 1. Type Codes and Encoding

### Type Code Reference

Complete list of valid type codes:

| Code | Hex | Type | Size | Encoding | Example | Status |
|------|-----|------|------|----------|---------|--------|
| 0 | 0x00 | Null | 1 | 0x00 | null value | ✅ Defined |
| 1 | 0x01 | Boolean False | 1 | 0x01 | false | ✅ Defined |
| 2 | 0x02 | Boolean True | 1 | 0x02 | true | ✅ Defined |
| 16 | 0x10 | Int64 | 9 | 0x10 + 8LE bytes | -123456789 | ✅ Defined |
| 17 | 0x11 | UInt64 | 9 | 0x11 + 8LE bytes | 987654321 | ✅ Defined |
| 18 | 0x12 | Float64 | 9 | 0x12 + 8LE bytes | 3.14159 | ✅ Defined |
| 32 | 0x20 | String | Var | 0x20 + VarUInt(len) + data | "hello" | ✅ Defined |
| 34 | 0x22 | Bytes | Var | 0x22 + VarUInt(len) + data | [0xDE, 0xAD] | ✅ Defined |
| 48 | 0x30 | Array | Var | 0x30 + VarUInt(count) + elements | [1, 2, 3] | ✅ Defined |
| 64 | 0x40 | Object | Var | 0x40 + VarUInt(count) + fields | {x: 1} | ✅ Defined |

### Type Code Ranges

- **0x00-0x12**: Primitive types (null, bool, integers, float)
- **0x13-0x1F**: Reserved (not currently assigned)
- **0x20+**: Compound types (string, bytes, array, object)
- **0x1C+**: Signals field name present in schema (compound types)

### Validity Check

Valid type codes: 0x00, 0x01, 0x02, 0x10, 0x11, 0x12, 0x20, 0x22, 0x30, 0x40

Any other code (0x03-0x0F, 0x13-0x1F, 0x21, 0x23-0x2F, 0x31-0x3F, 0x41+) is rejected with error `InvalidTypeCode`

---

## 2. Primitive Types

### Null (0x00)

- **Size**: 1 byte
- **Encoding**: Single byte 0x00
- **Meaning**: Represents absence of value
- **Usage**: Optional fields with null values
- **Example encoding**: `0x00`

### Boolean (0x01, 0x02)

- **False**: Single byte 0x01
- **True**: Single byte 0x02
- **Size**: 1 byte each
- **Meaning**: Logical true/false
- **No standard Boolean type code** (encode separately as 0x01 or 0x02)
- **Example encoding**:
  - true → `0x02`
  - false → `0x01`

### Signed Integer (0x10)

- **Type**: Int64 (2's complement)
- **Size**: 9 bytes (1 byte type + 8 bytes data)
- **Range**: -2^63 to 2^63-1 (-9,223,372,036,854,775,808 to 9,223,372,036,854,775,807)
- **Encoding**: 0x10 followed by 8 bytes little-endian
- **Example encoding**:
  - 0 → `0x10 00 00 00 00 00 00 00 00`
  - -1 → `0x10 FF FF FF FF FF FF FF FF`
  - 256 → `0x10 00 01 00 00 00 00 00 00`

### Unsigned Integer (0x11)

- **Type**: UInt64
- **Size**: 9 bytes (1 byte type + 8 bytes data)
- **Range**: 0 to 2^64-1 (0 to 18,446,744,073,709,551,615)
- **Encoding**: 0x11 followed by 8 bytes little-endian
- **Example encoding**:
  - 0 → `0x11 00 00 00 00 00 00 00 00`
  - 255 → `0x11 FF 00 00 00 00 00 00 00`
  - 2^32 → `0x11 00 00 00 00 01 00 00 00`

### Floating-Point (0x12)

- **Type**: IEEE 754 double precision (float64)
- **Size**: 9 bytes (1 byte type + 8 bytes data)
- **Range**: ±1.7976931348623157e+308, special values (Inf, -Inf)
- **Encoding**: 0x12 followed by 8 bytes little-endian IEEE 754
- **NaN handling**: Rejected with error `InvalidFloat`
- **Determinism**: -0.0 normalized to +0.0
- **Example encoding**:
  - 0.0 → `0x12 00 00 00 00 00 00 00 00`
  - 1.0 → `0x12 00 00 00 00 00 00 F0 3F`
  - 3.14159... → `0x12 6E 2D 4D FB 21 FB 09 40`

---

## 3. Compound Types

### String (0x20)

- **Type**: UTF-8 encoded text
- **Size**: Variable (1 byte type + VarUInt length + data)
- **Max length**: 16 MB (16,777,216 bytes)
- **Encoding**:
  ```
  [u8: 0x20]
  [VarUInt: byte_length]
  [byte_length bytes: UTF-8 data]
  ```
- **UTF-8 validation**: All bytes must form valid UTF-8 sequences
- **No normalization**: NFC/NFD not applied
- **Example encoding** ("hello"):
  - Length: 5 bytes
  - Encoding: `0x20 05 68 65 6C 6C 6F`
  - (0x20 = type, 0x05 = length, 68 65 6C 6C 6F = "hello")

### Bytes (0x22)

- **Type**: Binary data (no interpretation)
- **Size**: Variable (1 byte type + VarUInt length + data)
- **Max length**: 16 MB
- **Encoding**:
  ```
  [u8: 0x22]
  [VarUInt: byte_length]
  [byte_length bytes: raw data]
  ```
- **No validation**: Any byte values allowed (0x00-0xFF)
- **Example encoding** ([0xDE, 0xAD, 0xBE, 0xEF]):
  - Length: 4 bytes
  - Encoding: `0x22 04 DE AD BE EF`

### Array (0x30)

- **Type**: Homogeneous collection of values
- **Size**: Variable (1 byte type + VarUInt count + elements)
- **Max elements**: 1,000,000 per array
- **Element type constraint**: All elements must have identical type code
- **Encoding**:
  ```
  [u8: 0x30]
  [VarUInt: element_count]
  For each element:
    [u8: elementTypeCode]
    [element data]
  ```
- **Type consistency**: If first element is 0x10 (int64), all must be 0x10
- **Empty arrays**: Valid (count = 0)
- **Example encoding** ([1, 2, 3] as int64 array):
  - Count: 3 elements
  - Element type: 0x10 (int64)
  - Encoding: `0x30 03 0x10 01 00 00 00 00 00 00 00 00 0x10 02 00 ... 0x10 03 00 ...`

### Object (0x40)

- **Type**: Named field collection (key-value map)
- **Size**: Variable (1 byte type + VarUInt count + fields)
- **Max fields**: 65,536 per object
- **Field constraints**:
  - Field IDs in strictly ascending order
  - Field IDs: 0 to 2^32-1
  - Field values: Any type including nested objects/arrays
- **Encoding**:
  ```
  [u8: 0x40]
  [VarUInt: field_count]
  For each field:
    [VarUInt: fieldId]
    [u8: fieldValueTypeCode]
    [field value data]
  ```
- **Root requirement**: Root object at data_offset must be type 0x40
- **Example encoding** ({x: 1, y: 2} with fieldIds 0, 1):
  - Field count: 2
  - Field 0: fieldId=0, type=0x10 (int64), value=1
  - Field 1: fieldId=1, type=0x10 (int64), value=2
  - Encoding: `0x40 02 0x00 0x10 01 00 00 00 00 00 00 00 00 0x01 0x10 02 00 ...`

---

## 4. Schema Contract

### Schema Definition

Schema defines the structure of the root object and its nested objects.

**Format**:
```
[VarUInt: field_count]
For field index 0 to field_count-1:
  [VarUInt: fieldId]
  [u8: typeCode]
  If typeCode >= 0x1C (compound type):
    [VarUInt: name_length]
    [name_length bytes: field name (UTF-8)]
    If typeCode == 0x30 (Array, v2+ only):
      [ElementSchema block (recursive)]
```

### Field ID Ordering

**Constraint**: Field IDs must be in strictly ascending order

- Valid sequence: 0, 1, 2, 5, 100, 65535
- **Invalid**: 0, 2, 1 (out of order)
- **Invalid**: 0, 0, 1 (duplicate IDs)
- **Invalid**: 0, 1, 2, 2, 3 (duplicate)

**Validation error**: `FieldOrderViolation` if violated

### Type Code and Name

**Primitive types** (0x00, 0x01, 0x02, 0x10, 0x11, 0x12):
- No field name stored in schema
- Type is directly encoded after fieldId

**Compound types** (0x20, 0x22, 0x30, 0x40):
- Field name **must** be present
- Encoded as: VarUInt(name_length) + name_bytes
- Name must be valid UTF-8
- Name is not used for value matching (values matched by fieldId)

### Element Schema (Array fields, v2+)

**v2 feature**: Array fields can carry ElementSchema

**Encoding** (if typeCode == 0x30):
```
[VarUInt: element_count_in_schema]  // How many element type defs
For each element definition:
  [VarUInt: elementFieldId]
  [u8: elementTypeCode]
  [recursive field name if compound]
```

**Purpose**: Define expected types/fields for array elements (optional validation enhancement)

**Backward compatibility**: v1 readers ignore ElementSchema; v2 readers validate if present

---

## 5. Type Validation Contract

### GetBool()

- **Input type**: Field value with type 0x01 or 0x02
- **Output**: Boolean (true for 0x02, false for 0x01)
- **Error**: FieldTypeMismatch if type is not 0x01 or 0x02

### GetInt64()

- **Input type**: Field value with type 0x10
- **Output**: Signed 64-bit integer
- **Error**: FieldTypeMismatch if type is not 0x10
- **Bounds**: Full range -2^63 to 2^63-1

### GetUInt64()

- **Input type**: Field value with type 0x11
- **Output**: Unsigned 64-bit integer
- **Error**: FieldTypeMismatch if type is not 0x11
- **Bounds**: Full range 0 to 2^64-1

### GetFloat64()

- **Input type**: Field value with type 0x12
- **Output**: IEEE 754 double
- **Error**: FieldTypeMismatch if type is not 0x12
- **Special values**: Inf, -Inf allowed; NaN rejected in encoding

### GetString()

- **Input type**: Field value with type 0x20
- **Output**: ReadOnlyMemory<byte> (UTF-8 text)
- **Error**: FieldTypeMismatch if type is not 0x20
- **Validation**: UTF-8 correctness checked during strict validation

### GetBytes()

- **Input type**: Field value with type 0x22
- **Output**: ReadOnlyMemory<byte> (raw binary)
- **Error**: FieldTypeMismatch if type is not 0x22
- **No interpretation**: Raw bytes as-is

### GetArray()

- **Input type**: Field value with type 0x30
- **Output**: Array of homogeneous values
- **Error**: FieldTypeMismatch if type is not 0x30, ArrayTypeMismatch if element types inconsistent
- **Element access**: By index, all elements must have same type

### GetObject()

- **Input type**: Field value with type 0x40
- **Output**: Nested object with schema-defined fields
- **Error**: FieldTypeMismatch if type is not 0x40
- **Field access**: By fieldId, must match schema

---

## 6. Required vs Optional Fields

**Current behavior**:
- All fields are optional
- Fields can be absent from data block
- No "required field" marker in schema v1/v2

**Validation behavior**:
- GetXxx() on missing field returns error MissingRequiredField
- No distinction between "absent" and "null"

**Future consideration** (v3+):
- Could add required field flag in schema
- Could distinguish null from absent

---

## 7. Duplicate and Ordering Behavior

### Duplicate Keys (Objects)

Objects are defined as having unique fieldIds.
Duplicate fieldIds in data are rejected with error FieldCountMismatch.

**Schema**: [0, 1, 2]
**Valid data**: {0→value, 1→value, 2→value}
**Invalid data**: {0→value, 0→value2} (duplicate fieldId)

### Canonical Ordering

Objects encode fields in ascending fieldId order.
Encoder enforces this via SortedDictionary<fieldId, value>.

**Canonical property**:
- Same logical object → same byte sequence
- Field order does not affect identity (always encoded ascending)

---

## 8. Unsupported and Invalid Combinations

### Invalid Type Codes

Type codes not in {0x00, 0x01, 0x02, 0x10, 0x11, 0x12, 0x20, 0x22, 0x30, 0x40} are rejected.

Error: `InvalidTypeCode`

### Invalid Type Mismatches

Reading a value with wrong GetXxx():
- GetInt64() on type 0x11 (UInt64) → FieldTypeMismatch
- GetString() on type 0x22 (Bytes) → FieldTypeMismatch
- GetArray() on type 0x40 (Object) → FieldTypeMismatch

### Non-Homogeneous Arrays

Array with mixed element types:
```
[0x30 02 0x10 ... 0x11 ...]  // int64 then uint64
```
Rejected with error `ArrayTypeMismatch`

### Root Not Object

Data block must start with 0x40 (Object).
Starting with 0x10 (Int64) is rejected.

Error: `InvalidTypeCode` or schema mismatch

### Truncated Values

Type byte indicates size, but data is truncated:
- 0x10 at end of file (needs 8 bytes) → `TruncatedBlock`
- 0x20 with length 100 but only 50 bytes follow → `TruncatedBlock`

---

## 9. Evidence Status

### Fully Implemented and Tested

- ✅ Type codes 0x00, 0x01, 0x02, 0x10, 0x11, 0x12 (primitives)
- ✅ Type codes 0x20, 0x22 (string, bytes)
- ✅ Type code 0x30 (array, homogeneous)
- ✅ Type code 0x40 (object)
- ✅ Schema parsing and validation
- ✅ Field ID ordering checks
- ✅ Type validation contract
- ✅ Deterministic encoding (field order, float normalization)
- ✅ 106/106 .NET tests passing

### Partially Implemented

- ⚠️ ElementSchema (v2 feature, present but optional)
- ⚠️ BLAKE3 validation (structure only, not cryptographic verification)
- ⚠️ Native C tests (code present, not in CMake build)

### Not Verified

- ❓ Very large schemas (>50,000 fields) - limit exists but not stress-tested
- ❓ Very deep nesting (>20 levels) - limit exists but not stress-tested
- ❓ Memory allocation patterns - not measured

---

## Summary

ICFG type system is:
- **Complete**: 11 type codes covering all common data types
- **Simple**: No complex numeric types, fixed-size primitives
- **Deterministic**: Canonical encoding ensures reproducibility
- **Validated**: Type mismatches caught at read time
- **Efficient**: Binary encoding, zero-copy reading for primitives and compounds

