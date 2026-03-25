# IRONDEL2 - Patch Format Specification (MVP)

**Version**: 1.0 MVP
**Date**: 2026-03-05
**Scope**: Binary format definition for Delta v2 (Content-Defined Chunking)

---

## Overview

IRONDEL2 is a deterministic delta format optimized for "shift/insert" patterns using content-defined chunking (CDC). It encodes base→target deltas as a stream of COPY and LIT operations with optional BLAKE3 integrity checking.

**Design Goals**:
- 5× smaller than IUPDDEL1 on insert/shift workloads
- Deterministic: identical inputs → identical patch bytes
- Fail-closed: strict format validation
- Streaming-friendly: Apply can process ops sequentially

---

## File Structure

### Header (Fixed 100 bytes)

| Offset | Field | Type | Value/Notes |
|--------|-------|------|-------------|
| 0–7 | magic | [8]u8 | "IRONDEL2" (ASCII) |
| 8 | version | u8 | 0x01 |
| 9 | flags | u8 | 0x00 (reserved for future use) |
| 10–11 | reserved | u16 | 0x0000 (reserved for future use) |
| 12–19 | base_len | u64 | Length of base input (little-endian) |
| 20–27 | target_len | u64 | Expected length of reconstructed output (little-endian) |
| 28–59 | base_blake3_256 | [32]u8 | BLAKE3-256 hash of base input |
| 60–91 | target_blake3_256 | [32]u8 | BLAKE3-256 hash of expected output |
| 92–95 | op_count | u32 | Number of operations in patch (little-endian) |
| 96–99 | header_crc32 | u32 | CRC32 of header with this field zeroed (little-endian) |

**Total**: 100 bytes

**Endianness**: Little-endian (LE) throughout

**Header Validation** (fail-closed):
1. Magic must be exactly "IRONDEL2"
2. Version must be 0x01
3. Flags must be 0x00
4. Reserved must be 0x0000
5. base_len must match provided base length
6. op_count must be > 0 and ≤ MAX_OPS
7. header_crc32 must match CRC32 of header[0:96] with header[96:100] = [0,0,0,0]

---

### Operation Stream (Variable)

Immediately follows header at offset 100. Each operation is:

#### COPY Operation (opcode 0x01)

```
Offset | Field | Type | Notes
-------|-------|------|-------
0      | opcode | u8 | 0x01
1–8    | base_offset | u64 | Byte offset in base to copy from (LE)
9–12   | length | u32 | Number of bytes to copy (LE)
```

**Total**: 13 bytes per COPY

**Validation**:
- `base_offset + length ≤ base_len` (no overrun)
- `length ≤ MAX_COPY_LEN` (1GB per op)

---

#### LIT Operation (opcode 0x02)

```
Offset | Field | Type | Notes
-------|-------|------|-------
0      | opcode | u8 | 0x02
1–4    | length | u32 | Number of literal bytes (LE)
5...   | data | [length]u8 | Raw bytes to append to output
```

**Total**: 5 + length bytes

**Validation**:
- `length ≤ MAX_LIT_BYTES` (cannot exceed target_len)
- Sufficient bytes available in patch

---

### Validation Rules (Fail-Closed)

**Format**:
1. Header magic/version/flags/reserved match exactly
2. Header CRC32 correct
3. op_count ≤ MAX_OPS
4. Ops stream parseable without truncation

**Semantic**:
5. Cumulative COPY+LIT output length == target_len (exact match)
6. All COPY base_offset ranges within [0, base_len)
7. All LIT lengths within bounds

**Integrity** (optional, default ON):
8. Computed base_blake3 must match header field
9. Computed target_blake3 (from output) must match header field

**DoS Limits** (enforce):
- MAX_OPS = 20,000,000 (ops per patch)
- MAX_LIT_BYTES = target_len (hard cap, cannot exceed target)
- MAX_COPY_LEN = 1,073,741,824 (1GB per single COPY op)

---

## Algorithm: Create (Deterministic)

### Input
- `base`: base firmware bytes
- `target`: target firmware bytes

### Output
- `patch`: IRONDEL2 patch bytes (deterministic)

### Process

1. **Hash Computation**
   - `base_blake3 ← BLAKE3-256(base)`
   - `target_blake3 ← BLAKE3-256(target)`

2. **CDC Chunking** (deterministic)
   - Apply CdcChunker to both base and target
   - Parameters (frozen):
     - MIN_CHUNK = 2048 bytes
     - AVG_CHUNK = 4096 bytes
     - MAX_CHUNK = 8192 bytes
     - WINDOW_SIZE = 48 bytes
   - Output: List of (offset, length) chunk boundaries

3. **Chunk Matching** (deterministic)
   - For each target chunk:
     - Compute BLAKE3-128 (first 16 bytes of BLAKE3-256) of chunk
     - Look up in base chunk index (built in step 2)
     - If hash match found AND byte-exact match verified:
       - Emit COPY(base_offset, chunk_len)
     - Else:
       - Emit LIT(chunk_len, chunk_bytes)

4. **Merge Adjacent Ops** (deterministic)
   - Consecutive COPY with contiguous base_offset → merge
   - Consecutive LIT → merge
   - Result: minimal op count

5. **Header Generation**
   - Construct 100-byte header (all fields)
   - Compute CRC32 of header[0:96] with [96:100] = [0,0,0,0]
   - Write header_crc32 field

6. **Serialization**
   - Write header (100 bytes)
   - Write ops in order (variable)
   - Return patch bytes

---

## Algorithm: Apply (Deterministic)

### Input
- `base`: base firmware bytes
- `patch`: IRONDEL2 patch bytes

### Output
- `output`: reconstructed firmware bytes
- `error`: IupdError (fail-closed)

### Process

1. **Header Parsing & Validation**
   - Read magic, version, flags, reserved (8 bytes)
     - Fail if magic ≠ "IRONDEL2"
     - Fail if version ≠ 0x01
     - Fail if flags ≠ 0x00 or reserved ≠ 0x0000
   - Read base_len, target_len (8 + 8 bytes)
     - Fail if base_len ≠ length(base)
   - Read base_blake3, target_blake3 (32 + 32 bytes)
   - Read op_count (4 bytes)
     - Fail if op_count = 0 or > MAX_OPS
   - Read header_crc32 (4 bytes)
   - Verify CRC32:
     - Compute CRC32(header[0:96] with [96:100] = [0,0,0,0])
     - Fail if ≠ header_crc32

2. **Base Hash Verification** (optional, default ON)
   - Compute BLAKE3-256(base)
   - Fail if ≠ base_blake3

3. **Op Stream Execution** (streaming-friendly)
   - Initialize output buffer (reserve target_len if possible)
   - Offset = 100 (ops start)
   - For each op (up to op_count):
     - Parse opcode at offset
     - If 0x01 (COPY):
       - Parse base_offset (u64), length (u32)
       - Fail if base_offset + length > base_len
       - Append base[base_offset:base_offset+length] to output
       - Offset += 13
     - Else if 0x02 (LIT):
       - Parse length (u32)
       - Parse data[0:length]
       - Fail if offset + 5 + length > patch.len
       - Append data to output
       - Offset += 5 + length
     - Else:
       - Fail with "Invalid opcode"

4. **Output Validation** (fail-closed)
   - Length(output) must == target_len (exact)
   - Fail if mismatch

5. **Target Hash Verification** (optional, default ON)
   - Compute BLAKE3-256(output)
   - Fail if ≠ target_blake3

6. **Return**
   - Success: return output
   - Failure: return empty array + error details

---

## Determinism Guarantees

1. **CDC Boundaries**: Frozen chunk parameters (MIN/AVG/MAX/WINDOW) ensure identical chunk boundaries across platforms
2. **Chunk Matching**: Deterministic hash lookup + sorted matching (lowest offset first) ensures reproducible chunks
3. **Op Merging**: Deterministic order and merging rules
4. **Serialization**: Little-endian fixed-size fields
5. **Header CRC32**: Computed deterministically from frozen algorithm

**Test**: Same base+target always produces identical patch bytes (SHA256 verified)

---

## Performance & Compression Targets

### Synthetic Workloads

| Scenario | Base Size | Target Size | Delta v1 | Delta v2 | Ratio |
|----------|-----------|-------------|----------|----------|-------|
| Insert at start | 512MB | 512MB+16KB | ~16KB+metadata | <3.2KB | <5× |
| Middle insert | 512MB | 512MB+32KB | ~32KB+metadata | <6.4KB | <5× |
| Random edits | 512MB | 512MB (2%diff) | ~10MB | ~2MB | <5× |

**Target**: v2 patch ≤ v1 patch / 5 (or test fails)

---

## CRC32 Algorithm

**Polynomial**: 0x04C11DB7 (ISO)
**Initial Value**: 0xFFFFFFFF
**Final XOR**: 0xFFFFFFFF
**Reflected**: Yes (standard)

Standard C# `System.IO.Compression.Crc32` or equivalent.

---

## Error Codes (Integration with IupdError)

| Code | Meaning |
|------|---------|
| DELTA_MALFORMED | Format violation (truncated, invalid opcode) |
| DELTA_MAGIC_MISMATCH | Magic ≠ "IRONDEL2" |
| DELTA_BASE_HASH_MISMATCH | base_blake3 doesn't match input |
| DELTA_TARGET_HASH_MISMATCH | target_blake3 doesn't match output |
| DELTA_ENTRY_OUT_OF_RANGE | COPY outside base bounds |
| DELTA_SIZE_MISMATCH | Output length ≠ target_len |
| DELTA_VERSION_UNSUPPORTED | Version ≠ 0x01 |

---

## Non-Goals

- **Compression**: Delta v2 MVP is uncompressed (future: deflate/zstd)
- **Streaming Header Writes**: Header fully constructed before output
- **Fallback to v1**: Tests validate ratio; no auto-fallback (manual choice)
- **Native Implementation**: C# only for MVP (C99 later if needed)

---

## Compatibility

- **Delta v1 Unchanged**: IUPDDEL1 remains production format
- **Version Field**: Allows future v3 formats
- **Flags/Reserved**: Provides extension points

---

**Status**: SPEC FROZEN for IRONDEL2 MVP
