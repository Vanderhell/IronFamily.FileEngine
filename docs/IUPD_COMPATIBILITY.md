# IUPD Compatibility and Surface Matrix

**Status**: Current repository implementation
**Date**: 2026-04-06
**Scope**: Compatibility rules across `.NET`, `native/ironfamily_c`, and `libs/ironcfg-c`

---

## 1. Scope

`IUPD` compatibility in this repository is not one flat rule set.

There are three materially different surfaces:

- `.NET` reader/writer in `libs/ironconfig-dotnet`
- native strict verifier in `native/ironfamily_c`
- legacy in-memory C API in `libs/ironcfg-c`

This document separates them explicitly.

---

## 2. Version Matrix

| Surface | Reads v1 | Reads v2 | Writes v1 | Writes v2 | Notes |
|---|---|---|---|---|---|
| `.NET` reader | yes | yes | n/a | n/a | current compatibility reader |
| `.NET` writer | no | yes | no | yes | current emitted format is `v2` |
| `native/ironfamily_c` strict verifier | no | yes | no | no | verification-only `v2` surface |
| `libs/ironcfg-c` in-memory API | yes-style surface | not documented as full `v2` strict verifier | no | no | older compatibility surface |

### Practical truth

- if you need the active writer path, use `.NET v2`
- if you need the active strict native verification path, use `native/ironfamily_c v2`
- if you need the older in-memory C compatibility API, treat `libs/ironcfg-c` separately instead of assuming parity with the strict verifier

---

## 3. `.NET` Reader Compatibility

Current `.NET` reader accepts:

- `v1`
- `v2`

### `v1`

Current compatibility behavior:

- accepted by header parser
- mapped to profile `MINIMAL`
- does not require `v2` profile byte
- does not go through `v2`-only witness and profile-byte header rules

### `v2`

Current compatibility behavior:

- accepted by header parser
- uses explicit profile byte
- uses `37` byte header layout
- enables witness, UpdateSequence, and incremental metadata logic according to profile

---

## 4. `.NET` Writer Compatibility

Current `.NET` writer behavior:

- emits `v2` only
- does not emit `v1`
- uses current `v2` header layout
- writes profile byte
- writes signature footer when profile requires strict signature semantics
- writes `UpdateSequence` trailer when provided, and auto-injects `UpdateSequence(1)` for `SECURE` and `OPTIMIZED`
- requires incremental metadata for `INCREMENTAL`

### Resulting compatibility rule

Current writer output should be described as:

- `v2-native` active repository output

It should not be described as a writer that targets both `v1` and `v2`.

---

## 5. Profile Acceptance Matrix

### Defined profiles in `.NET`

| Profile | Byte | Defined | Default writer support | Default reader acceptance |
|---|---:|---|---|---|
| `MINIMAL` | `0x00` | yes | yes | no for `v2` unless bench override |
| `FAST` | `0x01` | yes | yes | no for `v2` unless bench override |
| `SECURE` | `0x02` | yes | yes | yes |
| `OPTIMIZED` | `0x03` | yes | yes | yes |
| `INCREMENTAL` | `0x04` | yes | yes | yes |

### Current default `v2` acceptance in `.NET`

Accepted by default:

- `SECURE`
- `OPTIMIZED`
- `INCREMENTAL`

Rejected by default:

- `MINIMAL`
- `FAST`

Override:

- `IRONFAMILY_BENCH_ALLOW_ALL_IUPD_PROFILES=1`

### Important consequence

There is a difference between:

- profile being defined in code
- profile being writable
- profile being accepted by default in strict repository validation

Those are not the same compatibility claim.

---

## 6. Integrity Compatibility

### CRC32

Current compatibility facts:

- all profiles carry per-chunk CRC32 in chunk entries
- strict validation checks chunk CRC32
- strict validation also checks manifest CRC32

### BLAKE3

Current compatibility facts:

- required for `SECURE`
- required for `OPTIMIZED`
- required for `INCREMENTAL`
- not required for `MINIMAL`
- not required for `FAST`

### Witness

Current compatibility facts:

- witness is a `v2` path
- witness verification is skipped for `v1`
- witness is enforced only for profiles that require witness strict semantics
- current writer sets the witness-enabled flag for those profiles

---

## 7. Signature Compatibility

### `.NET`

Current `.NET` compatibility rules:

- signature footer is required for `SECURE`
- signature footer is required for `OPTIMIZED`
- signature footer is required for `INCREMENTAL`
- signature length must be exactly `64`
- verification key must be `32` bytes
- signature algorithm path is Ed25519-based

### Native strict verifier

Current `native/ironfamily_c` docs state a narrower profile gate:

- profile must be `SECURE` or `OPTIMIZED`

That means the public native strict verifier documentation is currently narrower than the `.NET` reader’s accepted default `v2` profile set.

### Compatibility takeaway

Do not claim that every `.NET`-accepted strict profile is already identical to the documented native strict verifier contract.

---

## 8. UpdateSequence Compatibility

### `.NET`

Current `.NET` behavior:

- `UpdateSequence` trailer format is understood by the reader
- for profiles requiring strict signature policy, missing `UpdateSequence` is a validation failure
- optional replay guard can enforce monotonic progression

### Builder behavior

Current builder behavior:

- auto-injects `UpdateSequence(1)` for `SECURE`
- auto-injects `UpdateSequence(1)` for `OPTIMIZED`
- does not auto-inject it for every profile

### Native strict verifier

Current native strict verifier documents:

- `expected_min_update_sequence`
- extracted `out_update_sequence`
- anti-replay validation

So `UpdateSequence` is part of the active strict compatibility surface, not just an optional extension.

---

## 9. INCREMENTAL Compatibility

### `.NET`

Current `.NET` `INCREMENTAL` requirements:

- profile byte must be `INCREMENTAL`
- signature strict path applies
- witness strict path applies
- metadata trailer must exist
- metadata algorithm must be known
- base hash must be present

Known metadata algorithms:

- `DELTA_V1` = legacy compatibility
- `IRONDEL2` = active production path

### Native surfaces

Current repository native surfaces include:

- incremental metadata parser in `native/ironfamily_c`
- delta2 apply path in `native/ironfamily_c`
- diff apply path in `native/ironfamily_c`

But this still should not be described as one monolithic native `IUPD` API with full parity everywhere.

---

## 10. Native Surface Split

## 10.1 `native/ironfamily_c`

Current role:

- strict device-side `v2` verification
- replay-aware verification
- Ed25519 public-key verification
- incremental metadata and delta support in adjacent headers

This is the active strict native path.

## 10.2 `libs/ironcfg-c`

Current role:

- older in-memory parse/validate/apply API
- exposes `IUPD_VERSION 0x01`
- not documented as the same thing as the strict `v2` verifier

This means compatibility statements must keep these two C surfaces distinct.

---

## 11. Safe Compatibility Claims

- `.NET` currently reads `v1` and `v2`
- `.NET` currently writes `v2`
- active strict native verification surface is `v2`
- `libs/ironcfg-c` exposes a separate older `IUPD` C API surface
- `SECURE`, `OPTIMIZED`, and `INCREMENTAL` are the default accepted strict `v2` profiles in `.NET`
- `MINIMAL` and `FAST` are defined and writable in `.NET`, but not default-accepted for `v2` verification unless benchmark override is enabled
- `INCREMENTAL` has additional compatibility requirements beyond base `v2` packaging

---

## 12. Claims To Avoid

- do not claim that all three surfaces share one identical `IUPD` compatibility contract
- do not claim that the `.NET` writer emits both `v1` and `v2`
- do not claim that `libs/ironcfg-c` is the same as the active strict native verifier
- do not claim that `MINIMAL` and `FAST` are default-accepted `v2` strict profiles
- do not claim full native parity for every `IUPD` path unless that specific path was verified separately
