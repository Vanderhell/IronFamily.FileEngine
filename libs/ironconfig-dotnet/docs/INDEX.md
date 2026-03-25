# IronConfig V2.6.0-rc1 Documentation Index

**Navigation for IronConfig runtime core + format validation + signing/trust**

---

## Current Documentation (Single Source of Truth)

### Getting Started
- **[Testing Guide](TESTING.md)** — How to run tests, canonical CI command, expected results
  - Command: `dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"`
  - Expected: 265 passed, 6 skipped
  - Proof: artifacts/test-proof/v2_6/TEST_SUMMARY.md

### Engineering Contracts
- **[Runtime Contract](RUNTIME_CONTRACT.md)** — Determinism guarantees, JSON shape, exit codes
  - Deterministic output (byte-identical across runs)
  - Unified error model (17 categories)
  - Exit codes: 0/1/2/3/10

### Security & Audit
- **[Security Audit V2.6](SECURITY_AUDIT_V2_6.md)** — Technical threat model, attack surface, mitigations
  - Threat categories: tamper, replay, key compromise, TOCTOU, downgrade
  - Residual risk assessment
  - Cryptographic algorithms (BLAKE3, Ed25519, CRC32)

### Release Information
- **[Release Candidate V2.6](RELEASE_RC_V2_6.md)** — RC1 notes, feature completeness, deployment guide
  - What's included (runtime parity, guard tests, signing/trust)
  - Known limitations
  - Migration from V2.5
- **[Changelog V2.6](../CHANGELOG_V2_6.md)** — Release history and achievements

### Test Proof
- **[Canonical Test Baseline](../artifacts/test-proof/v2_6/TEST_SUMMARY.md)** — Authoritative test results
  - 265 tests passing (100%)
  - Guard tests: 31 (IRONCFG 10 / ILOG 9 / IUPD 12)
  - Determinism: proven

---

## What's Not Here (Archived)

- **V2.2 Release Proof** — Use V2.6 proof instead
- **Old Session Notes** — Available in docs/_archive if needed
- **Old Phase Plans** — Implementation complete; see CHANGELOG_V2_6 for summary

---

## Quick Facts

| Property | Value |
|----------|-------|
| Current Version | V2.6.0-rc1 |
| Release Date | 2026-02-14 |
| Test Status | 265 passed, 6 skipped (100% pass rate) |
| Guard Tests | 31 (all passing) |
| Format Stability | IRONCFG v1, ILOG v1, IUPD v2 |
| Backward Compatible | ✅ V2.6 reads V2.5 files |
| Git Tag | v2.6.0-rc1 |

---

## For Different Roles

**Testing/CI Engineer**: Start with [Testing Guide](TESTING.md)

**Security Reviewer**: Start with [Security Audit](SECURITY_AUDIT_V2_6.md)

**Product Manager**: Start with [Release RC](RELEASE_RC_V2_6.md)

**Implementation**: Start with [Runtime Contract](RUNTIME_CONTRACT.md)

---

**Last Updated**: 2026-02-14

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
