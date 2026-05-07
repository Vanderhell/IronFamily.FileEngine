# IronFamily.FileEngine — Cleanup Phase 2 Report

Date: 2026-05-07
Branch: master
Commits made this run: 9

## 1. Summary

- Phase A (legacy delete): skipped in this run (already deleted earlier); re-verify grep as specified returns hits due to active `IronConfig.Crypto` namespace usage in non-legacy code
- Phase B (duplicate vectors): done (deleted untracked `libs/ironconfig-dotnet/tests/artifacts/vectors/`)
- Phase C (empty shells): partially done (deleted untracked `tools/rv002_repro/`; skipped `tools/bench/` and `tools/bench_deps/` due to tracked content)
- Phase D (consolidate bench): 6 of 7 tools merged (skipped `bench_profiles` — build broke); `unified-bench` left standalone
- Phase E (CI guard): added `.github/workflows/forbidden-paths.yml`
- Build status: .NET PASS, CMake PASS

## 2. Bench consolidation results

| Tool | Action taken | Notes |
|------|--------------|-------|
| bench-datasets | merged into `tools/megabench/Tools/BenchDatasets/` | kept as separate csproj |
| bench_profiles | skipped | `dotnet build` failed: CS0017 (multiple entry points) + CS1503 in BenchmarkProfilesStabilized; needs refactor decision |
| CRC32_KAT | merged into `tools/megabench/Tools/Crc32Kat/` | kept as separate csproj |
| diff-bench | merged into `tools/megabench/Tools/DiffBench/` | kept as separate csproj |
| ilog_bench | merged into `tools/megabench/Tools/IlogBench/` | kept as separate csproj |
| incremental-bench | merged into `tools/megabench/Tools/IncrementalBench/` | kept as separate csproj |
| iupd_bench_real | merged into `tools/megabench/Tools/IupdBenchReal/` | deleted empty nested `tools/rv002_repro/` |

## 3. unified-bench analysis

`tools/unified-bench/` appears to be a self-contained benchmark suite (with competitor codecs + a lot of internal writeups/logs) that produces internal decision docs (e.g. verdict/recommendations) alongside executable benchmarks. It is larger than the other small tools and contains multiple subfolders (CompetitorBenchmarks/MainBenchmarks/MicroBenchmarks/MemoryProfiling) plus many deliverable markdown/txt/log files. Recommendation: keep it separate under `tools/` for now; if it is still actively used, plan a dedicated migration into `tools/megabench/` after agreeing on what deliverables belong in-repo vs artifacts.

Top-level files (selected): `unified-bench.csproj`, `Program.cs`, `IoTArtifactBenchmark.cs`, `IoTLogBenchmark.cs`, `SerializationFormatBenchmark.cs`, `VERDICT_AND_RECOMMENDATIONS.md`.

## 4. Skipped — needs human review

- Phase A re-verify command is too broad: it flags active `IronConfig.Crypto` namespace usage (non-legacy). If you want a strict re-verify, search specifically for legacy project path references (e.g. `legacy/IronConfig.Crypto/*.csproj`) or for removed legacy namespaces (Icf2/Icfx/Icxs/Bjv) outside legacy.
- `tools/bench/` not deleted (contains tracked `compare_baseline.ps1`). Decide whether to keep it under `tools/megabench/Tools/` or retire it.
- `tools/bench_deps/` not deleted (contains `README.md`). Decide whether it should live under `tools/megabench/Tools/` or be removed.

## 5. Loose tools/ files (Icf2VectorGenerator.*, icfg_golden_vector_gen.cs)

- `tools/Icf2VectorGenerator.csproj` is not in any solution; the implementation was retired (stub) to remove references to deleted legacy ICF2 APIs.
- `tools/Icf2VectorGenerator.cs` exists (retired stub).
- `tools/icfg_golden_vector_gen.cs` exists and is not referenced by any solution; treat as possibly dead until confirmed.

## 6. New top-level structure

```
.
├── .github/
│   ├── ISSUE_TEMPLATE/
│   ├── workflows/
│   ├── CODEOWNERS
│   ├── pull_request_template.md
│   ├── release.yml
│   ├── repo-topics.txt
├── artifacts/
│   ├── bench/
│   ├── vectors/
├── bench/
│   ├── baselines/
│   ├── budgets/
│   ├── reproduce.ps1
│   ├── reproduce.sh
├── docs/
│   ├── wiki/
│   ├── ENGINE_TRUTH_SUMMARY.md
│   ├── ICFG_COMPATIBILITY.md
│   ├── ICFG_SCHEMA_AND_TYPES.md
│   ├── ICFG_SPEC.md
│   ├── ILOG_COMPATIBILITY.md
│   ├── ILOG_SCHEMA_AND_TYPES.md
│   ├── ILOG_SPEC.md
│   ├── IUPD_COMPATIBILITY.md
│   ├── IUPD_SPEC.md
│   ├── IUPD_STRUCTURE_AND_VALIDATION.md
│   ├── NATIVE_C_API.md
│   ├── NATIVE_C_EXAMPLES.md
│   ├── README.md
│   ├── RELEASE_PROCESS.md
├── incremental_vectors/
│   ├── refusal_01_wrong_base_hash/
│   ├── refusal_02_unknown_algorithm/
│   ├── refusal_03_corrupted_crc32/
│   ├── refusal_04_target_hash_mismatch/
│   ├── refusal_05_missing_metadata/
│   ├── success_01_delta_v1_simple/
│   ├── success_02_irondel2_simple/
│   ├── success_03_delta_v1_medium/
│   ├── success_04_irondel2_medium/
│   ├── success_05_delta_v1_no_target/
│   ├── manifest.json
├── libs/
│   ├── bjv-c/
│   ├── bjv-cpp/
│   ├── ironcfg-c/
│   ├── ironcfg-cpp/
│   ├── ironconfig-dotnet/
├── native/
│   ├── cmake/
│   ├── ironfamily_c/
│   ├── tests/
│   ├── third_party/
│   ├── CMakeLists.txt
├── out/
│   ├── native/
├── releases/
│   ├── v0.1.0-internal.md
├── specs/
│   ├── IRONDEL2_SPEC_MIN.md
├── testing/
│   ├── fixtures/
│   ├── ilog/
│   ├── ironcfg/
│   ├── iupd/
│   ├── README.md
├── tests/
│   ├── IronFamily.OtaCli.Tests/
├── tools/
│   ├── artifacts/
│   ├── bench/
│   ├── bench_deps/
│   ├── bench_profiles/
│   ├── datasets/
│   ├── delta-matrix/
│   ├── diagnostics/
│   ├── docs_truth_gate/
│   ├── evidence_verifier/
│   ├── gauntlet/
│   ├── git_policy/
│   ├── incremental-vector-gen/
│   ├── incremental-vector-test-net/
│   ├── ironcert/
│   ├── ironconfigtool/
│   ├── IronFamily.OtaCli/
│   ├── IronFamily.Vectors/
│   ├── megabench/
│   ├── unified-bench/
│   ├── dataset_generator.csx
│   ├── generate_golden_vectors.py
│   ├── Icf2VectorGenerator.cs
│   ├── Icf2VectorGenerator.csproj
│   ├── icfg_golden_vector_gen.cs
├── vectors/
│   ├── large/
│   ├── medium/
│   ├── small/
├── .gitignore
├── CLEANUP_REPORT.md
├── CMakeLists.txt
├── CMakePresets.json
├── CODE_OF_CONDUCT.md
├── CONTRIBUTING.md
├── Directory.Build.props
├── global.json
├── CHANGELOG.md
├── LICENSE
├── README.md
├── SECURITY.md
```

## 7. Build verification

- dotnet build: PASS (`dotnet build .\\libs\\ironconfig-dotnet\\IronConfig.sln -c Release`)
- cmake build: PASS (`cmake --preset native-windows-debug` + `cmake --build --preset native-windows-debug`)

## 8. Recommended next steps

1. Decide what to do with `tools/bench_profiles/` (fix multiple-entry-point project layout vs retire it).
2. Decide whether `tools/unified-bench/` should be migrated into megabench, kept as a separate tool, or archived; also decide whether its deliverable/log files belong under `artifacts/` instead of tracked source.
3. Decide whether to retire or migrate `tools/bench/` and `tools/bench_deps/` into `tools/megabench/Tools/`.
4. If you still want Phase A re-verify to be meaningful, replace the grep with a check for project path references to legacy folders (not namespace string matches).

---

### Commits made this run
```
86d89ce ci: add forbidden-paths guard to prevent build artifacts in git
5d47006 refactor(bench): consolidate iupd_bench_real into megabench/Tools
98c210c refactor(bench): consolidate incremental-bench into megabench/Tools
1fd4530 refactor(bench): consolidate ilog_bench into megabench/Tools
3711967 refactor(bench): consolidate diff-bench into megabench/Tools
a606ff9 refactor(bench): consolidate CRC32_KAT into megabench/Tools
ba4bc3e refactor(bench): consolidate bench-datasets into megabench/Tools
eac5a8e chore(tools): retire legacy ICF2 vector generator
27e763a chore(tools): remove legacy format commands from ironconfigtool
```
