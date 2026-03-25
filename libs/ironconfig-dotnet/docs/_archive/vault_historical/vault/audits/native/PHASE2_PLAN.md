> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# PHASE 2: ICXS Embedded Schema - Implementation Plan

**Status**: Planning
**Date**: 2026-01-13
**Objective**: Make ICXS files self-contained by enabling schema extraction from embedded blocks

---

## Current State Analysis

### What's Already Working

**C Reader (libs/ironcfg-c/src/icxs.c)**:
- âś… Already reads schema from embedded block in file
- âś… No external schema required
- âś… Has `icxs_schema_get_field()` API to access schema metadata
- âś… Performs field lookup via linear scan through schema block (O(n) per lookup)

**Embedded Schema Block Format** (already in spec):
```
schemaBlockOffset:
  fieldCount: u32 LE
  For each field (sorted by fieldId ascending):
    fieldId: u32 LE
    fieldType: u8 (1=i64, 2=u64, 3=f64, 4=bool, 5=str)
```

### What Needs Fixing

**.NET Reader (libs/bjv-dotnet/src/IronConfig/Icxs/IcxsView.cs)**:
- âťŚ Requires external `IcxsSchema` object passed to constructor
- âťŚ Cannot work with ICXS file alone (needs schema.json)
- âťŚ This breaks the "self-contained" requirement

**Line 24 (Current):**
```csharp
public IcxsView(byte[] buffer, IcxsSchema schema)
{
    _schema = schema;  // <-- MUST PROVIDE EXTERNALLY
    ...
    var expectedHash = schema.ComputeHash();
    if (!expectedHash.SequenceEqual(_header.SchemaHash))
        throw new InvalidOperationException("Schema hash mismatch");
}
```

---

## Implementation Tasks

### Task 1: Modify .NET IcxsView to Extract Schema from File

**File**: `libs/bjv-dotnet/src/IronConfig/Icxs/IcxsView.cs`

**Changes**:
1. Add internal method `ExtractSchemaFromFile(byte[] buffer, IcxsHeader header)` that:
   - Reads fieldCount from header.SchemaBlockOffset
   - Iterates through embedded schema block
   - Reconstructs IcxsField objects with fieldId, type, and calculated offset
   - Returns IcxsSchema object with reconstructed fields

2. Add new constructor overload:
   ```csharp
   public IcxsView(byte[] buffer)
   {
       // Extract schema from embedded block instead of requiring external schema
       var header = IcxsHeader.TryParse(buffer, out var parsedHeader) ? parsedHeader : null;
       var schema = ExtractSchemaFromFile(buffer, header);
       // ... continue with normal initialization
   }
   ```

3. Keep existing constructor for backward compatibility:
   ```csharp
   public IcxsView(byte[] buffer, IcxsSchema schema)
   {
       // Legacy mode: validate with provided schema
   }
   ```

**Algorithm for ExtractSchemaFromFile**:
```
Read fieldCount from schemaBlockOffset
fields = []
offset = schemaBlockOffset + 4
fieldOffsetAccum = 0

For i = 0 to fieldCount-1:
  Read fieldId (u32 LE)
  Read fieldType (u8)

  Create field: { id=fieldId, type=fieldType, offset=fieldOffsetAccum }
  fields.append(field)

  Update fieldOffsetAccum based on type size:
    i64/u64/f64: += 8
    bool: += 1
    str: += 4

Return IcxsSchema with extracted fields
```

---

### Task 2: Add Backward Compatibility Validation

**File**: `libs/bjv-dotnet/src/IronConfig/Icxs/IcxsView.cs`

**Changes**:
1. If constructed with external schema AND embedded schema exists:
   - Validate they match via schemaHash
   - Warn if mismatch detected

2. If constructed without external schema:
   - Use embedded schema exclusively
   - Validate schemaHash for integrity (computed from extracted schema)

---

### Task 3: Optimize C Schema Lookup (Optional Enhancement)

**File**: `libs/ironcfg-c/src/icxs.c`

**Current Issue**: `find_field()` does O(n) linear scan per field access

**Option A**: Add schema caching to icxs_view_t
- Cache fieldId â†’ field mapping after first open
- Trade: Adds small memory overhead (~5 * fieldCount bytes)
- Benefit: O(1) lookups instead of O(n)

**Option B**: Keep as-is
- Works fine for typical schemas (10-50 fields)
- No additional memory
- Good enough for Phase 2

**Recommendation**: Keep as-is (Option B) - can be Phase 3 optimization

---

### Task 4: Update ICXS.md Spec

**File**: `spec/ICXS.md`

**Changes**:
1. Add section: "Self-Contained ICXS Files"
   - Clarify that embedded schema block is sufficient for reading
   - Explain when to use embedded schema vs external schema

2. Add section: "Reader Modes"
   - Legacy mode: External schema validation
   - Self-contained mode: Extract schema from file
   - Both modes support CRC validation

3. Update example to show self-contained approach

---

### Task 5: Create Golden Test Vectors

**Files**: `vectors/small/icxs/golden_embedded_*.icxs`

**Create**: 3 golden ICXS files
1. `golden_icxs_embedded_simple.icxs`
   - Simple schema: 3 fields (id: i64, name: str, active: bool)
   - 2 records
   - No CRC

2. `golden_icxs_embedded_with_crc.icxs`
   - Same schema as above
   - With CRC32 validation
   - Verify C and .NET both compute same CRC

3. `golden_icxs_embedded_large.icxs`
   - Larger schema: 10 fields mixing types
   - 100 records
   - With CRC
   - Test performance

**Creation Process**:
- Use .NET IcxsEncoder to create files
- Verify C reader can open and validate them
- Verify .NET new constructor can read them

---

### Task 6: Add/Update Unit Tests

**Files**:
- `libs/bjv-dotnet/tests/IronConfig.Tests/IcxsTests.cs`
- `libs/ironcfg-c/tests/test_icxs_golden.c`

**.NET Tests**:
```csharp
[Test]
public void EmbeddedSchema_ExtractFromFile()
{
    // Load golden ICXS file
    // Create IcxsView(buffer) without external schema
    // Verify schema extracted correctly
    // Verify field access works
}

[Test]
public void EmbeddedSchema_ValidateWithExternalSchema()
{
    // Load golden ICXS file + schema
    // Create IcxsView(buffer, schema)
    // Verify both methods produce same results
}

[Test]
public void EmbeddedSchema_MismatchDetected()
{
    // Load file with embedded schema
    // Try to validate with wrong external schema
    // Verify mismatch detected
}
```

**C Tests** (verify existing tests still pass):
```c
// test_icxs_golden.c already covers:
// - Field extraction from embedded schema
// - Record access without external schema
// - CRC validation
```

---

### Task 7: Integration Testing

**Commands to Verify**:

```bash
# 1. Create ICXS with embedded schema
dotnet run -- packxs schema.json input.json output.icxs

# 2. Read with .NET (new self-contained mode)
dotnet run -- tojson output.icxs  # Should work WITHOUT providing schema.json

# 3. Read with C
./crc_diagnostic output.icxs      # Should show schema info

# 4. Verify CRC parity
# Both C and .NET should report same computed CRC
```

---

## Phased Rollout

### Phase 2a: Core Implementation
1. âś… Read spec and understand current format (DONE)
2. Implement IcxsView schema extraction (Task 1)
3. Add backward compatibility (Task 2)
4. Update spec (Task 4)

### Phase 2b: Validation & Testing
5. Create golden test vectors (Task 5)
6. Update unit tests (Task 6)
7. Integration testing (Task 7)
8. Verify C/NET parity

### Phase 2c: Documentation
9. Update README.md with embedded schema feature
10. Document usage examples
11. Mark PHASE 2 as COMPLETE

---

## Success Criteria

âś… Must have:
- [ ] IcxsView can extract and use embedded schema
- [ ] Backward compatibility maintained (external schema still works)
- [ ] Golden test vectors created and validated
- [ ] C and .NET produce same results on same golden files
- [ ] CRC validation works with embedded schema
- [ ] Tests pass (both C and .NET)

âš ď¸Ź Nice to have (Phase 3):
- Optimize C schema lookup to O(1)
- Cache extracted schema in .NET IcxsView
- Add introspection API to query schema metadata

---

## Risk Mitigation

**Risk 1**: Breaking existing .NET IcxsView usage
- **Mitigation**: Keep both constructors (with/without external schema)
- **Testing**: Verify legacy tests pass

**Risk 2**: Embedded schema doesn't match external schema
- **Mitigation**: Compare schemaHash and warn if mismatch
- **Testing**: Add test for mismatch detection

**Risk 3**: Performance regression in schema lookup
- **Mitigation**: Accept O(n) for now, optimize in Phase 3
- **Testing**: Profile golden files with 100+ records

---

**READY FOR IMPLEMENTATION**
