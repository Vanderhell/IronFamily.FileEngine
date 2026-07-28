# Build and Install

This repository has two maintained implementation surfaces:

- `.NET` libraries and tools under `libs/ironconfig-dotnet/`
- native `C99` code under `native/` and `libs/ironcfg-c/`

## Prerequisites

- `.NET SDK 8.0` for the managed libraries and test projects
- `CMake 3.20+` and a C compiler for the native tree
- `OpenSSL` development libraries only when configuring optional legacy codec builds

## .NET build

```powershell
dotnet restore libs/ironconfig-dotnet/IronConfig.sln
dotnet build libs/ironconfig-dotnet/IronConfig.sln -c Release --no-restore
dotnet test libs/ironconfig-dotnet/IronConfig.sln -c Release --no-build
```

## Native build

The default native top-level build does not require `OpenSSL`. Optional legacy codec builds do.

```powershell
cmake -S native -B native/build
cmake --build native/build --config Release
ctest --test-dir native/build -C Release --output-on-failure
```

Optional first-party hardening builds:

```powershell
cmake -S native -B native/build-strict -DIRONFAMILY_STRICT_WARNINGS=ON
cmake --build native/build-strict --config Release

cmake -S native -B native/build-asan -DIRONFAMILY_ENABLE_SANITIZERS=ON
cmake --build native/build-asan --config Release
ctest --test-dir native/build-asan -C Release --output-on-failure
```

## Consumer notes

- The canonical native direction is portable `C99`.
- The managed codebase currently remains the main host-tooling and reference surface.
- `.NET` and native surfaces should be treated as separate contracts unless a specific parity gate proves equivalence.
