# ironcfg-c

`libs/ironcfg-c` is a native C API surface built as part of the repository's maintained native tree.

## Public active headers

- `libs/ironcfg-c/include/ironcfg/ironcfg.h`
- `libs/ironcfg-c/include/ironcfg/ilog.h`
- `libs/ironcfg-c/include/ironcfg/iupd.h`

## Scope notes

- This library also contains historical codec code in the same tree.
- Historical codec presence does not expand the active supported engine set.
- `libs/ironcfg-c` is not the same contract as `native/ironfamily_c`.

## Build

```powershell
cmake -S libs/ironcfg-c -B libs/ironcfg-c/build
cmake --build libs/ironcfg-c/build --config Release
ctest --test-dir libs/ironcfg-c/build -C Release --output-on-failure
```

## Current dependency limit

- `libs/ironcfg-c/CMakeLists.txt` currently requires `OpenSSL`.
