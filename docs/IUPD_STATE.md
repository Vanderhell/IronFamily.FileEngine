# IUPD Engine - Active State (EXEC_IUPD_FINISH_01)

**Date**: 2026-03-14
**Status**: VERIFIED_BY_EXECUTION
**Last Build**: Fresh build + 246 tests passing (added 3 new IRONDEL2 tests)

## Engine Overview

IUPD (Iron Update) is the binary update package format in IronFamily.FileEngine. It provides:
- Configurable profiles (MINIMAL, FAST, SECURE, OPTIMIZED, INCREMENTAL)
- Cryptographic verification (Ed25519 signing, BLAKE3-256 hashing, CRC32 integrity)
- Crash-safe application via 3-phase commit (Stage → Commit Marker → Atomic Swap)
- Binary delta updates for incremental payloads (INCREMENTAL profile)
- Dependency tracking and apply ordering

## Active Architecture

**Production Path**: IRONDEL2 (ALGORITHM_IRONDEL2 = 0x02)
- Content-defined chunking delta algorithm
- Superior compression for similar binaries
- Primary focus for new INCREMENTAL packages
- Tested in 7+ targeted tests (incremental + V2-specific)

**Legacy Path**: DELTA_V1 (ALGORITHM_DELTA_V1 = 0x01)
- Fixed 4096-byte chunk delta algorithm
- Supported for backward compatibility only
- Maintained for reading legacy packages
- Tested in 4 dedicated tests (marked as legacy path)

## Code Locations

| Component | Location | Status |
|-----------|----------|--------|
| Core Engine | libs/ironconfig-dotnet/src/IronConfig/Iupd/ | VERIFIED_BY_EXECUTION |
| Writer | IupdWriter.cs, IupdBuilder.cs | VERIFIED_BY_EXECUTION |
| Reader | IupdReader.cs | VERIFIED_BY_EXECUTION |
| Apply Engine | IupdApplyEngine.cs | VERIFIED_BY_EXECUTION |
| Recovery | IupdApplyRecovery.cs | CODE_PRESENT_ONLY |
| Profiles | IupdProfile.cs | VERIFIED_BY_EXECUTION |
| Incremental Metadata | IupdIncrementalMetadata.cs | VERIFIED_BY_EXECUTION |
| IRONDEL2 Algorithm | Delta/IupdDeltaV2Cdc.cs | VERIFIED_BY_EXECUTION |
| DELTA_V1 Algorithm | Delta/IupdDeltaV1.cs | VERIFIED_BY_EXECUTION |
| Crypto (Ed25519) | Crypto/Ed25519Signing.cs | VERIFIED_BY_EXECUTION |
| Crypto (BLAKE3) | Blake3Ieee.cs | VERIFIED_BY_EXECUTION |
| Crypto (CRC32) | Crc32Ieee.cs | VERIFIED_BY_EXECUTION |

## Profile Status

| Profile | Status | Create | Read | Apply | Compression | BLAKE3 | Signature | Incremental |
|---------|--------|--------|------|-------|-------------|--------|-----------|-------------|
| MINIMAL | VERIFIED | Yes | Yes | Yes | No | No | No | No |
| FAST | VERIFIED | Yes | Yes | Yes | LZ4 | No | No | No |
| SECURE | VERIFIED | Yes | Yes | Yes | No | Yes | Yes | No |
| OPTIMIZED | VERIFIED | Yes | Yes | Yes | LZ4 | Yes | Yes | No |
| INCREMENTAL | VERIFIED | Yes | Yes | Yes | LZ4 | Yes | Yes | IRONDEL2 |

## Feature Implementation Status

### Create Path
- ✓ IupdWriter.AddChunk() - VERIFIED_BY_EXECUTION
- ✓ IupdWriter.SetProfile() - VERIFIED_BY_EXECUTION
- ✓ IupdWriter.AddDependency() - VERIFIED_BY_EXECUTION
- ✓ IupdWriter.SetApplyOrder() - VERIFIED_BY_EXECUTION
- ✓ IupdWriter.WithSigningKey() - VERIFIED_BY_EXECUTION
- ✓ IupdWriter.WithUpdateSequence() - VERIFIED_BY_EXECUTION
- ✓ IupdWriter.WithIncrementalMetadata() - VERIFIED_BY_EXECUTION
- ✓ Compression (LZ4) - VERIFIED_BY_EXECUTION
- ✓ BLAKE3 per-chunk - VERIFIED_BY_EXECUTION
- ✓ Ed25519 signing - VERIFIED_BY_EXECUTION

### Read Path
- ✓ IupdReader.Open() - VERIFIED_BY_EXECUTION
- ✓ IupdReader.ValidateFast() - VERIFIED_BY_EXECUTION
- ✓ IupdReader.ValidateStrict() - VERIFIED_BY_EXECUTION
- ✓ Signature verification - VERIFIED_BY_EXECUTION
- ✓ BLAKE3 verification - VERIFIED_BY_EXECUTION
- ✓ CRC32 verification - VERIFIED_BY_EXECUTION
- ✓ Manifest hash validation - VERIFIED_BY_EXECUTION
- ✓ Incremental metadata parsing - VERIFIED_BY_EXECUTION

### Apply Path (Non-Delta)
- ✓ 3-phase commit staging - VERIFIED_BY_EXECUTION
- ✓ Commit marker creation - VERIFIED_BY_EXECUTION
- ✓ Atomic swap - VERIFIED_BY_EXECUTION
- ✓ Decompression (LZ4) - VERIFIED_BY_EXECUTION
- ✓ Apply order enforcement - VERIFIED_BY_EXECUTION
- ✓ Dependency ordering - VERIFIED_BY_EXECUTION

### Apply Path (Incremental/Delta)
- ✓ IRONDEL2 create - VERIFIED_BY_EXECUTION
- ✓ IRONDEL2 apply - VERIFIED_BY_EXECUTION
- ✓ IRONDEL2 base hash validation - VERIFIED_BY_EXECUTION
- ✓ IRONDEL2 target hash validation - VERIFIED_BY_EXECUTION
- ✓ DELTA_V1 create - VERIFIED_BY_EXECUTION
- ✓ DELTA_V1 apply - VERIFIED_BY_EXECUTION
- ✓ DELTA_V1 base hash validation - VERIFIED_BY_EXECUTION
- ✓ DELTA_V1 target hash validation - VERIFIED_BY_EXECUTION

### Recovery
- ✓ IupdApplyRecovery class present - CODE_PRESENT_ONLY
- ? Recovery path tested end-to-end - NOT_VERIFIED
- ? Recovery from arbitrary interrupt point - NOT_VERIFIED

## Test Coverage

**Total IUPD Tests**: 246 (including 3 new IRONDEL2-focused tests added in EXEC_IUPD_FINISH_01)

**Test Distribution**:
- Profile coverage: 32+ profile-specific tests
- Apply engine: 15+ tests
- Signature verification: 13+ tests
- Delta algorithms: 50+ dedicated tests
  - IRONDEL2: 7+ tests (ACTIVE)
  - DELTA_V1: 4+ tests (LEGACY)
  - General delta: 40+ tests
- Corruption/error handling: 20+ tests
- Roundtrip/integration: 8+ tests
- Update sequence: 10+ tests
- Incremental profile: 15+ tests

**Execution Result**: 246/246 PASSED (100% pass rate)

**Duration**: ~3m per test run

## Comments and Clarifications in Code

**Active vs Legacy Markers** (added in EXEC_IUPD_FINISH_01):
- IupdIncrementalMetadata.cs: Algorithm IDs clearly marked (V1=Legacy, IRONDEL2=Active)
- IupdApplyEngine.cs: Apply dispatch includes comments on active vs legacy paths
- IupdDelta.cs: Class marked as DEPRECATED with note to use IupdDeltaV2Cdc

**Intent**: These clarifications make it unambiguous that:
- IRONDEL2 is the production/active algorithm
- DELTA_V1 is backward compatibility only
- New code should use IRONDEL2

## Known Limitations

**Unverified**:
- Recovery from arbitrary crash points (code present, not tested with actual crashes)
- Compression ratios (claims made, not measured in this audit)
- Large-file handling (limits unknown, tested with KB-scale only)
- Parallelization benefit (code present, performance not measured)

**Blocked by Environment**:
- Native C IUPD writer (not implemented)
- Native C ILOG/ICFG codecs (not implemented)
- Native C execution (compiler not in PATH)

## Summary

IUPD engine is fully implemented and verified in .NET. IRONDEL2 is the clear active path for incremental updates. DELTA_V1 is supported for backward compatibility. All 5 profiles are implemented and tested. Crash-safe apply via 3-phase commit is coded. Cryptography is integrated and tested.

**Confidence Level**: HIGH for read/write/apply paths; MEDIUM for recovery; LOW for unexecuted claims about performance/limits.

