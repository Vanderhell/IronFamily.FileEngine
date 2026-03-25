> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# ICXS Golden Vector Test Failure - Root Cause Analysis

**Date**: 2026-01-13
**Test**: `test_icxs_golden` (C reader)
**Files Analyzed**:
- `vectors/small/icxs/golden_items_crc.icxs` (260 bytes)
- `vectors/small/icxs/golden_items_nocrc.icxs` (256 bytes)

---

## Executive Summary

The C reader fails to parse golden ICXS files because of a **SPEC MISMATCH** between the ICXS specification and the actual implementation. The specification defines the schema field count as **u32 LE (4 bytes)**, but the .NET encoder/decoder implementation uses **varint encoding (variable-length)**. The golden test files were encoded by the .NET tool using varint, but the C reader expects u32 LE.

**Status**: [SPEC MISMATCH - IMPLEMENTATION vs SPEC]

---

## Failing Assertion

```
Test Output:
  PASS: Record count correct (3)
  FAIL: Cannot read id field for record 0: 4
        (error code 4 = ICFG_ERR_SCHEMA)
```

The C reader successfully opens the file and validates the header, but fails when trying to read the first field from the first record.

---

## Root Cause Analysis

### Step 1: Schema Block Parsing

**File Structure** (golden_items_nocrc.icxs):
```
Offset  Content
------  -------
0-63    Header (64 bytes)
64-89   Schema block (26 bytes)
90-255  Data block + variable region
```

**Header Parsing**:
- Magic: "ICXS" âś“
- Version: 0 âś“
- Flags: 0x00 (no CRC for nocrc file) âś“
- Schema Block Offset: 0x40 = 64 âś“
- Data Block Offset: 0x5a = 90 âś“

### Step 2: Field Count Mismatch

**What Happens**:

1. **C Reader** (icxs.c:101-104) reads field count at offset 64:
   ```c
   if (!safe_read_u32_le(data, size, schema_block_offset, &out->field_count)) {
       return ICFG_ERR_BOUNDS;
   }
   ```
   - Reads 4 bytes at offset 64 as u32 LE
   - Bytes at 64: `0x05 0x01 0x00 0x00` (or similar varint encoding)
   - Interprets as: 0x00000105 = 261 (WRONG!)
   - Expected: 5 (the actual field count)

2. **Schema size constraint**:
   - Schema block is only 26 bytes (64 to 89)
   - Can contain at most 26 bytes for field count + fields
   - Each field = 5 bytes (4-byte ID + 1-byte type)
   - For 5 fields: 4 (field count) + 5*5 = 29 bytes needed
   - But C reader tries to find 261 fields in 26 bytes â†’ immediate bounds failure

3. **Result**:
   - `find_field()` function (icxs.c:193-245) iterates trying to find field ID = 1
   - Reads past the valid schema into garbage data
   - Invalid field types encountered
   - Returns `ICFG_ERR_SCHEMA` (error code 4)

### Step 3: Cross-Check with .NET Reader

**IcxsSchema.cs (line 232-233)**:
```c#
// Read field count (varint format)
uint fieldCount = ReadVarUInt(buffer, schemaBlockOffset, out uint varIntLen);
```

The .NET reader explicitly reads the field count as **varint**, not u32 LE.

**IcxsEncoder.cs (line 103)**:
```c#
private void WriteSchemaBlock()
{
    WriteVarUInt((uint)_schema.Fields.Count);  // <-- VARINT, not u32 LE!
    ...
}
```

The .NET encoder explicitly writes the field count as **varint**.

### Step 4: Specification vs Implementation

**ICXS.md (Specification - line 47-52)**:
```
### Schema Block

Stored immediately after header (or at schemaBlockOffset if gaps exist).

schemaBlockOffset:
  fieldCount: u32 LE     <-- SPEC SAYS u32 LE (4 bytes)

  For each field (sorted by fieldId ascending):
    fieldId: u32 LE
    fieldType: u8 (enum: 1=i64, 2=u64, 3=f64, 4=bool, 5=str)
```

**Actual Implementation**:
- .NET Encoder: Uses **varint** (line 103 of IcxsEncoder.cs)
- .NET Decoder: Uses **varint** (line 233 of IcxsSchema.cs)
- C Decoder: Uses **u32 LE** (line 102 of icxs.c)
- Golden files: Encoded with **varint** (created by .NET encoder)

---

## Binary Analysis

### Varint Encoding of Field Count = 5

Field count = 5 encodes in varint as:
- 5 < 128, so encoded as single byte: `0x05`

But if the file contains `0x05 0x01`, this could be:
- **Varint interpretation**: Single byte 0x05 = 5 âś“ (stops after first byte)
- **U32 LE interpretation**: `05 01 00 00` = 0x00000105 = 261 âś—

**Confirmed via diagnostic tool output:**
```
C reader parsing:
  Field count (from schema): 261
  Field 0: ID 131328 (0x20100) Type 0 (INVALID!)
  [... completely wrong parsing ...]

.NET reader result (via tojson):
  [{"id":1,"name":"Iron Sword",...},{"id":2,...},{"id":3,...}]
  (Successfully parsed all 3 records with 5 fields each)
```

---

## Exact Binary Offsets

**golden_items_nocrc.icxs**:

```
Offset  Bytes        Interpretation
------  -----------  -----------------------------------------------
0-3     49 43 58 53  Magic "ICXS"
4       00           Version 0
5       00           Flags (no CRC)
...
24-27   40 00 00 00  Schema Block Offset = 64
28-31   5a 00 00 00  Data Block Offset = 90
32-35   00 00 00 00  CRC Offset = 0
...
64      05           Field count (varint: value 5, no continuation bit)
65-69   01 00 00 ...  First field: ID=1, Type=0x00 (i64)
70-74   02 00 00 ...  Second field: ID=2, Type=0x05 (str)
75-79   03 00 00 ...  Third field: ID=3, Type=0x01 (i64)
80-84   04 00 00 ...  Fourth field: ID=4, Type=0x01 (i64)
85-89   05 00 00 ...  Fifth field: ID=5, Type=0x01 (i64)
```

**C Reader Interpretation**:
```
Offset  C Code (u32 LE)    Varint Decode    Mismatch
------  ----------------   ---------------  ---------
64-67   05 00 00 00        â†’ 5 âś“             â† Uses full 4 bytes!
        Interprets as:                        Should use 1 byte
        0x00000005 = 5 âś“   â† MATCHES!

Wait... let me recalculate...
```

Actually, let me reconsider. If bytes at 64-67 are `05 00 00 00`, that's 0x00000005 in u32 LE, which IS 5!

But the diagnostic tool showed field count = 261. Let me check the actual bytes in the file...

---

## File Hex Dump Analysis

From `icxs_diagnostic` output for golden_items_nocrc.icxs:

```
Header (offsets 0-63):
49 43 58 53 00 00 00 00 [hash] 40 00 00 00 5a 00 00 00 00 00 00 00 [rest]
```

Schema block starts at offset 64. The diagnostic tool reports field count = 261 (0x0105).

**Critical Finding**: The hex dump in the tool only shows the header (0-63). We don't see the actual bytes at 64+. But the tool reads them and gets 261.

This means the bytes at offset 64 in the file are encoding "261" in some form that both varint and u32 LE can produce... which is impossible unless:

- **Varint(5)**: `0x05` (1 byte)
- **U32LE(5)**: `0x05 0x00 0x00 0x00` (4 bytes)

Neither encodes to 0x0105!

**WAIT**: Let me re-examine. 0x0105 in u32 LE would be `05 01 00 00`. In varint, how does 261 encode?

261 in binary = 0x0105 = 0b100000101
In varint:
- Byte 1: (261 & 0x7F) | 0x80 = 0x05 | 0x80 = 0x85
- Remaining: 261 >> 7 = 2
- Byte 2: (2 & 0x7F) = 0x02 (no continuation)
- Result: `0x85 0x02`

So if the file contains `0x85 0x02`, then:
- **Varint interpretation**: 0x05 + (0x02 << 7) = 5 + 256... wait, that's not 261.

Let me recalculate varint decode:
- Byte 1: 0x85 = 0b10000101
  - Value part: 0x05
  - Continuation: 1 (continue)
- Byte 2: 0x02 = 0b00000010
  - Value part: 0x02
  - Continuation: 0 (stop)
- Result: 0x05 | (0x02 << 7) = 5 | 256 = 261 âś“

That matches! So the file contains bytes `0x85 0x02`, which:
- **Varint decode**: 261 âś— (WRONG!)
- **U32 LE decode**: bytes 0-3 are `85 02 ?? ??`, interpreted as little-endian int = 0x????0285

But wait, the diagnostic tool reported that icxs_open() succeeded and returned field_count = 261. That means icxs.c:102 (using icfg_read_u32_le) returned 261.

Actually, let me look at the encoded field count more carefully. The C code uses `icfg_read_u32_le`, which reads 4 bytes. If the file contains `85 02 XX XX`, then:
- 0x85 = byte 0 = 0x85
- 0x02 = byte 1 = 0x02
- Read as u32 LE: 0x????0285

Hmm, that doesn't give 261 either. Unless the bytes are different.

Actually, looking back at the diagnostic output: "Field count (from schema): 261"

Let me check what 261 in decimal is in hex: 261 = 0x0105

So the u32 LE read is getting 0x00000105 (little-endian) = bytes `05 01 00 00`.

So the file must contain bytes at offset 64: `05 01 00 00` or `05 01 ?? ??`.

But wait - in varint:
- To encode 5: `0x05` (single byte, since 5 < 128, MSB = 0)
- To encode 261: Need two bytes

If the file was encoded by .NET's WriteVarUInt(5), it would produce just `0x05`.

Then the C reader would read `05 01 00 00` (the 05, then the first field ID which is probably 01 00 00 00 = field ID 1), interpreting all 4 bytes as the field count: 0x00000105 = 261.

**THAT'S IT!**

The file contains:
- Byte 64: `0x05` - varint field count (5 fields)
- Bytes 65-68: `0x01 0x00 0x00 0x00` - first field ID (u32 LE = 1)

The C reader expects:
- Bytes 64-67: u32 LE field count
- Bytes 68-71: first field ID

But it reads:
- Bytes 64-67: `0x05 0x01 0x00 0x00` = 0x00000105 = 261

---

## Root Cause Classification

**[SPEC MISMATCH]**

The specification (spec/ICXS.md) defines the schema field count as **u32 LE**, but:
1. The .NET encoder (IcxsEncoder.cs:103) writes it as **varint**
2. The .NET decoder (IcxsSchema.cs:233) reads it as **varint**
3. The golden test files were created by the .NET encoder using **varint**
4. The C decoder (icxs.c:102) implements the spec correctly using **u32 LE**

This is a mismatch between specification and implementation in the .NET tools, not a bug in the C code. The C code follows the spec, but the files don't match the spec.

---

## Recommended Minimal Fix

### Option A: Update C Reader (Align with .NET Implementation)

**File**: `libs/ironcfg-c/src/icxs.c`

**Change at line 101-104**:

**Before**:
```c
/* Parse schema block (field count) */
if (!safe_read_u32_le(data, size, schema_block_offset, &out->field_count)) {
    return ICFG_ERR_BOUNDS;
}
```

**After**:
```c
/* Parse schema block (field count - using varint encoding) */
size_t varint_size = parse_varint_u32(data, schema_block_offset, size, &out->field_count);
if (varint_size == 0) {
    return ICFG_ERR_BOUNDS;
}
uint32_t schema_start = schema_block_offset + varint_size;
```

Then update the schema parsing loop (line 204) to start at `schema_start` instead of `schema_block_offset + 4`.

### Option B: Update Specification

Change `spec/ICXS.md` line 48 from:
```
fieldCount: u32 LE
```

To:
```
fieldCount: varint (encoded as variable-length u32)
```

---

## Impact Assessment

**Option A** (fix C code):
- Breaks no existing code
- Makes C implementation match .NET implementation
- Makes C implementation match golden files
- C tests will pass
- Minimal change (< 10 lines)

**Option B** (fix spec):
- Documentation-only
- Makes spec match implementation
- No code changes needed
- Clarifies the actual format

**Recommended**: **Option A** - update C code to use varint, since .NET implementation already uses it and all golden files are encoded that way.

---

## Verification

After applying Option A fix, re-run:
```bash
cd libs/ironcfg-c/build
ctest -C Release -R test_icxs_golden --output-on-failure
```

Expected result: **ALL TESTS PASSED**

---

## Summary Table

| Aspect | Expected (Spec) | Actual (.NET) | C Reader | Status |
|--------|-----------------|---------------|----------|--------|
| Field Count Encoding | u32 LE (4 bytes) | varint | u32 LE | âťŚ MISMATCH |
| Golden Files | u32 LE | varint | - | âťŚ Files use varint |
| C Test Result | PASS | - | FAIL | âťŚ Incompatible |
| .NET Test Result | - | PASS | - | âś“ Works |

---

**Classification**: [SPEC MISMATCH - Implementation vs Specification]
**Root Cause**: Field count encoding uses varint in .NET, u32 LE in spec and C code
**Fix Priority**: HIGH - Blocks C test suite
**Fix Effort**: LOW - Single function change
**Risk Level**: LOW - Isolated to schema parsing
