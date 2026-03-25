# ironcfg-c: Native IronConfig Readers

Production-grade C99 readers for ICXS and ICFX formats with zero dynamic allocations (except optional string decode).

## Build

### Linux / macOS
```bash
mkdir build && cd build
cmake ..
make
ctest
```

### Windows (MSVC)
```bash
mkdir build && cd build
cmake .. -G "Visual Studio 16 2019"
cmake --build . --config Release
ctest -C Release
```

### CMake Options
- `SKIP_TESTS=ON` â€” Disable tests

## API Overview

### ICXS Reader (icxs.h)
Zero-copy, O(1) field access for schema-based tables:

```c
#include <ironcfg/icxs.h>

/* Open file */
icxs_view_t view;
uint8_t data[...];
icxs_open(data, size, &view);

/* Validate (including CRC if enabled) */
icxs_validate(&view);

/* Get record */
icxs_record_t record;
icxs_get_record(&view, 0, &record);

/* Read fields by ID */
int64_t damage;
icxs_get_i64(&record, 3, &damage);  /* field_id=3 */

const uint8_t* name_ptr;
uint32_t name_len;
icxs_get_str(&record, 2, &name_ptr, &name_len);  /* Returns ptr+len, no copy */
```

### ICFX Reader (icfx.h)
Zero-copy binary JSON with optional O(1) object indexing:

```c
#include <ironcfg/icfx.h>

/* Open file */
icfx_view_t view;
icfx_open(data, size, &view);

/* Get root value */
icfx_value_t root = icfx_root(&view);

/* Navigate */
if (icfx_is_object(&root)) {
    icfx_value_t field;
    if (icfx_obj_try_get_by_keyid(&root, key_id, &field) == ICFG_OK) {
        int64_t v = icfx_get_i64(&field);
    }
}
```

## Status Codes
- `ICFG_OK` â€” Success
- `ICFG_ERR_MAGIC` â€” Invalid magic number
- `ICFG_ERR_BOUNDS` â€” Data out of bounds
- `ICFG_ERR_CRC` â€” CRC validation failed (file corrupted)
- `ICFG_ERR_SCHEMA` â€” Schema validation error
- `ICFG_ERR_TYPE` â€” Type mismatch
- `ICFG_ERR_RANGE` â€” Value out of range / not found
- `ICFG_ERR_UNSUPPORTED` â€” Unsupported feature
- `ICFG_ERR_INVALID_ARGUMENT` â€” Invalid argument

## Design Principles
- **Zero-copy**: All reads use pointers into the original buffer
- **No allocations**: Parsing and field access require no malloc (except optional UTF-8 decode)
- **Safe**: Bounds checks on all reads; no undefined behavior
- **Deterministic**: Consistent error codes and behavior
- **Fast**: O(1) field access for ICXS; O(n) object scan or O(1) indexed for ICFX

## Testing
```bash
cd build
ctest -V
```

Run against golden vectors in `vectors/small/icxs/` and `vectors/small/icfx/`.

## Compatibility
- C99 (ISO/IEC 9899:1999)
- No external dependencies
- POSIX and Windows compatible

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
