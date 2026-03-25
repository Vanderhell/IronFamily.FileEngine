> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONCFG Implementation Contract

**Version**: 1.0
**Date**: 2026-01-16
**Status**: Normative
**Scope**: All IRONCFG implementations (C99, .NET, future)
**References**: FAMILY_STANDARD.md, spec/IRONCFG.md, spec/IRONCFG_CANONICAL.md, spec/IRONCFG_VALIDATION.md, audits/ironcfg/PARITY_ANALYSIS.md

---

## 1. Contract Scope and Authority

### 1.1 Binding Nature

This contract is **binding** on all IRONCFG implementations (C99, .NET, Python, Rust, Go, or any future language).

**Implementations MUST obey** every rule in sections 2â€“8.

**Implementations MUST verify** compliance via:
- Static analysis (linters, code review)
- Dynamic analysis (test suites)
- ironcert certification gates
- Cross-language parity tests

### 1.2 Violations

Violation of any MUST rule:
- Blocks certification (ironcert certify fails)
- Requires breaking change (new magic number, new version)
- Requires public security advisory
- Disqualifies "production-ready" status

### 1.3 Philosophy

This contract exists to prevent:
- **Format drift** (implementations silently diverging)
- **Undefined behavior** (safety regressions)
- **Silent failures** (data loss or corruption)
- **Performance degradation** (DoS vulnerabilities)
- **Non-determinism** (same input, different output)

---

## 2. Memory Allocation Rules

### 2.1 Untrusted Input Size Handling (MUST)

All file values (sizes, counts, offsets) are **untrusted**. No allocation or operation MUST be based on untrusted values without prior validation.

**Rule (MANDATORY)**:
- BEFORE allocating memory: validate header fields
- BEFORE processing data: check limits against constants (section 2.4)
- BEFORE trusting a size: verify it is within bounds

**Forbidden Patterns (MUST NOT)**:
- `malloc(file_size)` without prior check `file_size <= 256 MB`
- `alloca(string_length_from_file)` without prior check
- `new byte[field_count]` where `field_count` is from untrusted source
- `Span(buffer, offset, size)` where `size` exceeds remaining buffer

**Required Patterns (MUST)**:
```c
// C99
uint32_t claimed_size = LEu32(header + 16);
if (claimed_size > MAX_SCHEMA_SIZE) return LIMIT_EXCEEDED;
if (claimed_size == 0) return INVALID_SCHEMA;
uint8_t *buffer = malloc(claimed_size);  // NOW it's safe
```

```csharp
// .NET
uint claimedSize = BitConverter.ToUInt32(header, 16);
if (claimedSize > MAX_SCHEMA_SIZE) return LimitExceeded;
if (claimedSize == 0) return InvalidSchema;
byte[] buffer = new byte[claimedSize];  // NOW it's safe
```

### 2.2 Pre-Computation from Header (MUST)

All allocation sizes MUST be computed from header before any allocation:

**Rule (MANDATORY)**:
1. Read header (64 bytes, always safe)
2. Validate all offsets and sizes (section 2.3)
3. Pre-compute total memory needed
4. Allocate once (or use stack for small buffers)
5. Process all blocks

**Forbidden Pattern (MUST NOT)**:
- Multiple allocations during parsing (realloc in loop)
- Allocation during data traversal (malloc per field)
- Unbounded growth (allocation size increases per element)

**Required Pattern (MUST)**:
```
allocate_total = 0
for each block:
  if block_offset > 0:
    allocate_total += block_size  // pre-computed from header
allocate(allocate_total)  // single allocation
```

### 2.3 Offset and Size Validation Before Use (MUST)

Before trusting ANY offset or size from file:

**Validation Sequence (MANDATORY)**:
1. `offset > 0 && offset < fileSize` (in bounds)
2. `size > 0 && size < fileSize` (non-zero, in bounds)
3. `offset + size <= fileSize` (no overflow, no extend-beyond)
4. `offset >= previous_offset + previous_size` (monotonic, no overlap)

**Forbidden**:
- Using offset before validation
- Using size before validation
- Assuming offset â‰Ą 64 (could be less)
- Assuming size â‰¤ fileSize - offset (could be equal)

### 2.4 Hard Allocation Limits (MUST)

Every implementation MUST enforce these limits before allocation:

| Resource | Limit | Enforcement Point | Error Code |
|----------|-------|-------------------|-----------|
| File size | 256 MB | Header validation | LIMIT_EXCEEDED |
| Schema block | 1 MB | Schema size validation | LIMIT_EXCEEDED |
| String pool | 1 MB | Pool size validation | LIMIT_EXCEEDED |
| Data block | 256 MB | Data size validation | LIMIT_EXCEEDED |
| Field count | 65,536 | Schema parsing | LIMIT_EXCEEDED |
| Array element count | 1,000,000 | Array parsing | LIMIT_EXCEEDED |
| String length | 16 MB | String parsing | LIMIT_EXCEEDED |

**Enforcement (MUST)**:
```c
// C99: validate before malloc
if (size > LIMIT) return LIMIT_EXCEEDED;
```

```csharp
// .NET: validate before new
if (size > LIMIT) return LimitExceeded;
```

### 2.5 Stack Usage Bounds (MUST)

Stack allocation MUST be bounded and deterministic:

**Rules**:
- `alloca()` or `stackalloc` only for fixed-size structures (< 4 KB)
- Never allocate on stack based on file value (use heap)
- Recursion depth MUST NOT exceed 32 (enforced during parsing)
- Temporary buffers on stack: max 4 KB per level

**Forbidden**:
- `alloca(buffer_size)` where `buffer_size` is from file
- `stackalloc byte[count]` where `count` is from file
- Deep recursion (>32 levels)

**Allowed**:
- `alloca(8)` for reading single integer
- `stackalloc byte[256]` for header parsing
- `stackalloc Span<byte>[32]` for recursion tracking

### 2.6 Memory Cleanup on Error (MUST)

On validation failure, all allocated memory MUST be freed:

**Rules**:
- No memory leaks on error path
- No dangling pointers left in structures
- No uninitialized memory returned to caller

**C99 Pattern (MUST)**:
```c
uint8_t *buffer = malloc(size);
if (buffer == NULL) return ARITHMETIC_OVERFLOW;

int result = parse(buffer);
free(buffer);  // Always freed, even on error
return result;
```

**.NET Pattern (MUST)**:
```csharp
using (var buffer = new MemoryPool<byte>().Rent(size)) {
  int result = Parse(buffer.Memory.Span);
  return result;
}  // Automatically freed
```

---

## 3. Performance Invariants

### 3.1 Time Complexity Guarantees (MUST)

Every operation MUST meet these complexity bounds:

| Operation | Max Complexity | Justification |
|-----------|----------------|---------------|
| validate_fast | O(1) | Header checks only |
| validate_strict | O(n) | Full file scan |
| encode | O(n) | Single pass |
| parse | O(n) | Single traversal |
| sort (schema) | O(n log n) | Standard sort |
| sort (string pool) | O(n log n) | Standard sort |

**Enforcement (MUST)**:
- No nested loops that would be O(nÂ˛) or worse
- No repeated full scans (scan once, cache results)
- No redundant sorting passes

### 3.2 Linear Scan Guarantee (MUST)

Parsing and validation MUST scan the file at most **once per block**:

**Rules**:
- Schema block: single parse pass (no re-scanning for field verification)
- String pool: single parse pass (no re-scanning for duplicate detection)
- Data block: single traversal (no revisiting nodes)

**Forbidden Pattern (MUST NOT)**:
```c
// BAD: O(nÂ˛)
for (int i = 0; i < field_count; i++) {
  for (int j = 0; j < field_count; j++) {
    if (fields[i].id == fields[j].id) {
      // duplicate detection
    }
  }
}

// GOOD: O(n)
for (int i = 1; i < field_count; i++) {
  if (fields[i].id <= fields[i-1].id) {
    // ascending check (already sorted by parsing)
  }
}
```

### 3.3 No Hidden Scans (MUST NOT)

Forbidden: operations that look O(1) but are actually O(n):

**Forbidden**:
- Calling `strlen()` inside loop (cumulative O(nÂ˛))
- Dictionary lookup that scans all keys (not O(1) hash table)
- Linear search for fieldId (not indexed)

**Required**:
- Pre-computed sizes (store length in header)
- Hash tables or arrays indexed by fieldId
- One-pass validation (no separate verification pass)

### 3.4 Memory Access Patterns (SHOULD)

Implementations SHOULD use efficient memory access:
- Sequential reads (cache-friendly)
- No random seeks (minimize I/O)
- Batch processing where possible
- Memory-mapping suitable for large files

**Not enforceable** (implementation choice), but discouraged:
- Reading same bytes multiple times
- Pointer chasing / fragmented access
- Excessive GC allocation during validation

---

## 4. Error Handling Rules

### 4.1 Fail-Fast Requirement (MUST)

On validation failure, MUST stop immediately:

**Rule (MANDATORY)**:
- Detect error â†’ return error code + offset
- Do NOT continue validation
- Do NOT attempt recovery
- Do NOT try "best effort" parsing

**Forbidden Pattern (MUST NOT)**:
```c
// BAD: Continues on error
if (crc_mismatch) {
  log_warning("CRC failed, continuing anyway");
  continue;  // WRONG
}

// GOOD: Fails fast
if (crc_mismatch) {
  return CRC32_MISMATCH;
}
```

### 4.2 Error Code Semantics (MUST)

Error codes (1-24) MUST have identical meaning across all implementations:

**Rule (MANDATORY)**:
- Code 1 always means TRUNCATED_FILE
- Code 2 always means INVALID_MAGIC
- Code X always means the same thing
- Error messages may vary (strings differ), but codes must match

**Enforcement**:
- All implementations use same enum/const values
- Parity tests verify C and .NET use same codes
- ironcert rejects mismatched error codes

### 4.3 Byte Offset Reporting (MUST)

Every error MUST include byte offset of problem:

**Rule (MANDATORY)**:
- Offset = byte position in file where error detected
- Offset MUST be exact (not "approximately here")
- Offset MUST be within [0, fileSize)
- Same file, same validation mode â†’ same offset in all implementations

**Enforcement**:
- Code review: verify offset calculation
- Parity tests: assert offset matches in C and .NET
- Golden test vectors: record expected offset

### 4.4 No Recovery or "Best Effort" (MUST NOT)

Forbidden: attempting to salvage partial data from corrupt files:

**Forbidden Patterns (MUST NOT)**:
- Skipping invalid fields and continuing
- Filling missing fields with defaults
- Truncating oversized strings instead of rejecting
- Using partial parse results
- "Partial validation" mode (either valid or invalid)

**Required**:
- All-or-nothing: file is valid or invalid, no middle ground
- No partial data returned on error
- Error prevents use of any decoded data

### 4.5 Logging vs API Error (MUST)

Logging (diagnostic) MUST NOT affect API behavior:

**Rule (MANDATORY)**:
- Error code returned = deterministic from file
- Log messages = may vary (localization, verbosity)
- Logging MUST NOT change validation result
- Logging MUST NOT suppress error codes

**Implementation**:
```c
// Logging is separate from API
validate_result_t result = validate_strict(file, size);
if (result.error_code != OK) {
  log_error("Validation failed: code=%d offset=%u",
            result.error_code, result.offset);
  return result;  // Same result regardless of logging
}
```

---

## 5. Determinism Protection Rules

### 5.1 Banned Sources of Non-Determinism (MUST NOT)

Operations that introduce non-determinism are **absolutely forbidden** in encode/validate:

| Source | Forbidden | Impact | Enforcement |
|--------|-----------|--------|-------------|
| gettimeofday(), time() | encode, validate | Different output per run | Code review |
| random(), rand() | encode, validate | Non-reproducible output | Code review, static analysis |
| Date.Now, DateTime.UtcNow | encode, validate | Time-dependent | Code review |
| Randomized hashing | dict/set iteration | Non-deterministic order | Use sorted containers |
| Uninitialized memory | output bytes | Garbage in result | Initialize all data |
| Floating-point rounding | intermediate math | Platform-dependent results | Use explicit canonicalization |
| Locale-aware operations | string comparison | LC_COLLATE affects order | Use byte-order comparison |
| Environment variables | size decisions | Host-dependent behavior | Use only file-declared sizes |
| Thread-local state | encoding decisions | Race conditions | Use immutable inputs |

**Enforcement**:
- Static analysis: grep for gettimeofday, random, time() calls
- Code review: flag any non-deterministic operations
- Determinism test: encode 3Ă—, verify identical bytes

### 5.2 Determinism Test Obligation (MUST)

Every implementation MUST pass determinism test:

```
for i in 1..3:
  output[i] = encode(input)
assert output[1] == output[2] == output[3] (byte-for-byte)
```

**Enforcement**:
- Unit test (mandatory, part of test suite)
- CI/CD gate: determinism test fails â†’ build fails
- ironcert: `ironcert certify <engine>` includes determinism test

### 5.3 Bitwise Reproducibility (MUST)

Two logical configurations MUST encode to bitwise identical files:

**Rule (MANDATORY)**:
- If logical config A â‰  logical config B â†’ encoded bytes differ
- If logical config A = logical config B â†’ encoded bytes IDENTICAL
- No "essentially the same" - bytes MUST match exactly

**Enforcement**:
- Round-trip test: parse â†’ encode â†’ parse â†’ identical
- Golden vectors: expected bytes pre-computed and verified
- Parity test: C and .NET produce identical bytes

### 5.4 Uninitialized Memory Prohibition (MUST)

All output bytes MUST be explicitly written:

**Rule (MANDATORY)**:
- No uninitialized memory in output buffer
- All header fields explicitly set
- All padding (if any) explicitly zeroed
- CRC/BLAKE3 computed and stored

**Enforcement**:
- Code review: verify no "skip" of field writes
- Valgrind/MemorySanitizer: detect uninitialized reads
- Hex dump comparison: no random bytes in output

### 5.5 VarUInt Canonical Encoding (MUST)

All VarUInt values MUST use minimal byte representation:

**Rule (MANDATORY)**:
- Value 0-127 â†’ 1 byte
- Value 128-16383 â†’ 2 bytes
- No non-minimal encodings (0x80 0x00 forbidden for 0)

**Enforcement**:
- Encoder: never emit non-minimal VarUInt
- Validator: reject non-minimal VarUInt with NON_MINIMAL_VARUINT
- Golden vectors: pre-computed bytes verify this

### 5.6 Float Canonical Form (MUST)

All floating-point values MUST be canonical:

**Rules (MANDATORY)**:
- -0.0 normalized to +0.0 before encoding
- NaN forbidden (encoder never emits, validator rejects)
- Infinity allowed (Â±Inf encoded as-is)
- Bit pattern identical across all encodes

**Enforcement**:
- Encoder: check for NaN before writing, normalize -0.0 to +0.0
- Validator: reject NaN with INVALID_FLOAT
- Test vectors: float test values include edge cases

---

## 6. Breaking Change Policy

### 6.1 Semantic Versioning for Formats (MUST)

Breaking changes require format version or magic number change:

**What is BREAKING** (requires new magic or version):
- Change binary layout (add field, move offset, remove block)
- Change interpretation of existing bytes (redefine type 0x10)
- Tighten validation (make valid file invalid)
- Change error codes (renumber or redefine)
- Change limits (new limit < old limit)
- Change canonical rules (different canonical form)

**What is NOT BREAKING**:
- Relax validation (make invalid file valid) â€” backward compatible
- Add new optional features (new type codes) â€” if new magic used
- Improve performance â€” output identical, API compatible
- Better error messages â€” same error codes
- Add diagnostic logging â€” API result identical
- Support new languages â€” same binary format

### 6.2 Magic Number and Version Bumps (MUST)

Incompatible changes MUST increment magic or version:

**Magic Number Change** (required when):
- Format fundamentally incompatible
- Example: IRONCFG v1 â†’ new format uses "ICF2" magic

**Version Field Bump** (allowed only if):
- Same magic
- Defined forward/backward compatibility matrix
- Existing parsers can ignore new fields
- Example: BJV2 v1 â†’ BJV2 v2 (if ever needed)

**Current Policy**:
- IRONCFG = magic "ICFG", version 1
- Any breaking change â†’ new engine (e.g., "ICG2") or version 2 spec

### 6.3 Format Stability Covenant (MUST)

IRONCFG v1 is **stable**:

**Covenant (MANDATORY)**:
- Binary format locked (no changes without new magic)
- Error codes locked (no renumbering)
- Limits locked (same enforcement everywhere)
- Canonical rules locked (same output bytes)

**Post-Certification**:
- Breaking change requires new magic number
- Old magic remains supported indefinitely
- New version can be developed in parallel

### 6.4 Allowed Evolution Without Breaking (SHOULD)

Implementations MAY improve without breaking compatibility:

**Improvements Allowed**:
- Add BLAKE3 (optional, no breaking change)
- Add external schema support (optional, no breaking change)
- Add compression wrapper (separate layer, no breaking change)
- Improve performance (same output, no breaking change)
- Better validation error messages (same codes, no breaking change)

**Improvements Disallowed**:
- Strict subset of current format (breaks backward compat)
- Different byte encoding (breaks determinism)
- Different error codes (breaks parity)
- Removal of required features

---

## 7. Test Obligations

### 7.1 Mandatory Test Coverage (MUST)

Every implementation MUST include:

#### 7.1.1 Unit Tests
- [ ] All 24 error codes detectable (one test per code)
- [ ] Golden vector round-trip (parse + encode â†’ identical)
- [ ] Determinism (encode 3Ă—, all identical bytes)
- [ ] Field sorting (lexicographic order verified)
- [ ] VarUInt encode/decode all values
- [ ] Float canonicalization (-0.0 â†’ +0.0)
- [ ] Float NaN rejection
- [ ] UTF-8 validation (valid and invalid sequences)
- [ ] Offset overflow detection
- [ ] CRC32 matches test vector (0xCBF43926 for "123456789")
- [ ] BLAKE3 matches reference (if implemented)
- [ ] String pool ordering
- [ ] Recursion depth limit
- [ ] All hard limits (field count, array size, string length)

#### 7.1.2 Integration Tests
- [ ] All golden vectors (small, medium, large, edge cases)
- [ ] All malformed vectors (invalid magic, truncation, corruption)
- [ ] External schema (if supported)
- [ ] Roundtrip consistency (decode â†’ encode â†’ decode)

#### 7.1.3 Parity Tests (for C and .NET)
- [ ] Same input â†’ same output bytes (encode)
- [ ] Same file â†’ same error codes (validate)
- [ ] Same file â†’ same error offsets (validate)
- [ ] Same file â†’ identical parsed data (decode)

### 7.2 ironcert Certification Gates (MUST)

All implementations MUST pass ironcert before deployment:

```bash
ironcert certify ironcfg
```

**Gates (MANDATORY)**:
- [ ] Spec compliance: format matches spec/IRONCFG.md
- [ ] Canonical compliance: outputs match spec/IRONCFG_CANONICAL.md
- [ ] Validation: passes spec/IRONCFG_VALIDATION.md tests
- [ ] Parity: C and .NET outputs match (if both implemented)
- [ ] Golden vectors: all decode/encode correctly
- [ ] Determinism: encode 3Ă—, all identical
- [ ] Corruption detection: single-bit flip detected
- [ ] Performance: validate_fast < 1 ms, validate_strict < 100 ms per MB

**Exit Status**:
- Exit 0: all gates passed, implementation certified
- Exit 1: any gate failed, implementation rejected

### 7.3 Regression Test Suite (MUST)

Maintain golden vectors that never change:

**Vectors (Immutable)**:
- Small, medium, large configs
- All type combinations
- Edge cases (nulls, unicode, floats)
- Malformed files (test error detection)

**Golden Manifest**:
- `vectors/small/ironcfg/manifest.json`
- Lists all vectors with:
  - Expected file size
  - Expected CRC32
  - Expected BLAKE3
  - Expected error code (for malformed)

**Change Control**:
- Golden vectors LOCKED (changes only with new magic/version)
- Changes to vectors require justification (format change)
- Version history maintained in git

### 7.4 Fuzzing Requirements (SHOULD)

For maximum safety, implementations SHOULD include:

- Fuzz harness (input: random bytes, validate + parse)
- Corpus (test cases from golden vectors + mutations)
- Crash detection (no SIGSEGV, SEGFAULT, exception escapes)
- Regression test: replay corpus after fixes

**Not mandatory** (SHOULD, not MUST), but highly recommended.

---

## 8. Implementation Checklist

Use this checklist for code review and certification:

### 8.1 Memory Safety (MUST)
- [ ] No unbounded allocation based on file values
- [ ] All sizes validated before use
- [ ] Stack allocation fixed-size only
- [ ] All memory freed on error path
- [ ] No buffer overruns (bounds checked every read)
- [ ] No integer overflow in arithmetic
- [ ] No signed/unsigned conversion errors

### 8.2 Performance (MUST)
- [ ] validate_fast O(1) time
- [ ] validate_strict O(n) time
- [ ] No nested loops (O(nÂ˛) forbidden)
- [ ] No hidden scans (strlen in loop, dict lookup, etc.)
- [ ] Single pass through each block
- [ ] Sorting O(n log n) standard library

### 8.3 Error Handling (MUST)
- [ ] Fail-fast on first error
- [ ] Error codes 1-24 used correctly
- [ ] Byte offset calculated and reported
- [ ] No recovery or best-effort parsing
- [ ] No silent failures
- [ ] Logging separate from API errors

### 8.4 Determinism (MUST)
- [ ] No time-based operations (gettimeofday, time, Date.Now)
- [ ] No randomness (rand, Random, rng)
- [ ] No uninitialized memory in output
- [ ] Determinism test passes (encode 3Ă—)
- [ ] Bitwise identical output for same input
- [ ] All VarUInt minimal-encoded
- [ ] All floats canonical (-0.0 â†’ +0.0, NaN rejected)

### 8.5 Format Compliance (MUST)
- [ ] Magic "ICFG" correct
- [ ] Version 1 correct
- [ ] Reserved fields zero
- [ ] All offsets little-endian
- [ ] All field types correct
- [ ] All limits enforced
- [ ] CRC32 IEEE 802.3 correct
- [ ] BLAKE3 (if implemented) correct

### 8.6 Testing (MUST)
- [ ] All 24 error codes tested
- [ ] All golden vectors pass
- [ ] Determinism test passes
- [ ] Roundtrip test passes (decode + encode)
- [ ] Parity test passes (C vs .NET)
- [ ] Corruption detection test passes
- [ ] ironcert certify passes

### 8.7 Documentation (MUST)
- [ ] README explains capabilities
- [ ] API documented (functions, parameters, return)
- [ ] Error codes documented (each code explained)
- [ ] Build instructions provided
- [ ] Test instructions provided
- [ ] Known limitations listed

---

## 9. Enforcement Mechanisms

### 9.1 Code Review (MANUAL)

Before merging to main:
- [ ] All MUST rules verified manually
- [ ] No forbidden patterns found
- [ ] Performance analysis (no O(nÂ˛))
- [ ] Memory safety reviewed
- [ ] Determinism mechanisms present

### 9.2 Static Analysis (AUTOMATED)

CI/CD runs:
- Linters (C: clang-tidy, .NET: FxCop)
- Type checkers (C: sparse, .NET: Roslyn)
- Security scanners (C: clang-analyzer, .NET: Roslyn analyzers)
- Undefined behavior detectors (Valgrind, MemorySanitizer)

**Must pass before PR merge**:
- No critical warnings
- No undefined behavior detected
- No dangerous patterns found

### 9.3 Dynamic Testing (AUTOMATED)

Every commit runs:
- All unit tests (24+ error codes, golden vectors, edge cases)
- All integration tests (all vector categories)
- Determinism test (encode 3Ă—, all identical)
- Parity test (C and .NET match)
- Fuzzing (if available)

**Must pass**:
- 100% of tests pass
- Determinism verified
- Parity verified

### 9.4 ironcert Certification (AUTOMATED)

Before release:
```bash
ironcert certify ironcfg
```

**Must pass all gates**:
- Spec compliance
- Canonical compliance
- Golden vector round-trip
- Determinism
- Corruption detection
- Performance

**No exceptions** â€” if any gate fails, implementation cannot be released.

---

## 10. Long-Term Maintenance Rules

### 10.1 Format Stability (MUST)

IRONCFG v1 is **locked**:
- Binary format never changes (same bytes for same config)
- Error codes never change (same codes for same errors)
- Limits never change (same enforcement everywhere)

**If enhancement needed**:
- Use new magic number (e.g., "ICG2")
- Create new engine specification
- Maintain IRONCFG v1 indefinitely

### 10.2 Version Deprecation (SHOULD NOT)

IRONCFG v1:
- MUST be supported forever
- MUST NOT be deprecated (indefinite backward compatibility)
- MAY be superseded by v2 (v1 still supported)

**Sunset Policy**:
- No hard deprecation date
- Security fixes for v1 indefinite
- New features in v2+ (v1 not updated)

### 10.3 Compatibility Matrix (MUST)

Maintain official compatibility matrix:

**File**: `audits/ironcfg/COMPATIBILITY_MATRIX.md` (future)

| Implementation | Magic | Version | Status | Date |
|---|---|---|---|---|
| C99 (ref) | ICFG | 1 | CERTIFIED | 2026-01-16 |
| .NET (ref) | ICFG | 1 | CERTIFIED | 2026-01-16 |
| Python (future) | ICFG | 1 | PLANNED | â€” |

### 10.4 Security Policy (MUST)

For security issues:
- [ ] Report to maintainers (private issue)
- [ ] Embargo period: 30 days
- [ ] Fix released to all implementations simultaneously
- [ ] CVE assigned (if severity high)
- [ ] Post-mortem published

---

## 11. Anti-Patterns and What NOT To Do

### 11.1 Forbidden Optimization Shortcuts

These "optimizations" WILL break parity or determinism:

| Pattern | Why Forbidden | Consequence |
|---------|---------------|-------------|
| Skip CRC32 for speed | Breaks error detection | Undetected corruption |
| Use OS CRC32 lib | May differ from IEEE 802.3 | Parity failure |
| Reuse string objects | Non-deterministic object identity | Memory/hash-based bugs |
| Cache values across invocations | State leaks between calls | Non-determinism |
| Use thread-local state | Race conditions | Non-deterministic output |
| Lazy initialization | Skips validation on first run | Inconsistent error detection |
| Compressed output | Different bytes, extra decode step | Format violation |
| Truncate long strings | Data loss | Validation failure |
| Estimate sizes | Approximation errors | Bounds violations |
| Use default allocators | May fail silently | Undetected OOM |

### 11.2 Forbidden API Shortcuts

These API patterns WILL cause maintenance issues:

| Pattern | Why Forbidden | Consequence |
|---------|---|---|
| Return multiple errors | Breaks first-error semantics | Non-deterministic reporting |
| Expose internal structures | Breaks encapsulation | Version upgrades break users |
| Allow configuration options | Breaks determinism (different configs) | Format divergence |
| Promise "fast enough" | Vague SLA | Unmet expectations |
| Silent degradation | Hides errors | Latent bugs |
| Partial results on error | Breaks all-or-nothing | Data loss |
| Locale-aware comparison | Breaks determinism | Platform-dependent output |
| Platform-specific behavior | Breaks parity | C vs .NET divergence |

---

## 12. Acceptance Criteria for Implementation

An implementation is acceptable if and only if:

- [ ] **All MUST rules obeyed** (sections 2-8)
- [ ] **All unit tests pass** (section 7.1.1)
- [ ] **All integration tests pass** (section 7.1.2)
- [ ] **All parity tests pass** (section 7.1.3, if C and .NET both exist)
- [ ] **ironcert certify passes** (section 7.2)
- [ ] **Determinism verified** (encode 3Ă—, identical bytes)
- [ ] **Code review passed** (section 9.1)
- [ ] **Static analysis clean** (section 9.2)
- [ ] **No anti-patterns** (section 11)
- [ ] **Documentation complete** (section 8.7)

**No exceptions** â€” all criteria must be met.

---

## References

- FAMILY_STANDARD.md â€” Family-wide requirements
- spec/IRONCFG.md â€” Binary format specification
- spec/IRONCFG_CANONICAL.md â€” Canonicalization rules
- spec/IRONCFG_VALIDATION.md â€” Validation model
- audits/ironcfg/PARITY_ANALYSIS.md â€” C99/.NET parity analysis
- vectors/small/ironcfg/README.md â€” Test vectors and golden set
- tools/ironcert/ â€” Certification tool
