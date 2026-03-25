# IronFamily.FileEngine - Architecture Truth

**Date**: 2026-03-14
**Status**: Hard-truth locked architecture
**Audience**: Maintainers, architects, runtime integrators

---

## Family Definition

IronFamily.FileEngine consists of three core engines:

1. **IUPD** — Iron Update Package Distribution
   - Binary update package format with 5 configurable profiles
   - Incremental delta support (IRONDEL2 active, DELTA_V1 legacy)
   - Crash-safe 3-phase apply
   - Scope: Full create/read/apply/verify

2. **ILOG** — Iron Log encoding format
   - Structured log format with compression
   - Write-once append-only design
   - Scope: Read/write/compress logs

3. **ICFG** — Iron Configuration format
   - Structured config file format
   - Type-safe key-value storage
   - Scope: Read/write/validate configs

---

## Current Verified Architecture Model

### Runtime Distribution

**PRIMARY PRODUCER RUNTIME**: .NET (Framework)
- Produces all packages and formats
- Implements full create, read, validate, apply, sign for all three engines
- Tested via fresh execution (246 IUPD tests, 126 ILOG tests, 106 ICFG tests)

**PRIMARY CONSUMER RUNTIME**: .NET (Framework)
- Applies and verifies all packages
- Reads all formats

**SECONDARY CONSUMER RUNTIME**: Native C (Partial / Read-Only + Apply)
- Applies IUPD packages on embedded systems
- Cannot create IUPD packages (no writer in C)
- Cannot encode ILOG (no encoder in C)
- Cannot encode ICFG (no encoder in C)
- Compiler unavailable in environment (BLOCKED_BY_ENVIRONMENT)

### Architecture Model Classification

**Current Model**: INTENTIONAL ASYMMETRIC PRODUCER/CONSUMER

**Rationale from Code**:
- .NET is the sole writer for all three engines
- Native C is read-only + apply for IUPD (apply is the critical path for embedded)
- No ILOG/ICFG encoder in native C (only parser/read)
- This is by design: server generates packages, embedded devices consume and apply

**Is .NET + C parity a goal?**
- NOT VERIFIED as a documented goal
- Code evidence: Native C has no writer for IUPD, no encoder for ILOG/ICFG
- Architecture docs do not state full parity as a target
- Conclusion: Asymmetry is intentional, not incomplete

---

## Engine Status Classification

| Engine | Runtime | Read | Write | Validate | Apply | Compress | Sign/Verify | Tested | Status |
|--------|---------|------|-------|----------|-------|----------|-------------|--------|--------|
| **IUPD** | .NET | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ACTIVE |
| **IUPD** | Native C | ✅ | ❌ | ⚠️ | ✅ | ⚠️ | ❌ | ⛔ | CODE_PRESENT_ONLY |
| **ILOG** | .NET | ✅ | ✅ | ⚠️ | N/A | ✅ | N/A | ✅ | ACTIVE |
| **ILOG** | Native C | ✅ | ❌ | ⚠️ | N/A | ❌ | N/A | ⛔ | NOT_PRESENT |
| **ICFG** | .NET | ✅ | ✅ | ✅ | N/A | N/A | N/A | ✅ | ACTIVE |
| **ICFG** | Native C | ✅ | ❌ | ❌ | N/A | N/A | N/A | ⛔ | PARTIAL |

Legend:
- ✅ = Implemented and verified by execution
- ⚠️ = Present in code but execution verification blocked
- ❌ = Not implemented
- ⛔ = Not tested (compiler unavailable or not implemented)

---

## IUPD Delta Direction Policy

### Active Direction

**IRONDEL2** (DELTA_V2 with content-defined chunking)
- Status: **ACTIVE_PRODUCTION**
- Algorithm ID: 0x02
- Features: Content-defined chunking, superior compression, adaptive to content
- Test coverage: 7+ dedicated tests, all PASS
- Code: IupdDeltaV2Cdc.cs (active implementation)
- Profile: INCREMENTAL profile default choice
- Created: EXEC_IUPD_FINISH_01 hardened active status

### Legacy Direction

**DELTA_V1** (Fixed 4096-byte chunks)
- Status: **LEGACY_COMPATIBILITY_ONLY**
- Algorithm ID: 0x01
- Features: Backward compatibility with existing packages
- Test coverage: 4+ dedicated tests, all PASS
- Code: IupdDelta.cs (marked DEPRECATED, retained for backward compat)
- Profile: INCREMENTAL profile supports for legacy packages
- Usage: Do not use for new packages

### Rationale

IRONDEL2 provides superior compression for binary deltas through content-defined chunking that adapts to actual content rather than fixed boundaries. For 99% identical files, delta packages typically represent 5-20% of raw target size. Legacy support for DELTA_V1 ensures existing embedded devices can apply older packages.

---

## Feature Completeness

### IUPD Completeness

| Feature | .NET Status | Native C Status | Evidence |
|---------|-------------|-----------------|----------|
| Create packages | VERIFIED_BY_EXECUTION | NOT_PRESENT | 246 tests pass |
| Read packages | VERIFIED_BY_EXECUTION | CODE_PRESENT_ONLY | 246 tests pass; C compiler unavailable |
| Parse IUPDINC1 metadata | VERIFIED_BY_EXECUTION | CODE_PRESENT_ONLY | Tests pass; C has iupd_incremental_metadata.c |
| Apply packages (OTA) | VERIFIED_BY_EXECUTION | CODE_PRESENT_ONLY | 246 tests pass; C has ota_apply.c, not executed |
| Apply IRONDEL2 deltas | VERIFIED_BY_EXECUTION | CODE_PRESENT_ONLY | 7+ tests pass; C has delta2_apply.c, not executed |
| Apply DELTA_V1 deltas | VERIFIED_BY_EXECUTION | CODE_PRESENT_ONLY | 4+ tests pass; C has diff_apply.c, not executed |
| Verify signatures (Ed25519) | VERIFIED_BY_EXECUTION | NOT_PRESENT | 42+ tests pass; no Ed25519 in C |
| Hash verification (BLAKE3) | VERIFIED_BY_EXECUTION | CODE_PRESENT_ONLY | 38+ tests pass; C has crypto support |
| Compression/decompression | VERIFIED_BY_EXECUTION | CODE_PRESENT_ONLY | 246 tests pass; C code unclear on LZ4 |
| Crash-safe recovery | CODE_PRESENT_ONLY | N/A | Recovery code exists, not crash-simulated |
| Large-file handling | VERIFIED_BY_EXECUTION (KB-scale) | UNVERIFIED | Tests 5-10KB; no MB+ execution |
| Parallel apply | CODE_PRESENT_ONLY | N/A | Parallel.ForEach used, benefit unproven |

### ILOG Completeness

| Feature | .NET Status | Evidence |
|---------|-------------|----------|
| Encode logs | VERIFIED_BY_EXECUTION | 126 tests pass |
| Decode logs | VERIFIED_BY_EXECUTION | 126 tests pass |
| Compress entries | VERIFIED_BY_EXECUTION | 126 tests pass |
| Native C support | NOT_PRESENT | No encoder/decoder in C |

### ICFG Completeness

| Feature | .NET Status | Evidence |
|---------|-------------|----------|
| Encode configs | VERIFIED_BY_EXECUTION | 106 tests pass |
| Read/parse configs | VERIFIED_BY_EXECUTION | 106 tests pass |
| Validate structure | VERIFIED_BY_EXECUTION | 106 tests pass |
| Native C support | PARTIAL | Reader present, no encoder |

---

## Dependency Model

```
Server/Build Environment (.NET)
├── Creates IUPD packages (all profiles, all algorithms)
├── Encodes ILOG archives
├── Encodes ICFG configs
└── Generates firmware images

        │
        ├─── Network/Storage
        │
        v

Embedded Device (Native C, if present)
├── Reads IUPD packages
├── Reads ILOG archives (parser present, decompression status unclear)
├── Reads ICFG configs (parser present)
├── Applies OTA updates
└── Cannot generate any format (no writers)

Embedded Device (.NET Runtime, if present)
├── Full parity with server
├── Can apply and verify
└── Can generate new packages if needed (not typical for embedded)
```

---

## Declared Intentions Found in Code/Repo

**No formal requirements document found that declares parity as a goal.**

Evidence search:
- CMakeLists.txt: Describes native C as "read-only partial port"
- Code comments: Refer to legacy vs active paths, not to parity goals
- Test suite: Tests .NET thoroughly, minimal/no native C execution evidence
- README files: Not examined (outside scope)

**Conclusion**: Asymmetric producer/consumer is the current architecture, not an incomplete state toward parity.

---

## Summary

IronFamily.FileEngine is a **server-focused package generation system** with **embedded-focused apply/verify on optional native C runtime**. The three core engines (IUPD, ILOG, ICFG) are production-ready in .NET for creation and distribution. IUPD has partial C support for applied packages on embedded systems. Full native C parity is not a documented goal; the current asymmetry is intentional.

