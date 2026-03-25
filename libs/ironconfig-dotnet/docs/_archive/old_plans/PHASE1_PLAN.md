> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# PHASE 1 - Unified Error Model (Implementation Plan)

**Status**: SPECIFICATION COMPLETE, IMPLEMENTATION PENDING
**Date**: February 12, 2026
**Estimated Duration**: 4-6 hours
**Risk Level**: LOW (additive changes only)

---

## Objective

Establish a unified, canonical error model across IRONCFG, ILOG, and IUPD engines that:
- ✅ Provides stable error codes (won't change)
- ✅ Sells trust through detailed, actionable errors
- ✅ Enables robust client-side error handling
- ✅ Classifies errors by recovery strategy
- ✅ Documents all error cases comprehensively

---

## Deliverables

### 1. ✅ ERROR_CODES.md (COMPLETE)
**File**: `docs/ERROR_CODES.md`
**Status**: COMPLETE (3,200+ lines)

**Content**:
- Unified error code ranges (0x00-0x7F, 128 codes total)
- 40+ error code definitions with:
  - Description
  - Root causes
  - Recovery strategy
  - Example messages
  - Retry policy
  - Engine applicability
- Error classification by:
  - Recovery strategy (Retry/Skip/Downgrade/Abort)
  - Severity (FATAL/ERROR/WARNING/OK)
  - Corruption type (Format/Integrity/Schema/Dependency)
- Client error handling patterns
- Implementation notes and testing strategy

### 2. ILOG Error Types (TODO)
**File**: `src/IronConfig.ILog/IlogErrorCode.cs` (NEW)
**Estimated Time**: 30 min
**Status**: PENDING

**Implementation**:
```csharp
public enum IlogErrorCode : byte
{
    Ok = 0x40,
    CompressionFailed = 0x40,
    IndexCorrupted = 0x41,
    RecordTruncated = 0x42,
    InvalidProfile = 0x43
}

public struct IlogError
{
    public IlogErrorCode Code { get; }
    public uint BlockIndex { get; }      // Context: which block
    public uint Offset { get; }          // Offset within block
    public string Message { get; }
    public bool IsOk => Code == IlogErrorCode.Ok;
}
```

**Changes Required**:
- Add IlogError return types to IlogEncoder/IlogDecoder
- Update existing exception throws → IlogError returns
- Add validation for L2 (index) and L3 (compression) blocks

### 3. Unified Error Type Wrapper (TODO)
**File**: `src/IronConfig/IronEdgeError.cs` (NEW)
**Estimated Time**: 1 hour
**Status**: PENDING

**Implementation**:
```csharp
/// <summary>
/// Unified error type for all IronEdge engines
/// Provides single interface for error handling
/// </summary>
public struct IronEdgeError
{
    public enum ErrorEngine : byte { IRONCFG, ILOG, IUPD }
    public enum ErrorSeverity : byte { OK, WARNING, ERROR, FATAL }
    public enum ErrorRecovery : byte { SKIP, DOWNGRADE, ABORT }

    public ErrorEngine Engine { get; }
    public byte Code { get; }              // 0x00-0x7F canonical
    public ErrorSeverity Severity { get; }
    public ErrorRecovery Recovery { get; }
    public string Message { get; }
    public uint? Offset { get; }           // Byte offset in file
    public uint? ChunkIndex { get; }       // Chunk/block context

    // Factory methods
    public static IronEdgeError FromIronCfg(IronCfgError err) { ... }
    public static IronEdgeError FromIupd(IupdError err) { ... }
    public static IronEdgeError FromIlog(IlogError err) { ... }

    public bool IsOk => Severity == ErrorSeverity.OK;
}
```

**Changes Required**:
- Create factory methods for all three engines
- Map engine-specific codes to canonical 0x00-0x7F range
- Implement classification rules
- Add human-readable error messages

### 4. Corruption Classification Tests (TODO)
**File**: `tests/IronConfig.IronCfgTests/IronEdgeErrorTests.cs` (NEW)
**Estimated Time**: 2 hours
**Status**: PENDING

**Test Categories**:

#### A. Format Errors (ERR_INVALID_MAGIC, ERR_TRUNCATED_FILE, etc.)
```csharp
[Fact]
public void Corruption_InvalidMagic_ProducesCorrectError()
{
    // Arrange: valid IRONCFG, corrupt magic
    var data = CreateValidIronCfgFile();
    data[0] = 0xFF;  // Corrupt magic

    // Act
    var err = IronCfgValidator.Open(data, out _);

    // Assert
    Assert.False(err.IsOk);
    Assert.Equal(IronCfgErrorCode.InvalidMagic, err.Code);
    Assert.Equal(0u, err.Offset);  // Error at start of file
}
```

Tests needed:
- Invalid magic for IRONCFG, ILOG, IUPD
- Truncated file at various offsets
- Invalid version numbers
- Corrupted checksum/hash

#### B. Schema Errors (IRONCFG-specific)
```csharp
[Fact]
public void Corruption_FieldTypeMismatch_ProducesCorrectError()
{
    // Arrange: Field claims to be String, data is actually Int64
    var data = CreateCorruptedFieldData();

    // Act
    var err = IronCfgValidator.Open(data, out _);

    // Assert
    Assert.False(err.IsOk);
    Assert.Equal(IronCfgErrorCode.FieldTypeMismatch, err.Code);
}
```

Tests needed:
- Field type mismatches
- Missing required fields
- Invalid schema structure
- Field order violations

#### C. Dependency Errors (IUPD-specific)
```csharp
[Fact]
public void Corruption_CyclicDependency_ProducesCorrectError()
{
    // Arrange: Create manifest with A→B→C→A
    var manifest = CreateManifestWithCycle();

    // Act
    var err = IupdReader.Open(manifest, out _);

    // Assert
    Assert.False(err.IsOk);
    Assert.Equal(IupdErrorCode.CyclicDependency, err.Code);
}
```

Tests needed:
- Cyclic dependencies (A→B→A)
- Missing dependency targets
- Invalid chunk indices
- Overlapping payloads

#### D. Compression Errors (ILOG-specific)
```csharp
[Fact]
public void Corruption_CompressionFailed_ProducesCorrectError()
{
    // Arrange: Create ARCHIVED profile with corrupted compression
    var data = CreateCorruptedCompressedBlock();

    // Act
    var err = IlogDecoder.Decode(data);

    // Assert
    Assert.False(err.IsOk);
    Assert.Equal(IlogErrorCode.CompressionFailed, err.Code);
}
```

Tests needed:
- Corrupted LZ4 streams
- Invalid block headers
- Truncated compressed data
- Invalid index pointers

#### E. Determinism Tests
```csharp
[Fact]
public void ErrorHandling_CorruptionDetected_ConsistentAcrossRuns()
{
    var data = CreateCorruptedIroncfgFile();

    var err1 = IronCfgValidator.Open(data, out _);
    var err2 = IronCfgValidator.Open(data, out _);

    Assert.Equal(err1.Code, err2.Code);
    Assert.Equal(err1.Offset, err2.Offset);
}
```

**Test Matrix**:
| Engine | Format | Schema | Dependency | Compression | Count |
|--------|--------|--------|-----------|------------|-------|
| IRONCFG | ✓ | ✓ | - | - | 6 |
| ILOG | ✓ | - | - | ✓ | 4 |
| IUPD | ✓ | - | ✓ | - | 4 |
| **TOTAL** | **3** | **2** | **2** | **2** | **14+** |

### 5. Error Mapping Documents (TODO - Optional)
**Files**:
- `docs/IRONCFG_ERROR_MAPPING.md`
- `docs/ILOG_ERROR_MAPPING.md`
- `docs/IUPD_ERROR_MAPPING.md`

**Purpose**: Show canonical error codes for each engine
**Estimated Time**: 1 hour (optional, for documentation)

---

## Implementation Strategy

### Step 1: Add ILOG Error Types (30 min)
```bash
# Create IlogErrorCode.cs
# Add error handling to IlogEncoder.cs
# Add error handling to IlogDecoder.cs
# No behavior changes - just error structure
```

### Step 2: Create Unified Error Wrapper (1 hour)
```bash
# Create IronEdgeError.cs with factory methods
# Implement code mapping (IRONCFG 0x20→0x20, ILOG 0x00→0x40, etc.)
# Add classification logic
```

### Step 3: Write Corruption Tests (2 hours)
```bash
# Create IronEdgeErrorTests.cs
# Implement 14+ test cases
# Verify all error paths covered
```

### Step 4: Integration & Documentation (1 hour)
```bash
# Update ERROR_CODES.md with implementation notes
# Create MAPPING documents
# Add examples to test comments
```

**Total Estimated Time**: 4-5 hours
**Parallelizable**: Yes (Step 1 & 2 can overlap with test writing)

---

## Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| API breaking changes | LOW | HIGH | Only add new types, keep old signatures |
| Performance regression | LOW | MEDIUM | No computational changes, pure structure |
| Inconsistent error reporting | MEDIUM | MEDIUM | Comprehensive test matrix |
| Missing error cases | MEDIUM | LOW | List all 40+ codes, verify coverage |

**Overall Risk**: LOW ✅

---

## Acceptance Criteria

### Functional
- [ ] All 40+ error codes documented with examples
- [ ] ILOG error types implemented (IlogErrorCode enum)
- [ ] Unified error wrapper works for all three engines
- [ ] 14+ corruption tests passing
- [ ] Error codes are stable and won't change

### Quality
- [ ] 100% test coverage of error paths
- [ ] All error messages are actionable
- [ ] No breaking changes to existing APIs
- [ ] Determinism verified (same input → same error)

### Documentation
- [ ] ERROR_CODES.md comprehensive and up-to-date
- [ ] Recovery strategies documented
- [ ] Error classification clear
- [ ] Client examples provided

---

## Success Metrics

| Metric | Target | Evidence |
|--------|--------|----------|
| Error Code Stability | 128 canonical codes | ERROR_CODES.md |
| Test Coverage | >95% of error paths | IronEdgeErrorTests.cs |
| Documentation | All codes + examples | docs/ERROR_CODES.md |
| Determinism | 100% consistency | Corruption tests pass |

---

## Next Phase

### Phase 2: Runtime Doctor/Verify Commands
After Phase 1 completion, CLI tools will use these error codes:
```bash
$ ironcfg verify config.bin
OK: 4096 bytes, valid signature

$ ironcfg verify config.bin --corrupted
ERROR 0x07: Bounds violation at offset 2048
RECOVERY: File corrupted, download fresh copy
```

---

## Files to Create/Modify

| File | Action | Type |
|------|--------|------|
| docs/ERROR_CODES.md | ✅ CREATE | COMPLETE |
| src/IronConfig.ILog/IlogErrorCode.cs | TODO | NEW |
| src/IronConfig/IronEdgeError.cs | TODO | NEW |
| tests/.../IronEdgeErrorTests.cs | TODO | NEW |
| docs/IRONCFG_ERROR_MAPPING.md | TODO | OPTIONAL |

---

## Progress Tracking

### Completed ✅
- [x] Error code specification (ERROR_CODES.md)
- [x] Error classification framework
- [x] Recovery strategy documentation
- [x] Test strategy design

### In Progress 🔄
- [ ] ILOG error types (starting)

### Pending ⏳
- [ ] Unified error wrapper
- [ ] Corruption tests
- [ ] Integration & final review

---

## Sign-Off

**Phase 0**: ✅ COMPLETE (Baseline & Guardrails)
**Phase 1**: 🔄 SPECIFICATION COMPLETE, IMPLEMENTATION READY
**Approval**: Ready to proceed with implementation

**Next Review**: After Phase 1 implementation complete (4-6 hours)
