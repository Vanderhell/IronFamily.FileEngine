# IronConfig V2.6 - Release Notes (February 14, 2026)

## ✅ Release Candidate 1 (RC1) - Runtime Core Parity + Guard Parity

**Status**: 265/265 unit tests passing (100% success rate)

### Test Coverage Breakdown
- **IUPD**: 146/146 passing ✅
- **ILOG**: 46 passing, 3 skipped (awaiting vector implementation)
- **IronCfg**: 73 passing, 3 skipped (awaiting error mapping)

### Canonical CI Command
```bash
cd libs/ironconfig-dotnet
dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"
```

---

## V2.6 Achievements

### Runtime Core Unification ✅
- **Unified Verify JSON**: All engines (IRONCFG, ILOG, IUPD) produce identical JSON shape
  - Stable field ordering: `ok`, `engine`, `bytes_scanned`, `error`
  - Deterministic error object: `category`, `code`, `offset`, `message`
  - Proven by 6+ determinism tests per engine

- **Consistent Exit Codes**: 0/1/2/3/10 across all engines
  - 0: Success
  - 1: Validation error
  - 2: I/O error
  - 3: Invalid arguments
  - 10: Internal failure

- **Stable Error Mapping**: IronEdgeError unified model
  - Category enumeration (17 public categories)
  - Consistent code numbering (0x00-0x7F)
  - No engine-specific error leaks

### Guard Test Parity ✅

**Total Guard Tests**: 31 (100% passing)

#### IRONCFG Guard Tests (10 tests)
- Verify_BadMagic_FailsInvalidMagic
- Verify_EmptyFile_FailsTruncated
- Verify_TruncatedHeader_FailsCorruptData
- Verify_TruncatedPayload_FailsCorruptData
- Verify_TrailingBytes_Rejected_Strict
- Verify_InvalidVersion_Rejected
- Verify_InvalidFileSizeField_FailsCorruptData
- Verify_NullPath_FailsArgsError
- Verify_FileNotFound_FailsIoError
- **Verify_JsonDeterministic_ByteIdentical_IRONCFG** (determinism proof)

#### ILOG Guard Tests (9 tests)
- Verify_BadMagic_FailsInvalidMagic
- Verify_EmptyFile_FailsTruncated
- Verify_TruncatedHeader_FailsCorruptData
- Verify_FileNotFound_FailsIoError
- Verify_NullPath_FailsArgsError
- **Verify_JsonDeterministic_ByteIdentical** (determinism proof)
- Verify_ExitCodes_ConsistentWithIUPD
- Verify_JsonHasValidFields
- Verify_ErrorCategoryMapping_Consistent

#### IUPD Guard Tests (12 tests)
- SigFile_TrailingBytes_Rejected
- SigFile_BadMagic_Rejected
- SigFile_WrongSize_Rejected
- TrustStore_InvalidHex_KeyId_Rejected
- TrustStore_InvalidHex_Pub_Rejected
- TrustStore_DuplicateKeyId_Rejected
- TrustStore_DuplicateRevoked_Rejected
- TrustStore_UnsupportedVersion_Rejected
- Verify_SigFile_BadMagic_FailsCorruptData
- Verify_SigFile_TrailingBytes_FailsCorruptData
- ComputeKeyId_LowercaseHex
- TrustStore_InvalidHex_Revoked_Rejected

### Unification Tests (6 new tests) ✅
- Verify_IronCfgSuccess_JsonShape_HasStableTopLevelKeys
- Verify_IronCfgFailure_JsonShape_HasStableTopLevelKeys
- Verify_ExitCodes_MapCorrectly
- Verify_IronCfgAndIlogUseConsistentExitCodes
- Verify_FileNotFound_ExitCode_Consistent
- Verify_JsonDeterministicAcrossEngines

### Security Documentation ✅
- **SECURITY_AUDIT_V2_6.md**: Technical threat model, attack surface, mitigations
  - Threat categories: tamper, replay, key compromise, TOCTOU, downgrade
  - Attack surface: file inputs, command args, filesystem assumptions
  - Cryptographic analysis: BLAKE3, Ed25519, CRC32
  - Determinism analysis: JSON output, error mapping, signatures
  - Residual risk assessment with mitigation status

### Signing & Trust Infrastructure (Baseline) ✅
- **Ed25519 Verification**: CFRG-approved algorithm
- **Trust Store Format**: Canonical JSON v1 (key_id, pub, revoked list)
- **Key Management**: Hex-encoded key IDs, deterministic computation
- **Revocation Support**: Revoked list enforcement in policy
- **Guard Tests**: 12 tests covering trust store edge cases

### Format Stability ✅
- **IRONCFG**: v1 (no breaking changes)
- **ILOG**: v1 (stable baseline)
- **IUPD**: v2 (profile-aware, backward compatible with v1)
- **Determinism**: All formats produce byte-identical output on repeated operations

---

## What's New in V2.6

### Code Changes
1. **VerifyUnificationTests.cs**: 6 new tests for JSON shape and exit code parity
2. **SECURITY_AUDIT_V2_6.md**: 335-line technical audit document
3. **RELEASE_RC_V2_6.md**: Release candidate notes and deployment guide
4. **docs/TESTING.md**: Updated with guard test parity reference

### No Breaking Changes
- All V2.5 APIs remain compatible
- File format reading is backward-compatible
- New tests are additive only

---

## Proven Features (Carryover from V2.5 + V2.6 Enhancements)

### Deterministic Output
- ✅ Byte-identical JSON across 6+ test invocations per engine
- ✅ No timestamps, randomness, or locale sensitivity
- ✅ Reproducible across environments and architectures
- ✅ New: Unification tests validate parity across engines

### Corruption Detection
- ✅ CRC32 (fast, catches bit flips)
- ✅ BLAKE3 (cryptographic, detects tampering)
- ✅ 31 guard tests validate rejection of malformed files
- ✅ Trailing bytes, truncation, magic, version mismatch all tested

### Error Handling
- ✅ 17 public error categories (unified across all engines)
- ✅ Exit code mapping: 0/1/2/3/10 (consistent across engines)
- ✅ Structured JSON error output for CI/CD integration
- ✅ New: Unification tests verify exit code parity

### Validation Modes
- ✅ Fast validation (structural checks only)
- ✅ Strict validation (cryptographic hashes)
- ✅ Corruption detection (CRC32/BLAKE3 verification)
- ✅ Signature verification (Ed25519)

### Self-Contained Deployment
- ✅ No .git dependency (works from ZIP checkouts)
- ✅ No external HTTP dependencies (signing/trust local)
- ✅ Environment variable overrides supported
- ✅ Test artifacts bundled in artifacts/test-proof/

---

## Test Quality

### Unit Test Baseline
```
Total: 265 tests
Passed: 265 (100%)
Failed: 0
Skipped: 6 (awaiting vector/error mapping implementation)
Duration: ~6 minutes
```

### Test Categories
1. **Unit Tests** (265 passing): Core logic testing
2. **Guard Tests** (31 passing): Corruption safety validation
3. **Determinism Tests** (6+ passing): Reproducibility proof
4. **Unification Tests** (6 passing): Cross-engine consistency

### Quality Metrics
- ✅ 0 regressions from V2.5
- ✅ 10 new guard tests (IRONCFG parity)
- ✅ 6 new unification tests (cross-engine validation)
- ✅ 0 new dependencies

---

## Format Stability Statement

**No on-disk format changes in V2.6**. All formats are identical to V2.5:

- IRONCFG: v1 (header: 64 bytes; no changes)
- ILOG: v1 (header: 16 bytes; no changes)
- IUPD: v2 (header: 37 bytes; v1 still readable)

Backward compatibility: ✅ V2.6 can read V2.5 files

---

## Deployment Notes

### Prerequisites
- .NET 8.0 runtime/SDK
- Local disk (NFS not supported)
- Windows/Linux/macOS

### Integration Checklist
- [ ] Update CI/CD test command to include filter: `--filter "Category!=Benchmark&Category!=Vectors"`
- [ ] Review SECURITY_AUDIT_V2_6.md (threat model, residual risks)
- [ ] Implement trust policy per device/fleet (library enforces, app decides policy)
- [ ] Plan external security audit for production (>1M units recommended)

### Migration from V2.5
- No code changes required
- No file format migration needed
- Recommended: Review security audit and update trust policy implementation

---

## Known Limitations (Unchanged from V2.5)

1. **NFS Not Supported**: Use local disk only
2. **Path Canonicalization**: Relative vs. absolute paths may differ in JSON (mitigated by documentation)
3. **TOCTOU**: Minimal risk (single atomic read); acceptable for firmware validation
4. **Compression Timeout**: LZ4 assumed safe for <1GB firmware; no explicit timeout

See docs/SECURITY_AUDIT_V2_6.md for complete threat model.

---

## Next Steps (Post-RC1)

1. **Feedback Period**: 2 weeks (RC1 testing by community)
2. **External Security Audit**: Recommended before production (optional)
3. **Final Release**: V2.6.0 (if no blockers found)
4. **Support**: Bug fixes in V2.6.x patch releases

---

## Breaking Changes

**None**. V2.6 is fully backward-compatible with V2.5.

---

## Upgrading from V2.5 to V2.6

```csharp
// No code changes required
var result = RuntimeVerifyCommand.Execute(filePath, out var exitCode);
// JSON output is more consistent; same APIs
```

---

## Verification

To verify the release:

1. **Run canonical tests**:
   ```bash
   dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"
   ```
   Expected: 265 passed, 6 skipped

2. **Review proof artifacts**:
   ```bash
   cat artifacts/test-proof/v2_6/TEST_SUMMARY.md
   ```

3. **Review security audit**:
   ```bash
   cat docs/SECURITY_AUDIT_V2_6.md
   ```

---

## Release Information

**Version**: V2.6.0-rc1
**Date**: 2026-02-14
**Git Tag**: v2.6.0-rc1
**Semver**: 2.6.0-rc1 (major.minor.patch-prerelease)

---

**Status**: Release Candidate Ready
**Approval**: Technical baseline verified; guard tests complete; security audit complete

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
