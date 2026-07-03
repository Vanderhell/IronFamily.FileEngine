# Native C Examples

This repository does not currently ship a maintained examples source directory for the native APIs.

Use the public headers as the canonical interface reference:

- `native/ironfamily_c/include/ironfamily/iupd_reader.h`
- `libs/ironcfg-c/include/ironcfg/ironcfg.h`

Minimal calling shape for strict `IUPD` verification:

```c
iron_error_t err = iron_iupd_verify_strict(
    &reader,
    file_size,
    pubkey32,
    min_sequence,
    &out_sequence
);
```

Minimal calling shape for `ICFG` open and validation:

```c
ironcfg_view_t view;
ironcfg_error_t err = ironcfg_open(buffer, buffer_size, &view);
if (err.code == IRONCFG_OK) {
    err = ironcfg_validate_strict(buffer, buffer_size);
}
```

Build commands for the maintained native tree are documented in `docs/BUILD_AND_INSTALL.md`.
