> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONCFG C99 / .NET Parity Analysis

**Version**: 1.0
**Date**: 2026-01-16
**Status**: Normative
**Scope**: Cross-language byte-identical behavior
**References**: spec/IRONCFG.md, spec/IRONCFG_CANONICAL.md, spec/IRONCFG_VALIDATION.md

---

## 1. Parity Principles

### 1.1 Fundamental Requirement

Both C99 and .NET implementations MUST:
- **Encode** same logical configuration to **byte-identical** IRONCFG binary
- **Decode** same IRONCFG file to **identical** configuration (bit-identical numeric values)
- **Validate** same file to **identical** error code and byte offset
- **Reject** same invalid files with **identical** error codes

**No permissible deviation** in:
- Integer arithmetic results
- Floating-point bit patterns
- Offset calculations
- Error detection or reporting
- VarUInt encoding/decoding
- String encoding/comparison

### 1.2 Parity Scope

**Within Scope** (MUST be parity-enforced):
- File format parsing and generation
- CRC32 computation (IEEE 802.3)
- BLAKE3 computation
- Offset calculations
- Type checks and conversions
- Error code and offset reporting

**Out of Scope** (MAY differ):
- Memory management patterns (GC vs malloc)
- Performance optimizations (cache locality, SIMD)
- API surface (methods, classes, namespaces)
- Error messages (strings may vary, but codes must match)
- Logging and diagnostics (non-functional)

---

## 2. Integer Model Parity

### 2.1 Integer Type Mapping (MUST)

Map all IRONCFG integer types to fixed-size types:

| IRONCFG Type | C99 Type | .NET Type | Size | Range |
|--------------|----------|-----------|------|-------|
| u8 | `uint8_t` | `byte` | 1 | 0 to 255 |
| i64 | `int64_t` | `long` | 8 | -2^63 to 2^63-1 |
| u64 | `uint64_t` | `ulong` | 8 | 0 to 2^64-1 |
| u32 | `uint32_t` | `uint` | 4 | 0 to 2^32-1 |
| f64 | `double` | `double` | 8 | IEEE 754 |

**Standard Compliance (MUST)**:
- C99: `#include <stdint.h>` for fixed-size types (POSIX or compiler built-ins)
- .NET: `System.Int64`, `System.UInt64`, etc.
- Both: never use `long`, `int`, `size_t` for format-critical values

### 2.2 Signed vs Unsigned Handling (MUST)

**Rule (MANDATORY)**:
- All format offsets, sizes, counts: use **unsigned** types only (`uint32_t`, `uint64_t`, `ulong`)
- All numeric data in files: use **declared** types (i64 vs u64)
- Never implicitly convert between signed/unsigned without explicit bounds check

**Specific Rules**:
- VarUInt decoding: use `uint32_t` or `uint64_t`, never `int32_t`
- Offset arithmetic: `(uint32_t)offset + (uint32_t)size`, check overflow
- Array index: `(size_t)index`, where `size_t` is confirmed to match file value
- CRC accumulator: `uint32_t`, never signed
- Field count: `uint32_t`, never signed

**C99 Implementation**:
```c
uint32_t offset = 0;
uint32_t size = 0;
// Bad: if (offset + size > file_size)  // signed overflow is UB
// Good:
if (size > UINT32_MAX - offset) {
  return ARITHMETIC_OVERFLOW;
}
if (offset + size > file_size) {
  return BOUNDS_VIOLATION;
}
```

**.NET Implementation**:
```csharp
uint offset = 0;
uint size = 0;
// Good: checked() will throw on overflow (same as returning error)
try {
  checked {
    if (offset + size > fileSize) {
      return ArithmeticOverflow;
    }
  }
} catch (OverflowException) {
  return ArithmeticOverflow;
}
// Alternatively, use explicit bounds check
if (size > uint.MaxValue - offset) {
  return ArithmeticOverflow;
}
```

### 2.3 Arithmetic Overflow Detection (MUST)

All arithmetic operations on format values MUST detect overflow:

**Pattern (MANDATORY for all operations)**:
1. Check bounds BEFORE operation
2. Perform operation
3. Return error if overflow detected
4. Never rely on wrapping behavior

**C99 Overflow Rules**:
- Unsigned integer overflow: wrapping is defined behavior (allowed but MUST be guarded)
- Signed integer overflow: undefined behavior (MUST BE PREVENTED)
- Right shift of signed values: implementation-defined (avoid)

**Mitigation (MUST)**:
- Use unsigned types for all format operations
- Explicit bounds check before any addition/multiplication
- Use compiler warnings: `-Wconversion`, `-Woverflow`, `-Wsign-conversion`
- Use static analysis: clang-tidy, infer

**.NET Overflow Rules**:
- Unchecked: wrapping is defined (allow only for intentional bit operations)
- Checked: overflow throws exception (appropriate for format validation)

**Mitigation (MUST)**:
- Use `uint` or `ulong` for all format operations
- Use `checked` contexts where overflow is possible
- Enable compiler warning `/warn:4`

### 2.4 VarUInt Overflow Detection (MUST)

VarUInt decoding MUST detect both:
1. **Encoding overflow**: reading > max_bytes without termination
2. **Value overflow**: decoded value > type's max (u32, u64)

**C99 Implementation (MUST)**:
```c
uint64_t decode_varuint64(const uint8_t *buffer, size_t buffer_size,
                          size_t *offset, int *error) {
  uint64_t result = 0;
  uint32_t shift = 0;
  int byte_count = 0;

  while (*offset < buffer_size && byte_count < 10) {
    uint8_t byte = buffer[*offset];
    (*offset)++;
    byte_count++;

    // Guard overflow in shift/accumulate
    if (shift > 63) {  // u64 has 64 bits, shift starts at 0
      *error = NON_MINIMAL_VARUINT;
      return 0;
    }

    // Accumulate with overflow check
    uint64_t mask = ((uint64_t)(byte & 0x7F)) << shift;
    if (result > UINT64_MAX - mask) {
      *error = ARITHMETIC_OVERFLOW;
      return 0;
    }
    result += mask;

    if ((byte & 0x80) == 0) {
      return result;
    }
    shift += 7;
  }

  *error = BOUNDS_VIOLATION;  // ran out of buffer or too many bytes
  return 0;
}
```

**.NET Implementation (MUST)**:
```csharp
private uint DecodeVarUint32(byte[] buffer, ref int offset, out IronCfgError error) {
  uint result = 0;
  int shift = 0;
  int byte_count = 0;

  try {
    checked {
      while (offset < buffer.Length && byte_count < 5) {
        byte b = buffer[offset++];
        byte_count++;

        if (shift >= 32) {
          error = new IronCfgError { Code = NON_MINIMAL_VARUINT, Offset = offset };
          return 0;
        }

        uint mask = ((uint)(b & 0x7F)) << shift;
        result = checked(result + mask);

        if ((b & 0x80) == 0) {
          error = null;
          return result;
        }
        shift += 7;
      }
    }
  } catch (OverflowException) {
    error = new IronCfgError { Code = ARITHMETIC_OVERFLOW, Offset = offset };
    return 0;
  }

  error = new IronCfgError { Code = BOUNDS_VIOLATION, Offset = offset };
  return 0;
}
```

### 2.5 Forbidden Integer Patterns (MUST NOT)

**NEVER use**:
- `size_t` for format-critical values (platform-dependent: 32 or 64 bits)
- `int`, `long`, `unsigned long` (not fixed-size)
- Implicit sign conversion (e.g., `int i = (int)uint32_value`)
- Bitwise NOT on signed types (e.g., `~(int)x`)
- Right shift on negative numbers (implementation-defined)
- Division by zero (always check denominator)
- Modulo with negative operands (sign of result is implementation-defined)

**ALWAYS use**:
- `uint32_t`, `uint64_t`, `int64_t` (or .NET equivalents)
- Explicit casts with bounds checks
- Unsigned types for bit operations
- Explicit modular arithmetic rules

---

## 3. Floating-Point Model Parity

### 3.1 IEEE 754 Double Representation (MUST)

Both implementations MUST use IEEE 754 binary64 (double precision):

**Specification**:
- 64 bits total: 1 sign bit, 11 exponent bits, 52 mantissa bits
- Endianness: **little-endian** (mandated by spec/IRONCFG.md section 5.4)
- Bit pattern directly stored in file (no transformation)

**C99 Implementation (MUST)**:
```c
#include <stdint.h>
#include <string.h>
#include <math.h>

// Read f64 from file (little-endian bytes)
double read_f64(const uint8_t *buffer, size_t offset) {
  uint64_t bits = 0;
  bits |= ((uint64_t)buffer[offset + 0]) << 0;
  bits |= ((uint64_t)buffer[offset + 1]) << 8;
  bits |= ((uint64_t)buffer[offset + 2]) << 16;
  bits |= ((uint64_t)buffer[offset + 3]) << 24;
  bits |= ((uint64_t)buffer[offset + 4]) << 32;
  bits |= ((uint64_t)buffer[offset + 5]) << 40;
  bits |= ((uint64_t)buffer[offset + 6]) << 48;
  bits |= ((uint64_t)buffer[offset + 7]) << 56;

  double result;
  memcpy(&result, &bits, sizeof(double));
  return result;
}

// Write f64 to file (little-endian bytes)
void write_f64(uint8_t *buffer, size_t offset, double value) {
  uint64_t bits;
  memcpy(&bits, &value, sizeof(double));

  buffer[offset + 0] = (bits >> 0) & 0xFF;
  buffer[offset + 1] = (bits >> 8) & 0xFF;
  buffer[offset + 2] = (bits >> 16) & 0xFF;
  buffer[offset + 3] = (bits >> 24) & 0xFF;
  buffer[offset + 4] = (bits >> 32) & 0xFF;
  buffer[offset + 5] = (bits >> 40) & 0xFF;
  buffer[offset + 6] = (bits >> 48) & 0xFF;
  buffer[offset + 7] = (bits >> 56) & 0xFF;
}
```

**.NET Implementation (MUST)**:
```csharp
// Read f64 from file (little-endian bytes)
private static double ReadF64(byte[] buffer, int offset) {
  long bits = 0;
  bits |= ((long)buffer[offset + 0]) << 0;
  bits |= ((long)buffer[offset + 1]) << 8;
  bits |= ((long)buffer[offset + 2]) << 16;
  bits |= ((long)buffer[offset + 3]) << 24;
  bits |= ((long)buffer[offset + 4]) << 32;
  bits |= ((long)buffer[offset + 5]) << 40;
  bits |= ((long)buffer[offset + 6]) << 48;
  bits |= ((long)buffer[offset + 7]) << 56;

  return BitConverter.Int64BitsToDouble(bits);
}

// Write f64 to file (little-endian bytes)
private static void WriteF64(byte[] buffer, int offset, double value) {
  long bits = BitConverter.DoubleToLongBits(value);
  buffer[offset + 0] = (byte)((bits >> 0) & 0xFF);
  buffer[offset + 1] = (byte)((bits >> 8) & 0xFF);
  buffer[offset + 2] = (byte)((bits >> 16) & 0xFF);
  buffer[offset + 3] = (byte)((bits >> 24) & 0xFF);
  buffer[offset + 4] = (byte)((bits >> 32) & 0xFF);
  buffer[offset + 5] = (byte)((bits >> 40) & 0xFF);
  buffer[offset + 6] = (byte)((bits >> 48) & 0xFF);
  buffer[offset + 7] = (byte)((bits >> 56) & 0xFF);
}
```

### 3.2 NaN Detection and Rejection (MUST)

NaN MUST be detected and rejected identically in both languages.

**IEEE 754 NaN Definition**:
- Exponent: all 1 bits (bits 52-62)
- Mantissa: any non-zero value (bits 0-51)
- Sign bit: irrelevant

**Detection Rules (MUST)**:
- Quiet NaN (MSB of mantissa = 1)
- Signaling NaN (MSB of mantissa = 0)
- Any NaN variant → error INVALID_FLOAT

**C99 Detection (MUST)**:
```c
int is_nan(double value) {
  // Portable: use fetestexcept, fpclassify, or bit-level check
  // Bit-level (most portable):
  uint64_t bits;
  memcpy(&bits, &value, sizeof(double));

  // Exponent: bits 52-62
  uint32_t exponent = (bits >> 52) & 0x7FF;
  // Mantissa: bits 0-51
  uint64_t mantissa = bits & 0xFFFFFFFFFFFFFULL;

  return (exponent == 0x7FF) && (mantissa != 0);
}

// In parser:
double f = read_f64(buffer, offset);
if (is_nan(f)) {
  return INVALID_FLOAT;
}
```

**.NET Detection (MUST)**:
```csharp
private static bool IsNaN(double value) {
  // Use bit-level check for absolute certainty
  long bits = BitConverter.DoubleToLongBits(value);

  // Exponent: bits 52-62
  int exponent = (int)((bits >> 52) & 0x7FF);
  // Mantissa: bits 0-51
  long mantissa = bits & 0xFFFFFFFFFFFFFUL;

  return (exponent == 0x7FF) && (mantissa != 0L);
}

// In parser:
double f = ReadF64(buffer, offset);
if (IsNaN(f)) {
  return InvalidFloat;
}
```

**Forbidden Pattern (MUST NOT)**:
- Never use `isnan()`, `Double.IsNaN()`, `float.IsNaN()` without verification
- Never rely on compiler builtins (may differ across versions)
- Always use bit-level check for parity enforcement

### 3.3 Negative Zero Normalization (MUST)

Both -0.0 and +0.0 MUST normalize to +0.0 representation.

**IEEE 754 Representation**:
- +0.0: sign bit = 0, exponent = 0, mantissa = 0 → bits = 0x0000000000000000
- -0.0: sign bit = 1, exponent = 0, mantissa = 0 → bits = 0x8000000000000000

**Encoding Rule (MUST)**:
- Before writing f64 to file, check if value is -0.0
- If -0.0 detected, convert to +0.0
- Write canonical +0.0 bytes to file

**Decoding Rule (MUST)**:
- After reading f64 from file, optionally normalize -0.0 to +0.0
- Acceptable: treat both as equivalent, no error

**C99 Implementation (MUST)**:
```c
double canonicalize_f64(double value) {
  uint64_t bits;
  memcpy(&bits, &value, sizeof(double));

  // Check if -0.0: sign bit = 1, exponent = 0, mantissa = 0
  if (bits == 0x8000000000000000ULL) {
    // Convert to +0.0
    return 0.0;  // or: memcpy from 0x0000000000000000
  }
  return value;
}
```

**.NET Implementation (MUST)**:
```csharp
private static double CanonicalizeF64(double value) {
  long bits = BitConverter.DoubleToLongBits(value);

  // Check if -0.0
  if (bits == unchecked((long)0x8000000000000000UL)) {
    return 0.0d;
  }
  return value;
}
```

### 3.4 Infinity Representation (MUST)

Infinity is allowed and MUST be preserved as-is.

**IEEE 754 Representation**:
- +Inf: sign bit = 0, exponent = all 1, mantissa = 0 → 0x7FF0000000000000
- -Inf: sign bit = 1, exponent = all 1, mantissa = 0 → 0xFFF0000000000000

**Rules**:
- No special handling (not canonical, just allowed)
- Read/write as any other f64 value
- No normalization required

### 3.5 Floating-Point Comparison (MUST)

For any float comparisons in validation or canonicalization, use bit-level comparison only.

**Rule (MUST)**:
- Never use `==`, `<`, `>` on float values from files (unsafe)
- Always compare bit patterns as integers
- This avoids platform-dependent IEEE-754 behavior (signaling exceptions, etc.)

**C99 Pattern (MUST)**:
```c
int f64_equal(double a, double b) {
  uint64_t bits_a, bits_b;
  memcpy(&bits_a, &a, sizeof(double));
  memcpy(&bits_b, &b, sizeof(double));
  return bits_a == bits_b;
}
```

**.NET Pattern (MUST)**:
```csharp
private static bool F64Equal(double a, double b) {
  long bits_a = BitConverter.DoubleToLongBits(a);
  long bits_b = BitConverter.DoubleToLongBits(b);
  return bits_a == bits_b;
}
```

---

## 4. Memory and Bounds Model Parity

### 4.1 Pointer Arithmetic vs Span Slicing (MUST)

C99 uses pointer arithmetic; .NET uses Span/Memory slicing. Both MUST perform identical offset calculations.

**Fundamental Rule (MANDATORY)**:
- Every offset calculation MUST be identical in both languages
- No implicit conversions or size_t assumptions
- Explicit bounds check before every access

**C99 Pattern (MUST)**:
```c
// Read from offset in buffer
const uint8_t *data = (const uint8_t *)buffer;
size_t buf_len = file_size;

// Calculate absolute offset
uint32_t abs_offset = schema_offset;  // from header
size_t read_size = 8;

// Bounds check
if (abs_offset > UINT32_MAX || (size_t)abs_offset > buf_len) {
  return BOUNDS_VIOLATION;
}
if ((size_t)abs_offset + read_size > buf_len) {
  return BOUNDS_VIOLATION;
}

// Safe read
uint8_t value[8];
memcpy(value, &data[abs_offset], 8);
```

**.NET Pattern (MUST)**:
```csharp
// Read from offset in buffer
ReadOnlySpan<byte> data = buffer.AsSpan();
int abs_offset = schema_offset;  // from header
int read_size = 8;

// Bounds check
if (abs_offset < 0 || abs_offset > data.Length) {
  return BoundsViolation;
}
if (abs_offset + read_size > data.Length) {
  return BoundsViolation;
}

// Safe read
Span<byte> value = stackalloc byte[8];
data.Slice(abs_offset, 8).CopyTo(value);
```

### 4.2 Absolute Offset Calculation (MUST)

All offset calculations MUST follow identical rules:

**Rule**:
```
absolute_offset = header_offset_field + 0
No relative offsets (spec/IRONCFG.md uses absolute offsets only)
```

**Verification (MANDATORY for every offset)**:
1. Read offset value from header (guaranteed little-endian)
2. Check: `0 < offset <= fileSize`
3. Check: `offset + size <= fileSize` (where size is non-zero)
4. Proceed to read at `buffer[offset]` onwards

**Pattern (MUST)**:
```c
// C99
uint32_t offset = LEu32(header + 12);  // schemaOffset
uint32_t size = LEu32(header + 16);    // schemaSize

if (offset == 0 || size == 0) return BOUNDS_VIOLATION;
if (offset > file_size) return BOUNDS_VIOLATION;
if (size > file_size - offset) return BOUNDS_VIOLATION;  // no overflow

const uint8_t *block = &file_data[offset];
// Process block (size bytes)
```

```csharp
// .NET
uint offset = BitConverter.ToUInt32(header, 12);  // schemaOffset
uint size = BitConverter.ToUInt32(header, 16);    // schemaSize

if (offset == 0 || size == 0) return BoundsViolation;
if (offset > fileSize) return BoundsViolation;
if (size > fileSize - offset) return BoundsViolation;  // no overflow

ReadOnlySpan<byte> block = data.Slice((int)offset, (int)size);
// Process block
```

### 4.3 Little-Endian Byte Conversion (MUST)

All multi-byte integers MUST be converted from/to little-endian explicitly.

**C99 Implementation (MUST)**:
```c
#include <stdint.h>

// Read little-endian u32
static inline uint32_t LEu32(const uint8_t *data) {
  return ((uint32_t)data[0] << 0) |
         ((uint32_t)data[1] << 8) |
         ((uint32_t)data[2] << 16) |
         ((uint32_t)data[3] << 24);
}

// Read little-endian u64
static inline uint64_t LEu64(const uint8_t *data) {
  return ((uint64_t)data[0] << 0) |
         ((uint64_t)data[1] << 8) |
         ((uint64_t)data[2] << 16) |
         ((uint64_t)data[3] << 24) |
         ((uint64_t)data[4] << 32) |
         ((uint64_t)data[5] << 40) |
         ((uint64_t)data[6] << 48) |
         ((uint64_t)data[7] << 56);
}

// Write little-endian u32
static inline void write_LEu32(uint8_t *data, uint32_t value) {
  data[0] = (value >> 0) & 0xFF;
  data[1] = (value >> 8) & 0xFF;
  data[2] = (value >> 16) & 0xFF;
  data[3] = (value >> 24) & 0xFF;
}
```

**.NET Implementation (MUST)**:
```csharp
// Use BitConverter with explicit LE check or BinaryReader
private static uint ReadLEu32(byte[] data, int offset) {
  if (!BitConverter.IsLittleEndian) {
    return ((uint)data[offset + 3] << 24) |
           ((uint)data[offset + 2] << 16) |
           ((uint)data[offset + 1] << 8) |
           ((uint)data[offset + 0] << 0);
  }
  return BitConverter.ToUInt32(data, offset);
}

// Or, explicitly byte-by-byte (safest for parity)
private static uint ReadLEu32(ReadOnlySpan<byte> data, int offset) {
  return ((uint)data[offset + 0] << 0) |
         ((uint)data[offset + 1] << 8) |
         ((uint)data[offset + 2] << 16) |
         ((uint)data[offset + 3] << 24);
}
```

### 4.4 Forbidden Memory Access Patterns (MUST NOT)

**NEVER use**:
- Struct casting on file data (platform packing/alignment undefined)
- Direct `memcpy` from file to struct (endianness/alignment issues)
- Assumptions about pointer alignment (file data has no guarantees)
- `reinterpret_cast` or `Unsafe.As<>` on file bytes
- Unaligned pointer dereference (UB in C, may trap on ARM)

**ALWAYS use**:
- Byte-by-byte read with explicit endian conversion
- Bounds-checked slice operations (C Span, .NET Span)
- Explicit type conversion functions (LEu32, etc.)

---

## 5. Alignment and Layout Parity

### 5.1 Binary Format Layout (MUST NOT assume alignment)

IRONCFG is a byte-aligned format with NO padding between fields.

**Rule (MANDATORY)**:
- Every field starts at exact byte position specified in header
- No implicit alignment (e.g., "u64 must be 8-byte aligned")
- File layout is linear: byte 0, byte 1, byte 2, ..., no gaps

**Implication for C99**:
- Do NOT use struct unpacking with `#pragma pack` or compiler attributes
- Read header field-by-field with byte offsets
- Never cast file buffer to struct

**Example (BAD - parity violation)**:
```c
// BAD: Relies on struct packing, platform-dependent
struct IroncfgHeader {
  char magic[4];
  uint8_t version;
  uint8_t flags;
  uint16_t reserved0;
  // ... etc
};
IroncfgHeader *hdr = (IroncfgHeader *)file_data;  // UB, alignment issue
```

**Example (GOOD - parity-safe)**:
```c
// GOOD: Explicit byte reading
uint32_t magic = LEu32(&file_data[0]);  // bytes 0-3
uint8_t version = file_data[4];
uint8_t flags = file_data[5];
uint16_t reserved0 = (file_data[6] | (file_data[7] << 8));
// ... etc
```

### 5.2 Stack vs Heap (MUST NOT affect results)

C99 may use stack (Span in .NET), but both MUST process identically.

**Rule**:
- Output is bit-identical regardless of allocation strategy
- Error codes and offsets identical regardless of memory management
- No undefined behavior from stack overflow (check VarUInt/recursion limits)

**Safe Patterns**:
- C99: `alloca()` for small temp buffers (< 4 KB), check limits
- .NET: `stackalloc` for small buffers, heap for large structures
- Both: pre-compute sizes from header before allocation

---

## 6. Error Behavior Parity

### 6.1 Error Code Mapping (MUST)

C99 and .NET MUST define error codes identically:

| Code | C99 Name | .NET Name | Meaning |
|------|----------|-----------|---------|
| 0 | OK | Ok | File valid |
| 1 | TRUNCATED_FILE | TruncatedFile | File < 64 bytes |
| 2 | INVALID_MAGIC | InvalidMagic | Magic ≠ "ICFG" |
| ... | (see spec/IRONCFG_VALIDATION.md) | ... | ... |

**Requirement**:
- Enum values MUST be identical (0, 1, 2, ..., 24)
- Name mapping is cosmetic (C99 UPPER_CASE, .NET PascalCase acceptable)
- Code is what matters for parity tests

**C99 Definition (MUST)**:
```c
typedef enum {
  IRONCFG_OK = 0,
  IRONCFG_TRUNCATED_FILE = 1,
  IRONCFG_INVALID_MAGIC = 2,
  IRONCFG_INVALID_VERSION = 3,
  // ... etc
} IronCfgError;
```

**.NET Definition (MUST)**:
```csharp
public enum IronCfgError : uint {
  Ok = 0,
  TruncatedFile = 1,
  InvalidMagic = 2,
  InvalidVersion = 3,
  // ... etc
}
```

### 6.2 Byte Offset Calculation Parity (MUST)

Both implementations MUST report identical byte offsets for same error:

**Rule (MANDATORY)**:
- Offset = byte position within file where error detected
- Offset MUST be exact (not rounded, not nearest block)
- Offset MUST be reproducible: same input file → same offset

**Examples**:
- Invalid magic: offset = 0
- Invalid version: offset = 4
- Non-ascending fieldId at byte 95: offset = 95
- Invalid UTF-8 at byte 542: offset = 542

**Parity Test (MUST)**:
```
for each invalid file:
  c_result = ironcfg_validate(file);
  net_result = IronConfigValidator.Validate(file);
  assert c_result.code == net_result.code
  assert c_result.offset == net_result.offset
```

### 6.3 First-Error Semantics (MUST)

Both implementations MUST stop on first error (no multi-error reporting):

**Rule (MANDATORY)**:
- Detect error → return immediately
- Do not continue validation
- Do not accumulate errors
- Do not attempt recovery

**Implication**:
- Validation determinism: same error reported regardless of validation implementation details
- No "best effort" parsing (file is either valid or invalid)

---

## 7. Type Conversions and Casts (MUST NOT diverge)

### 7.1 String Conversion to Integer (MUST)

Field names and string values are stored as UTF-8 bytes. Any conversion MUST be identical.

**Rule**:
- Field names are NOT integers (they are strings)
- fieldId IS an integer (VarUInt-encoded, separate from name)
- Strings are compared byte-by-byte (not via integer parsing)

**Forbidden Pattern (MUST NOT)**:
- `atoi(field_name)` — field names are not meant to be numeric
- Any implicit string-to-int conversion without explicit bounds check

**Correct Pattern (MUST)**:
```c
// C99: Compare field names lexicographically by UTF-8 bytes
int compare_field_names(const uint8_t *name1, size_t len1,
                        const uint8_t *name2, size_t len2) {
  size_t min_len = len1 < len2 ? len1 : len2;
  int cmp = memcmp(name1, name2, min_len);
  if (cmp != 0) return cmp;
  // Shorter string is "less than" longer if prefix matches
  if (len1 < len2) return -1;
  if (len1 > len2) return 1;
  return 0;
}
```

```csharp
// .NET: Compare field names lexicographically by UTF-8 bytes
private static int CompareFieldNames(ReadOnlySpan<byte> name1,
                                      ReadOnlySpan<byte> name2) {
  int cmp = name1.SequenceCompareTo(name2);
  return cmp;  // Already lexicographic by default
}
```

### 7.2 Size_t and Platform Integers (MUST NOT use for format)

`size_t` is platform-dependent (32 or 64 bits). IRONCFG MUST use fixed-size types.

**Rule (MANDATORY)**:
- All format offsets/sizes: `uint32_t` (C) or `uint` (NET)
- Never assume `size_t` matches file value
- Safe conversion: `(size_t)uint32_value` (always safe)
- Unsafe conversion: `(uint32_t)size_t_value` (may truncate)

**Pattern (MUST)**:
```c
// C99
uint32_t file_offset = LEu32(header + 12);  // Fixed-size from file
// Safe to convert for pointer arithmetic:
size_t pointer_offset = (size_t)file_offset;
if (pointer_offset > buffer_size) return BOUNDS_VIOLATION;
```

```csharp
// .NET: Not an issue (no size_t equivalent), use uint directly
uint fileOffset = BitConverter.ToUInt32(header, 12);
if (fileOffset > bufferSize) return BoundsViolation;
```

---

## 8. CRC32 and BLAKE3 Parity

### 8.1 CRC32 IEEE 802.3 Computation (MUST)

Both implementations MUST compute CRC32 identically:

**Algorithm Parameters (MANDATORY)**:
- Polynomial: 0xEDB88320 (IEEE 802.3)
- Initial value: 0xFFFFFFFF
- Final XOR: 0xFFFFFFFF
- Input reflection: yes (process LSB first)
- Output reflection: yes

**Reference (MUST)**:
- Do not rely on OS or library CRC (may differ)
- Use explicit polynomial-based table or algorithm
- Verify against known test vector (CRC of "123456789" = 0xCBF43926)

**C99 Implementation (MUST)**:
```c
static const uint32_t CRC32_TABLE[256] = {
  // Precomputed table for polynomial 0xEDB88320
  0x00000000, 0x77073096, ... // [all 256 entries]
};

uint32_t crc32_ieee(const uint8_t *data, size_t len) {
  uint32_t crc = 0xFFFFFFFF;
  for (size_t i = 0; i < len; i++) {
    uint8_t byte = data[i];
    crc = (crc >> 8) ^ CRC32_TABLE[(crc ^ byte) & 0xFF];
  }
  return crc ^ 0xFFFFFFFF;
}
```

**.NET Implementation (MUST)**:
```csharp
private static readonly uint[] CRC32_TABLE = InitCrc32Table();

private static uint[] InitCrc32Table() {
  uint[] table = new uint[256];
  for (uint i = 0; i < 256; i++) {
    uint crc = i;
    for (int j = 0; j < 8; j++) {
      crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
    }
    table[i] = crc;
  }
  return table;
}

private static uint Crc32Ieee(ReadOnlySpan<byte> data) {
  uint crc = 0xFFFFFFFF;
  foreach (byte b in data) {
    crc = (crc >> 8) ^ CRC32_TABLE[(crc ^ b) & 0xFF];
  }
  return crc ^ 0xFFFFFFFF;
}
```

**Parity Test (MUST)**:
```
test_data = "123456789"
expected_crc = 0xCBF43926
c_crc = crc32_ieee(test_data, 9)
net_crc = Crc32Ieee(test_data)
assert c_crc == expected_crc
assert net_crc == expected_crc
assert c_crc == net_crc
```

### 8.2 BLAKE3 Computation (MUST)

BLAKE3 is more complex. Use a reference library or trusted implementation:

**Requirement**:
- Output: 32 bytes (first 256 bits of 64-byte hash)
- Both implementations MUST use same library version or algorithm

**Recommendation**:
- C99: use `blake3.c` from blake3.io reference repo
- .NET: use `System.Security.Cryptography` if available, else NuGet package
- Verify with known test vectors

---

## 9. Specific API Constraints for Parity

### 9.1 Encoding API (MUST)

**Signature (C99)**:
```c
struct IroncfgResult {
  int error_code;  // 0 = success, 1-24 = error
  size_t bytes_written;
  const char *error_message;
};

struct IroncfgResult ironcfg_encode(
  const IroncfgConfig *config,
  uint8_t *output_buffer,
  size_t output_capacity,
  uint32_t flags  // CRC32, BLAKE3, etc.
);
```

**Signature (.NET)**:
```csharp
public class IroncfgResult {
  public int ErrorCode { get; set; }
  public int BytesWritten { get; set; }
  public string ErrorMessage { get; set; }
}

public IroncfgResult Encode(
  IroncfgConfig config,
  byte[] outputBuffer,
  IroncfgFlags flags  // CRC32, BLAKE3, etc.
);
```

**Requirement**:
- Same input config → byte-identical output
- Same flags → same output format
- Error codes identical
- Deterministic: encode 3×, all identical

### 9.2 Decoding API (MUST)

**Signature (C99)**:
```c
struct IroncfgResult ironcfg_parse(
  const uint8_t *file_data,
  size_t file_size,
  IroncfgConfig **output_config,
  int validation_mode  // FAST or STRICT
);
```

**Signature (.NET)**:
```csharp
public IroncfgResult Parse(
  ReadOnlySpan<byte> fileData,
  out IroncfgConfig config,
  IroncfgValidationMode mode  // Fast or Strict
);
```

**Requirement**:
- Same file → identical config
- Same validation mode → identical error codes/offsets
- Byte-identical numeric values

### 9.3 Validation API (MUST)

**Signature (C99)**:
```c
struct IroncfgError {
  int code;
  uint32_t offset;
};

struct IroncfgError ironcfg_validate(
  const uint8_t *file_data,
  size_t file_size,
  int validation_mode  // FAST or STRICT
);
```

**Signature (.NET)**:
```csharp
public class IroncfgError {
  public int Code { get; set; }
  public uint Offset { get; set; }
}

public IroncfgError Validate(
  ReadOnlySpan<byte> fileData,
  IroncfgValidationMode mode  // Fast or Strict
);
```

**Requirement**:
- Same file → identical error codes and offsets
- Deterministic: validate 3×, all identical

---

## 10. Forbidden Implementation Shortcuts (MUST NOT)

These patterns WILL cause parity violations:

| Pattern | Risk | Mitigation |
|---------|------|-----------|
| Use `size_t` for file offsets | 32/64 bit platform difference | Use `uint32_t` fixed type |
| Cast file buffer to struct | Alignment/endianness UB | Byte-by-byte read with LE conversion |
| Use `isnan(f)` without bit check | Compiler-dependent | Use explicit bit-level NaN detection |
| Rely on struct padding | Platform-dependent | No structs for file data |
| Use signed shift on format data | Implementation-defined | Use unsigned types only |
| Assume native endianness | Platform-dependent | Explicit little-endian conversion |
| Use `memcpy` from misaligned pointer | UB on some architectures | Use byte-wise copy |
| Division without checking divisor | Undefined if divisor=0 | Always check denominator |
| Use `%` with negative operand | Sign of result implementation-defined | Use unsigned types |
| Compare floats with `==` | IEEE-754 undefined behavior | Compare bit patterns only |
| Accumulate floating-point values | Rounding errors, non-determinism | No accumulation in format parsing |
| Use OS-specific CRC library | May differ from IEEE 802.3 | Use reference implementation |
| Rely on locale for string sorting | Platform/locale-dependent | Use explicit byte order (UTF-8 byte order) |
| Use `strcoll` or `wcscoll` | Locale-dependent | Use memcmp on UTF-8 bytes |
| Assume Unicode normalization | Platform/ICU version dependent | Explicit: NO normalization |
| Use recursion without depth limit | Stack overflow risk | Iterative parsing or explicit limit |
| Skip error checking in inner loop | Silent failures | Check every operation |
| Report multi-errors in validation | Non-deterministic | First-error rule only |

---

## 11. Parity Test Plan

### 11.1 Unit Tests (MUST)

All these must pass identically in both C and .NET:

- [ ] encode / decode round-trip for each test vector
- [ ] determinism: encode 3×, bytes identical
- [ ] CRC32 computation matches known test vector
- [ ] BLAKE3 computation matches known test vector
- [ ] VarUInt encode/decode all values 0-2^32, 0-2^64
- [ ] Float canonicalization: -0.0 → +0.0
- [ ] Float NaN rejection
- [ ] UTF-8 validation: valid and invalid sequences
- [ ] Field name sorting: lexicographic order
- [ ] Offset calculation: no overflow
- [ ] Error codes: all 24 codes detectable

### 11.2 Cross-Language Parity Tests (MUST)

For each golden vector and each invalid file:

```
C_result = c_implementation(file)
NET_result = net_implementation(file)
assert C_result.error_code == NET_result.error_code
assert C_result.offset == NET_result.offset
if C_result.error_code == 0:  // success
  assert C_result.data == NET_result.data (bit-identical)
```

### 11.3 Fuzzing and Malformed Input (MUST)

Apply ironcert fuzzing with both implementations:

```
for each mutation:
  C_error = c_validate(mutated_file)
  NET_error = net_validate(mutated_file)
  assert C_error.code == NET_error.code
  // Offset may differ slightly if error in variable-length structure
```

---

## 12. Parity Enforcement Checklist

Verify every implementation commit against:

- [ ] All header fields read with explicit little-endian conversion
- [ ] All offsets validated (0 < offset ≤ fileSize)
- [ ] All sizes validated (offset + size ≤ fileSize, no overflow)
- [ ] VarUInt decoded with min-byte enforcement
- [ ] Float values checked for NaN (bit-level)
- [ ] Float -0.0 normalized to +0.0 before writing
- [ ] UTF-8 validation applied to all strings
- [ ] Field names/strings compared as bytes (lexicographic)
- [ ] CRC32 matches test vector 0xCBF43926 for "123456789"
- [ ] BLAKE3 matches reference output
- [ ] Error codes 0-24 all defined and distinct
- [ ] Error offsets calculated with same rules in both languages
- [ ] No signed integer types used for format values
- [ ] No `size_t` used for format-critical sizes
- [ ] No struct casting on file data
- [ ] No locale-dependent string functions used
- [ ] No compiler-specific pragmas affecting format interpretation
- [ ] No undefined behavior on invalid input
- [ ] First-error semantics enforced (validation stops on first error)
- [ ] Determinism verified (3 encodes, all identical)
- [ ] Parity verified (C and .NET identical outputs)

---

## References

- spec/IRONCFG.md — Binary format specification
- spec/IRONCFG_CANONICAL.md — Canonicalization rules
- spec/IRONCFG_VALIDATION.md — Validation model
- IEEE 754:2019 — Floating-point arithmetic standard
- RFC 3629 — UTF-8 text encoding
- CRC Catalogue (http://www.ross.net/crc/) — CRC polynomial verification
- blake3.io — BLAKE3 reference implementation
