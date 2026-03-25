# IronFamily.FileEngine

[![CI](https://img.shields.io/github/actions/workflow/status/Vanderhell/IronFamily.FileEngine/ci.yml?branch=master&label=CI)](https://github.com/Vanderhell/IronFamily.FileEngine/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](./LICENSE)
[![Stage: Active Development](https://img.shields.io/badge/stage-active%20development-orange)](#)
[![Latest Tag](https://img.shields.io/github/v/tag/Vanderhell/IronFamily.FileEngine?sort=semver&label=tag)](https://github.com/Vanderhell/IronFamily.FileEngine/tags)
[![Wiki](https://img.shields.io/badge/wiki-enabled-blue)](https://github.com/Vanderhell/IronFamily.FileEngine/wiki)

Internal monorepo for IronFamily file/data engines and tooling.

Status: this project is actively under development and still changing.

## What is in this repo

- `libs/` .NET and native libraries (`IronConfig`, `ILog`, `IUPD`, related components)
- `native/` native tests and low-level integration
- `tools/` internal tooling (including `megabench`)
- `vectors/` canonical test vectors (`small`, `medium`; local-only `large`)
- `docs/` technical documentation and decisions

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

## Benchmarking

Overview benchmark:

```powershell
dotnet run -c Release --project tools/megabench/MegaBench.csproj -- bench-overview --datasets 100KB,1MB --label current
```

Latest benchmark analysis:
- `artifacts/bench/megabench_metrics/overview/overview_mega_all_20260325_analysis.md`
- [Benchmark summary doc](./docs/BENCHMARK_RESULTS_2026-03-25.md)

## Repository standards

- Clean root, no ad-hoc logs/scripts in repository root
- Canonical vectors live under `vectors/`
- CI gates are required before merging
- Documentation must stay evidence-linked for normative claims

## Wiki

Wiki content source is maintained under `docs/wiki/`.
Use this directory as source-of-truth for future GitHub Wiki sync.

## Release

- Release process: [docs/RELEASE_PROCESS.md](./docs/RELEASE_PROCESS.md)
- Changelog: [CHANGELOG.md](./CHANGELOG.md)
- Current internal baseline tag: `v0.1.0-internal`
