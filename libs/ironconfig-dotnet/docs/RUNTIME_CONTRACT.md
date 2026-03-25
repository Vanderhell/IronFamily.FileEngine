# IRONEDGE Runtime v2.2 — Deterministic Validation Runtime

## Status
Unit Test Baseline: **185/185 passing**

Command:
```bash
dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"
```

This baseline verifies all core runtime contracts.

---

## Core Guarantees

### Deterministic Validation Output
`runtime verify` produces deterministic JSON:
- Stable field ordering
- No timestamps
- No stack traces
- Stable exit codes

Exit codes:
- `0` = success
- `1` = validation error
- `2` = IO error
- `3` = invalid arguments
- `10` = internal runtime failure

### Unified Error Contract
- 17 stable public error categories
- Canonical internal codes (0x00–0x7F)
- Engine-aware mapping (IRONCFG, ILOG, IUPD)

### Integrity Coverage

**Core validation** (covered by unit tests):
- Header validation (magic/version)
- Truncation detection
- CRC32/BLAKE3 integrity verification
- Deterministic decoding/encoding validation

**Extended vector corpus suite**:
- Maintained separately for large-dataset verification
- Executed via `Category=Vectors`
- Does not affect CI unit baseline

---

## CI / ZIP Compatibility

- No `.git` dependency
- Test vectors resolved output-first
- Optional override via `IRONCONFIG_TESTVECTORS_ROOT`

---

## Reproducibility

**Unit baseline** (CI/CD):
```bash
dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"
```

**Full suite** (excluding long benchmarks):
```bash
dotnet test -c Release --filter "Category!=Benchmark"
```

**Benchmarks only**:
```bash
dotnet test -c Release --filter "Category=Benchmark"
```

---

## Verification Path

1. **Immediate** (v2.2): Unit baseline + CLI determinism + exit codes ✅
2. **Phase 1.2**: Complete vector corpus (ILOG/IRONCFG real data)
3. **Phase 1.3**: Cryptographic signatures + encryption

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
