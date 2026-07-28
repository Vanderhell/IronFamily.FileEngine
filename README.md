# IronFamily.FileEngine

[![CI](https://img.shields.io/github/actions/workflow/status/Vanderhell/IronFamily.FileEngine/ci.yml?branch=master&label=CI)](https://github.com/Vanderhell/IronFamily.FileEngine/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](./LICENSE)
[![Stage: Active Development](https://img.shields.io/badge/stage-active%20development-orange)](#)
[![Latest Tag](https://img.shields.io/github/v/tag/Vanderhell/IronFamily.FileEngine?sort=semver&label=tag)](https://github.com/Vanderhell/IronFamily.FileEngine/tags)

Monorepo for the active `ICFG`, `ILOG`, and `IUPD` engines plus their native and managed support code.

The canonical native direction is portable `C99`. The current `C#` and `.NET` code remains in scope as host tooling, reference implementation, vector generation, parity support, and bindings support. Native and managed surfaces are not claimed equivalent unless a specific gate proves parity.

Historical codecs remain in the source tree for compatibility work, but they are not active supported engines. This documentation does not claim production readiness.

## Repository layout

- `libs/ironconfig-dotnet/` managed libraries, tests, and host tooling
- `libs/ironcfg-c/` public C API surface and related native code
- `native/` top-level native build and `ironfamily_c` verifier-oriented code
- `vectors/` canonical vectors referenced by tests
- `docs/` public documentation for active repository scope

## Build and test

```powershell
dotnet restore libs/ironconfig-dotnet/IronConfig.sln
dotnet build -c Release libs/ironconfig-dotnet/IronConfig.sln
dotnet test -c Release libs/ironconfig-dotnet/IronConfig.sln
```

Native:

```powershell
cmake -S native -B native/build
cmake --build native/build --config Release
ctest --test-dir native/build -C Release --output-on-failure
```

Optional native hardening configs:

```powershell
cmake -S native -B native/build-strict -DIRONFAMILY_STRICT_WARNINGS=ON
cmake --build native/build-strict --config Release

cmake -S native -B native/build-asan -DIRONFAMILY_ENABLE_SANITIZERS=ON
cmake --build native/build-asan --config Release
```

## Current dependency limits

- The default native build does not require `OpenSSL`.
- `OpenSSL` is only required when optional legacy codec targets are explicitly enabled.
- The full native tree should not be described as dependency-free embedded core code because optional legacy paths still exist.
- `.NET 8.0` is required for the managed solution and its tests.

## Documentation

- Documentation index: [docs/README.md](./docs/README.md)
- Build and install: [docs/BUILD_AND_INSTALL.md](./docs/BUILD_AND_INSTALL.md)
- Architecture and scope: [docs/ARCHITECTURE_SCOPE.md](./docs/ARCHITECTURE_SCOPE.md)
- Compatibility and versioning: [docs/COMPATIBILITY_AND_VERSIONING.md](./docs/COMPATIBILITY_AND_VERSIONING.md)
- Security model: [docs/SECURITY_MODEL.md](./docs/SECURITY_MODEL.md)
- Implementation status: [docs/IMPLEMENTATION_STATUS.md](./docs/IMPLEMENTATION_STATUS.md)
- Release process: [docs/RELEASE_PROCESS.md](./docs/RELEASE_PROCESS.md)
- Changelog: [CHANGELOG.md](./CHANGELOG.md)
