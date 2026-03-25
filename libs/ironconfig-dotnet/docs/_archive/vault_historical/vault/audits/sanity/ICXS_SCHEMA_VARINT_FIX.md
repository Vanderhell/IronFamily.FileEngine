> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# ICXS Schema VarUInt Fix - Verification Report

**Date**: 2026-01-13
**Status**: ✅ COMPLETE - All Tests Passing
**Fix Applied**: C reader schema field count parsing updated to use VarUInt (varint) encoding

---

## Summary

Fixed the ICXS C reader to correctly parse schema field counts using VarUInt (variable-length integer) encoding, matching the reference .NET implementation and actual golden test vector format.

**Files Modified**:
1. `libs/ironcfg-c/include/ironcfg/icxs.h` - Added `schema_fields_offset` field to track where field definitions start
2. `libs/ironcfg-c/src/icxs.c` - Updated schema parsing to use VarUInt for field count
3. `spec/ICXS.md` - Updated specification to document VarUInt encoding

---

## Before the Fix

### C Reader Behavior (BROKEN)
```
Golden file at offset 64: 0x05 (varint field count = 5)
C reader expected:        u32 LE (4 bytes)
C reader read:           0x05 0x01 0x00 0x00 = 261 (WRONG!)
Result:                  FAIL - Cannot read id field for record 0: ICFG_ERR_SCHEMA
```

### .NET Reader Behavior (WORKING)
```
Golden file at offset 64: 0x05 (varint field count = 5)
.NET reader expected:     VarUInt
.NET reader read:         0x05 = 5 ✓ (CORRECT!)
Result:                   SUCCESS - All records parsed correctly
```

**Test Result Before**: ❌ `test_icxs_golden` FAILED

---

## The Root Cause

**Specification Mismatch**: The ICXS specification (spec/ICXS.md) documented field count as u32 LE, but:
- The .NET encoder (IcxsEncoder.cs) writes it as VarUInt
- The .NET decoder (IcxsSchema.cs) reads it as VarUInt
- The C reader (icxs.c) expected u32 LE per spec
- Result: C reader incompatible with actual golden files

---

## The Fix

### 1. Updated C Reader Header (icxs.h)

Added field to track where schema field definitions start:

```c
typedef struct {
    ...
    uint32_t schema_fields_offset;  /* Offset where field definitions start (after varint field_count) */
    ...
} icxs_view_t;
```

### 2. Updated C Reader Implementation (icxs.c)

**Change 1**: Parse field count as VarUInt in `icxs_open()`:
```c
/* Parse schema block (field count as varint) */
size_t field_count_len = parse_varint_u32(data, schema_block_offset, size, &out->field_count);
if (field_count_len == 0) {
    return ICFG_ERR_BOUNDS;
}
uint32_t schema_fields_offset = schema_block_offset + field_count_len;
```

**Change 2**: Store schema_fields_offset in view:
```c
out->schema_fields_offset = schema_fields_offset;
```

**Change 3**: Use schema_fields_offset in `find_field()`:
```c
size_t offset = v->schema_fields_offset;  /* Start at actual field definitions (after varint field_count) */
```

### 3. Updated Specification (spec/ICXS.md)

Changed schema field count definition from:
```
fieldCount: u32 LE
```

To:
```
fieldCount: VarUInt (variable-length u32 encoding, 1-5 bytes)

**Note**: fieldCount uses variable-length encoding (varint) to minimize file size
for schemas with few fields. This matches the reference .NET encoder/decoder and
golden test vectors.
```

---

## After the Fix

### C Reader Behavior (FIXED)
```
Golden file at offset 64: 0x05 (varint field count = 5)
C reader expected:        VarUInt
C reader read:           0x05 = 5 ✓ (CORRECT!)
Schema fields start:      offset 65 ✓
Result:                   SUCCESS - All records parsed correctly
```

---

## Test Results

### C Test Suite

**Before Fix**:
```
1/4 test_icxs_basic ..................   PASSED
2/4 test_icxs_golden .................   FAILED
3/4 test_icfx_golden .................   PASSED
4/4 test_icfx_vsp_indexed ............   PASSED

75% tests passed, 1 tests failed
```

**After Fix**:
```
1/4 test_icxs_basic ..................   PASSED    0.03 sec
2/4 test_icxs_golden .................   PASSED    0.02 sec ✓ FIXED!
3/4 test_icfx_golden .................   PASSED    0.07 sec
4/4 test_icfx_vsp_indexed ............   PASSED    0.09 sec

100% tests passed, 0 tests failed out of 4

Total Test time: 0.31 sec
```

**Key Test Output** (test_icxs_golden):
```
=== Testing golden_items_crc.icxs ===
PASS: File opened and header validated
PASS: File validated (CRC check if present)
PASS: Record count correct (3)
  Record 0: OK (id=1, name=Iron Sword, damage=25, speed=15, rarity=2)
  Record 1: OK (id=2, name=Golden Shield, damage=10, speed=20, rarity=3)
  Record 2: OK (id=3, name=Diamond Pickaxe, damage=30, speed=10, rarity=4)
PASS: Total damage sum correct: 65

=== Testing golden_items_nocrc.icxs ===
PASS: File opened and header validated
PASS: File validated (CRC check if present)
PASS: Record count correct (3)
  Record 0: OK (id=1, name=Iron Sword, damage=25, speed=15, rarity=2)
  Record 1: OK (id=2, name=Golden Shield, damage=10, speed=20, rarity=3)
  Record 2: OK (id=3, name=Diamond Pickaxe, damage=30, speed=10, rarity=4)
PASS: Total damage sum correct: 65

===============================================
ALL TESTS PASSED
===============================================
```

### .NET Test Suite

**Before Fix**: ✅ 69/69 tests passing
**After Fix**: ✅ 69/69 tests passing

**Result**: No regressions introduced. All .NET tests still pass.

```
Passed!  - Failed: 0, Passed: 69, Skipped: 0, Total: 69, Duration: 165 ms
```

---

## Verification Summary

| Component | Before | After | Status |
|-----------|--------|-------|--------|
| C ICXS Tests | 2/2 passing | 2/2 passing | ✓ FIXED |
| C ICFX Tests | 2/2 passing | 2/2 passing | ✓ No regression |
| C Total | 3/4 passing | 4/4 passing | ✓ ALL PASS |
| .NET Total | 69/69 passing | 69/69 passing | ✓ No regression |
| Spec Accuracy | ❌ Mismatch | ✓ Correct | ✓ FIXED |

---

## Impact Analysis

### What Changed
- C reader now uses VarUInt for schema field count (matching .NET implementation)
- Added `schema_fields_offset` field to icxs_view_t struct (internal only)
- Updated spec to document actual format

### What Stayed the Same
- **File format**: No changes to golden vectors or encoding
- **API**: Public function signatures unchanged
- **Other readers**: ICFX reader unaffected
- **Test vectors**: All golden files work as-is
- **No regressions**: All other tests continue to pass

### Backward Compatibility
- **Breaking change**: None for consumers (C reader now correctly implements format)
- **Golden files**: All existing files now parse correctly (were not parsed before)
- **New code using C reader**: Will now work with ICXS files

---

## Root Cause Resolution

**Original Issue** (from ICXS_GOLDEN_FAIL_ANALYSIS.md):
> The specification (spec/ICXS.md) defines the schema field count as u32 LE, but the .NET encoder writes it as varint, and the C decoder expected u32 LE per spec.

**Resolution**:
✅ C reader now uses VarUInt to match .NET implementation
✅ Specification updated to document VarUInt encoding
✅ All tests passing

**Classification**: [SPEC MISMATCH] → Fixed by aligning C implementation with .NET encoder/decoder behavior

---

## Changes Checklist

- [x] Parse schema field count as VarUInt in C reader
- [x] Track schema_fields_offset for field definition location
- [x] Update find_field() to use schema_fields_offset
- [x] Update spec/ICXS.md to document VarUInt encoding
- [x] Rebuild C library
- [x] Run C test suite (4/4 PASS)
- [x] Run .NET test suite (69/69 PASS)
- [x] Verify no regressions
- [x] Create verification report

---

## Conclusion

The ICXS C reader has been successfully fixed to use VarUInt encoding for schema field counts, matching the reference .NET implementation and enabling correct parsing of golden test vectors.

**Status**: ✅ READY FOR PRODUCTION
- All C tests passing (4/4)
- All .NET tests passing (69/69)
- Specification aligned with implementation
- No regressions introduced

---

**Generated**: 2026-01-13
**Fix Type**: Schema Parsing Alignment
**Risk Level**: LOW (isolated change, high test coverage)
**Verification**: COMPLETE
