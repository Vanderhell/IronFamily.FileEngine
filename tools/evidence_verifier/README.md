# Evidence Verifier

Automated validation of EVIDENCE_MATRIX.md claims against source code and tests.

## Purpose

Ensures that:
1. All evidence references use stable anchors (no brittle line numbers)
2. All referenced symbols actually exist in source files
3. All test references follow the correct format
4. No stale references remain after code changes

## Reference Formats

### Code Anchors

Format: `path#Symbol`

Examples:
- `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs#IupdProfile` - enum/class
- `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs#RequiresBlake3` - method
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgHeader.cs#HEADER_SIZE` - constant

Validation: File exists AND symbol token found in file

### Test Anchors

Format: `Project:Class.Method`

Examples:
- `IronConfig.Iupd.Tests:SpecLockTests.ProfileRoundTrip`
- `IronConfig.IronCfgTests:MinimalFuzzTests.Fuzz_InvalidMagic`

Validation: Format check (actual test execution happens in CI)

### CI Anchors

Format: `path` or `path#job:name`

Examples:
- `.github/workflows/determinism.yml#job:determinism-windows`
- `bench/baselines/baseline.json`

Validation: File exists check

## Usage

### PowerShell (Windows)

```powershell
.\tools\evidence_verifier\verify_evidence.ps1
```

Exit codes:
- `0` - All validations passed
- `2` - Validation failed

### Bash (Linux/Mac)

```bash
bash tools/evidence_verifier/verify_evidence.sh
```

Exit codes:
- `0` - All validations passed
- `2` - Validation failed

## CI Integration

The verifier is run as a CI gate in `.github/workflows/evidence.yml`:

1. Parse EVIDENCE_MATRIX.md
2. Validate all anchors and formats
3. Check symbol existence
4. Run EvidenceSymbolTests and EvidenceBehaviorTests
5. Fail CI if any validation fails

## Adding New Claims

When adding a new claim to EVIDENCE_MATRIX:

1. **Choose the correct anchor format**
   - Code: `path#ActualSymbolName` (must exist in file)
   - Test: `Project:Class.ExactMethodName` (must match test method)
   - CI: `path#job:jobname` (must be actual job in workflow)

2. **No line numbers** - Never use `:123` or `:123-456`

3. **Symbol must exist** - The verifier checks all symbols

4. **Exact method names** - Test references must point to actual test methods, not just class names

## Maintenance

### Line Number Changes

When refactoring code:
- ✅ OK: Line numbers change (anchors don't break)
- ❌ NOT OK: Renaming symbols (anchors must be updated)
- ❌ NOT OK: Deleting symbols (would break anchor)

### Symbol Renaming

If a symbol is renamed:
1. Update EVIDENCE_MATRIX anchor to new symbol name
2. Run verifier to confirm new anchor is valid
3. Update EvidenceSymbolTests if needed

## Truth Status

All 50 EVIDENCE_MATRIX claims use stable anchors.
All anchors are validated on every build.
No line-number brittleness remains.
