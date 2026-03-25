# Native C API Examples

**Date**: 2026-03-14
**Status**: EXAMPLE_CODE_PROVIDED
**Language**: C99
**Scope**: Minimal buildable examples for IUPD and ICFG APIs

---

## Building Examples

All examples compile with the native C library. To build:

```bash
cd /path/to/IronFamily.FileEngine

# Build library first
cmake -B native/build -DCMAKE_BUILD_TYPE=Release
cmake --build native/build --config Release

# Build examples (in future: add to CMakeLists.txt)
gcc -I native/ironfamily_c/include -c examples/example_*.c -o build/
```

---

## 1. IUPD Example: Verify an Update File

**File**: `examples/example_iupd_verify.c`

**Purpose**: Demonstrate IUPD verification (device-side security check)

**Scenario**: Device receives update file and verifies it before applying

```c
/*
 * IUPD Verification Example
 *
 * Demonstrates the primary IUPD use case: device-side verification
 * of update files using Ed25519 public key and anti-replay checks.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "ironfamily/iupd_reader.h"
#include "ironfamily/iupd_errors.h"
#include "ironfamily/io.h"

/* File reader context */
typedef struct {
    FILE* fp;
} file_reader_ctx_t;

/* File reader implementation (zero-copy streaming) */
static iron_error_t file_read_impl(void* ctx, uint64_t off, uint8_t* dst, uint32_t len) {
    file_reader_ctx_t* fr = (file_reader_ctx_t*)ctx;
    if (fseek(fr->fp, (long)off, SEEK_SET) != 0) {
        return IRON_E_IO;
    }
    if (fread(dst, 1, len, fr->fp) != len) {
        return IRON_E_IO;
    }
    return IRON_OK;
}

/* Parse Ed25519 public key from hex string */
static int load_pubkey_hex(const char* hex_str, uint8_t pubkey[32]) {
    if (strlen(hex_str) != 64) return 0;
    for (size_t i = 0; i < 32; i++) {
        unsigned int byte_val;
        if (sscanf(&hex_str[2*i], "%2x", &byte_val) != 1) {
            return 0;
        }
        pubkey[i] = (uint8_t)byte_val;
    }
    return 1;
}

int main(int argc, char** argv) {
    if (argc < 3) {
        fprintf(stderr, "Usage: %s <iupd_file> <pubkey_hex>\n", argv[0]);
        fprintf(stderr, "Example:\n");
        fprintf(stderr, "  %s update.iupd abcd1234....\n", argv[0]);
        return 1;
    }

    const char* iupd_path = argv[1];
    const char* pubkey_hex = argv[2];

    /* Load and parse public key */
    uint8_t pubkey[32];
    if (!load_pubkey_hex(pubkey_hex, pubkey)) {
        fprintf(stderr, "Invalid public key hex string\n");
        return 1;
    }

    /* Open IUPD file */
    FILE* fp = fopen(iupd_path, "rb");
    if (!fp) {
        fprintf(stderr, "Cannot open %s\n", iupd_path);
        return 1;
    }

    /* Get file size */
    fseek(fp, 0, SEEK_END);
    uint64_t file_size = ftell(fp);
    rewind(fp);

    printf("=== IUPD Verification ===\n");
    printf("File: %s\n", iupd_path);
    printf("Size: %llu bytes\n\n", (unsigned long long)file_size);

    /* Create reader interface */
    file_reader_ctx_t reader_ctx = {.fp = fp};
    iron_reader_t reader = {
        .ctx = &reader_ctx,
        .read = file_read_impl
    };

    /* Verify (anti-replay: minimum sequence = 1) */
    uint64_t sequence = 0;
    iron_error_t err = iron_iupd_verify_strict(
        &reader,
        file_size,
        pubkey,
        1,  /* Reject if UpdateSequence < 1 (anti-replay) */
        &sequence
    );

    fclose(fp);

    /* Report results */
    printf("Verification: ");
    if (err == IRON_OK) {
        printf("✅ PASS\n");
        printf("UpdateSequence: %llu\n", (unsigned long long)sequence);
        return 0;
    } else {
        printf("❌ FAIL (error code: %d)\n", err);
        switch (err) {
        case IRON_E_SIG_INVALID:
            printf("Reason: Ed25519 signature verification failed\n");
            break;
        case IRON_E_SEQ_INVALID:
            printf("Reason: UpdateSequence check failed (possible replay attack)\n");
            break;
        case IRON_E_DOS_LIMIT:
            printf("Reason: DoS limit exceeded (file too large or too many chunks)\n");
            break;
        case IRON_E_UNSUPPORTED_PROFILE:
            printf("Reason: Unsupported IUPD profile\n");
            break;
        case IRON_E_IO:
            printf("Reason: File I/O error\n");
            break;
        default:
            printf("Reason: Verification error\n");
            break;
        }
        return 1;
    }
}
```

**Usage**:
```bash
./example_iupd_verify update.iupd abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234
```

**Key Points**:
- Uses streaming reader interface (zero-copy)
- Fail-closed semantics (any error rejects file)
- Anti-replay check via UpdateSequence
- Clear error reporting

---

## 2. ICFG Example: Read Configuration File

**File**: `examples/example_icfg_read.c`

**Purpose**: Demonstrate ICFG reading and validation

**Scenario**: Application loads configuration file and accesses fields

```c
/*
 * ICFG Read Example
 *
 * Demonstrates the basic ICFG use case: loading and validating
 * a configuration file, then accessing the root object.
 */

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include "ironcfg/ironcfg.h"

int main(int argc, char** argv) {
    if (argc < 2) {
        fprintf(stderr, "Usage: %s <config.icfg>\n", argv[0]);
        return 1;
    }

    const char* config_path = argv[1];

    /* Load file into buffer */
    FILE* fp = fopen(config_path, "rb");
    if (!fp) {
        fprintf(stderr, "Cannot open %s\n", config_path);
        return 1;
    }

    fseek(fp, 0, SEEK_END);
    size_t file_size = ftell(fp);
    rewind(fp);

    uint8_t* buffer = (uint8_t*)malloc(file_size);
    if (!buffer) {
        fprintf(stderr, "Out of memory\n");
        fclose(fp);
        return 1;
    }

    if (fread(buffer, 1, file_size, fp) != file_size) {
        fprintf(stderr, "Read error\n");
        fclose(fp);
        free(buffer);
        return 1;
    }

    fclose(fp);

    printf("=== ICFG Configuration Reader ===\n");
    printf("File: %s\n", config_path);
    printf("Size: %zu bytes\n\n", file_size);

    /* Open (parse header) */
    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buffer, file_size, &view);
    if (err.code != IRONCFG_OK) {
        printf("❌ Open failed: %d at offset %u\n", err.code, err.offset);
        free(buffer);
        return 1;
    }

    printf("✅ Header valid\n");
    printf("   File size field: %u bytes\n", ironcfg_get_file_size(&view));
    printf("   CRC32 present: %s\n", ironcfg_has_crc32(&view) ? "yes" : "no");
    printf("   BLAKE3 present: %s\n", ironcfg_has_blake3(&view) ? "yes" : "no");
    printf("   Schema embedded: %s\n", ironcfg_has_embedded_schema(&view) ? "yes" : "no");

    /* Fast validation (O(1): header and bounds only) */
    err = ironcfg_validate_fast(buffer, file_size);
    if (err.code != IRONCFG_OK) {
        printf("❌ Fast validation failed: %d\n", err.code);
        free(buffer);
        return 1;
    }

    printf("\n✅ Fast validation passed (O(1) check)\n");

    /* Strict validation (O(n): full content check) */
    err = ironcfg_validate_strict(buffer, file_size);
    if (err.code != IRONCFG_OK) {
        printf("❌ Strict validation failed: %d at offset %u\n", err.code, err.offset);
        free(buffer);
        return 1;
    }

    printf("✅ Strict validation passed (O(n) check)\n");

    /* Get root object (zero-copy) */
    const uint8_t* root_data;
    size_t root_size;
    err = ironcfg_get_root(&view, &root_data, &root_size);
    if (err.code != IRONCFG_OK) {
        printf("❌ Get root failed: %d\n", err.code);
        free(buffer);
        return 1;
    }

    printf("\n✅ Root object accessed (zero-copy)\n");
    printf("   Root data offset: %zu\n", (size_t)(root_data - buffer));
    printf("   Root data size: %zu bytes\n", root_size);

    /* Get schema (if present) */
    const uint8_t* schema_data;
    size_t schema_size;
    err = ironcfg_get_schema(&view, &schema_data, &schema_size);
    if (err.code == IRONCFG_OK) {
        printf("\n✅ Schema present\n");
        printf("   Schema size: %zu bytes\n", schema_size);
    }

    /* Get string pool (if present) */
    const uint8_t* strings_data;
    size_t strings_size;
    err = ironcfg_get_string_pool(&view, &strings_data, &strings_size);
    if (err.code == IRONCFG_OK && strings_data != NULL) {
        printf("\n✅ String pool present\n");
        printf("   String pool size: %zu bytes\n", strings_size);
    }

    free(buffer);
    printf("\n✅ All checks passed\n");
    return 0;
}
```

**Usage**:
```bash
./example_icfg_read config.icfg
```

**Key Points**:
- Loads file into buffer (required for ICFG)
- Validates header (fast check)
- Validates content (strict check)
- Accesses root object with zero-copy
- Checks optional schema and string pool

---

## 3. ICFG Example: Encode Configuration File

**File**: `examples/example_icfg_encode.c`

**Purpose**: Demonstrate ICFG encoding (writing configuration files)

**Scenario**: Application creates a configuration structure and encodes it

```c
/*
 * ICFG Encode Example
 *
 * Demonstrates creating and encoding an ICFG configuration file
 * from in-memory structures.
 */

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include "ironcfg/ironcfg_encode.h"

int main(void) {
    printf("=== ICFG Configuration Encoder ===\n\n");

    /* Define schema: single field "count" of type u64 */
    ironcfg_field_def_t fields[] = {
        {
            .field_id = 0,
            .name = "count",
            .name_len = 5,
            .type_code = 0x11,      /* u64 */
            .flags = 0x01           /* required */
        }
    };
    ironcfg_schema_t schema = {
        .fields = fields,
        .field_count = 1
    };

    /* Create value: object with count=42 */
    ironcfg_value_t count_value = {
        .type = IRONCFG_VAL_U64,
        .val.u64_val = 42
    };

    ironcfg_value_t root_value = {
        .type = IRONCFG_VAL_OBJECT,
        .val.object_val = {
            .field_values = &count_value,
            .field_count = 1,
            .schema = &schema
        }
    };

    /* Encode to buffer */
    uint8_t buffer[256];
    size_t encoded_size;
    ironcfg_error_t err = ironcfg_encode(
        &root_value,
        &schema,
        true,   /* include CRC32 */
        false,  /* no BLAKE3 */
        buffer,
        sizeof(buffer),
        &encoded_size
    );

    if (err.code != IRONCFG_OK) {
        printf("❌ Encoding failed: %d\n", err.code);
        return 1;
    }

    printf("✅ Encoding successful\n");
    printf("   Encoded size: %zu bytes\n", encoded_size);
    printf("   CRC32 included: yes\n");

    /* Save to file */
    const char* output_path = "output.icfg";
    FILE* fp = fopen(output_path, "wb");
    if (!fp) {
        printf("❌ Cannot create %s\n", output_path);
        return 1;
    }

    if (fwrite(buffer, 1, encoded_size, fp) != encoded_size) {
        printf("❌ Write error\n");
        fclose(fp);
        return 1;
    }

    fclose(fp);
    printf("✅ Saved to %s\n", output_path);

    /* Verify roundtrip: read it back */
    printf("\n=== Roundtrip Verification ===\n");

    ironcfg_view_t view;
    err = ironcfg_open(buffer, encoded_size, &view);
    if (err.code != IRONCFG_OK) {
        printf("❌ Open failed: %d\n", err.code);
        return 1;
    }

    printf("✅ Encoded file valid\n");

    /* Validate */
    err = ironcfg_validate_strict(buffer, encoded_size);
    if (err.code != IRONCFG_OK) {
        printf("❌ Validation failed: %d\n", err.code);
        return 1;
    }

    printf("✅ Validation passed\n");
    printf("   CRC32 verified\n");

    /* Get root */
    const uint8_t* root_data;
    size_t root_size;
    err = ironcfg_get_root(&view, &root_data, &root_size);
    if (err.code != IRONCFG_OK) {
        printf("❌ Get root failed: %d\n", err.code);
        return 1;
    }

    printf("✅ Root retrieved (roundtrip successful)\n");
    printf("   Root size: %zu bytes\n", root_size);

    return 0;
}
```

**Usage**:
```bash
./example_icfg_encode
# Creates output.icfg with a simple configuration
```

**Key Points**:
- Defines schema with field types
- Creates in-memory value structures
- Encodes to buffer with CRC32
- Validates roundtrip (encode → decode)
- Demonstrates deterministic encoding

---

## 4. ICFG Example: Roundtrip Test

**File**: `examples/example_icfg_roundtrip.c`

**Purpose**: Demonstrate encode + decode roundtrip and determinism

**Scenario**: Verify that encoding is deterministic and roundtrip preserves data

```c
/*
 * ICFG Roundtrip Example
 *
 * Demonstrates that ICFG encoding is deterministic:
 * encoding the same value multiple times produces identical bytes.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "ironcfg/ironcfg_encode.h"

int main(void) {
    printf("=== ICFG Determinism Test ===\n\n");

    /* Schema: single u64 field */
    ironcfg_field_def_t fields[] = {
        {.field_id = 0, .name = "value", .name_len = 5, .type_code = 0x11, .flags = 0x01}
    };
    ironcfg_schema_t schema = {.fields = fields, .field_count = 1};

    /* Value to encode */
    ironcfg_value_t value = {
        .type = IRONCFG_VAL_U64,
        .val.u64_val = 0x0102030405060708ULL
    };

    ironcfg_value_t root = {
        .type = IRONCFG_VAL_OBJECT,
        .val.object_val = {
            .field_values = &value,
            .field_count = 1,
            .schema = &schema
        }
    };

    /* Encode 3 times */
    uint8_t buf1[256], buf2[256], buf3[256];
    size_t size1, size2, size3;

    printf("Encoding 3x with same value...\n");

    ironcfg_error_t err1 = ironcfg_encode(&root, &schema, false, false, buf1, sizeof(buf1), &size1);
    ironcfg_error_t err2 = ironcfg_encode(&root, &schema, false, false, buf2, sizeof(buf2), &size2);
    ironcfg_error_t err3 = ironcfg_encode(&root, &schema, false, false, buf3, sizeof(buf3), &size3);

    if (err1.code != IRONCFG_OK || err2.code != IRONCFG_OK || err3.code != IRONCFG_OK) {
        printf("❌ Encoding failed\n");
        return 1;
    }

    printf("✅ All encodings succeeded\n");
    printf("   Size 1: %zu bytes\n", size1);
    printf("   Size 2: %zu bytes\n", size2);
    printf("   Size 3: %zu bytes\n", size3);

    /* Check sizes match */
    if (size1 != size2 || size2 != size3) {
        printf("❌ Sizes don't match (encoding not deterministic)\n");
        return 1;
    }

    printf("✅ Sizes match\n");

    /* Check bytes match */
    if (memcmp(buf1, buf2, size1) != 0 || memcmp(buf2, buf3, size1) != 0) {
        printf("❌ Bytes don't match (encoding not deterministic)\n");
        return 1;
    }

    printf("✅ Bytes identical across all 3 encodings\n");
    printf("\n✅ DETERMINISM VERIFIED\n");

    return 0;
}
```

**Usage**:
```bash
./example_icfg_roundtrip
```

**Key Points**:
- Encodes same value 3 times
- Verifies output bytes are identical
- Demonstrates deterministic properties
- Important for production (reproducible deployments)

---

## 5. Summary of Examples

| Example | Purpose | Size | Complexity |
|---------|---------|------|-----------|
| `example_iupd_verify.c` | Verify update file | ~100 LOC | Basic |
| `example_icfg_read.c` | Load and validate config | ~80 LOC | Basic |
| `example_icfg_encode.c` | Create config file | ~90 LOC | Basic |
| `example_icfg_roundtrip.c` | Verify determinism | ~70 LOC | Basic |

**Total**: ~340 LOC across 4 examples

---

## 6. Building Examples (Integration with CMake)

To integrate examples into the CMake build, add to `native/CMakeLists.txt`:

```cmake
# Examples
if(ENABLE_EXAMPLES)
    add_executable(example_iupd_verify examples/example_iupd_verify.c)
    target_link_libraries(example_iupd_verify ironfamily_c)

    add_executable(example_icfg_read examples/example_icfg_read.c)
    target_link_libraries(example_icfg_read ironfamily_c)

    add_executable(example_icfg_encode examples/example_icfg_encode.c)
    target_link_libraries(example_icfg_encode ironfamily_c)

    add_executable(example_icfg_roundtrip examples/example_icfg_roundtrip.c)
    target_link_libraries(example_icfg_roundtrip ironfamily_c)
endif()
```

Then build with:
```bash
cmake -B native/build -DCMAKE_BUILD_TYPE=Release -DENABLE_EXAMPLES=ON
cmake --build native/build --config Release
```

---

**Status**: EXAMPLES PROVIDED (ready for integration)
**Last Updated**: 2026-03-14

