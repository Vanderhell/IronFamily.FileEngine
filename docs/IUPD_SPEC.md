# IUPD Format Specification

**Status**: Current repository implementation
**Date**: 2026-04-06
**Scope**: `.NET` reference implementation plus currently exposed native verification surfaces

---

## 1. Scope

`IUPD` is the update package format in this repository.

This document is intentionally limited to claims that are directly supported by the current codebase:

- `.NET` writer and reader in `libs/ironconfig-dotnet/src/IronConfig/Iupd/`
- native strict verifier surface in `native/ironfamily_c/include/ironfamily/`
- legacy in-memory C API surface in `libs/ironcfg-c/include/ironcfg/iupd.h`

Where the surfaces differ, this document calls that out explicitly instead of flattening them into one story.

---

## 2. Magic And Versions

### Primary magic values

- `0x44505549` = ASCII `IUPD` in little-endian
- `0x31445055` = ASCII `UPD1` in little-endian

### Version support in the current repository

- `.NET` reader accepts `v1` and `v2`
- `.NET` writer emits `v2`
- native strict verifier in `native/ironfamily_c` is scoped to `v2`
- legacy `libs/ironcfg-c` header exposes a `v1`-style in-memory reader API

This means the repository currently contains both backward-compatible `.NET` handling and split native surfaces rather than one single unified native `IUPD` API.

---

## 3. Profiles

The active `.NET` implementation defines five named profiles:

| Profile | Byte | Compression | BLAKE3 | Dependencies | Signature strict | Witness strict | Notes |
|---|---|---|---|---|---|---|---|
| `MINIMAL` | `0x00` | no | no | no | no | no | smallest overhead |
| `FAST` | `0x01` | yes | no | no | no | no | compressed payloads |
| `SECURE` | `0x02` | no | yes | yes | yes | yes | cryptographic validation |
| `OPTIMIZED` | `0x03` | yes | yes | yes | yes | yes | current default writer profile |
| `INCREMENTAL` | `0x04` | yes | yes | yes | yes | yes | patch-bound update flow |

### Current verification policy in `.NET`

The `.NET` reader does not treat every defined profile as equally acceptable by default.

Allowed by default for `v2` verification:

- `SECURE`
- `OPTIMIZED`
- `INCREMENTAL`

Not accepted by default for `v2` verification unless benchmark override is enabled:

- `MINIMAL`
- `FAST`

Benchmark override environment variable:

- `IRONFAMILY_BENCH_ALLOW_ALL_IUPD_PROFILES=1`

---

## 4. File Layout

Current `v2` writer layout:

```text
[file header]
[chunk table]
[manifest]
[signature footer, when required]
[UpdateSequence trailer, when present]
[payloads]
[INCREMENTAL metadata trailer, when present]
```

Current `v1` reader compatibility path uses the older `v1` header layout and defaults the profile to `MINIMAL`.

---

## 5. V2 File Header

**Header size**: `37` bytes

| Offset | Size | Type | Field | Meaning |
|---|---:|---|---|---|
| `0` | 4 | `u32LE` | magic | `IUPD` |
| `4` | 1 | `u8` | version | `0x02` |
| `5` | 1 | `u8` | profile | one of the defined `IupdProfile` values |
| `6` | 4 | `u32LE` | flags | currently witness flag uses bit 0 |
| `10` | 2 | `u16LE` | header_size | must be `37` |
| `12` | 1 | `u8` | reserved | currently `0` |
| `13` | 8 | `u64LE` | chunk_table_offset | absolute byte offset |
| `21` | 8 | `u64LE` | manifest_offset | absolute byte offset |
| `29` | 8 | `u64LE` | payload_offset | absolute byte offset |

### Defined `v2` header flag

- bit `0`: `IUPD_WITNESS_ENABLED`

The `.NET` writer sets this flag for profiles that require witness verification.

---

## 6. V1 File Header

The `.NET` reader still supports `v1`.

**Header size**: `36` bytes

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 4 | `u32LE` | magic |
| `4` | 1 | `u8` | version |
| `5` | 4 | `u32LE` | flags |
| `9` | 2 | `u16LE` | header_size |
| `11` | 1 | `u8` | reserved |
| `12` | 8 | `u64LE` | chunk_table_offset |
| `20` | 8 | `u64LE` | manifest_offset |
| `28` | 8 | `u64LE` | payload_offset |

The `.NET` reader maps `v1` files to profile `MINIMAL` for compatibility.

---

## 7. Chunk Table

Each chunk table entry is `56` bytes:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 4 | `u32LE` | chunk_index |
| `4` | 8 | `u64LE` | payload_size |
| `12` | 8 | `u64LE` | payload_offset |
| `20` | 4 | `u32LE` | payload_crc32 |
| `24` | 32 | `bytes` | payload_blake3 |

### Integrity meaning

- CRC32 is checked for every chunk in strict validation
- BLAKE3 is checked in strict validation for profiles that require it

### Payload meaning

- chunk table stores payload offset and stored payload size
- when a profile supports compression, the stored payload may be wrapped compressed data
- CRC32 and BLAKE3 are computed against the original uncompressed payload in the writer

---

## 8. Manifest

The current `.NET` writer emits a `24` byte manifest header followed by dependency entries, apply-order entries, and an `8` byte integrity footer.

### Manifest header

| Offset | Size | Type | Field | Meaning |
|---|---:|---|---|---|
| `0` | 1 | `u8` | manifest_version | currently `0x02` in the writer |
| `1` | 3 | bytes | reserved | currently zeroed |
| `4` | 4 | `u32LE` | target_version | writer stores the profile byte here in `v2` |
| `8` | 4 | `u32LE` | dependency_count | number of dependency edges |
| `12` | 4 | `u32LE` | apply_order_count | number of apply-order entries |
| `16` | 8 | `u64LE` | manifest_size | total manifest size including trailing integrity footer |

### Manifest body

- dependency entries: `8` bytes each
  - `from` chunk: `u32LE`
  - `to` chunk: `u32LE`
- apply-order entries: `4` bytes each
  - chunk index: `u32LE`

### Manifest footer

Last `8` bytes of manifest:

- manifest CRC32: `4` bytes
- reserved: `4` bytes

### Canonical manifest hash range

The signature and witness hash are computed over:

- manifest bytes from `manifest_offset`
- through `manifest_size - 8`

In other words, the last `8` bytes of the manifest are excluded from the canonical hash range.

---

## 9. Signature Footer

For profiles that require strict signature verification, the current `.NET` implementation expects a signature footer immediately after the manifest.

Layout:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 4 | `u32LE` | signature_length |
| `4` | 64 | bytes | Ed25519 signature |
| `68` | 32 | bytes | witness hash |

### Current rules

- signature length must be exactly `64`
- public key length must be exactly `32`
- signature verification uses Ed25519
- manifest canonical hash uses BLAKE3-256
- witness hash is the same canonical BLAKE3-256 manifest hash stored in the footer

### Profiles that currently require it in `.NET`

- `SECURE`
- `OPTIMIZED`
- `INCREMENTAL`

The native strict verifier in `native/ironfamily_c` documents a narrower profile whitelist centered on strict secure verification rather than the full `.NET` profile matrix.

---

## 10. UpdateSequence Trailer

Current trailer layout:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 8 | bytes | magic = `IUPDSEQ1` |
| `8` | 4 | `u32LE` | length = `21` |
| `12` | 1 | `u8` | trailer version = `1` |
| `13` | 8 | `u64LE` | sequence |

### Placement

- after signature footer
- before payloads

### Current verification behavior

- optional for formats that do not require strict signature policy
- required by the `.NET` reader for profiles that require strict signature policy
- replay enforcement can additionally compare the sequence against a caller-provided replay guard

The fluent `.NET` builder auto-injects `UpdateSequence(1)` for `SECURE` and `OPTIMIZED` when not explicitly provided.

---

## 11. INCREMENTAL Metadata Trailer

The `INCREMENTAL` profile uses a metadata trailer written after payloads.

Magic:

- `IUPDINC1`

Current serialized structure:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 8 | bytes | magic |
| `8` | 4 | `u32LE` | total trailer length |
| `12` | 1 | `u8` | trailer version = `1` |
| `13` | 1 | `u8` | algorithm_id |
| `14` | 1 | `u8` | base_hash_length |
| `15` | variable | bytes | base_hash |
| `...` | 1 | `u8` | target_hash_length |
| `...` | variable | bytes | target_hash |
| `...` | 4 | `u32LE` | CRC32 over trailer bytes before the CRC field |

### Known algorithm IDs

| Algorithm | Byte | Status |
|---|---:|---|
| `DELTA_V1` | `0x01` | legacy compatibility |
| `IRONDEL2` | `0x02` | active production path |

### Current `.NET` requirements for `INCREMENTAL`

- metadata trailer must exist
- algorithm ID must be known
- base hash must be present

---

## 12. Validation Levels

### `.NET` fast validation

Current fast validation includes:

- header parsing
- chunk table validation
- dependency validation
- apply-order validation
- required signature verification
- witness verification
- UpdateSequence verification
- INCREMENTAL metadata verification

### `.NET` strict validation

Strict validation adds:

- per-chunk CRC32 verification
- per-chunk BLAKE3 verification when required by profile
- manifest CRC32 verification

---

## 13. Limits

Current `.NET` reader limits:

- max chunks: `1,000,000`
- max chunk size: `1 GiB`
- max manifest size: `100 MiB`

These are implementation limits, not just documentation guidance.

---

## 14. Native Surfaces

## 14.1 `native/ironfamily_c`

Public strict verifier entry point:

- `iron_iupd_verify_strict(...)`

Current documented purpose:

- minimal device-side strict verification
- Ed25519 signature verification
- UpdateSequence validation
- DoS limit enforcement

Related native headers also present:

- `iupd_incremental_metadata.h`
- `delta2_apply.h`
- `diff_apply.h`
- `ota_apply.h`

## 14.2 `libs/ironcfg-c`

Public in-memory C API surface:

- `iupd_open`
- `iupd_validate_fast`
- `iupd_validate_strict`
- `iupd_apply_begin`
- `iupd_apply_next`
- `iupd_apply_end`

Important truth:

- this header still exposes older `IUPD_VERSION 0x01` style constants
- it should not be described as the same surface as the stricter `native/ironfamily_c` `v2` verifier

---

## 15. Fresh Test State Already Confirmed In This Repository Session

Fresh `.NET` result previously executed in this repository session:

- `IronConfig.Iupd.Tests`: `253/253 passed`

Fresh native executable results previously executed in this repository session:

- `native/build/tests/Release/test_iupd_vectors.exe`: `6/6 passed`
- `native/build/tests/Release/test_incremental_metadata.exe`: `10/10 passed`
- `native/build/tests/Release/test_delta2_vectors.exe`: `2/2 passed`
- `native/build/tests/Release/test_diff_vectors.exe`: `1/1 passed`

These results are summarized together with code evidence in `docs/ENGINE_TRUTH_SUMMARY.md`.

