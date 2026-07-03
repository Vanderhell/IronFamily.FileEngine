# Native C API

This document describes the public native-facing headers that are part of the active repository surface.

## Scope

- `native/ironfamily_c/include/ironfamily/` for verifier-oriented native code
- `libs/ironcfg-c/include/ironcfg/` for the public C API surface

The repository keeps these surfaces distinct. They should not be described as one fully equivalent native implementation.

## Language target

- portable `C99`

## Public headers

### `native/ironfamily_c`

- `native/ironfamily_c/include/ironfamily/io.h`
- `native/ironfamily_c/include/ironfamily/iupd_reader.h`
- `native/ironfamily_c/include/ironfamily/iupd_incremental_metadata.h`
- `native/ironfamily_c/include/ironfamily/delta2_apply.h`
- `native/ironfamily_c/include/ironfamily/diff_apply.h`
- `native/ironfamily_c/include/ironfamily/ota_apply.h`

Primary strict verifier entry point:

```c
iron_error_t iron_iupd_verify_strict(
    const iron_reader_t* r,
    uint64_t file_size,
    const uint8_t ed25519_pubkey[32],
    uint64_t expected_min_update_sequence,
    uint64_t* out_update_sequence
);
```

### `libs/ironcfg-c`

- `libs/ironcfg-c/include/ironcfg/ironcfg.h`
- `libs/ironcfg-c/include/ironcfg/ilog.h`
- `libs/ironcfg-c/include/ironcfg/iupd.h`

Representative `ICFG` entry points:

```c
ironcfg_error_t ironcfg_open(
    const uint8_t *buffer,
    size_t buffer_size,
    ironcfg_view_t *out_view
);

ironcfg_error_t ironcfg_validate_fast(
    const uint8_t *buffer,
    size_t buffer_size
);

ironcfg_error_t ironcfg_validate_strict(
    const uint8_t *buffer,
    size_t buffer_size
);
```

## Shared API properties

- explicit error-code based APIs
- little-endian file formats
- caller-owned buffers

## Current limitations

- `native/ironfamily_c` is primarily a strict `IUPD` verification-oriented surface, not a complete replacement for the managed codebase
- `libs/ironcfg-c` currently requires `OpenSSL` in its CMake configuration
- the full native tree should not be documented as embedded-portable without acknowledging that dependency

## References

- `docs/engines/icfg/SPEC.md`
- `docs/engines/ilog/SPEC.md`
- `docs/engines/iupd/SPEC.md`
- `docs/native/C_EXAMPLES.md`
