# Documentation Policy - IronConfig V2.6

**Effective**: 2026-02-14

---

## Overview

This policy establishes how documentation is organized and maintained for IronConfig V2.6.0-rc1.

---

## Current Documentation (Active)

All current documentation lives in **`docs/`** and follows a single-purpose-per-file model:

| File | Purpose | Audience |
|------|---------|----------|
| `INDEX.md` | Navigation hub | All users |
| `STATUS.md` | One-page status summary | PMs, reviewers |
| `TESTING.md` | How to run tests | CI/CD engineers |
| `RUNTIME_CONTRACT.md` | API/determinism guarantees | Implementers |
| `SECURITY_AUDIT_V2_6.md` | Threat model & mitigations | Security reviewers |
| `RELEASE_RC_V2_6.md` | RC1 deployment guide | Product managers |
| Root `CHANGELOG_V2_6.md` | Release notes | All users |

---

## Archive (Historical)

All obsolete documentation lives in **`docs/_archive/`** organized by category:

```
docs/_archive/
├── old_plans/          (implementation plans, completed)
├── old_proofs/         (release proofs, V2.2/V2.4/V2.5)
├── old_benchmarks/     (historical benchmarks)
├── old_specs/          (old format specifications)
├── old_testing/        (old testing documentation)
├── session_logs/       (session summaries, phase reports)
├── audits_historical/  (audit trail and certifications)
├── vault_historical/   (vault debug logs and misc)
└── misc/               (other historical docs)
```

**Retention**: Archive maintains full git history via `git log -- docs/_archive/`

---

## Adding New Documentation

### Before Adding a New Doc

1. Check `docs/INDEX.md` — Is there an existing doc that covers this?
2. Check `docs/_archive/` — Was this solved before? Can you improve the old version?
3. Update existing docs first; only add new files if truly novel

### When Adding a Doc

1. **File location**: `docs/<TOPIC>.md`
2. **Naming**: Descriptive, lowercase-with-hyphens (e.g., `api-quickstart.md`)
3. **First heading**: `# Title` (must match purpose)
4. **Numeric claims**: Reference only `artifacts/test-proof/v2_6/TEST_SUMMARY.md` for current baselines
5. **Audience**: Specify at top comment
6. **Update INDEX.md**: Add entry to navigation

### Example Header

```markdown
# API Quickstart

**Audience**: Developers integrating IronConfig

**Status**: V2.6.0-rc1 Current

---
```

---

## Numeric Claims (Truth Source)

All current documentation must reference **ONLY** this baseline:

- **Test Results**: `artifacts/test-proof/v2_6/TEST_SUMMARY.md`
- **Counts**: 265 passed, 6 skipped, 31 guard tests, 100% pass rate
- **Version**: v2.6.0-rc1
- **Date**: 2026-02-14
- **Format Stability**: IRONCFG v1, ILOG v1, IUPD v2

**Rule**: If docs claim different numbers, they are wrong and must be updated.

---

## Root-Level Documents

Only these files are allowed at repository root:

- `README.md` (mandatory: minimal pointer to docs/)
- `CHANGELOG_V2_6.md` (mandatory: release notes)
- `.gitignore`, `.github/`, etc. (standard git files)

**Not allowed** at root:
- Old benchmark reports
- Session summaries
- Phase plans
- Status reports
- Proof artifacts

**Rule**: Move to archive if found.

---

## Module-Specific Docs

Source code modules may include module-specific `README.md`:

- **Location**: `src/<MODULE>/README.md`
- **Size limit**: ≤ 80 lines
- **Purpose**: Technical reference for that module ONLY
- **Example**: `src/IronConfig/Crypto/Ed25519Vendor/ATTRIBUTION.md` (source attribution)

Keep these — they're not user-facing documentation.

---

## Review Checklist

Before committing doc changes:

- [ ] No scattered `.md` files outside `docs/` (except root README/CHANGELOG and module READMEs)
- [ ] All numeric claims match `artifacts/test-proof/v2_6/TEST_SUMMARY.md`
- [ ] Links in `docs/INDEX.md` all resolve
- [ ] Old docs moved to `docs/_archive/` with `git mv` (preserves history)
- [ ] Archive subdirectories exist and are organized by category
- [ ] Root README.md is ≤ 40 lines and points to `docs/INDEX.md`

---

## Questions?

See `docs/INDEX.md` for navigation to all documentation.

---

**Policy Version**: V2.6.0-rc1
**Last Updated**: 2026-02-14
**Owner**: IronConfig Release Team

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
