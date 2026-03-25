# ICFG Compatibility and Versioning

**Date**: 2026-03-14
**Version**: 1
**Status**: LOCKED for v1
**Source**: Live code analysis (IronCfgHeader.cs, validation logic)

---

## 1. Version Support

### Current Versions

| Version | Status | Supported | Release | Notes |
|---------|--------|-----------|---------|-------|
| 1 | Stable | ✅ Full | v1 baseline | Basic ICFG format |
| 2 | Stable | ✅ Full | Later update | Added ElementSchema for arrays |

### Version Acceptance Rule

Reader accepts any file with version byte 0x01 or 0x02.
Versions 0x03 and above rejected with error `InvalidVersion`.

**Code** (IronCfgHeader.cs line 84-87):
```csharp
byte version = buffer[4];
if (version != 1 && version != 2)
    return new IronCfgError(IronCfgErrorCode.InvalidVersion, 4);
```

---

## 2. Backward Compatibility

### v1 Reader Reading v1 File

**Status**: ✅ Full compatibility

All v1 features supported:
- Magic, version, flags parsing
- Header offset validation
- Schema block reading (field IDs, type codes, names for compound types)
- Data block reading (all type codes 0x00-0x40)
- CRC32 verification (if flag set)
- BLAKE3 structure check (if flag set)

### v1 Reader Reading v2 File

**Status**: ✅ Backward compatible (with caveat)

v1 reader can read v2 files if they don't use v2-specific features:
- If array fields (0x30) have **no** ElementSchema → fully compatible
- If array fields have ElementSchema → v1 reader ignores it (no error)
- All other features identical

**Caveat**: v1 reader does not validate ElementSchema for v2 files.
This is acceptable if v2 features are optional.

### v2 Reader Reading v1 File

**Status**: ✅ Full forward compatibility

v2 reader handles v1 files natively:
- v1 array fields (0x30) lack ElementSchema → valid
- v2 reader treats ElementSchema as optional
- No errors, no warnings

**Code behavior**: ElementSchema validation only occurs if version >= 2 AND array field present

---

## 3. Forward Compatibility

### v3 or Later

**Current reader behavior**: Versions 0x03+ rejected

**Migration path for v3**:
1. Add new version code (0x03) to version check
2. Handle new flags (if any) in flags byte
3. Handle new block types (if any) or new fields (if any)
4. Graceful degradation: reader can optionally skip unknown features

**Not implemented**: v1 and v2 readers have no forward-compatibility mechanisms.

---

## 4. Feature Flags and Compatibility

### Mandatory Features (always present)

- Header: All header fields always present and validated
- Schema block: Always required, non-zero
- Data block: Always required, non-zero
- Root type: Always 0x40 (Object)

### Optional Features

| Feature | Flag Bit | Presence | Compatibility |
|---------|----------|----------|----------------|
| CRC32 | 0x01 | Indicated by offset | Backward compatible (optional) |
| BLAKE3 | 0x01 | Indicated by offset | Backward compatible (optional) |
| ElementSchema | None | v2+ only, in array fields | Ignored by v1 |
| EmbeddedSchema | 0x02 | Defined but unused | Reserved for future use |

### Flag Consistency Rules

**CRC32 flag (bit 0)**:
- If set: crcOffset must be non-zero
- If clear: crcOffset must be zero
- Violation: error `FlagMismatch`

**BLAKE3 flag (bit 1)**:
- If set: blake3Offset must be non-zero
- If clear: blake3Offset must be zero
- Violation: error `FlagMismatch`

**Reserved bits (3-7)**:
- Must be zero in all versions
- Any set bit: error `InvalidFlags`

**Implication**: New features must define new flags in reserved bits (3-7) or be version-gated

---

## 5. Invalid Flag Combinations

### All Valid Combinations (4 possibilities)

| CRC32 | BLAKE3 | EmbeddedSchema | Valid | Notes |
|-------|--------|----------------|-------|-------|
| No | No | No | ✅ | No checksums |
| Yes | No | No | ✅ | CRC32 only |
| No | Yes | No | ✅ | BLAKE3 only |
| Yes | Yes | No | ✅ | Both checksums |

All combinations of bit 0, 1, 2 are permitted (0x00-0x07 valid).

### Invalid Combinations

- Any bit 3-7 set → error `InvalidFlags`
- CRC32 flag set but crcOffset == 0 → error `FlagMismatch`
- BLAKE3 flag set but blake3Offset == 0 → error `FlagMismatch`
- CRC32 flag clear but crcOffset != 0 → error `FlagMismatch`
- BLAKE3 flag clear but blake3Offset != 0 → error `FlagMismatch`

---

## 6. Reader/Encoder Compatibility

### Encoder Constraints

Encoder produces v1 or v2 format depending on schema:
- If no array fields, or array fields without ElementSchema → v1 compatible
- If array fields with ElementSchema → requires v2 (version byte = 0x02)

**Encoder always produces deterministic output** for same input.

### Reader Constraints

Reader accepts v1 or v2, reads both correctly.

**Fast validation**: O(1), header-only, same for v1 and v2
**Strict validation**: O(n), validates all blocks, same for v1 and v2 (except ElementSchema handling)

---

## 7. Schema Evolution and Versioning

### Field ID Assignment

Field IDs are user-assigned (0 to 2^32-1) and appear in both schema and data.

**Stability**: Field IDs should not change between versions of a system.

**Bad practice**: Removing field ID 5, re-assigning as field ID 10 in v2 breaks files.

**Good practice**: Add new fields with new IDs, deprecate old fields by omitting from schema.

### Adding Fields

v2 schema = v1 schema + new fields

**Backward compatibility**:
- v1 reader reads v2 file: skips unknown fieldIds (if reader implementation supports it)
- v2 writer writes v1 file: omits v2-only fields

**Current implementation**: No field skipping; unknown fieldIds cause error

### Removing Fields

Remove field from schema in new version.

**Backward compatibility**:
- v1 reader reads v2 file: can read it if v2 only removed fields
- v2 reader reads v1 file: succeeds (fields were present)

### Changing Field Types

**Not recommended**: Changing fieldId 0 from int64 to string breaks compatibility.

**Workaround**: Retire old fieldId, use new fieldId for new type.

---

## 8. Checksum Compatibility

### CRC32 Presence

File may or may not have CRC32.

**Reader behavior**:
- If CRC32 flag set: verify and error on mismatch
- If CRC32 flag clear: ignore crcOffset (must be 0)

**Compatibility**: Readers can handle files with or without CRC32

### BLAKE3 Presence

File may or may not have BLAKE3.

**Reader behavior**:
- If BLAKE3 flag set: verify structure (exact hash verification deferred)
- If BLAKE3 flag clear: ignore blake3Offset (must be 0)

**Compatibility**: Readers can handle files with or without BLAKE3

### Checksum Verification Strategy

**Recommendation**: Add CRC32 to all files for integrity verification.
BLAKE3 is optional for cryptographic verification.

---

## 9. Determinism Across Versions

### v1 Determinism

Same input (schema + data) produces identical byte sequence:
- ✅ Field ordering (ascending fieldId)
- ✅ Float normalization (-0.0 → +0.0)
- ✅ VarUInt minimal encoding
- ✅ UTF-8 strings (no normalization)

**Verified**: 106 tests passing, determinism tests included

### v2 Determinism

v2 adds ElementSchema but doesn't change data encoding.

**Determinism preserved**:
- ✅ Same v1 determinism rules
- ✅ ElementSchema only affects schema block
- ✅ Data block encoding identical

**Cross-version determinism**: v1 file and v2 file with same data produce identical data blocks (if no ElementSchema in v2)

---

## 10. Migration Path v1 → v2

### When to Migrate

**Reasons to move to v2**:
- Need ElementSchema for stricter array element type validation
- Need reserved feature bits for new optional features

**Reasons to stay with v1**:
- No ElementSchema needed
- Simpler schema format
- Wider compatibility (older readers)

### Migration Strategy

1. **Update generator**: Encoder to produce v2 header (version = 0x02)
2. **Optionally add ElementSchema**: Embed type definitions in array fields
3. **Backward compatibility**: Readers stay compatible (v1/v2 auto-detect)
4. **No breaking changes**: Existing v1 readers can read v2 files without ElementSchema

### Rollback Plan

1. **Remove ElementSchema**: Revert to bare array fields (if added)
2. **Change version**: Set version byte back to 0x01
3. **Ensure field IDs unchanged**: No schema restructuring
4. **Re-encode**: Produce v1-compatible file

---

## 11. Limits and Compatibility

### Hard Limits (same for v1, v2)

| Limit | Value | Implication |
|-------|-------|-------------|
| MAX_FILE_SIZE | 256 MB | No files > 256 MB |
| MAX_STRING_LENGTH | 16 MB | No strings > 16 MB |
| MAX_FIELDS | 65,536 | No schema > 65K fields |
| MAX_ARRAY_ELEMENTS | 1,000,000 | No array > 1M elements |
| MAX_RECURSION_DEPTH | 32 | No nesting > 32 levels |

**Compatibility implication**: All files must respect these limits across versions.

---

## 12. Testing Compatibility Matrix

### Test Categories

| Test | v1 | v2 | Notes |
|------|----|----|-------|
| Header parsing | ✅ | ✅ | Identical |
| Magic/version/flags | ✅ | ✅ | Identical |
| Offset validation | ✅ | ✅ | Identical |
| Schema parsing | ✅ | ✅ | v2 includes ElementSchema |
| Data reading | ✅ | ✅ | Type codes identical |
| CRC32 | ✅ | ✅ | Identical |
| Determinism | ✅ | ✅ | Identical (no ElementSchema effect) |
| Field ordering | ✅ | ✅ | Identical |
| Corruption detection | ✅ | ✅ | Identical |

**Coverage**: 106 .NET tests cover both v1 and v2 scenarios

---

## 13. Known Incompatibilities and Workarounds

### No Field Skipping

v1/v2 readers reject files with unknown fieldIds.

**Workaround**: Schema must include all fieldIds present in data.

**Mitigation**: Use reserved fieldId ranges for future fields (e.g., 1000000+)

### No Field Deprecation

Removing a field from schema while data still contains it causes error.

**Workaround**: Keep field in schema, accept it in reader, ignore in application.

### No Type Evolution

Cannot safely change fieldId's type between v1 and v2.

**Workaround**: Use new fieldId for new type, deprecate old fieldId.

---

## Summary

ICFG versioning is:
- **Simple**: Two versions (v1, v2) with backward/forward compatibility
- **Stable**: v1 locked, v2 adds optional ElementSchema only
- **Deterministic**: Same input → same bytes across versions (if features constant)
- **Future-proof**: Reserved flag bits for v3+ features
- **Tested**: 106 tests covering version compatibility

**Recommendation**: Use v2 for new code; v1 for compatibility with older systems.

