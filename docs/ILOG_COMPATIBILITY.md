# ILOG Compatibility and Versioning

**Status**: v1 specification (no versioning beyond v1 yet)
**Last Updated**: 2026-03-14

---

## Version Rules

**Current Version**: 0x01
**Version Field**: Byte 4 of file header (single byte u8)
**Scope**: Entire ILOG format specification

**Version Semantics**:
- Version defines the block structure, profile flags, and validation rules
- Version changes are NOT backward compatible unless explicitly defined
- Unknown versions are rejected with ILOG_ERR_UNSUPPORTED_VERSION
- No version upgrades in-place (cannot convert v1 to future version)

---

## Backward Compatibility (v0 â†’ v1)

**Not Applicable** (v1 is first released version)

---

## Forward Compatibility (v1 â†’ future)

**Rules for Future Versions**:
1. New versions must define their own block structures
2. Readers must explicitly handle version differences
3. v1 readers reject version > 0x01
4. No automatic upgrade or fallback

**Recommendation**: If extending ILOG beyond v1, consider:
- New format version (separate from v1)
- Migration tool (v1 â†’ v2 converter if needed)
- Clear version boundary (no mixed-version files)

---

## Profile Compatibility

**Profile Independence**:
- Each profile (MINIMAL, INTEGRITY, SEARCHABLE, ARCHIVED, AUDITED) is fully independent
- Readers must support all 5 profiles
- Encoders may produce any profile

**Cross-Profile Restrictions**:
- Cannot combine L2 (SEARCHABLE) + L3 (ARCHIVED) in same file (invalid flags)
- Cannot have L2 without L1, or L3 without L1, etc. (structural rules)
- File is either one profile or none (invalid)

**Invalid Flag Combinations** (fail on read):
```
Flags   Profile        Valid?
------  ---------------  ------
0x01    MINIMAL            âś“
0x03    INTEGRITY          âś“
0x09    SEARCHABLE         âś“
0x11    ARCHIVED           âś“
0x27    AUDITED            âś“
0x02    (L3 without L1?)   âś— FAIL
0x04    (L2 without L1?)   âś— FAIL
0x05    (L4 without L1?)   âś— FAIL
0x0B    (L2 + L3?)         âś— FAIL
0x1B    (L3 + L4 only?)    âś— FAIL (invalid for AUDITED)
```

---

## Reader Compatibility

**Reader Requirements**:
1. Must accept files with any valid profile
2. Must reject files with invalid flag combinations
3. Must fail deterministically on corruption
4. Must use fail-closed semantics (reject on any error)

**Reader Behavior on Unsupported Features**:
- Unknown version: ILOG_ERR_UNSUPPORTED_VERSION
- Unknown block type: ILOG_ERR_UNSUPPORTED_BLOCK_TYPE
- Invalid flags: ILOG_ERR_INVALID_FLAGS
- No graceful degradation (fail hard)

---

## Encoding Compatibility

**Encoder Requirements**:
1. May produce any of the 5 profiles
2. Must enforce correct block structure per profile
3. Must set flags correctly (no extra bits)
4. Must create valid files that readers can verify

**Encoder Validation** (in IlogEncodeOptions):
- AUDITED profile requires both private and public keys
- Other profiles do not allow signatures
- No mixing of integrity methods (CRC32 XOR BLAKE3 per profile)

---

## Wire Format Stability

**Stable Elements** (must never change):
- Magic: 0x474F4C49 ("ILOG")
- File header structure (16 bytes)
- Block header structure (72 bytes)
- Block type IDs (0x01=L0, 0x02=L1, etc.)
- Endianness (little-endian only)
- CRC32 polynomial (0xEDB88320)
- BLAKE3 algorithm (standard Blake3)
- Ed25519 algorithm (standard Ed25519)

**Unstable Elements** (may change in future versions):
- Compression algorithm (L3)
- Index structure (L2)
- Signature algorithm (L4 for v2+, but not v1)
- Block payload formats (within version)

---

## Compression Compatibility (L3 ARCHIVE)

**Current Algorithm**: LZ4 + LZ77 hybrid (compress_version 0x01)
**Backward Compatibility**: No prior versions exist

**Future Compression Versions**:
- Must be signaled by compress_version in L3 payload
- Unknown compress_versions fail with error
- Cannot mix versions in same file

---

## Cryptography Compatibility

**Ed25519 (AUDITED Profile)**:
- Algorithm: Standard Ed25519 (RFC 8032)
- Key size: 32 bytes (seed for private, public)
- Signature size: 64 bytes
- No algorithm versioning in v1

**BLAKE3**:
- Algorithm: Standard BLAKE3
- Hash size: 32 bytes
- No algorithm versioning in v1

**CRC32**:
- Polynomial: IEEE 0xEDB88320 (standard)
- No algorithm versioning in v1

**If Crypto Algorithms Change in v2**:
- Must define new version (not supported in v1)
- Cannot upgrade in-place

---

## Multi-Block Log Files

**Witness Chain (AUDITED Profile Only)**:
- Each AUDITED block contains prev_seal_hash (previous block's BLAKE3)
- Enables verification of segment ordering
- First block has prev_seal_hash = 0x00...00

**Single-Block Assumption** (v1):
- Current implementation creates one file per logical log
- Designed for future extension to append-only logs

---

## Timestamp Semantics

**Timestamp Field** (L0.timestamp_epoch):
- Value: UTC milliseconds since Unix epoch (Jan 1, 1970 00:00:00 UTC)
- Range: 0 to 2^63-1 (signed i64 in encoding, unsigned u64 in spec)
- Deterministic Mode: Timestamp = 0 when IRONFAMILY_DETERMINISTIC=1

**Backward Compatibility**:
- All timestamp values valid (no range restriction)
- Future: If timezone semantics change, new field needed
- NOT: No versioning for timestamp format in v1

---

## Event Count Semantics

**Event Count Field** (L0.event_count):
- Value: u32LE (0 to 4,294,967,295)
- Meaning: Number of events or records (semantics app-defined)
- Validation: No strict validation (opaque byte stream)

**Compatibility Note**:
- Readers must not assume event_count matches actual records in event_data
- Writers must set event_count to actual logical event count
- Future: If event structure becomes mandatory, new version needed

---

## Reserved Fields

**File Header Reserved** (bytes 6-7):
- Current value: 0x0000 (required)
- Validation: Must be 0x0000 (fail if not)
- Purpose: Reserved for future flags
- Compatibility: If future versions use these bytes, must increment file version

**L0 Payload Reserved** (within stream_version):
- Current value: 0x01
- Future: If stream format changes, increment stream_version

**L4 Seal Reserved** (in AUDITED):
- Reserved byte: 0x00
- Purpose: Reserved for future AUDITED flags
- If used in future, must increment seal_version

---

## Migration Path (if v2 defined)

**v1 â†’ v2 Conversion**:
1. Write new v2 file (new version byte)
2. Re-encode payload with v2 rules
3. Cannot in-place upgrade
4. Requires explicit conversion tool

**Recommendation**: Define v1 as stable format, separate v2 if major changes needed

---

## Specification Stability

**v1 Specification Lock**:
- ILOG_SPEC.md defines v1 format
- ILOG profiles are defined by the v1 specification and reader validation rules in this repository
- ILOG_COMPATIBILITY.md defines versioning rules

**Lock Date**: 2026-03-14
**Stability**: v1 format frozen; no changes until v2 revision

---

## Known Limitations / Unknowns

| Item | Status | Details |
|------|--------|---------|
| Multi-block witness chain | LIMITED | Defined for the format, intended for future multi-block use |
| Streaming decompression | NOT IMPLEMENTED | Full buffer only |
| Very large logs (>1 GB) | UNSPECIFIED | Practical deployment limits depend on implementation |
| Timestamp year 2038+ | SUPPORTED BY FORMAT | Uses a wide integer timestamp representation |
| Event count > 4B | IMPOSSIBLE | u32 limit in format |
| Future version migration | NOT DEFINED | Rules exist; tools not built |

---

## Deployment Recommendations

**For New Deployments**:
1. Use AUDITED profile for tamper-proof logs
2. Validate with strict validation mode
3. Expect v1 (fail on version != 0x01)
4. Store public keys separately from files (security)

**For Existing Systems**:
1. If upgrading from v0 (none exist): N/A
2. If reading legacy ILOG: Support all 5 profiles (v1 requirement)
3. If versioning beyond v1: Create new format, do not extend v1

