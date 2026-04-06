# IronFamily.FileEngine

[![CI](https://img.shields.io/github/actions/workflow/status/Vanderhell/IronFamily.FileEngine/ci.yml?branch=master&label=CI)](https://github.com/Vanderhell/IronFamily.FileEngine/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](./LICENSE)
[![Stage: Active Development](https://img.shields.io/badge/stage-active%20development-orange)](#)
[![Latest Tag](https://img.shields.io/github/v/tag/Vanderhell/IronFamily.FileEngine?sort=semver&label=tag)](https://github.com/Vanderhell/IronFamily.FileEngine/tags)
[![Wiki](https://img.shields.io/badge/wiki-enabled-blue)](https://github.com/Vanderhell/IronFamily.FileEngine/wiki)

Monorepo for IronFamily file and data engines, native components, and supporting tools.

Status: this project is actively under development and still changing.

## What is in this repo

- `libs/` .NET and native libraries (`IronConfig`, `ILog`, `IUPD`, related components)
- `native/` native code and low-level integration assets
- `tools/` development and benchmarking tools
- `vectors/` canonical test vectors
- `docs/` technical documentation and specifications

## Quick start

```powershell
dotnet build -c Release libs/ironconfig-dotnet/IronConfig.sln
dotnet test  -c Release libs/ironconfig-dotnet/IronConfig.sln
```

Native (if configured in your environment):

```powershell
cmake -S native -B native/build
cmake --build native/build --config Release
ctest --test-dir native/build -C Release --output-on-failure
```

## Performance

Overview benchmark:

```powershell
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-overview --datasets 100KB,1MB --label current
```

Reference benchmark documents:
- `artifacts/bench/megabench_metrics/overview/overview_mega_all_20260325_analysis.md`
- `artifacts/bench/megabench_metrics/overview/overview_mega_all_20260325_ranking.csv`

## Repository standards

- Clean root, no ad-hoc logs/scripts in repository root
- Canonical vectors live under `vectors/`
- CI gates are required before merging
- Documentation should stay aligned with code and released behavior

## Wiki

Wiki content source is maintained under `docs/wiki/`.
Use this directory as source-of-truth for future GitHub Wiki sync.

## Release

- Documentation index: [docs/README.md](./docs/README.md)
- Release process: [docs/RELEASE_PROCESS.md](./docs/RELEASE_PROCESS.md)
- Changelog: [CHANGELOG.md](./CHANGELOG.md)
- Releases and changelog history are tracked in the repository
