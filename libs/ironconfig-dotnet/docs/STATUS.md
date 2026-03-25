# IronConfig V2.6.0-rc1 Status

**Current Release**: V2.6.0-rc1 (Release Candidate 1)
**Released**: 2026-02-14
**Git Tag**: v2.6.0-rc1

---

## Baseline Test Results

### Canonical Command
```bash
cd libs/ironconfig-dotnet
dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"
```

### Results
- **Total**: 265 tests
- **Passed**: 259 (97.7%)
- **Failed**: 0
- **Skipped**: 6 (2.3%)

### Per-Engine Breakdown
| Engine | Passed | Skipped | Total | Status |
|--------|--------|---------|-------|--------|
| IUPD | 146 | 0 | 146 | ✅ |
| ILOG | 46 | 3 | 49 | ✅ |
| IronCfg | 67 | 3 | 70 | ✅ |
| **TOTAL** | **259** | **6** | **265** | **✅** |

---

## Guard Tests (Corruption Safety)

**Total**: 31 (100% passing)

- IRONCFG: 10 tests (magic, version, payload, determinism)
- ILOG: 9 tests (magic, truncation, determinism)
- IUPD: 12 tests (signing, trust store, determinism)

---

## What's Stable

✅ **Format Stability**
- IRONCFG: v1 (no changes since release)
- ILOG: v1 (stable baseline)
- IUPD: v2 (backward compatible with v1)

✅ **Determinism**
- JSON output is byte-identical across invocations
- Error mapping is stable
- Exit codes are consistent (0/1/2/3/10)

✅ **Backward Compatibility**
- V2.6 reads V2.5 files
- V2.5 APIs unchanged
- No breaking changes

---

## Why 6 Tests Skipped?

Tests awaiting external test vector implementation (not product bugs):
- ILOG corruption tests (3): Placeholder vectors, real vectors pending
- IronCfg error mapping tests (3): ILOG error integration pending

All 6 skipped tests are marked `[Skip]` and are non-blocking for RC.

---

## For More Information

- **[Documentation Index](INDEX.md)** — Navigation and quick links
- **[Testing Guide](TESTING.md)** — How to run tests and CI/CD setup
- **[Runtime Contract](RUNTIME_CONTRACT.md)** — API determinism guarantees
- **[Security Audit](SECURITY_AUDIT_V2_6.md)** — Threat model and mitigations
- **[Release Notes](RELEASE_RC_V2_6.md)** — Feature completeness and deployment

---

**Last Verified**: 2026-02-14

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
