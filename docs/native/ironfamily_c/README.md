# IronFamily C99 Native Library

Pure C99 cryptographic and update library for device-side OTA and firmware verification.

## Components

### IUPD v2 Verification (`iupd_reader.h`, `iupd_v2_spec_min.h`)
Strict verifier for IUPD v2 update packages:
- Profile whitelist (SECURE, OPTIMIZED only)
- Ed25519 signature verification over manifest BLAKE3 hash
- UpdateSequence anti-replay protection
- DoS limit enforcement (manifest, chunk count, chunk sizes)

**Usage:**
```c
iron_error_t iron_iupd_verify_strict(
    const iron_reader_t* r,
    uint64_t file_size,
    const uint8_t ed25519_pubkey[32],
    uint64_t expected_min_update_sequence,
    uint64_t* out_update_sequence
);
```

### IUPD Delta v1 Apply (`diff_apply.h`, `delta_v1_spec_min.h`)
Streaming application of IUPD Delta v1 binary patches:
- Fixed 4096-byte chunks (deterministic compression)
- Entry-based format (ChunkIndex + DataLen + Data)
- Base file hash verification (fail-closed)
- Streaming I/O with 64KB buffers (no full-file buffering)

**Format:** Magic "IUPDDEL1", 96-byte header, variable entries

**Usage:**
```c
int iron_diff_v1_apply(
    const iron_reader_t* base_r,
    uint64_t base_size,
    const iron_reader_t* patch_r,
    uint64_t patch_size,
    const iron_writer_t* out_w,
    uint64_t out_expected,
    uint64_t* out_written
);
```

### OTA Client Bundle (`ota_apply.h`)
High-level integrated API combining IUPD v2 verification and Delta v1 application:
1. Verifies IUPD v2 package container
2. Locates and extracts Delta v1 payload from IUPD package
3. Applies delta to base image
4. Verifies output hash (fail-closed)
5. Returns UpdateSequence for persistence

**Usage:**
```c
iron_error_t iron_ota_apply_iupd_v2_delta_v1(
    const iron_reader_t* base_r,
    uint64_t base_size,
    const iron_reader_t* iupd_pkg_r,
    uint64_t iupd_pkg_size,
    const iron_writer_t* out_w,
    const iron_reader_t* out_r,
    uint64_t out_size_expected,
    const uint8_t pubkey32[32],
    uint64_t expected_min_update_sequence,
    uint64_t* out_update_sequence
);
```

## Naming Clarification

### IUPD Delta v1 (Current)
- **Magic**: "IUPDDEL1"
- **Header**: 96 bytes fixed
- **Format**: Entry-based (ChunkIndex + DataLen + Data)
- **Chunk Size**: 4096 bytes (fixed)
- **Source**: `IupdDeltaV1.cs` in .NET reference implementation
- **File**: `delta_v1_spec_min.h`, `diff_apply.c`
- **Note**: Implementation named `diff_apply` for historical reasons (will be renamed to `delta_apply` in future)

### DiffEngine v1 (Future)
- **Status**: Not yet implemented in C99
- **Expected Magic**: "IFDIFF01" or similar
- **Expected Format**: Operation-based (COPY/INSERT opcodes)
- **Purpose**: Alternative compression scheme for future support

## Design Principles

- **No Heap Allocation**: All buffers stack-based (max 64KB chunks)
- **Streaming I/O**: Reader/writer callbacks enable zero-buffering
- **Fail-Closed**: Hash verification, bounds checking, whitelist enforcement
- **Zero-Copy**: Direct callback I/O, no intermediate buffering
- **Pure C99**: No dynamic memory, no C11+ features

## Error Codes

All functions return `iron_error_t`:
- `IRON_OK` (0): Success
- `IRON_E_IO`: I/O operation failed
- `IRON_E_FORMAT`: Invalid file format
- `IRON_E_UNSUPPORTED_VERSION`: Version not supported
- `IRON_E_PROFILE_NOT_ALLOWED`: IUPD profile not in whitelist
- `IRON_E_DOS_LIMIT`: DoS limit exceeded
- `IRON_E_SIG_INVALID`: Signature verification failed
- `IRON_E_SEQ_INVALID`: UpdateSequence validation failed
- `IRON_E_DIFF_*`: Delta-specific errors (base hash, target hash, out of range, limits)

## Callback Interfaces

### Reader
```c
typedef struct {
    void* ctx;
    iron_error_t (*read)(void* ctx, uint64_t off, uint8_t* dst, uint32_t len);
} iron_reader_t;
```
Random-access read from any offset.

### Writer
```c
typedef struct {
    void* ctx;
    int (*write)(void* ctx, uint64_t off, const uint8_t* src, uint32_t len);
} iron_writer_t;
```
Random-access write to any offset (0 = success, non-zero = error).

## Testing

All features are validated against golden vectors:
- IUPD v2: 6 vectors (1 OK, 5 negative cases)
- IUPD Delta v1: 1 vector (case_01: 512KB base + 13KB patch → 512KB output)

Build and test:
```bash
cmake --build native/build --config Debug
ctest --preset native-linux-debug --output-on-failure
```

## Dependencies

- **Ed25519**: `orlp/ed25519` reference (17 files, 100% C99)
- **BLAKE3**: `oconnor663/blake3` reference (365 lines, 100% C99)
- **CMake**: Build configuration for native/ironfamily_c

Both crypto libraries are battle-tested, optimized, and fully portable.
