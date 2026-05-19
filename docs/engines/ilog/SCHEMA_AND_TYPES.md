# ILOG Schema, Blocks, and Payload Types

**Status**: Current repository implementation
**Date**: 2026-04-06
**Scope**: `.NET` reader and encoder surfaces in `libs/ironconfig-dotnet/src/IronConfig.ILog/`

---

## 1. Scope

`ILOG` does not use a schema/type system like `ICFG`.

The relevant structure surface for `ILOG` is:

- file header
- profile flags
- block registry
- per-block payload layouts
- integrity and witness payload rules

This document describes that structure as it exists in the current implementation.

---

## 2. File Header

Current `.NET` reader and encoder use a `16` byte file header:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 4 | `u32LE` | magic = `ILOG` |
| `4` | 1 | `u8` | version = `0x01` |
| `5` | 1 | `u8` | flags |
| `6` | 2 | `u16LE` | reserved0 = `0` |
| `8` | 8 | `u64LE` | `toc_block_offset` |

Current open-time rules in the reader:

- magic must be `0x474F4C49`
- version must be `0x01`
- reserved bytes at `6..7` must be zero
- `toc_block_offset` must point to a readable block header inside the file

---

## 3. Flags And Profiles

### Flag bits

Current flags model:

| Bit | Meaning |
|---|---|
| `0` | little-endian marker |
| `1` | CRC32 enabled |
| `2` | BLAKE3 enabled |
| `3` | L2 present |
| `4` | L3 present |
| `5` | L4 present / witness-enabled path in audited files |

### Profile mapping

Current `.NET` profile mapping:

| Profile | Flags | L0 | L1 | L2 | L3 | L4 |
|---|---|---|---|---|---|---|
| `MINIMAL` | `0x01` | yes | yes | no | no | no |
| `INTEGRITY` | `0x03` | yes | yes | no | no | yes |
| `SEARCHABLE` | `0x09` | yes | yes | yes | no | no |
| `ARCHIVED` | `0x11` | no | yes | no | yes | no |
| `AUDITED` | `0x27` | yes | yes | no | no | yes |

Important implementation fact:

- `ARCHIVED` is storage-first and omits raw `L0`
- `AUDITED` uses bit `5` as the witness-enabled signal

---

## 4. Block Registry

Current block type registry:

| Block | Type | Meaning |
|---|---:|---|
| `L0_DATA` | `0x0001` | primary raw event payload |
| `L1_TOC` | `0x0002` | table of layers/blocks |
| `L2_INDEX` | `0x0003` | search index |
| `L3_ARCHIVE` | `0x0004` | compressed archive payload |
| `L4_SEAL` | `0x0005` | integrity / audited seal |

---

## 5. Block Header

Current reader and encoder both use a `72` byte block header.

The implementation-level layout currently consumed by the reader is:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 4 | `u32LE` | block magic = `BLK1` |
| `4` | 2 | `u16LE` | block type |
| `6` | 2 | `u16LE` | reserved1 |
| `8` | 8 | `u64LE` | block timestamp |
| `16` | 4 | `u32LE` | payload size |
| `20` | 4 | `u32LE` | payload CRC32 |
| `24` | 32 | bytes | payload BLAKE3 mirror or zeros |
| `56` | 16 | bytes | reserved padding |

Important implementation note:

- `ValidateFast()` contains a tolerant path for older or alternate header interpretations
- current encoder writes timestamp at offset `8`
- current strict validation relies mainly on payload size, CRC32, and mirrored BLAKE3 bytes where applicable

---

## 6. Primary Payload Semantics

Current primary payload resolution:

- if `L0_DATA` exists, it is the primary payload block
- if no `L0_DATA` exists but `L3_ARCHIVE` exists and flags say `L3`, then `L3_ARCHIVE` becomes the primary payload block

This is how the current implementation supports `ARCHIVED` as a storage-first profile.

---

## 7. L0_DATA Payload

Current payload layout:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 1 | `u8` | stream version = `0x01` |
| `1` | 4 | `u32LE` | event count |
| `5` | 8 | `u64LE` | timestamp epoch ms |
| `13` | variable | bytes | event data |

Current encoder behavior:

- whole input payload is encoded as one event
- event count is currently written as `1`
- timestamp is `0` in deterministic mode and current UTC ms otherwise

Current reader behavior:

- `stream_version` must be `0x01`
- payload shorter than `13` bytes is invalid for `L0`
- decoder strips the first `13` bytes and returns the remaining event data

This means the current implementation treats `L0` as a framed byte payload, not a rich typed record schema.

---

## 8. L1_TOC Payload

Current `L1` payload begins with:

- `toc_version: u8`

For `AUDITED`, the encoder then writes an additional witness header:

| Field | Size |
|---|---:|
| `witness_version` | 1 |
| `reserved` | 1 |
| `prev_seal_hash` | 32 |

After that, the encoder writes:

- `layer_count: u32LE`
- one layer entry per declared layer

### Layer entry layout

Each layer entry is `18` bytes:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 2 | `u16LE` | layer type |
| `2` | 4 | `u32LE` | block count |
| `6` | 4 | `u32LE` | flags / reserved for future use |
| `10` | 8 | bytes | reserved |

### Current audited witness behavior

Current strict verification logic:

- bit `5` in file flags enables witness expectation
- `toc_version == 2` is still accepted as a legacy witness path
- witness header must contain `witness_version == 1`
- witness reserved byte must be zero
- `prev_seal_hash` must be all zeros in the current single-block model

---

## 9. L2_INDEX Payload

Current `L2` payload layout:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 1 | `u8` | index version = `0x01` |
| `1` | 1 | `u8` | index type = `0x00` |
| `2` | 4 | `u32LE` | number of entries |
| `6` | repeated | entries | `(offset:u32LE, size:u32LE)` |

Each index entry is `8` bytes:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 4 | `u32LE` | byte offset within `L0` payload |
| `4` | 4 | `u32LE` | indexed chunk size |

Current encoder behavior:

- index is generated in `4096` byte chunks over the `L0` payload
- `SEARCHABLE` is the only current producer of `L2`

Current decoder behavior:

- reads `index_version`
- reads `index_type`
- reads `number_of_entries`
- then decodes `(offset, size)` pairs

---

## 10. L3_ARCHIVE Payload

Current `L3` payload is the compressed byte stream returned by `IlogCompressor.Compress(eventData)`.

Implementation-level facts:

- `ARCHIVED` writes `L3` as the primary payload carrier
- current decoder uses `IlogCompressor.TryDecompress(...)`
- strict validation rejects `L3` payloads that cannot be decompressed safely

This repository documents the compressor as an internal `ILOG` archive codec surface, not as a standalone typed schema.

---

## 11. L4_SEAL Payload

Current `L4` payload begins with:

| Offset | Size | Type | Field |
|---|---:|---|---|
| `0` | 1 | `u8` | seal version = `0x01` |
| `1` | 1 | `u8` | seal type = `0x00` |
| `2` | 1 | `u8` | coverage type = `0x01` |
| `3` | 1 | `u8` | reserved |
| `4` | 32 | bytes | hash area |

After the `32` byte hash area, current encoder writes:

| Field | Size |
|---|---:|
| algorithm id | 1 |
| optional signature length | 4 |
| optional signature bytes | variable |

### INTEGRITY profile meaning

For `INTEGRITY`:

- first `4` bytes of the hash area store the `L0` CRC32
- remaining hash bytes stay zero
- strict validation compares this stored CRC32 against computed `L0` CRC32

### AUDITED profile meaning

For `AUDITED`:

- hash area stores BLAKE3-256 over `L0` payload
- current encoder also mirrors that BLAKE3 value into block header bytes `24..55`
- strict validation compares:
  - header mirror vs payload hash
  - payload hash vs recomputed BLAKE3 over `L0`

### Signature semantics

Current encoder behavior:

- signature is optional even for `AUDITED`
- if keys are supplied in `IlogEncodeOptions`, it signs the hash
- algorithm id `1` denotes the current internal Ed25519-based signing path
- algorithm id `0` means no signature bytes were emitted

Current reader/decoder behavior:

- strict validation in `IlogReader.ValidateStrict()` enforces CRC32 and BLAKE3 integrity semantics
- `IlogDecoder.Verify()` additionally checks witness-related `L1` expectations
- the current public docs should not overstate full signature-chain enforcement beyond what these code paths currently do

---

## 12. Validation-Relevant Structural Rules

Current implementation rules that matter most:

- file header must be parseable and version `0x01`
- `L0` payloads must have a `13` byte framing header
- `L3` payloads must be decompressible if present
- `INTEGRITY` expects `L4` and matching CRC32
- `AUDITED` expects `L4`, non-zero BLAKE3 hash, and witness-compatible `L1`
- audited `L4` header mirror and payload hash must match

---

## 13. Fresh Test Coverage Already Confirmed In This Repository Session

Fresh `.NET` test result already executed in this repository session:

- `IronConfig.ILog.Tests`: `144/144 passed`

Relevant test groups visible in the repository:

- `IlogEncoderTests.cs`
- `IlogCompressorTests.cs`
- `IlogWitnessChainTests.cs`
- `ProfileBackcompatTests.cs`
- `IlogStrictRegressionTests.cs`
- `SpecLockTests.cs`

For broader engine status and executed-now evidence, see `docs/ENGINE_TRUTH_SUMMARY.md`.
