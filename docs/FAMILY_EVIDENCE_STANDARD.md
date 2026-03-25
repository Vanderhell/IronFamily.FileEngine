# Family Evidence Standard - Unified Model

**Status**: EXECUTION-STANDARD
**Last Updated**: 2026-03-14
**Scope**: IUPD, ILOG, ICFG

---

## Purpose

Unify evidence classification across the FileEngine family so every strong claim is backed by exactly one hard evidence category. No soft language. No grades (A/B/C). No vague claims.

---

## Allowed Evidence Categories (ONLY)

Choose exactly one per claim:

| Category | Meaning | Example |
|----------|---------|---------|
| **VERIFIED_BY_EXECUTION** | Test executed fresh, passed, output captured | "dotnet test ... (246 PASS)" with timestamp |
| **VERIFIED_BY_TARGETED_TEST** | Specific test exists and is designed for this claim, known to pass | IupdProfileTests.cs line 45: TestMinimalProfile_RoundTrip |
| **CODE_PRESENT_ONLY** | Code exists and is syntactically valid, but not executed in this execution | IupdDeltaV2Cdc.cs lines 1-500 exist; IRONDEL2 execution not yet run today |
| **BLOCKED_BY_ENVIRONMENT** | Would execute/test but environment prevents it (compiler missing, etc.) | Native C tests cannot run: cl.exe not in PATH |
| **NOT_PRESENT** | Code, test, or artifact does not exist in repo | Native ILOG codec: NOT_PRESENT in native/ directory |

---

## Forbidden Wording

Do NOT use these in evidence documents:
- "appears"
- "seems"
- "likely"
- "probably"
- "good"
- "solid"
- "mature"
- "production-ready" (without VERIFIED_BY_EXECUTION evidence)
- A/B/C grades or stars
- "best practice" (state fact instead)
- "should", "must" (use "is", "does", "has")

---

## Mandatory Engine Checklist

Every engine (IUPD, ILOG, ICFG) must have evidence for all 10 items:

1. **spec** — Is there a specification document? Is it current?
2. **implementation** — Is there working code? In .NET? In native C?
3. **tests** — Unit and integration tests? How many? Pass/fail rates?
4. **golden vectors** — Positive test cases? Executed? Pass?
5. **negative vectors** — Error/invalid case tests? Executed? Pass?
6. **compatibility rules** — Version/format compatibility documented? Tested?
7. **limits** — Documented max sizes, counts, performance bounds?
8. **benchmark evidence** — Benchmarks exist and can be run? Results captured?
9. **runtime parity matrix** — .NET vs native C compared? Identical behavior?
10. **known gaps** — Gaps documented and classified? Blockers identified?

---

## Evidence Document Structure

Every claim must follow this structure:

```markdown
### Surface: [Specific Feature/Function]
- **Status**: [ONE of: VERIFIED_BY_EXECUTION, VERIFIED_BY_TARGETED_TEST, CODE_PRESENT_ONLY, BLOCKED_BY_ENVIRONMENT, NOT_PRESENT]
- **Evidence**: [Exact file path, line range, test name, or execution command + output reference]
- **Notes**: [Exact boundary or limitation. No soft wording.]
```

Example (Good):
```markdown
### Surface: IUPD INCREMENTAL Profile
- **Status**: VERIFIED_BY_EXECUTION
- **Evidence**: dotnet test libs/ironconfig-dotnet/tests/IronConfig.Iupd.Tests -c Release (2026-03-14, 246/246 PASS)
- **Notes**: IupdIncrementalApplyTests.cs tests IRONDEL2 (active) and DELTA_V1 (legacy). Test execution captured file size: ~5.1 MB output, 3m 8s duration.
```

Example (Bad - DO NOT USE):
```markdown
### Surface: IUPD INCREMENTAL Profile
- **Status**: Mature
- **Evidence**: Tests seem to work well
- **Notes**: This appears to be production-ready
```

---

## Rules for Unknowns and Blockers

**If something is NOT_PRESENT:**
```markdown
- **Status**: NOT_PRESENT
- **Evidence**: Glob search in native/ for *ilog* returns 0 results
- **Notes**: Native ILOG codec does not exist. No equivalent C implementation. Decision: intentional (ILOG is .NET-only)
```

**If something is BLOCKED_BY_ENVIRONMENT:**
```markdown
- **Status**: BLOCKED_BY_ENVIRONMENT
- **Evidence**: Native C build attempted; `cl.exe` not in PATH. Command: `dotnet build native/CMakeLists.txt -c Release`
- **Notes**: MSVC compiler unavailable in this environment. Code is present and syntactically valid (code inspection OK). Execution testing blocked.
```

**If something is CODE_PRESENT_ONLY:**
```markdown
- **Status**: CODE_PRESENT_ONLY
- **Evidence**: File: native/src/iupd/blake3.c (lines 1-200). No execution run in this session.
- **Notes**: Code present, syntax valid, likely functional, but not freshly executed. Execution evidence would upgrade to VERIFIED_BY_EXECUTION.
```

---

## Per-Engine Evidence Documents

Each engine gets a dedicated matrix:
- **docs/IUPD_EVIDENCE_MATRIX.md** — All 10 checklist items for IUPD
- **docs/ILOG_EVIDENCE_MATRIX.md** — All 10 checklist items for ILOG
- **docs/ICFG_EVIDENCE_MATRIX.md** — All 10 checklist items for ICFG

No narrative. No soft language. Facts only. One status per claim.

---

## Runtime Parity Matrices

For each engine, create a parity matrix:

```markdown
| Surface | .NET | Native C | Status | Evidence | Notes |
|---------|------|----------|--------|----------|-------|
| Reader API | VERIFIED_BY_EXECUTION | [status] | [combined status] | [evidence] | [boundary] |
| Writer API | VERIFIED_BY_EXECUTION | [status] | [combined status] | [evidence] | [boundary] |
| ...
```

Status in parity matrix = worst of .NET and native C status.

---

## Quality Rules

**ALWAYS:**
- Use exact file paths, line numbers, test names
- Capture command + timestamp for executions
- State exact boundary (e.g., "tested to 100 MB, not beyond")
- Separate "what we know" from "what we don't know"

**NEVER:**
- Repeat marketing language
- Use grades or vague assessments
- Mix soft commentary with hard claims
- Assume current without fresh evidence

---

## File Organization

Evidence documents must be in `docs/`:
- `FAMILY_EVIDENCE_STANDARD.md` (this file)
- `IUPD_EVIDENCE_MATRIX.md` (engine-specific)
- `ILOG_EVIDENCE_MATRIX.md` (engine-specific)
- `ICFG_EVIDENCE_MATRIX.md` (engine-specific)

Supporting artifacts in `artifacts/_dump/exec_evidence_standard_unification_01/`:
- `EXECUTION_LOG.md` (what was run, when, results)
- `BUILD_COMMANDS.txt` (exact commands used)
- `TEST_COMMANDS.txt` (exact commands used)
- `BENCH_COMMANDS.txt` (exact commands used)
- `CHANGED_FILES.txt` (which files were modified)
- `FINAL_STATUS.md` (summary of evidence status by engine)

---

## Next: Engine-Specific Matrices

Each matrix will have exactly these sections (in order):

1. **spec** — specification documents
2. **implementation** — code artifacts
3. **tests** — test suites and execution evidence
4. **golden vectors** — positive test vectors
5. **negative vectors** — error/invalid vectors
6. **compatibility rules** — version/format compatibility
7. **limits** — documented and tested limits
8. **benchmark evidence** — benchmark results
9. **runtime parity matrix** — .NET vs native C
10. **known gaps** — missing functionality and blockers

No other sections. No narrative. Pure evidence tables.
