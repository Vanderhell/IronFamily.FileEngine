# IUPD Structure and Validation Flow

**Status**: Current repository implementation
**Date**: 2026-04-06
**Scope**: `.NET` writer/reader flow with native entry-point cross-reference

---

## 1. Scope

This document explains `IUPD` at the implementation-flow level:

- how the current writer assembles a package
- how the current reader parses a package
- what `ValidateFast()` actually checks
- what `ValidateStrict()` adds on top

It is not a replacement for `IUPD_SPEC.md` or `IUPD_COMPATIBILITY.md`.

---

## 2. Current Writer Build Order

Current `.NET` writer build flow:

1. validate writer-side profile constraints
2. pre-compress payloads where profile supports compression
3. calculate section sizes and absolute offsets
4. write file header
5. write chunk table
6. write manifest
7. write signature footer when required
8. write `UpdateSequence` trailer when present
9. write payloads in apply order
10. write `INCREMENTAL` metadata trailer when present

### Current writer-side hard requirements

- at least one chunk must exist
- apply order must be set and cover all chunks exactly once
- dependencies are rejected for profiles that do not support them
- `INCREMENTAL` requires patch metadata before build

---

## 3. Structural Sections

Current `v2` writer layout:

```text
[header]
[chunk table]
[manifest]
[signature footer]
[UpdateSequence trailer]
[payloads]
[INCREMENTAL metadata trailer]
```

Not every file contains every optional section, but this is the assembly order used by the current writer.

---

## 4. Header Parse Flow

Current reader parse flow starts by:

- checking minimum file size
- reading magic
- reading version
- selecting the `v1` or `v2` header layout
- validating expected header size
- validating profile byte for `v2`
- validating absolute offsets
- parsing update trailer metadata near payload boundaries

### Version split

Current reader logic branches early:

- `v1` uses the 36-byte header and defaults to profile `MINIMAL`
- `v2` uses the 37-byte header and reads explicit profile byte plus flags

---

## 5. Chunk Table Validation

Current `ValidateChunkTable()` covers:

- chunk count sanity
- chunk entry bounds
- payload size bounds
- payload offset bounds
- overlap checks across payloads
- basic per-entry structural validity

The chunk table is therefore part of fast structural validation, not only strict integrity validation.

---

## 6. Dependency Validation

Current `ValidateDependencies()` is only meaningful when dependency data is present.

Current repository behavior:

- `v1` may still carry dependency data for compatibility reasons
- `v2` requires a profile that supports dependencies
- cyclic or invalid dependency graphs are rejected

This means dependency support is both a format-level and profile-level rule.

---

## 7. Apply-Order Validation

Current `ValidateApplyOrder()` checks:

- apply order entries are structurally readable
- chunk references are valid
- required chunks are present
- duplicates and missing chunks are rejected

That makes apply order part of fast structural correctness, not just execution-time logic.

---

## 8. Signature Validation Flow

Current `VerifySignatureStrict()` is invoked from `ValidateFast()` when the profile requires strict signatures.

Current enforcement gates:

- signature footer must exist
- signature length must be exactly `64`
- signature bytes must stay within file bounds
- verification key must be present and `32` bytes
- Ed25519 verification over canonical manifest hash must succeed

If any of those checks fail, validation fails immediately.

---

## 9. Witness Validation Flow

Current `VerifyWitnessStrict()` is also part of fast validation for profiles that require it.

Current behavior:

- skipped for `v1`
- skipped when profile does not require witness strict semantics
- skipped if witness flag is not set
- otherwise expects the witness hash in the footer after the signature
- recomputes canonical BLAKE3 manifest hash
- rejects file if stored and computed witness hashes differ

This is how the current reader detects manifest tampering beyond CRC32 alone.

---

## 10. UpdateSequence Validation Flow

Current `ParseUpdateSequenceTrailer()` searches immediately before payloads for:

- magic `IUPDSEQ1`
- declared length `21`
- trailer version `1`
- `u64` sequence value

Current `VerifyUpdateSequenceStrict()` then:

- skips `v1`
- skips profiles without strict signature policy
- requires a parsed sequence for strict profiles
- optionally enforces monotonic progression through replay guard

So in current strict profiles, `UpdateSequence` is not decorative metadata; it is part of acceptance.

---

## 11. INCREMENTAL Metadata Validation Flow

Current `ParseIncrementalMetadataTrailer()` searches backward near EOF for:

- magic `IUPDINC1`
- reasonable trailer size
- parseable serialized metadata

Current `VerifyIncrementalStrict()` then requires for `INCREMENTAL`:

- metadata trailer present
- known algorithm ID
- base hash present

Known algorithm IDs in current code:

- `DELTA_V1`
- `IRONDEL2`

---

## 12. Fast Validation Summary

Current `ValidateFast()` includes:

- header parse
- chunk table validation
- dependency validation
- apply-order validation
- strict signature verification when required
- witness verification when required
- `UpdateSequence` verification when required
- incremental metadata verification when required

This is stronger than a typical “shape-only” fast validator. In current `IUPD`, fast validation already includes critical security gates.

---

## 13. Strict Validation Summary

Current `ValidateStrict()` performs:

1. all `ValidateFast()` checks first
2. per-chunk CRC32 verification
3. per-chunk BLAKE3 verification when required by profile
4. manifest CRC32 verification

### Practical meaning

- `ValidateFast()` answers “is this package structurally and policy-valid?”
- `ValidateStrict()` answers “is this package structurally valid and byte-integrity-correct?”

---

## 14. Writer/Reader Asymmetries

Current repository asymmetries that matter:

- writer emits `v2` only, reader accepts `v1` and `v2`
- writer can define `MINIMAL` and `FAST`, but reader default `v2` acceptance excludes them unless benchmark override is enabled
- writer auto-injects `UpdateSequence(1)` for `SECURE` and `OPTIMIZED`, but not universally
- native strict verifier docs currently describe a narrower accepted profile set than the `.NET` reader

These are compatibility-relevant differences, not incidental implementation details.

---

## 15. Native Cross-Reference

Current native headers related to this flow:

- `native/ironfamily_c/include/ironfamily/iupd_reader.h`
- `native/ironfamily_c/include/ironfamily/iupd_incremental_metadata.h`
- `native/ironfamily_c/include/ironfamily/delta2_apply.h`
- `native/ironfamily_c/include/ironfamily/diff_apply.h`

Current legacy in-memory C surface:

- `libs/ironcfg-c/include/ironcfg/iupd.h`

The strict native verifier should be treated as the active device-side verification path, while `libs/ironcfg-c` remains a separate older parse/validate/apply surface.

---

## 16. Fresh Test Context Already Confirmed In This Repository Session

Previously executed in this repository session:

- `.NET IUPD`: `253/253 passed`
- native `test_iupd_vectors.exe`: `6/6 passed`
- native `test_incremental_metadata.exe`: `10/10 passed`
- native `test_delta2_vectors.exe`: `2/2 passed`
- native `test_diff_vectors.exe`: `1/1 passed`

For current executed-now engine status, see `docs/ENGINE_TRUTH_SUMMARY.md`.
