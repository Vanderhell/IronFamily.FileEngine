> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONCFG CRC32 Implementation Audit Report

**Date**: 2026-01-12
**Scope**: IRONCFG reference libraries (C#/.NET)
**Conclusion**: **CRC is not computed and not validated**

---

## Executive Summary

The IRONCFG reference libraries **support the CRC field in the BJV2/BJV4 format specification** but **do not actually compute or validate CRC32 checksums**.

- ✅ CRC flag is correctly **SET** when `--crc on` is used
- ❌ CRC is never **COMPUTED** during encoding
- ❌ CRC is never **VALIDATED** during decoding/validation

**Threat**: Corrupted ICFG files are silently accepted as valid, providing no protection against data corruption.

---

## 1. Code Evidence

### 1.1 Keyword Search Results

Static analysis found:
- **15,959 bytes** of grep output for "crc" (case-insensitive)
- **1,596 bytes** for "crc32"
- **1,088 bytes** for "checksum"
- **0 bytes** for CRC polynomial constants (0xEDB88320, 0x04C11DB7)

**File**: `./_crc_audit/grep_crc.txt`

### 1.2 No CRC Polynomial Implementation

**Search Results**:
- git grep "0xEDB88320" → 0 matches
- git grep "0x04C11DB7" → 0 matches

**Conclusion**: No CRC-32 (IEEE 802.3) polynomial table or computation code exists.

---

## 2. Implementation Analysis

### 2.1 BjvEncoder: CRC Flag Set But Not Computed

**File**: `libs/bjv-dotnet/src/IronConfig/BjvEncoder.cs`

**Line 18**: Field declaration
```csharp
private readonly bool _useCrc;
```

**Line 20-25**: Constructor accepts useCrc parameter
```csharp
public BjvEncoder(bool isBjv4 = false, bool useVsp = false, bool useCrc = false)
{
    _isBjv4 = isBjv4;
    _useVsp = useVsp;
    _useCrc = useCrc;
}
```

**Line 104**: Flag is SET in header but no CRC value is computed
```csharp
byte flags = 0x01;
if (_useCrc) flags |= 0x02;  // FLAG set, but NO CRC computation follows
if (_useVsp) flags |= 0x04;
header[4] = flags;
```

**Lines 95-116**: `WriteHeaderAndFinalize()` method
- Sets CRC flag (line 104)
- Does NOT write any CRC value bytes
- Does NOT call any CRC calculation function

**Verdict**: ❌ **CRC not computed on write**

---

### 2.2 BjvDocument: CRC Flag Read But Not Validated

**File**: `libs/bjv-dotnet/src/IronConfig/BjvDocument.cs`

**Line 57**: CRC flag is READ
```csharp
bool hasCrc = (flags & 0x02) != 0;
```

**Line 77**: Flag is stored in property
```csharp
return new BjvDocument(data, isBjv4, hasCrc, hasVsp, dict, vsp);
```

**Lines 40-78**: `Parse()` method
- Reads and validates magic, flags, reserved fields
- Reads CRC flag
- Does NOT validate or check CRC value
- No CRC validation function is called

**Verdict**: ❌ **CRC not validated on read**

---

### 2.3 Validation Entry Point: No CRC Check

**File**: `tools/ironconfigtool/Program.cs`

**Lines 310-321**: `ValidateBjv()` function
```csharp
static void ValidateBjv(byte[] data)
{
    try
    {
        var doc = BjvDocument.Parse(data);
        Console.WriteLine($"✓ BJV{doc.Version} valid");
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Invalid BJV: {ex.Message}");
    }
}
```

**Finding**: Only calls `BjvDocument.Parse()` - no additional CRC validation logic.

**Verdict**: ❌ **CRC not validated in validate command**

---

## 3. Documentation Evidence

### 3.1 FORMAT.md Explicit Statement

**File**: `docs/FORMAT.md`
**Line 51**:
```
CRC32 checksum field/flag (not computed/verified by current reference libraries)
```

**Line 93** (Glossary):
```
CRC | CRC32 field/flag defined in BJV (reference libraries currently do not compute/verify CRC)
```

### 3.2 BJV v2 Specification

**File**: `spec/bjv_v2.md`
**Lines 176-184**:

```
## CRC32 [Optional]

BJV v2 defines an optional CRC32 trailer:

- If flags.bit1 = 1, the file layout reserves a CRC32 checksum at the offset
  indicated by the header's crc_offset field.
- CRC32 algorithm: IEEE 802.3
- Data covered: bytes [0 .. crc_offset - 1] (everything except the CRC itself)

**Important:** The current reference implementations in this repository do **not**
compute or validate CRC32. Do not rely on CRC for corruption detection unless you
add CRC handling in your integration.
```

---

## 4. Runtime Corruption Test

### 4.1 Test Setup

**Input**: `min.json`
```json
{
  "a": 1,
  "b": "x",
  "c": [1,2,3]
}
```

**Command**:
```bash
dotnet run -- pack min.json min.icfg --crc on
```

**Output**: 90-byte ICFG file with CRC flag set

**Dump Output**:
```
BJV Version: BJV2
CRC: present
VSP: present
Dictionary keys: 3
Root type: Object
```

### 4.2 Test Execution

**Step 1**: Validate original file
```
Result: ✓ BJV2 valid, ✓ File is valid (exit code 0)
```
✅ PASS (expected)

**Step 2**: Corrupt file at byte 45 (midpoint)
```
Original: 0x00
Corrupted: 0xFF (XOR with 0xFF)
File: min_corrupt.icfg
```

**Step 3**: Validate corrupted file
```
Result: ✓ BJV2 valid, ✓ File is valid (exit code 0)
```
✅ PASS (corrupted file accepted - should have FAILED!)

### 4.3 Test Conclusion

| Scenario | Expected | Actual | Status |
|----------|----------|--------|--------|
| Original file validation | PASS | PASS | ✓ OK |
| Corrupted file validation | FAIL (if CRC checked) | PASS | ❌ FAIL |

**Proof**: The corrupted file passes validation despite byte corruption, proving CRC is not validated.

---

## 5. Call Site Analysis

### 5.1 CRC Functions

**Search**: `ComputeCrc`, `ValidateCrc`, `CalculateCrc`, `Crc32`, `CrcCheck`

**Results**: Zero matches across entire repository

### 5.2 CRC Usage Patterns

| Location | Usage | Type |
|----------|-------|------|
| BjvEncoder.cs:18 | `_useCrc` field | Flag storage |
| BjvEncoder.cs:104 | `if (_useCrc) flags \|= 0x02` | Flag setting (no computation) |
| BjvDocument.cs:57 | `bool hasCrc = (flags & 0x02)` | Flag reading (no validation) |
| Program.cs:103 | `HasOption(args, "--crc", "on")` | CLI parameter |
| Program.cs:171 | Console output | Status reporting only |

**Verdict**: CRC is only **recognized as a flag**, never **computed or validated**.

---

## 6. Summary

### What IRONCFG Does

1. ✅ Defines CRC field in BJV2/4 format spec
2. ✅ Sets CRC flag in header when `--crc on` is used
3. ✅ Documents that CRC is not computed/validated

### What IRONCFG Doesn't Do

1. ❌ Compute CRC-32 (IEEE 802.3) during encoding
2. ❌ Validate CRC during decoding
3. ❌ Fail validation on corrupted files

### Risk Assessment

- **File Integrity**: ⚠️ No protection against corruption
- **User Trust**: ⚠️ CRC flag creates false sense of security
- **Documentation**: ✅ Clear and accurate disclaimers

---

## 7. Conclusion

**IRONCFG CRC is not computed and not validated.**

Evidence:
1. **Code**: No CRC computation function, no validation function, no polynomial constants
2. **Implementation**: Flag is set but never used for validation
3. **Runtime**: Corrupted files pass validation despite CRC flag present
4. **Documentation**: Explicitly states CRC is not implemented

**For users requiring CRC protection**: Implement external checksum mechanism.

---

## Appendix: Generated Files

```
./_crc_audit/
├── grep_*.txt              (keyword search results)
├── AUDIT_REPORT.md         (this report)
└── run_test/
    ├── min.json            (test input)
    ├── min.icfg            (original, valid)
    ├── min_corrupt.icfg    (corrupted, still valid)
    ├── corrupt.ps1         (corruption script)
    └── TEST_RESULTS.txt    (runtime test evidence)
```

**Report Date**: 2026-01-12
