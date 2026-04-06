# Native C API Contract

**Version**: 1.0
**Date**: 2026-03-14
**Scope**: Public C99 API for IUPD and ICFG codecs

---

## Overview

The IronFamily native C library provides zero-copy, streaming access to IUPD (update) and ICFG (configuration) formats. Designed for embedded and resource-constrained environments with fail-closed semantics.

**Key Properties**:
- **Language**: C99 (ISO/IEC 9899:1999)
- **Memory Model**: Zero-copy where possible, explicit ownership
- **Error Handling**: Error codes (enums), no exceptions
- **Thread Safety**: Stateless functions (thread-safe if no shared state)
- **Platforms**: Windows (MSVC 14.44), Linux (GCC/Clang in future)

---

## 1. Common Types & Constants

### 1.1 Error Handling

All functions return error codes via `enum` or structured error types.

**IUPD Error Codes** (`iupd_errors.h`):
```c
typedef enum {
    IRON_OK = 0,
    IRON_E_IO = 1,                    /* File I/O error */
    IRON_E_PARSE_ERROR = 2,           /* Malformed IUPD structure */
    IRON_E_SIG_INVALID = 3,           /* Ed25519 signature invalid */
    IRON_E_SEQ_INVALID = 4,           /* UpdateSequence validation failed */
    IRON_E_DOS_LIMIT = 5,             /* Exceeds size/count limits */
    IRON_E_UPD_UNKNOWN_ALGORITHM = 6, /* Unknown delta algorithm */
    IRON_E_CRC32_MISMATCH = 7,        /* CRC32 verification failed */
    IRON_E_APPLY_HASH_MISMATCH = 8,   /* Base/target hash mismatch */
    IRON_E_UNSUPPORTED_PROFILE = 9,   /* Profile not whitelisted */
    IRON_E_PROFILE_VALIDATION_FAILED = 10, /* Profile constraint violation */
    /* ... additional error codes ... */
} iron_error_t;
```

**ICFG Error Codes** (`ironcfg.h`):
```c
typedef enum {
    IRONCFG_OK = 0,
    IRONCFG_TRUNCATED_FILE = 1,
    IRONCFG_INVALID_MAGIC = 2,
    IRONCFG_INVALID_VERSION = 3,
    IRONCFG_INVALID_FLAGS = 4,
    IRONCFG_RESERVED_FIELD_NONZERO = 5,
    IRONCFG_FLAG_MISMATCH = 6,
    IRONCFG_BOUNDS_VIOLATION = 7,
    IRONCFG_ARITHMETIC_OVERFLOW = 8,
    IRONCFG_TRUNCATED_BLOCK = 9,
    IRONCFG_INVALID_SCHEMA = 10,
    IRONCFG_FIELD_ORDER_VIOLATION = 11,
    IRONCFG_INVALID_STRING = 12,
    IRONCFG_INVALID_TYPE_CODE = 13,
    IRONCFG_FIELD_TYPE_MISMATCH = 14,
    IRONCFG_MISSING_REQUIRED_FIELD = 15,
    IRONCFG_UNKNOWN_FIELD = 16,
    IRONCFG_FIELD_COUNT_MISMATCH = 17,
    IRONCFG_ARRAY_TYPE_MISMATCH = 18,
    IRONCFG_NON_MINIMAL_VARUINT = 19,
    IRONCFG_INVALID_FLOAT = 20,
    IRONCFG_RECURSION_LIMIT_EXCEEDED = 21,
    IRONCFG_LIMIT_EXCEEDED = 22,
    IRONCFG_CRC32_MISMATCH = 23,
    IRONCFG_BLAKE3_MISMATCH = 24
} ironcfg_error_code_t;
```

### 1.2 Reader Interface (Zero-Copy I/O)

Both IUPD and ICFG can use a streaming reader interface for efficient memory usage.

**Reader Definition** (`io.h`):
```c
typedef struct {
    void* ctx;                      /* Implementation context (FILE*, buffer, etc.) */
    /* Callback: read len bytes from offset off into dst buffer */
    iron_error_t (*read)(void* ctx, uint64_t off, uint8_t* dst, uint32_t len);
} iron_reader_t;
```

**Implementation**: File reader (for IUPD verification on devices)
```c
typedef struct {
    FILE* fp;
} file_reader_ctx_t;

static iron_error_t file_read_impl(void* ctx, uint64_t off, uint8_t* dst, uint32_t len) {
    file_reader_ctx_t* fr = (file_reader_ctx_t*)ctx;
    if (fseek(fr->fp, (long)off, SEEK_SET) != 0) return IRON_E_IO;
    if (fread(dst, 1, len, fr->fp) != len) return IRON_E_IO;
    return IRON_OK;
}
```

---

## 2. IUPD API (Update Codec)

### 2.1 Overview

**Purpose**: Verify IUPD v2 update files on devices with fail-closed semantics.

**Design**:
- **Device-side only**: Verification-only (no writer/encoder)
- **Zero-copy**: Uses streaming reader interface
- **Fail-closed**: Any error in verification rejects the file
- **Stateless**: Functions are pure (no internal state)

**File Format**:
- IUPD v2 strict verification surface (see `IUPD_SPEC.md`)
- Manifest and chunk table
- Optional or required UpdateSequence trailer depending on profile policy
- Ed25519 signature footer with 64-byte signature payload

### 2.2 Primary Function: `iron_iupd_verify_strict()`

**Header**: `ironfamily/iupd_reader.h`

**Signature**:
```c
iron_error_t iron_iupd_verify_strict(
    const iron_reader_t* r,
    uint64_t file_size,
    const uint8_t ed25519_pubkey[32],
    uint64_t expected_min_update_sequence,
    uint64_t* out_update_sequence
);
```

**Parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `r` | `const iron_reader_t*` | Streaming reader (file access interface) |
| `file_size` | `uint64_t` | Total IUPD file size in bytes |
| `ed25519_pubkey` | `const uint8_t[32]` | Ed25519 public key for signature verification (32 bytes, required) |
| `expected_min_update_sequence` | `uint64_t` | Minimum acceptable UpdateSequence (anti-replay check); 0 if disabled |
| `out_update_sequence` | `uint64_t*` | Output: extracted UpdateSequence from trailer; 0 if no trailer present |

**Return Value**: `iron_error_t`
- `IRON_OK` â€” Verification successful, file accepted
- `IRON_E_PARSE_ERROR` â€” IUPD format error (magic, version, structure)
- `IRON_E_SIG_INVALID` â€” Ed25519 signature verification failed
- `IRON_E_SEQ_INVALID` â€” UpdateSequence < expected_min_update_sequence
- `IRON_E_DOS_LIMIT` â€” Exceeds manifest size, chunk count, or chunk size limits
- `IRON_E_UPD_UNKNOWN_ALGORITHM` â€” Unknown delta algorithm ID
- `IRON_E_UNSUPPORTED_PROFILE` â€” Profile not in whitelist (SECURE, OPTIMIZED only)
- `IRON_E_CRC32_MISMATCH` â€” CRC32 verification failed
- `IRON_E_IO` â€” Reader callback error (file access failure)

**Verification Steps**:
1. Parse IUPD v2 header (magic, version, format)
2. Validate profile (SECURE or OPTIMIZED only; fail-closed)
3. Enforce DoS limits:
   - Manifest size: max 100 MB
   - Chunk count: max 1,000,000
   - Chunk size: max 1 GB
4. Verify Ed25519 signature over manifest
5. If UpdateSequence trailer present: validate sequence >= expected_min

**Usage Example**:
```c
#include <stdio.h>
#include "ironfamily/iupd_reader.h"

int main(void) {
    FILE* fp = fopen("update.iupd", "rb");

    /* Get file size */
    fseek(fp, 0, SEEK_END);
    uint64_t file_size = ftell(fp);
    rewind(fp);

    /* Create reader */
    file_reader_ctx_t ctx = {.fp = fp};
    iron_reader_t reader = {
        .ctx = &ctx,
        .read = file_read_impl
    };

    /* Ed25519 public key (32 bytes) */
    uint8_t pubkey[32] = {...};

    /* Verify */
    uint64_t sequence = 0;
    iron_error_t err = iron_iupd_verify_strict(
        &reader,
        file_size,
        pubkey,
        1,  /* Minimum sequence 1 (anti-replay) */
        &sequence
    );

    if (err == IRON_OK) {
        printf("Verification OK, UpdateSequence: %llu\n", sequence);
    } else {
        printf("Verification failed: %d\n", err);
    }

    fclose(fp);
    return err == IRON_OK ? 0 : 1;
}
```

### 2.3 Supporting Functions

**Metadata Parsing** (`iupd_incremental_metadata.h`):
```c
/* Parse INCREMENTAL profile metadata trailer */
iron_error_t iron_iupd_inc_parse_metadata(
    const uint8_t* trailer_data,
    uint32_t trailer_size,
    iupd_inc_metadata_t* out_metadata
);
```

**IUPD Type**: Not exposed as part of the public API surface

---

## 3. ICFG API (Configuration Format)

### 3.1 Overview

**Purpose**: Read, validate, and encode ICFG configuration files.

**Design**:
- **Full codec**: Read, validate, encode (symmetric)
- **Zero-copy**: View-based access to file contents
- **Dual validation**: Fast (O(1)) and strict (O(n)) modes
- **Extensible**: Custom string handling via function pointers (future)

**File Format**:
- Fixed 64-byte header
- Schema (object/array/primitive type definitions)
- String pool (interned strings)
- Data block (encoded objects/arrays)
- Optional CRC32 (32-bit checksum)
- Optional BLAKE3 (256-bit hash)

### 3.2 Core Functions

#### 3.2.1 `ironcfg_open()` â€” Open and Parse Header

**Header**: `ironcfg/ironcfg.h`

**Signature**:
```c
ironcfg_error_t ironcfg_open(
    const uint8_t *buffer,
    size_t buffer_size,
    ironcfg_view_t *out_view
);
```

**Parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `buffer` | `const uint8_t*` | ICFG file contents (64-byte header minimum) |
| `buffer_size` | `size_t` | Total buffer size in bytes |
| `out_view` | `ironcfg_view_t*` | Output: parsed view (zero-copy) |

**Return Value**: `ironcfg_error_t`
- `IRONCFG_OK` â€” Header parsed successfully
- `IRONCFG_TRUNCATED_FILE` â€” Buffer smaller than header
- `IRONCFG_INVALID_MAGIC` â€” Magic != 0x47464349 ("ICFG")
- `IRONCFG_INVALID_VERSION` â€” Version != 1
- Other errors possible (see error codes)

**Output Structure**:
```c
typedef struct {
    const uint8_t *buffer;
    size_t buffer_size;
    ironcfg_header_t header;  /* 64-byte parsed header */
} ironcfg_view_t;
```

**Usage Example**:
```c
#include "ironcfg/ironcfg.h"

int main(void) {
    uint8_t buffer[4096];
    /* ... load ICFG file into buffer ... */
    size_t size = 4096;

    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buffer, size, &view);

    if (err.code == IRONCFG_OK) {
        printf("File size: %u bytes\n", ironcfg_get_file_size(&view));
    } else {
        printf("Error: %d at offset %u\n", err.code, err.offset);
    }

    return 0;
}
```

#### 3.2.2 `ironcfg_validate_fast()` â€” O(1) Validation

**Signature**:
```c
ironcfg_error_t ironcfg_validate_fast(
    const uint8_t *buffer,
    size_t buffer_size
);
```

**Validation Scope**:
- Header integrity (magic, version, flags, reserved fields)
- Offset bounds (all offsets within file)
- No integrity checks on data contents

**Performance**: O(1) â€” constant time regardless of file size

**Use Case**: Quick pre-check before processing (e.g., on receive)

#### 3.2.3 `ironcfg_validate_strict()` â€” O(n) Full Validation

**Signature**:
```c
ironcfg_error_t ironcfg_validate_strict(
    const uint8_t *buffer,
    size_t buffer_size
);
```

**Validation Scope**:
- All fast validation checks
- Full schema validation (field ordering, type consistency)
- Data block integrity (all encoded values valid)
- CRC32/BLAKE3 verification (if present)
- Recursion depth limits

**Performance**: O(n) â€” linear in file size

**Use Case**: Before trusting file contents (default for security)

#### 3.2.4 `ironcfg_get_root()` â€” Access Root Object

**Signature**:
```c
ironcfg_error_t ironcfg_get_root(
    const ironcfg_view_t *view,
    const uint8_t **out_data,
    size_t *out_size
);
```

**Parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `view` | `const ironcfg_view_t*` | Validated view (from `ironcfg_open()`) |
| `out_data` | `const uint8_t**` | Output: pointer to root object data (zero-copy) |
| `out_size` | `size_t*` | Output: size of root object in bytes |

**Return Value**: Error code

**Prerequisites**: File must be opened with `ironcfg_open()` (header must be valid)

**Zero-Copy**: Returns direct pointer to buffer; no allocation

#### 3.2.5 `ironcfg_get_schema()` â€” Access Schema Block

**Signature**:
```c
ironcfg_error_t ironcfg_get_schema(
    const ironcfg_view_t *view,
    const uint8_t **out_data,
    size_t *out_size
);
```

**Returns**: Schema block (object field definitions, type codes)

#### 3.2.6 `ironcfg_get_string_pool()` â€” Access String Pool

**Signature**:
```c
ironcfg_error_t ironcfg_get_string_pool(
    const ironcfg_view_t *view,
    const uint8_t **out_data,
    size_t *out_size
);
```

**Returns**: String interning pool (may be NULL if not present in file)

#### 3.2.7 Utility Functions

**Get header structure**:
```c
const ironcfg_header_t *ironcfg_get_header(const ironcfg_view_t *view);
```

**Check flags**:
```c
bool ironcfg_has_crc32(const ironcfg_view_t *view);     /* CRC32 present */
bool ironcfg_has_blake3(const ironcfg_view_t *view);    /* BLAKE3 present */
bool ironcfg_has_embedded_schema(const ironcfg_view_t *view); /* Schema embedded */
uint32_t ironcfg_get_file_size(const ironcfg_view_t *view);
```

### 3.3 Encoding API (ICFG Writer)

**Header**: `ironcfg/ironcfg_encode.h`

**Function** (simplified):
```c
ironcfg_error_t ironcfg_encode(
    const ironcfg_value_t* root,
    const ironcfg_schema_t* schema,
    bool include_crc32,
    bool include_blake3,
    uint8_t* out_buffer,
    size_t out_buffer_size,
    size_t* out_size
);
```

**Purpose**: Create ICFG files from in-memory structures

**Parameters**:
- `root`: Root object to encode
- `schema`: Field definitions
- `include_crc32`: Add CRC32 checksum
- `include_blake3`: Add BLAKE3 hash
- `out_buffer`: Destination buffer
- `out_buffer_size`: Max size available
- `out_size`: Output: bytes written

**Properties**:
- Deterministic: Same input always produces same bytes
- Schema validation: Enforces field ordering and type constraints
- Compact: No padding or unnecessary data

---

## 4. Error Handling Best Practices

### 4.1 IUPD Error Checking

```c
iron_error_t err = iron_iupd_verify_strict(&reader, file_size, pubkey, 1, &seq);
if (err != IRON_OK) {
    switch (err) {
    case IRON_E_SIG_INVALID:
        /* Signature failure â€” reject update */
        break;
    case IRON_E_DOS_LIMIT:
        /* Size limit exceeded â€” reject to prevent DoS */
        break;
    case IRON_E_IO:
        /* File read error â€” retry or fail gracefully */
        break;
    default:
        /* Other failures â€” reject */
        break;
    }
    return 1;  /* Fail-closed */
}
```

### 4.2 ICFG Error Checking

```c
ironcfg_error_t err = ironcfg_open(buffer, size, &view);
if (err.code != IRONCFG_OK) {
    printf("Error: %d at offset %u\n", err.code, err.offset);
    return 1;
}

/* Validate before using */
err = ironcfg_validate_strict(buffer, size);
if (err.code != IRONCFG_OK) {
    printf("Validation failed: %d\n", err.code);
    return 1;
}

/* Safe to use now */
const uint8_t *root_data;
size_t root_size;
ironcfg_get_root(&view, &root_data, &root_size);
```

---

## 5. Memory & Thread Safety

### 5.1 Memory Model

**Zero-Copy**: Functions return pointers to input buffers; no allocation.

**Ownership**: Caller owns all buffers; no internal dynamic allocation (by design).

**Lifetime**: Returned pointers are valid as long as input buffers remain valid.

### 5.2 Thread Safety

**Stateless Functions**: All functions are pure (no shared state).

**Thread-Safe**: Multiple threads can call functions on different buffers simultaneously.

**Not Thread-Safe**: Sharing a `iron_reader_t` or `ironcfg_view_t` across threads without synchronization may cause issues (reader implementation dependent).

---

## 6. Performance Characteristics

### 6.1 IUPD Verification

| Operation | Time | Space |
|-----------|------|-------|
| Parse header | O(1) | O(1) |
| Verify signature | O(manifest_size) | O(1) |
| Check DoS limits | O(1) | O(1) |
| Full verification | O(file_size) | O(1)* |

*With streaming reader; buffering depends on reader implementation

### 6.2 ICFG Validation

| Operation | Mode | Time | Space |
|-----------|------|------|-------|
| Open (parse header) | â€” | O(1) | O(1) |
| Validate | Fast | O(1) | O(1) |
| Validate | Strict | O(file_size) | O(1) |
| Get root | â€” | O(1) | O(1) |

---

## 7. Compatibility & Versioning

### 7.1 API Stability

**Current Version**: 1.0

**Guarantees**:
- Function signatures stable (no additions without major version bump)
- Error codes stable (new codes added but existing ones preserved)
- Type definitions stable (no field removal, reordering)

**Future Changes**:
- v1.x: Additive changes (new functions, new error codes)
- v2.0: Breaking changes (signature changes, removed functions)

### 7.2 Format Compatibility

**IUPD native strict verifier**: Version 2 format only. The `.NET` reader supports v1 and v2, but the strict verifier documented here is the `v2` native surface.

**ICFG v1**: Version 1 format only. Future versions not forward-compatible.

---

## 8. Platform Support

### 8.1 Tested Platforms

| Platform | Compiler | Status |
|----------|----------|--------|
| Windows 11 | MSVC 14.44 | âś… VERIFIED_BY_EXECUTION |
| Linux | GCC/Clang | âŹł Planned (not yet verified) |
| Embedded (ARM) | GCC | âŹł Planned (not yet verified) |

### 8.2 Portability

**Standard C99**: Code is portable C99 (ISO/IEC 9899:1999).

**Endianness**: Assumes little-endian (IUPD/ICFG formats are little-endian).

**Size Assumptions**: 32-bit and 64-bit platforms supported; assumes 8-bit bytes.

---

## 9. Known Limitations

### 9.1 IUPD

- **No writer**: Device-side verification only
- **No streaming decompression**: Manifests must fit in available memory (with streaming reader)
- **ILOG not supported**: ILOG codec not implemented (see ILOG plan)

### 9.2 ICFG

- **No streaming parsing**: Must load entire file into buffer first
- **Fixed buffer access**: No custom I/O (unlike IUPD's reader interface)
- **Type system limitation**: No user-defined types (schema-driven only)

---

## 10. Examples

See `docs/NATIVE_C_EXAMPLES.md` for buildable example code.

---

## 11. References

- **IUPD Specification**: `docs/IUPD_SPEC.md`
- **ICFG Specification**: `docs/ICFG_SPEC.md`
- **Build Instructions**: `docs/BUILD.md`
- **Test Vectors**: `vectors/small/`, `incremental_vectors/`, `artifacts/vectors/v1/`

---

**Document Status**: VERIFIED_BY_EXECUTION (all functions tested with golden vectors)
**Last Updated**: 2026-03-14

