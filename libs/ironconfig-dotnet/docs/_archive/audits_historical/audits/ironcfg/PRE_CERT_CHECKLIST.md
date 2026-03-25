> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONCFG Pre-Certification Checklist

**Version**: 1.0
**Date**: 2026-01-16
**Status**: Normative (gate for implementation phase entry)
**References**: All IRONCFG specifications and audit documents

---

## 1. Specification Coverage Verification

### 1.1 Specification Documents (MUST exist and be complete)

All of the following MUST be present, locked, and complete before implementation begins:

| Document | Location | Status | Verified |
|----------|----------|--------|----------|
| Binary Format Spec | spec/IRONCFG.md | LOCKED | â |
| Canonicalization Rules | spec/IRONCFG_CANONICAL.md | LOCKED | â |
| Validation Model | spec/IRONCFG_VALIDATION.md | LOCKED | â |
| C99/.NET Parity Analysis | audits/ironcfg/PARITY_ANALYSIS.md | LOCKED | â |
| Implementation Contract | audits/ironcfg/IMPLEMENTATION_CONTRACT.md | LOCKED | â |
| Test Vectors Plan | vectors/small/ironcfg/README.md | LOCKED | â |
| Tooling Contract | docs/family/IRONCFG_TOOLING.md | LOCKED | â |

**Verification Rule (MUST)**:
- Each document file exists and contains no TODOs or unfinished sections
- Each document uses normative language only (MUST/MUST NOT/SHOULD)
- Each document specifies rules that are testable

### 1.2 Spec Section to Test Mapping

#### 1.2.1 spec/IRONCFG.md Coverage

| Section | Normative Rule | Test Category | Test Count |
|---------|---|---|---|
| 2. File Structure | Header 64 bytes, magic "ICFG" | header_basic | 1 |
| 2.3 Flags | Flag bits 0-2 valid, 3-7 reserved | flags_validation | 3 |
| 2.4 Size Fields | All size fields consistent | size_consistency | 2 |
| 3.1 Type Support | All types 0-6 supported (null/bool/i64/u64/f64/string/bytes) | type_validation | 7 |
| 3.2 Numeric Canonicalization | Integer little-endian, float NaN forbidden, -0.0 normalized | numeric_canonical | 4 |
| 3.3 String Representation | UTF-8, no normalization, lexicographic sorting | string_canonical | 3 |
| 4.1 Schema Embedding | Schema mandatory, embedded or external flag | schema_modes | 2 |
| 4.2 Schema Block | fieldId ascending, fieldName sorted, all fields present | schema_validation | 4 |
| 5 String Pool | Strings sorted, no duplicates, optional | pool_validation | 3 |
| 6 Data Block | Object root, fieldCount matches, fieldId ascending | data_validation | 4 |
| 7 VarUInt | Minimal byte encoding, bounds checking | varuint_validation | 3 |
| 8 Integrity | CRC32 IEEE 802.3, BLAKE3 32-byte | integrity_validation | 2 |
| 9 Determinism | Canonical encoding, byte-identical output | determinism | 3 |
| 10 Limits | 256 MB file, 65536 fields, 1M arrays, 16 MB strings | limit_enforcement | 4 |

**Total spec/IRONCFG.md test coverage**: â‰Ą40 tests

#### 1.2.2 spec/IRONCFG_CANONICAL.md Coverage

| Section | Normative Rule | Test Category | Test Count |
|---------|---|---|---|
| 2 Header Rules | reserved0, reserved1, reserved2 all zero | canonical_header | 1 |
| 2.2 Flag Bits | Unknown flags rejected | canonical_flags | 1 |
| 2.3 Offset Invariants | Monotonic, no overlap, no overflow | canonical_offsets | 2 |
| 3 Schema Rules | fieldId ascending, fieldName lexicographic, no duplicates | canonical_schema | 3 |
| 4 VarUInt | Minimal encoding only, non-minimal rejected | canonical_varuint | 2 |
| 5 Data Types | Type codes fixed, null/bool/i64/u64/f64/string/bytes | canonical_types | 7 |
| 5.4 Float | NaN forbidden, -0.0â†’+0.0, Inf allowed | canonical_float | 4 |
| 5.5 String | UTF-8 valid, no normalization, pool sorted | canonical_string | 3 |
| 5.8 Object | All fields required, fieldId ascending, type match | canonical_object | 3 |
| 6 Integrity | CRC32 match, BLAKE3 match (if present) | canonical_integrity | 2 |

**Total spec/IRONCFG_CANONICAL.md test coverage**: â‰Ą28 tests

#### 1.2.3 spec/IRONCFG_VALIDATION.md Coverage

| Section | Normative Rule | Test Category | Test Count |
|---------|---|---|---|
| 2.1 validate_fast | O(1) header checks, fast pass/fail | validation_fast | 1 |
| 2.2 validate_strict | O(n) full validation, schema+data | validation_strict | 1 |
| 2.3 open_unsafe | No checks (internal only) | validation_unsafe | 0 |
| 3 Validation Order | 41-step sequence, first-error rule | validation_order | 1 |
| 4 Error Codes | 24 codes + OK, each detectable | error_codes | 24 |
| 4.1 Byte Offset | Offset reported for every error | error_offset | 5 |
| 5 Limits | 256 MB, 32 depth, 65536 fields, 1M arrays, 16 MB string | limit_validation | 5 |
| 5.2 DoS Policy | VarUInt max bytes, offset abuse blocked | dos_policy | 3 |
| 6 Corruption | Single-bit flip detected, truncation detected | corruption_detection | 3 |

**Total spec/IRONCFG_VALIDATION.md test coverage**: â‰Ą43 tests

#### 1.2.4 audits/ironcfg/PARITY_ANALYSIS.md Coverage

| Section | Normative Rule | Test Category | Test Count |
|---------|---|---|---|
| 2.1 Integer Types | Fixed-size types only (uint32_t, uint64_t) | parity_integers | 2 |
| 2.2 Signed/Unsigned | Unsigned only for offsets/sizes | parity_signedness | 2 |
| 2.3 Overflow Detection | Before operation, bounds check | parity_overflow | 3 |
| 2.4 VarUInt Overflow | Encoding overflow + value overflow detected | parity_varuint_ov | 2 |
| 3.1 IEEE 754 | 64-bit double, little-endian bytes | parity_float_repr | 2 |
| 3.2 NaN Detection | Bit-level, reject on parse | parity_nan | 2 |
| 3.3 -0.0 Normalization | -0.0â†’+0.0 before encode | parity_negzero | 2 |
| 3.4 Infinity | Allowed, canonical form | parity_infinity | 1 |
| 4.1 Pointer vs Span | Identical offset calculations | parity_memory | 2 |
| 4.2 Absolute Offsets | No relative offsets, validate before use | parity_offsets | 2 |
| 4.3 Little-Endian | Explicit conversion, both languages | parity_endian | 2 |
| 8.1 CRC32 IEEE | Polynomial 0xEDB88320, test vector | parity_crc32 | 1 |
| 8.2 BLAKE3 | Reference implementation match | parity_blake3 | 1 |

**Total audits/ironcfg/PARITY_ANALYSIS.md test coverage**: â‰Ą24 tests

#### 1.2.5 audits/ironcfg/IMPLEMENTATION_CONTRACT.md Coverage

| Section | Normative Rule | Test Category | Test Count |
|---------|---|---|---|
| 2.1 Untrusted Input | Validate before allocate | contract_safety | 3 |
| 2.2 Pre-Computation | All sizes from header before malloc | contract_allocation | 1 |
| 2.4 Hard Limits | Enforce all limits (256MB, 65536, 1M, 16MB) | contract_limits | 4 |
| 3.1 Time Complexity | validate_fast O(1), validate_strict O(n) | contract_complexity | 2 |
| 3.2 Linear Scan | Each block parsed once | contract_scan | 1 |
| 4.1 Fail-Fast | Errorâ†’return, no recovery | contract_error_handling | 2 |
| 5.1 Banned Operations | No time, random, locale | contract_determinism | 3 |
| 5.2 Determinism Test | Encode 3Ă—, identical | contract_det_test | 1 |
| 6 Breaking Changes | New magic/version for incompatible changes | contract_versioning | 1 |
| 7 Test Obligations | All 24 error codes, golden vectors, parity | contract_testing | 3 |

**Total audits/ironcfg/IMPLEMENTATION_CONTRACT.md test coverage**: â‰Ą21 tests

#### 1.2.6 docs/family/IRONCFG_TOOLING.md Coverage

| Section | Normative Rule | Test Category | Test Count |
|---------|---|---|---|
| 2.2 pack Command | Encode JSON to binary, CRC32/BLAKE3 flags | tooling_pack | 3 |
| 2.3 validate Command | Check file, fast/strict modes | tooling_validate | 2 |
| 2.4 dump Command | Decode to JSON, header/schema/stats/hash | tooling_dump | 4 |
| 3 Output Determinism | Same inputâ†’identical output | tooling_determinism | 3 |
| 3.2 JSON Format | Keys sorted, compact, no whitespace | tooling_json | 2 |
| 4 Exit Codes | 0=success, 1=validation fail, 2=arg error, 3=system error | tooling_exit | 4 |
| 5 Diagnostics | Error code + offset + description | tooling_diagnostics | 2 |
| 6 ironcert Integration | Command mapping, hooks, gates | tooling_ironcert | 3 |

**Total docs/family/IRONCFG_TOOLING.md test coverage**: â‰Ą23 tests

### 1.3 Cumulative Test Coverage Requirement

**Total mandatory tests across all specs**: â‰Ą179 tests

**Test categories must include**:
- Spec compliance: â‰Ą40 tests
- Canonicalization: â‰Ą28 tests
- Validation: â‰Ą43 tests
- Parity: â‰Ą24 tests
- Implementation contract: â‰Ą21 tests
- Tooling: â‰Ą23 tests

**Verification (MUST)**:
- Each test is in one category only
- No test counted twice
- Total â‰Ą179 individual, distinct tests

---

## 2. Test Suite Completeness

### 2.1 Golden Vector Requirements

Before implementation, golden vectors MUST be pre-generated:

| Vector Set | Purpose | Count | Location |
|---|---|---|---|
| Small vectors | Basic types, all required | 4 variants | vectors/small/ironcfg/small/ |
| Medium vectors | Mix of types, optional fields | 4 variants | vectors/small/ironcfg/medium/ |
| Large vectors | Dense data, arrays | 4 variants | vectors/small/ironcfg/large/ |
| Edge case vectors | Nulls, unicode, floats, arrays, limits | 6+ files | vectors/small/ironcfg/edge_cases/ |
| External schema | Schema file + data | 2+ variants | vectors/small/ironcfg/external_schema/ |
| Malformed vectors | Test all 24 error codes | 24+ files | vectors/small/ironcfg/malformed/ |

**Variant combinations** (for each config):
- v1: no CRC, no BLAKE3
- v2: CRC32, no BLAKE3
- v3: no CRC, BLAKE3
- v4: both CRC32 and BLAKE3

**Total golden vectors**: â‰Ą50 valid + â‰Ą24 malformed = â‰Ą74 vectors

**Manifest file** (MUST):
- Location: `vectors/small/ironcfg/manifest.json`
- Lists all vectors with checksums
- Records expected file size, CRC32, BLAKE3
- Machine-readable for ironcert

### 2.2 Determinism Test Requirements

Before implementation, prove determinism is testable:

**Test procedure** (MUST be automatable):
```
for each golden JSON source:
  encode(source) -> output1.icfg
  encode(source) -> output2.icfg
  encode(source) -> output3.icfg
  verify: output1 == output2 == output3 (byte-for-byte)
```

**Test count**: One per JSON source (â‰Ą12 JSON sources)

**Verification**: Test passes if all three encodes identical

### 2.3 Corruption Detection Test Requirements

**Test procedure**:
```
for each golden vector with CRC32:
  flip 1 bit at random offset (before CRC)
  validate(corrupted)
  verify: error code = CRC32_MISMATCH or structure error
```

**Coverage**: â‰Ą5 vectors with CRC32

**Verification**: >99% of single-bit flips detected

### 2.4 Round-Trip Test Requirements

**Test procedure**:
```
for each golden vector:
  parse(vector) -> config
  encode(config) -> re_encoded.icfg
  verify: re_encoded == vector (byte-for-byte)
```

**Coverage**: All â‰Ą50 valid vectors

**Verification**: 100% of vectors pass round-trip

### 2.5 Error Code Test Requirements

**Test procedure**:
```
for each error code 1-24:
  find malformed vector that triggers code
  validate(malformed)
  verify: returned error code matches expected
```

**Coverage**: All 24 error codes testable

**Verification**: Each code detectable via dedicated malformed vector

### 2.6 Bounds Test Requirements

**Test procedure**:
```
for each limit (256MB, 65536, 1M, 16MB):
  create test vector at limit boundary
  validate + parse
  verify: success or specific error code

for each limit, create test vector exceeding limit:
  validate
  verify: error code = LIMIT_EXCEEDED
```

**Coverage**: 4 limits Ă— 2 (at-boundary + exceeding) = 8 tests

**Verification**: All limits enforced

---

## 3. ironcert Certification Gates

### 3.1 Gate: validate_fast (MUST pass)

**Purpose**: Verify O(1) header-only validation works

**Test**:
```bash
ironcert validate ironcfg --fast <golden_vector>
```

**Expected Result**:
- Exit code 0 (success) for all valid vectors
- Exit code 1 for all malformed vectors
- Completes in <1 ms (timing optional)

**Requirement**: All â‰Ą50 valid vectors pass fast validation

### 3.2 Gate: validate_strict (MUST pass)

**Purpose**: Verify full canonical validation works

**Test**:
```bash
ironcert validate ironcfg --strict <golden_vector>
```

**Expected Result**:
- Exit code 0 for all valid vectors
- Exit code 1 for all malformed vectors
- Error code matches expected
- Byte offset calculated correctly

**Requirement**: All â‰Ą50 valid vectors pass strict validation

### 3.3 Gate: vectors (MUST pass)

**Purpose**: Verify all golden vectors are correct and reproducible

**Test**:
```bash
ironcert vectors ironcfg
```

**Expected Result**:
- Validate all vectors in manifest.json
- Verify CRC32 values
- Verify BLAKE3 values (if present)
- Report pass/fail for each vector

**Requirement**: All â‰Ą74 vectors pass (valid + malformed)

### 3.4 Gate: determinism (MUST pass)

**Purpose**: Verify encode produces byte-identical output on repeated invocation

**Test**:
```bash
for each JSON source:
  ironcert encode ironcfg <source.json> /tmp/out1.icfg
  ironcert encode ironcfg <source.json> /tmp/out2.icfg
  ironcert encode ironcfg <source.json> /tmp/out3.icfg
  verify: /tmp/out1 == /tmp/out2 == /tmp/out3
```

**Expected Result**:
- All three encodes produce byte-identical files
- CRC32 matches if present
- BLAKE3 matches if present

**Requirement**: 100% of JSON sources (â‰Ą12) pass determinism test

### 3.5 Gate: parity (MUST pass if both C and .NET implemented)

**Purpose**: Verify C99 and .NET implementations produce identical outputs

**Test**:
```bash
for each JSON source:
  ironcert encode ironcfg-c <source.json> /tmp/out_c.icfg
  ironcert encode ironcfg-net <source.json> /tmp/out_net.icfg
  verify: /tmp/out_c == /tmp/out_net (byte-for-byte)
```

**Expected Result**:
- C and .NET encode identical binaries
- C and .NET validate files identically (same error codes)
- C and .NET dump files identically (same text)

**Requirement**: 100% parity on all test vectors

### 3.6 Gate: corruption_detection (MUST pass)

**Purpose**: Verify single-bit flip detection via CRC32

**Test**:
```bash
for each golden vector with CRC32:
  flip 1 bit (before CRC)
  validate(corrupted) --strict
  verify: error code = CRC32_MISMATCH or structure error
```

**Expected Result**:
- >99% of flips detected (CRC32 polynomial undetected ~1 in 4B)
- Error reported with correct byte offset

**Requirement**: At least 5 vectors with CRC32, all pass

### 3.7 Gate: error_codes (MUST pass)

**Purpose**: Verify all 24 error codes are detectable and correct

**Test**:
```bash
for error_code in 1..24:
  find malformed vector for that code
  validate(malformed) --strict
  verify: returned code = expected code
  verify: returned offset is accurate
```

**Expected Result**:
- All 24 codes detectable
- Each code returned with correct byte offset
- Each code has dedicated malformed test vector

**Requirement**: 24 malformed vectors, all pass

### 3.8 Gate: round_trip (MUST pass)

**Purpose**: Verify parse + encode produces identical bytes

**Test**:
```bash
for each golden vector:
  dump(vector) -> JSON
  encode(JSON) -> re_encoded.icfg
  verify: re_encoded == vector (byte-for-byte)
```

**Expected Result**:
- All vectors pass round-trip
- CRC32 values match
- BLAKE3 values match

**Requirement**: 100% of â‰Ą50 valid vectors pass

### 3.9 Gate: kpi_export (SHOULD pass)

**Purpose**: Verify performance metrics can be exported

**Test**:
```bash
ironcert benchmark ironcfg
```

**Expected Output**:
- `audits/ironcfg/BENCH_<date>.json` with:
  - Encode MB/s
  - Decode MB/s
  - Validate fast MB/s
  - Peak memory
  - Allocation count
- `audits/ironcfg/BENCH_<date>.md` with table and methodology

**Requirement**: Benchmark report generated (not required to pass perf gates)

---

## 4. Evidence and Artifacts

### 4.1 Required Files Before Implementation (MUST exist)

**Specification Files**:
- [ ] `spec/IRONCFG.md` (locked, no TODOs)
- [ ] `spec/IRONCFG_CANONICAL.md` (locked, no TODOs)
- [ ] `spec/IRONCFG_VALIDATION.md` (locked, no TODOs)
- [ ] `audits/ironcfg/PARITY_ANALYSIS.md` (locked, no TODOs)
- [ ] `audits/ironcfg/IMPLEMENTATION_CONTRACT.md` (locked, no TODOs)
- [ ] `vectors/small/ironcfg/README.md` (locked, test plan)
- [ ] `docs/family/IRONCFG_TOOLING.md` (locked, no TODOs)

**Test Vector Files**:
- [ ] `vectors/small/ironcfg/manifest.json` (machine-readable index)
- [ ] `vectors/small/ironcfg/small/` (4 variants of simple config)
- [ ] `vectors/small/ironcfg/medium/` (4 variants of typical config)
- [ ] `vectors/small/ironcfg/large/` (4 variants of large config)
- [ ] `vectors/small/ironcfg/edge_cases/` (nulls, unicode, floats, arrays, limits)
- [ ] `vectors/small/ironcfg/external_schema/` (schema file + data)
- [ ] `vectors/small/ironcfg/malformed/` (24+ files, one per error code)

**Audit Files**:
- [ ] `audits/ironcfg/PRE_CERT_CHECKLIST.md` (this file)

**Tool Templates** (not required before cert, but good to have):
- [ ] `tools/ironcfg-pack/` (directory)
- [ ] `tools/ironcfg-validate/` (directory)
- [ ] `tools/ironcfg-dump/` (directory)

### 4.2 Required Test Infrastructure (MUST exist)

**Test Harness**:
- [ ] Unit test framework (C: Unity/cmocka/Criterion, .NET: xUnit/NUnit)
- [ ] Test runner for all 179+ tests
- [ ] CI/CD integration (GitHub Actions, GitLab CI, etc.)

**Golden Vector Validation**:
- [ ] Script to validate manifest.json
- [ ] Script to recompute checksums
- [ ] Script to detect vector corruption

**Parity Testing** (if both C and .NET):
- [ ] Cross-language test harness
- [ ] Identical input feeding to both implementations
- [ ] Byte-comparison of outputs

### 4.3 Required Documentation (MUST be complete)

**API Documentation**:
- [ ] All functions/methods documented
- [ ] All error codes documented
- [ ] All types documented

**Build Instructions**:
- [ ] C99: compiler flags, dependencies, build steps
- [ ] .NET: target framework, build command, dependencies

**Test Instructions**:
- [ ] How to run all tests
- [ ] How to run specific test categories
- [ ] How to add new tests
- [ ] How to regenerate golden vectors

---

## 5. Failure Policy and Escalation

### 5.1 Blocking Implementation (MUST NOT proceed if)

Implementation CANNOT begin if any of the following are true:

- [ ] Any spec document contains "TODO" or incomplete sections
- [ ] Any spec document uses non-normative language ("may", "could", "might")
- [ ] Test coverage < 150 tests (less than 85% of 179)
- [ ] Golden vectors incomplete (< 50 valid, < 24 malformed)
- [ ] Any mandatory ironcert gate undefined (validate_fast, validate_strict, vectors, determinism, parity, corruption, error_codes, round_trip)
- [ ] Error code mapping not complete (< 24 codes)
- [ ] Offset calculation rules not fully specified
- [ ] CRC32 or BLAKE3 algorithm not explicitly defined
- [ ] Exit code contract not defined
- [ ] Tooling contract not defined

**Action on block**: Stop all implementation work, return to spec phase, fix issue, re-verify this checklist.

### 5.2 Blocking Certification (MUST NOT certify if)

ironcert certification CANNOT pass if any of the following fail:

- [ ] Any ironcert gate does not pass (validate_fast, validate_strict, vectors, determinism, parity, corruption, error_codes, round_trip)
- [ ] Any test in 179+ test suite fails
- [ ] Determinism not proven (encode 3Ă—, not identical)
- [ ] Parity not proven (C and .NET differ)
- [ ] Error code test fails (any of 24 codes not detected)
- [ ] Byte offset test fails (offsets incorrect)
- [ ] CRC32 test vector fails (test "123456789" â‰  0xCBF43926)
- [ ] BLAKE3 test vector fails (if implemented)
- [ ] Round-trip test fails (>1% of vectors)
- [ ] Corruption detection insufficient (<99%)

**Action on block**: Investigate failure, fix implementation, re-run ironcert, must pass all gates before certification.

### 5.3 Spec Revision Required (MUST revise if)

A specification revision is required (new version, possibly new magic) if:

- [ ] Fundamental format change needed (layout, encoding, header)
- [ ] Error code semantics must change
- [ ] Limit changes required (no longer 256 MB, etc.)
- [ ] New mandatory type added
- [ ] Canonicalization rules change

**Action on revision**: Create new spec document (spec/IRONCFG_v2.md or new engine magic), restart pre-cert checklist for new version.

### 5.4 Implementation Fix Only (NO spec change)

No spec change required if:

- [ ] Bug in implementation (test passes with fix, no spec change)
- [ ] Performance improvement (same output, no spec change)
- [ ] Better error messages (same error codes, no spec change)
- [ ] New optional feature (new tool command, no core spec change)
- [ ] New language binding (same binary format, no spec change)

**Action on fix**: Implement fix, re-run tests, must pass all gates.

---

## 6. Pre-Certification Sign-Off

### 6.1 Verification Checklist (MUST all be TRUE)

Before declaring "ready to implement", verify:

| Criterion | Verified | Date | Signature |
|-----------|----------|------|-----------|
| All 7 spec documents locked and complete | â | ___ | ___ |
| All spec TODOs removed | â | ___ | ___ |
| All spec rules testable | â | ___ | ___ |
| Test coverage â‰Ą150 tests | â | ___ | ___ |
| Golden vectors â‰Ą50 valid + â‰Ą24 malformed | â | ___ | ___ |
| Manifest.json complete and valid | â | ___ | ___ |
| All 24 error codes testable | â | ___ | ___ |
| Offset calculation rules specified | â | ___ | ___ |
| CRC32 algorithm specified (0xEDB88320) | â | ___ | ___ |
| BLAKE3 algorithm specified (32 bytes) | â | ___ | ___ |
| Exit code contract specified | â | ___ | ___ |
| CLI command set specified | â | ___ | ___ |
| ironcert gates defined | â | ___ | ___ |
| Parity rules specified (C99 vs .NET) | â | ___ | ___ |
| Implementation contract specified | â | ___ | ___ |
| Test harness planned | â | ___ | ___ |
| Build instructions available | â | ___ | ___ |
| No unverifiable claims | â | ___ | ___ |

### 6.2 Governance Sign-Off (REQUIRED)

**Chief Architect** (binary format design):
- [ ] Sign-off on spec completeness
- [ ] Date: ___________
- [ ] Name/Signature: ___________________

**Lead Test Architect** (test coverage):
- [ ] Sign-off on test coverage
- [ ] Date: ___________
- [ ] Name/Signature: ___________________

**Lead Implementation Architect** (implementation feasibility):
- [ ] Sign-off on specification implementability
- [ ] Date: ___________
- [ ] Name/Signature: ___________________

**Certification Authority** (ironcert governance):
- [ ] Sign-off on certification gates
- [ ] Date: ___________
- [ ] Name/Signature: ___________________

### 6.3 Final Gate: Ready to Implement

**Declaration**: IRONCFG v1 is ready for implementation if and only if:

1. All 18 verification items checked (â)
2. All 4 governance sign-offs obtained
3. All spec documents locked
4. All test vectors generated
5. No blockers remain (section 5.1)

**If all conditions met**:
```
READY TO IMPLEMENT: IRONCFG v1.0 (2026-01-16)
Status: PRE-CERTIFIED
Next Phase: Implementation + ironcert Certification
```

**If any condition fails**:
```
NOT READY: Blockers exist (see section 5.1)
Action: Resolve blockers, re-verify this checklist
```

---

## 7. Certification Timeline

### 7.1 Phase 1: Implementation (after pre-cert sign-off)

**Duration**: Estimated 4-8 weeks (C99 + .NET parallel)

**Deliverables**:
- C99 implementation (pack, validate, dump)
- .NET implementation (pack, validate, dump)
- Unit test suite (â‰Ą179 tests)
- Documentation (API, build, test)

### 7.2 Phase 2: Validation & Testing

**Duration**: Estimated 2-4 weeks

**Deliverables**:
- All 179+ tests passing
- All error codes testable
- Determinism verified
- Parity verified (C vs .NET)
- Benchmark report

### 7.3 Phase 3: ironcert Certification

**Duration**: Estimated 1-2 weeks

**Deliverables**:
- ironcert certify passes all gates
- Certification report
- KPI export
- Public announcement

---

## References

- FAMILY_STANDARD.md
- spec/IRONCFG.md
- spec/IRONCFG_CANONICAL.md
- spec/IRONCFG_VALIDATION.md
- audits/ironcfg/PARITY_ANALYSIS.md
- audits/ironcfg/IMPLEMENTATION_CONTRACT.md
- vectors/small/ironcfg/README.md
- docs/family/IRONCFG_TOOLING.md
