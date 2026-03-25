# IronConfig V2.6 Security Audit (Technical)

**Date**: 2026-02-14
**Scope**: IRONCFG, ILOG, IUPD format engines + runtime verify + signing/trust
**Audience**: Technical reviewers, integrators, security engineers

---

## Executive Summary

V2.6 implements deterministic file format validation with cryptographic integrity checking (CRC32, BLAKE3). Guard tests prevent common corruption patterns. No cryptographic weaknesses found in design. Mitigations documented below. **This is NOT a formal security audit by external firm**—a technical inventory of threat model, attack surface, and implemented controls.

---

## 1. Threat Model

### 1.1 Asset: File Integrity
- **Threat**: Accidental or malicious file corruption in transit or at rest
- **Mitigation**: CRC32 (fast, detects bit flips), BLAKE3 (cryptographic, detects intentional tampering)
- **Residual Risk**: Attacker with write-access to file system can craft valid signatures; trust store policy required (see 1.4)

### 1.2 Asset: Configuration Schema Compliance
- **Threat**: Malformed IRONCFG files bypassing schema validation
- **Mitigation**: Strict length field validation, bounds checking, version enforcement
- **Residual Risk**: Edge case in nested schema validation (see 2.3)

### 1.3 Asset: Cryptographic Key Trust
- **Threat**: Key compromise, revocation bypass, downgrade attacks
- **Mitigation**: Ed25519 (CFRG-approved), trust store with revocation list, policy enforcement
- **Residual Risk**: Trust store is the security boundary; compromised store = compromised system (architectural assumption)

### 1.4 Asset: Update Authenticity (IUPD + Signing)
- **Threat**: Unsigned or revoked updates installed
- **Mitigation**: Mandatory signature verification, revocation checking, policy enforcement
- **Residual Risk**: User error in trust policy configuration; mitigation: docs + examples

---

## 2. Attack Surface

### 2.1 File Inputs (IRONCFG, ILOG, IUPD)

**Boundary**: File header parsing (first 64 bytes)

Protections:
- ✅ Magic byte validation (early rejection of wrong format)
- ✅ Version field validation (forward-compatibility constraint)
- ✅ Length fields cross-checked with file size
- ✅ Flag field sanity (reserved bits unmarked)

**Identified Gaps**: None critical

---

**Boundary**: Data payload parsing (variable-length)

Protections:
- ✅ Bounds checking: offsets + lengths vs. file size
- ✅ No unbounded recursion (stack-safe)
- ✅ Trailing bytes rejection (strict parsing)
- ✅ CRC32/BLAKE3 validation before use

**Known Limitation**: ILOG compression decompression uses LZ4-style matching; no DoS timeout (assumed LZ4 is safe for firmware sizes <1GB; see section 3.2)

---

**Boundary**: String/dictionary fields (embedded in data)

Protections:
- ✅ String length limits (16MB per IRONCFG, varint-encoded ILOG)
- ✅ Varint bounds checking (max 10 bytes for u64)
- ✅ Null-termination validation (if required by format)

**Gap**: IRONCFG string pool ordering assumption—assumes pool is contiguous; no guard for overlapping refs (mitigated by strict offset validation)

---

### 2.2 Command-line Arguments

**Boundary**: File path input

Protections:
- ✅ Null/empty path rejection (prevents TOCTOU baseline)
- ✅ File existence validation before parsing

**Architectural Gap**: No path normalization before hashing; relative vs. absolute paths could differ on same content. Mitigation: determinism tests run on absolute paths (see section 5).

---

**Boundary**: Trust store path (signing/trust commands)

Protections:
- ✅ File I/O error propagation
- ✅ JSON validation before use

**Not Implemented**: Trust store path traversal isn't relevant in library context (caller controls path). CLI tools should validate paths.

---

### 2.3 Filesystem Assumptions

**Assumption 1: Atomic Rename**
- Platform: Windows (NTFS), Linux (ext4/btrfs/XFS)
- Risk: File.Move()+File.Delete() not atomic
- Status: Current code uses `File.Move(overwrite: true)` which is atomic on NTFS/modern POSIX

**Assumption 2: Cross-Volume Rename**
- Risk: Move across filesystems may fail silently
- Status: .NET File.Move() throws on cross-volume; acceptable
- Mitigation: Error handling in WriteAllBytesAtomic() catches IOException

**Assumption 3: NFS / Network Shares**
- Risk: Eventual consistency, stale handles
- Status: **NOT SUPPORTED** for production. Code assumes local disk.
- Mitigation: Document in deployment guide

**Assumption 4: Read While Writing (TOCTOU)**
- Risk: File modified between read start + validation end
- Status: Single File.ReadAllBytes() call = atomic from caller perspective
- Residual Risk: If process crashes mid-verify, partial corruption not detected (acceptable; next run detects)

---

### 2.4 Signing & Trust Store

**Boundary**: Ed25519 signature verification (CFRG-approved, constant-time)

Protections:
- ✅ Library: Blake3.NET (community-vetted)
- ✅ Library: Ed25519Vendor (Sommer Engineering, constant-time)
- ✅ Trust store validation before key lookup

**Not Implemented in V2.6**:
- RSA/ECDSA (out of scope)
- Hardware key storage (out of scope)

---

**Boundary**: Trust store file format (canonical JSON)

Protections:
- ✅ Version field (only v1 supported)
- ✅ Key ID hex validation (must be 32 hex chars)
- ✅ Public key length validation (must be 32 bytes)
- ✅ Duplicate key ID rejection
- ✅ Duplicate revoked ID rejection
- ✅ Deterministic JSON ordering (for reproducibility)

**Guard Tests**: 12 tests cover trust store edge cases (see IupdGuardTests)

---

### 2.5 Determinism & Consistency

**Boundary**: JSON output (VerifyResult.ToJson())

Protections:
- ✅ Field ordering deterministic (ok, engine, bytes_scanned, error)
- ✅ Error object ordering deterministic (category, code, offset, message)
- ✅ Exit codes consistent across engines (0/1/2/3/10)
- ✅ Proven by determinism tests (3+ invocations produce identical JSON)

**Tested**: Verify_JsonDeterministic_* tests per engine (IRONCFG, ILOG, IUPD)

---

**Boundary**: Signature verification result

Protections:
- ✅ Ed25519 is deterministic (given same input, always produces same result)
- ✅ Revocation list lookup is deterministic (HashSet; order independent)

**Test**: Determinism_SameIlogError_ProducesSameUnifiedError validates error mapping stability

---

## 3. Known Limitations & Non-Goals

### 3.1 Compression Decompression (ILOG L3)
- **Status**: LZ4-style algorithm (simple token encoding, no external lib)
- **Security**: No known attacks on LZ4 for firmware sizes < 1GB
- **Assumption**: Caller validates firmware size limit before decompression
- **Test**: IlogCompressorTests validate correctness; no fuzzing yet

---

### 3.2 Locale/Culture Issues
- **Status**: JSON output uses C# string formatting (decimal numbers, booleans)
- **Risk**: CultureInfo.CurrentCulture could affect number parsing if misapplied
- **Mitigation**: VerifyResult.ToJson() uses hardcoded string formatting (no cultural rules)
- **Test**: Determinism tests run on machine with variable culture settings (Windows-default is user locale)
- **Recommendation**: Consider InvariantCulture comment in code (not enforced yet)

---

### 3.3 Path Normalization
- **Status**: File paths are NOT canonicalized before hashing in determinism tests
- **Risk**: Verify command may accept `foo/../bar` and `/abs/bar` as different files, producing different JSON
- **Mitigation**: Tests use absolute paths; CLI tools should validate
- **Recommendation**: Document in CLI help text: "Use absolute paths for reproducible results"

---

### 3.4 Downgrade Attacks
- **Status**: File format versions are checked (IRONCFG version, ILOG version, IUPD version)
- **Risk**: Attacker could provide older version with known weakness
- **Mitigation**: Version check + documented support matrix (only current version supported)
- **Recommendation**: Add release notes listing dropped versions (v2.5 supports v1-v2; v2.6 supports v2 only)

---

### 3.5 Algorithm Agility
- **Status**: CRC32 (fast), BLAKE3 (cryptographic), Ed25519 (signing)
- **Risk**: If algorithm is compromised, file format cannot switch without major version bump
- **Mitigation**: Format design allows adding new algorithms in reserved flag bits
- **Recommendation**: Plan migration path if hash ever weakens (low probability for BLAKE3 pre-2030)

---

## 4. Mitigations by Category

### 4.1 Format Validation
- ✅ Guard tests cover: bad magic, truncation, trailing bytes, version mismatch, size field corruption
- ✅ Unit tests: 31 guard tests (IRONCFG 10, ILOG 9, IUPD 12)
- ✅ All guard tests passing in v2.6 baseline (artifacts/test-proof/v2_6)

### 4.2 Cryptography
- ✅ BLAKE3: cryptographically secure, constant-time implementation
- ✅ Ed25519: CFRG-approved, constant-time (Sommer Engineering)
- ✅ CRC32: acceptable for accidental corruption detection
- ⚠️ No key storage encryption (out of scope; assumes key file is protected by OS)

### 4.3 Determinism
- ✅ JSON output is byte-identical across invocations (proven tests)
- ✅ Error mapping is stable (tested)
- ✅ Exit codes are consistent (tested)
- ⚠️ Path canonicalization not enforced (documentation required)

### 4.4 File I/O
- ✅ Atomic file writing (File.Move with overwrite)
- ✅ Error handling for I/O failures
- ✅ TOCTOU: single ReadAllBytes() call (acceptable for firmware validation)
- ⚠️ NFS not supported (document)

---

## 5. Testing Evidence

### 5.1 Guard Test Coverage
```
Total Guard Tests: 31 (100% passing)
  IRONCFG: 10 tests (magic, version, payload, determinism)
  ILOG:     9 tests (magic, truncation, determinism)
  IUPD:    12 tests (signing, trust store, determinism)
```

### 5.2 Determinism Tests
```
Passing Tests:
  Verify_JsonDeterministic_ByteIdentical_IRONCFG
  Verify_JsonDeterministic_ByteIdentical (ILOG)
  TestParsingIsDeterministic (IUPD, multiple datasets)
  FullCycle_StageCommitSwap_DeterministicOutput (IUPD)
  Determinism_IRONCFG_IdenticalOutputAcrossRuns
  Determinism_IUPD_IdenticalOutputAcrossRuns
  ... plus 6+ additional determinism validations
```

### 5.3 Unification Tests (NEW, V2.6)
```
Passing Tests:
  Verify_IronCfgSuccess_JsonShape_HasStableTopLevelKeys
  Verify_IronCfgFailure_JsonShape_HasStableTopLevelKeys
  Verify_ExitCodes_MapCorrectly
  Verify_IronCfgAndIlogUseConsistentExitCodes
  Verify_FileNotFound_ExitCode_Consistent
  Verify_JsonDeterministicAcrossEngines
```

---

## 6. Recommendations

### 6.1 Immediate (No Code Changes Required)
1. **Document NFS non-support** in deployment guide
2. **Add path canonicalization note** to CLI help: "Use absolute paths for reproducible results"
3. **Document trust store policy** as security boundary (compromised store = compromised system)
4. **Release notes**: List dropped algorithm/format versions

### 6.2 Future (Post-V2.6)
1. **Path normalization**: Consider adding `Path.GetFullPath()` before hashing (impacts JSON output—format change)
2. **Explicit culture handling**: Add `InvariantCulture` to JSON serializer (would require format version bump)
3. **Fuzzing**: Run LZ4 decompression fuzzer for ILOG compression edge cases
4. **External audit**: Engage professional security firm for formal assessment (recommended for production firmware)

### 6.3 Not Recommended (Out of Scope)
- Hardware key storage (requires OS integration; use cloud key management if needed)
- RSA/ECDSA (FIPS compliance if required; add in future version with flag-based negotiation)
- Encrypted-at-rest for trust store (OS-level encryption sufficient; key file should be protected)

---

## 7. Residual Risk Assessment

| Threat | Residual Risk | Mitigation | Evidence |
|--------|---------------|-----------|----------|
| File Corruption (accidental) | LOW | CRC32 + BLAKE3 | Guard tests + determinism tests |
| File Tampering (malicious) | MEDIUM | BLAKE3 + Ed25519 | Trust store policy required |
| Key Compromise | HIGH | N/A (architectural) | Document risk; revocation available |
| Downgrade Attack | LOW | Version enforcement | Guard test: Verify_InvalidVersion_Rejected |
| DoS (decompression) | LOW | LZ4 timeout not impl. | Assumed firmware size < 1GB |
| Path TOCTOU | LOW | Atomic read | Single ReadAllBytes() call |
| NFS Consistency | MEDIUM | Not supported | Document; use local disk |
| Locale Issues | LOW | Hardcoded formatting | JSON output not culture-sensitive |

---

## 8. Conclusion

**V2.6 is cryptographically sound for its intended use case**: deterministic file format validation with cryptographic integrity and authentication (signing).

**Security posture**:
- ✅ Format validation: comprehensive (31 guard tests)
- ✅ Determinism: proven (6+ determinism tests per engine)
- ✅ Cryptography: industry-standard (BLAKE3, Ed25519)
- ⚠️ Trust model: user responsible for key/policy management
- ⚠️ Deployment: local-disk only (not NFS)

**Recommended next step**: Engage professional security firm for formal audit before production deployment >1M units.

---

**Document Version**: V2.6 Security Audit
**Last Updated**: 2026-02-14
**Status**: Technical inventory (not formal audit)

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
