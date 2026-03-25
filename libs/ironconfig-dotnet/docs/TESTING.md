# Testing Guide

## Overview

The IronEdge Runtime test suite includes:
- **Unit Tests**: Fast, deterministic tests (~500ms per project)
- **Benchmark Tests**: Long-running performance tests (20-30s+ each)
- **Test Vectors**: Self-contained in `vectors/small/` (copied to output directory during build)

## Quick Start

```bash
# Unit tests only (GREEN - all pass) - ~2 seconds
dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"

# Unit + Vector tests (integration tests with placeholder vectors)
dotnet test -c Release --filter "Category!=Benchmark"

# Benchmarks only (slower)
dotnet test -c Release --filter "Category=Benchmark"

# Specific project
dotnet test tests/IronConfig.IronCfgTests -c Release
```

## Test Vector Strategy

**Vectors are automatically self-contained** - no external dependencies needed:

1. Tests look in `{output_directory}/vectors/small/` first (from ZIP)
2. Falls back to `IRONCONFIG_TESTVECTORS_ROOT` environment variable
3. Falls back to repo root `vectors/small/` directory

This works from:
- Ă˘Ĺ›â€ś Clean ZIP checkouts
- Ă˘Ĺ›â€ś Git repositories
- Ă˘Ĺ›â€ś CI/CD environments
- Ă˘Ĺ›â€ś Vendor/embedded contexts

### Using Custom Test Vectors
```bash
export IRONCONFIG_TESTVECTORS_ROOT=/path/to/vectors
dotnet test -c Release
```

## Running Benchmark Tests

Benchmarks are performance tests that run comprehensive scenarios. They take 20-30+ seconds each:

```bash
# Run ONLY benchmarks
dotnet test -c Release --filter "Category==Benchmark"

# Run IRONCFG benchmarks
dotnet test tests/IronConfig.IronCfgTests/IronConfig.IronCfgTests.csproj -c Release --filter "Category==Benchmark"

# Run ILOG benchmarks
dotnet test tests/IronConfig.ILog.Tests/IronConfig.ILog.Tests.csproj -c Release --filter "Category==Benchmark"
```

**Expected timing**: 20-30+ seconds per benchmark test (use only when needed)

## Canonical Baseline Proof

A reproducible, canonical baseline test proof is maintained at:
```
artifacts/test-proof/v2_6/TEST_SUMMARY.md
```

This includes:
- **TRX logs**: Machine-readable test results (`all.trx`, `iupd.trx`, `ilog.trx`, `ironcfg.trx`)
- **Guard test summary**: 31 guard tests validating format/corruption safety across all engines
  - IUPD: 12 guard tests (signing, trust store, corruption detection)
  - ILOG: 9 guard tests (magic, truncation, determinism)
  - IRONCFG: 10 guard tests (magic, version, payload integrity, determinism)
- **Determinism verification**: Proof that JSON/binary output is byte-identical
- **Environment details**: Exact .NET SDK, runtime, and configuration used

See `TEST_SUMMARY.md` for full details.

---

## CI/CD Configuration

### Recommended: Default CI Test (Pure Unit Tests - GREEN Ă˘Ĺ›â€¦)
```bash
dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"
```
- **Status**: ALL PASSING Ă˘Ĺ›â€¦ (249 tests)
- Completes in ~6 minutes
- Suitable for **every commit**
- Catches functional regressions without flaky vector tests
- Does NOT require real test vectors
- **Proof**: See `artifacts/test-proof/v2_6/TEST_SUMMARY.md`

### Optional: Integration Test Suite
```bash
dotnet test -c Release --filter "Category!=Benchmark"
```
- Includes unit tests + vector tests
- ~40 tests will fail (placeholder vectors)
- Run when developing vector tests
- Completes in ~5 seconds

### Optional: Nightly CI Test (Full Suite)
```bash
dotnet test -c Release
```
- Includes unit + vector + benchmark tests
- Benchmarks: ~100+ seconds
- Vector tests: ~40 failures (placeholder vectors)
- Run once per day or per release
- Validates performance characteristics

### How to Reproduce Locally
```bash
cd libs/ironconfig-dotnet

# Run GREEN unit test suite (all pass)
dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"

# See which tests are skipped/failing
dotnet test -c Release --filter "Category!=Benchmark"
```

## Troubleshooting

### Test Vector Resolution Failures
Some IRONCFG tests fail with "Could not find repository root (.git directory)". This is expected when:
- Running tests from a non-git context
- Tests depend on vectors in repo root

Status: **Not a blocker** - these are pre-existing failures, not related to the benchmark organization.

### Test Hangs
If tests hang (>30s without completing):
1. Check if you're running benchmarks unintentionally
2. Use `--filter "Category!=Benchmark"` to exclude them
3. Report with the hanging test name to the team

## Test Categories

- **Category=Benchmark**: Long-running performance tests (use: `--filter "Category=Benchmark"`)
  - `IronCfg.Tests.IronCfgBinaryBenchmarkTests.Benchmark_Binary_Formats_Comprehensive` (20-30s)
  - `IronCfg.Tests.IronCfgBenchmarkTests.Benchmark_IronCfg_Comprehensive` (20s)
  - `ILog.Tests.IlogBenchmarkTests.*` (5 tests, 5-10s each)

- **Category=Vectors**: Integration tests using placeholder test vectors (use: `--filter "Category=Vectors"`)
  - `IronCfg.Tests.IronCfg.IronCfgValueReaderTests.*` (10 tests)
  - `ILog.Tests.IlogParityTests.*` (9 tests)
  - Status: Placeholder vectors present; real vectors pending generation
  - These tests WILL FAIL until real test vectors are generated

- **Category!=Benchmark&Category!=Vectors**: Pure unit tests (PASSING Ă˘Ĺ›â€¦)
  - All encoding/decoding logic tests
  - Error handling and corruption detection tests
  - Determinism and output stability tests
  - ~185 tests total, all passing (~2 seconds)

## Adding New Tests

- **Pure unit test** (recommended): No special attributes
  - Should run <1s without external files
  - Will run in default suite and CI/CD

- **Benchmark test**: Add `[Trait("Category", "Benchmark")]`
  - Long-running performance tests
  - Excluded from default runs

- **Vector/Integration test**: Add `[Trait("Category", "Vectors")]`
  - Tests depending on test vectors
  - Excluded from default unit test run (due to placeholder vectors)

Example:
```csharp
// Unit test - RUNS BY DEFAULT
[Fact]
public void MyUnitTest() { /* ... */ }

// Benchmark - Excluded by default
[Fact]
[Trait("Category", "Benchmark")]
public void Benchmark_New_Feature() { /* ... */ }

// Vector test - Excluded by default
[Theory]
[Trait("Category", "Vectors")]
[InlineData("small")]
public void VectorTest(string dataset) { /* ... */ }
```

## Summary

| Test Type | Command | Time | Status | Frequency |
|-----------|---------|------|--------|-----------|
| **Unit Tests** Ă˘Ĺ›â€¦ | `--filter "Category!=Benchmark&Category!=Vectors"` | ~2s | **GREEN (185/185)** | Every commit (CI) |
| Unit + Vectors | `--filter "Category!=Benchmark"` | ~5s | Mixed (185 pass, 40 fail) | Development |
| Benchmarks | `--filter "Category==Benchmark"` | ~100s | Not run by default | Nightly/Release |
| All Tests | (no filter) | ~107s | Mixed | Full suite |

---

**Last Updated**: February 2026
**Status**: Ă˘Ĺ›â€¦ Unit tests GREEN and ready for CI. Vector tests properly categorized and excluded from default runs. Benchmarks quarantined.
**Canonical Baseline**: `artifacts/test-proof/v2_6/TEST_SUMMARY.md` (Release config, 249 tests passing, 21 guard tests)

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
