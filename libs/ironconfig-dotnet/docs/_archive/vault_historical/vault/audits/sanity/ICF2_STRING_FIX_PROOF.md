> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# ICF2 String Column Fix - Proof & Verification

## Root Cause: Category C - BlobStart Computed Wrong

### The Bug
The **decoder** was reading string length values in the middle of calculating offsets, causing `blobStart` to be calculated at the wrong position.

**Original logic:**
```csharp
// Read initial offset
uint strOffset = ReadVarUInt(_data, ref pos);

// Skip rows before requested row
for (uint i = 0; i < row; i++)
{
    uint delta = ReadVarUInt(_data, ref pos);  // Treated as delta!
    strOffset += delta;
}

// Read current row length
uint strLength = ReadVarUInt(_data, ref pos);  // Mixed in with offset reading

// THIS WAS WRONG - pos is in the middle of length varints
uint blobStart = pos;
```

This attempted to:
1. Read initial offset
2. Accumulate deltas for rows
3. Read the current row's length
4. Set blobStart

But the **encoder** actually writes **lengths, not deltas**, and **all length values come before the blob**.

### Encoder Format
```
[rowCount VarUInt]
[initial_offset=0 VarUInt]
[length_row0 VarUInt]
[length_row1 VarUInt]
... more lengths ...
[blob: all string bytes concatenated]
```

Example for `["Alice", ""]`:
```
[2]              # rowCount
[0]              # initial offset (always 0)
[5]              # length of "Alice"
[0]              # length of ""
[Alice]          # blob
```

### The Fix
Read ALL metadata first, THEN set blobStart:

```csharp
private string DecodeStringValue(uint row, int colId, uint colOffset)
{
    uint pos = colOffset;
    uint rowCount = ReadVarUInt(_data, ref pos);

    if (row >= rowCount)
        throw new IndexOutOfRangeException($"Row {row} out of column {colId}");

    // Skip initial offset (always 0)
    ReadVarUInt(_data, ref pos);

    // Read ALL length values first
    var lengths = new uint[rowCount];
    for (uint i = 0; i < rowCount; i++)
    {
        lengths[i] = ReadVarUInt(_data, ref pos);
    }

    // NOW pos points to blob start
    uint blobStart = pos;

    // Calculate offset as sum of previous row lengths
    uint strOffset = 0;
    for (uint i = 0; i < row; i++)
    {
        strOffset += lengths[i];
    }

    uint strLength = lengths[row];
    return Encoding.UTF8.GetString(_data, (int)(blobStart + strOffset), (int)strLength);
}
```

## Verification: Before/After

### Test Case: Two Rows `["Alice", ""]`

**BEFORE FIX:**
```
Reading row 0, expected: "Alice"
Got: "\0Alic" (0x00 0x41 0x6C 0x69 0x63)
```
- The 0x00 was the varuint encoding of the second row's length (0)
- We were reading from 1 byte too early, mixing metadata with blob data

**AFTER FIX:**
```
Reading row 0, expected: "Alice"
Got: "Alice" âś“

Reading row 1, expected: ""
Got: "" âś“
```

## Test Results

### Before Fix
```
Failed!  - Failed:    28, Passed:    49, Skipped:    0, Total:    77
  - Icf2_ToJson_RoundTrip_Normalized: FAIL
  - Icf2_LargeSchema_ValidateMultipleFields: FAIL
  - 20+ ICXS tests (out of scope)
```

### After Fix
```
Passed!  - Failed:    22, Passed:    58, Skipped:    0, Total:    80
  âś“ Icf2_Golden_ValidateCrcOn
  âś“ Icf2_Golden_ValidateNoCrc
  âś“ Icf2_ToJson_RoundTrip_Normalized
  âś“ Icf2_LargeSchema_ValidateMultipleFields
  âś“ Icf2_Determinism_Encode3x_IdenticalBytes
  âś“ Icf2_Corruption_FlipByte_FailsValidation
  âś“ Icf2_Corruption_Truncate_FailsValidation
  âś“ Icf2_Bounds_InvalidOffset_FailsValidation
  âś“ Icf2_Repro_SingleRowOneString
  âś“ Icf2_Repro_SingleRowEmptyString
  âś“ Icf2_Repro_TwoRowsStrings

  All 11 ICF2 tests PASS
```

### Remaining Failures
All 22 remaining failures are **ICXS tests** with missing test vectors:
- Root cause: `vectors/small/icxs/item.schema.json` and related files not present
- **Out of scope** for ICF2 string fix

## Additional Fix: SkipStringColumn

Updated the column-skipping logic to correctly calculate string column size:

**Old:**
```csharp
// After reading all varints, incorrectly added blob size again
pos += totalBlobSize;  // WRONG - pos already moved
return pos - (uint)offset;
```

**New:**
```csharp
// pos is at blob start after reading all metadata
// Column size = metadata size + blob size
uint columnSize = (pos - (uint)offset) + totalBlobSize;
return columnSize;
```

This ensures multi-column files don't misalign column offsets.

## Files Modified

1. **libs/bjv-dotnet/src/IronConfig/Icf2/Icf2View.cs**
   - Fixed `DecodeStringValue()` to read all metadata before setting blobStart
   - Fixed `SkipStringColumn()` to calculate correct column size

2. **libs/bjv-dotnet/tests/IronConfig.Tests/Icf2Tests.cs**
   - Added 3 minimal repro tests:
     - `Icf2_Repro_SingleRowOneString`
     - `Icf2_Repro_SingleRowEmptyString`
     - `Icf2_Repro_TwoRowsStrings`

3. **libs/bjv-dotnet/vectors/small/icf2/_repro/**
   - Created minimal test cases for repro and debugging

## Validation

âś… **Format Unchanged** - No changes to encoder format, only decoder logic
âś… **Validation Strength** - No weakening of validation logic
âś… **No Regressions** - All previously passing tests still pass
âś… **Minimal Fix** - Changed only the string decoding logic, not other components
âś… **100% ICF2 Pass Rate** - 11/11 ICF2 tests passing

## Status: COMPLETE âś…

String column roundtrip now works correctly:
- âś… "Alice" roundtrips as "Alice"
- âś… "" (empty) roundtrips as ""
- âś… Multi-row files read correctly
- âś… Golden vector tests pass
- âś… Determinism preserved

NOT COMMITTED (per requirements)
