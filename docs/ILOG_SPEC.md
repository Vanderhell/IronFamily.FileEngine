# ILOG Format Specification (v1)

**Status**: Production (verified by execution)
**Last Updated**: 2026-03-14
**Source of Truth**: Live code in libs/ironconfig-dotnet/src/IronConfig.ILog/ and libs/ironcfg-c/src/ilog.c

---

## 1. MAGIC AND VERSION

**File Magic**: `0x474F4C49` (little-endian) = ASCII "ILOG"
**Format Version**: 0x01 (byte)
**Endianness**: Little-endian only (bit 0 of flags always = 1)

---

## 2. FILE HEADER

**Size**: 16 bytes
**Location**: Bytes 0-15 of file
**Layout**:
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       4     u32LE     magic             0x474F4C49 ("ILOG")
4       1     u8        version           0x01
5       1     u8        flags             Profile flags (see section 3)
6       2     u16LE     reserved          Must be 0x0000
8       8     u64LE     toc_offset        Absolute byte offset of L1 (TOC) block header
```

**Validation**:
- Magic must equal 0x474F4C49
- Version must equal 0x01
- Reserved must be 0x0000
- toc_offset must be >= 16 (after file header)
- toc_offset must be < file size

---

## 3. FLAGS BYTE (byte 5)

**Bit Layout**:
```
Bit   Meaning                  Active In Profiles
---   -------------------     ------------------
0     Little-Endian (always 1) All
1     Has CRC32 (L4)          INTEGRITY, AUDITED
2     Has BLAKE3 (L4)         AUDITED
3     Has L2 (INDEX)          SEARCHABLE
4     Has L3 (ARCHIVE)        ARCHIVED
5     Witness Enabled (L4)    AUDITED
6-7   Reserved                All (must be 0)
```

**Valid Flag Combinations**:
- MINIMAL: 0x01 (only bit 0)
- INTEGRITY: 0x03 (bits 0, 1)
- SEARCHABLE: 0x09 (bits 0, 3)
- ARCHIVED: 0x11 (bits 0, 4)
- AUDITED: 0x27 (bits 0, 1, 2, 5)

**Invalid Combinations**: File fails validation if flags contain unsupported combinations (e.g., both L2 and L3, or L3 with BLAKE3 only).

---

## 4. BLOCK STRUCTURE

**File consists of**:
- 1x File Header (16 bytes)
- 1x L0 (DATA) block
- 1x L1 (TOC) block
- 0 or 1x L2 (INDEX) block (SEARCHABLE only)
- 0 or 1x L3 (ARCHIVE) block (ARCHIVED only)
- 0 or 1x L4 (SEAL) block (INTEGRITY or AUDITED)

**Block Header** (identical for all block types):
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       4     u32LE     magic             0x314B4C42 ("BLK1")
4       2     u16LE     block_type        See section 4.1
6       2     u16LE     block_version     0x01
8       8     u64LE     payload_offset    Offset to block payload (from file start)
16      8     u64LE     payload_size      Size in bytes of block payload
24      4     u32LE     payload_crc32     IEEE CRC32 of payload (if present)
28      4     u32LE     reserved1         0x00000000
32      32    bytes     hash_or_data      Semantics depend on block type (see 4.1)
```

**Total Block Header Size**: 72 bytes

**Block Types**:
```
Type  Name              Semantics
----  ----------------  -----------------------------------------------
0x01  L0 (DATA)         Event/record data
0x02  L1 (TOC)          Table of contents (metadata for all blocks)
0x03  L2 (INDEX)        Index into L0 data (for fast search)
0x04  L3 (ARCHIVE)      Compressed version of L0 (LZ4+LZ77 hybrid)
0x05  L4 (SEAL)         Integrity seal (CRC32 or BLAKE3 chain)
```

---

### 4.1 Block Type Details

#### L0 (DATA) Block (0x01)

**Payload Structure**:
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       1     u8        stream_version    0x01
1       4     u32LE     event_count       Number of events in payload
5       8     u64LE     timestamp_epoch   UTC milliseconds since Unix epoch (or 0 in deterministic mode)
13      ...   bytes     event_data        Raw event/record bytes
```

**Validation**:
- stream_version must be 0x01
- event_count must match actual event structure (if applicable)
- timestamp_epoch must be <= current time (or 0x00 in deterministic mode)

#### L1 (TOC) Block (0x02)

**Payload Structure**:
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       1     u8        toc_version       0x01
1       1     u8        flags_copy        Copy of file header flags (for validation)
2       1     u8        block_count       Number of blocks in file
3       ...   vars      block_refs        Variable-length block references (see 4.1.2)
```

**AUDITED Profile Only** (witness chain):
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       1     u8        witness_version   0x01
1       1     u8        reserved          0x00
2       32    bytes     prev_seal_hash    BLAKE3 hash of previous block's seal (or zeros for first block)
```

#### L2 (INDEX) Block (0x03)

**Payload Structure**:
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       1     u8        index_version     0x01
1       2     u16LE     entry_count       Number of index entries
3       ...   bytes     entries           Entry array (6 bytes each)
```

**Entry Layout** (6 bytes per entry):
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       4     u32LE     record_offset     Offset within L0 data
4       2     u16LE     record_size       Size of record
```

**Validation**:
- entry_count must be > 0
- record_offset must be within L0 payload bounds
- record_offset + record_size must be <= L0 payload size
- Entries must be in strictly ascending offset order

#### L3 (ARCHIVE) Block (0x04)

**Payload Structure**:
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       1     u8        compress_version  0x01
1       4     u32LE     original_size     Uncompressed L0 payload size
5       ...   bytes     compressed_data   LZ4+LZ77 hybrid compressed data
```

**Compression Method**: LZ4 + LZ77 hybrid
- Hash table: 16 bits (65536 entries)
- Min match length: 4 bytes
- Max match offset: 65535 bytes
- Sliding window: 65536 bytes
- LZ77 lookahead: 512 bytes
- Token encoding: Literal offset/length, Match offset/length

**Validation**:
- compress_version must be 0x01
- original_size must match L0 payload size
- Decompressed output must exactly match L0 payload
- Decompression must fail safely on corrupt compressed data

#### L4 (SEAL) Block (0x05)

**For INTEGRITY Profile**:
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       1     u8        seal_version      0x01
1       3     reserved  0x00 0x00 0x00
4       4     u32LE     crc32_payload     IEEE CRC32 of L0 payload
```

**For AUDITED Profile**:
```
Offset  Size  Type      Name              Description
------  ----  --------  ----------------  ---------------------------
0       1     u8        seal_version      0x01
1       1     u8        reserved          0x00
2       2     u16LE     sig_algo          0x01 = Ed25519
4       4     u32LE     blake3_payload    BLAKE3 hash of L0 payload (part 1)
8       28    bytes     blake3_payload+   BLAKE3 hash of L0 payload (remaining 28 bytes)
36      64    bytes     signature         Ed25519 signature (64 bytes)
```

**Validation**:
- seal_version must be 0x01
- For INTEGRITY: CRC32 must match computed CRC32 of L0 payload
- For AUDITED: BLAKE3 must match computed BLAKE3 of L0 payload; signature must verify with public key

---

## 5. CRC32 (IEEE)

**Polynomial**: 0xEDB88320
**Initial Value**: 0xFFFFFFFF
**Final XOR**: 0xFFFFFFFF
**Input**: Reflected
**Output**: Reflected
**Scope**: Only payload bytes (excluding all block headers)

**Computation (pseudocode)**:
```
crc = 0xFFFFFFFF
for each byte b in payload:
  crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF]
result = crc ^ 0xFFFFFFFF
```

---

## 6. BLAKE3

**Hash Length**: 32 bytes (256 bits)
**Scope**: Full L0 payload (header + event data)
**Purpose**: Integrity + witness chain (AUDITED profile only)

**For Witness Chain**:
- Each block's seal contains BLAKE3(L0_payload + previous_seal_hash)
- Enables tamper detection and chain-of-custody verification

---

## 7. ED25519 SIGNATURES

**Key Size**: 32 bytes (public), 32 bytes (private)
**Signature Size**: 64 bytes
**Message**: Concatenation of:
  1. L0 payload (all bytes)
  2. L1 block header (entire 72-byte header)
  3. L4 seal header (bytes 0-35: version, reserved, sig_algo, BLAKE3 hash)

**Verification**:
- Extract public key from IlogEncodeOptions
- Compute Ed25519 signature over message bytes
- Compare with signature bytes in L4 block
- Fail-closed if signature invalid or public key missing

---

## 8. FORMAT LIMITS

**Implicit (from code)**:
- Minimum file size: 8 bytes (smaller fails bounds check on magic)
- Maximum file size: Bounded by u64 in block headers (~18 EB theoretical)
- Maximum single block payload: u64 (practical limit ~1 GB)
- Maximum L0 event data: u32 event_count (4.29 billion events)
- Block compression chunk: 4096 bytes (L3 archive layer)

**Not Enforced by Format** (only in encoder):
- Timestamp range (encoder allows any u64 value)
- Event count semantics (treated as opaque byte stream)

---

## 9. VALIDATION RULES

**Fast Validation** (O(1)):
1. Check magic == 0x474F4C49
2. Check version == 0x01
3. Check reserved == 0x0000
4. Check flags are valid profile flags (no invalid bit combinations)
5. Check toc_offset >= 16 and < file size
6. Check L1 block exists and is readable

**Strict Validation** (O(n)):
1. All fast validation rules
2. Verify all block headers (magic, version, type validity)
3. Verify all payload bounds (payload_offset + payload_size <= file size)
4. For INTEGRITY: Verify CRC32 of L0 payload
5. For AUDITED: Verify BLAKE3 of L0 payload; verify Ed25519 signature
6. For SEARCHABLE: Verify index entries are in-bounds and sorted
7. For ARCHIVED: Verify L3 decompression produces correct L0 payload
8. Verify block order (L0, L1, optional L2/L3, optional L4)

---

## 10. ERROR HANDLING

**Read/Decode Failure**:
- All validation failures return specific error codes (see IlogErrorCode enum)
- No partial parsing on error
- Errors must include byte offset for debugging

**Unsupported Features**:
- Unsupported block types: Fail with ILOG_ERR_UNSUPPORTED_BLOCK_TYPE
- Unsupported flags: Fail with ILOG_ERR_INVALID_FLAGS
- Future versions: Fail with ILOG_ERR_UNSUPPORTED_VERSION

---

## 11. PROFILE DEPENDENCY TABLE

See ILOG_PROFILE_MATRIX.md for detailed feature matrix.

| Profile | L0 | L1 | L2 | L3 | L4 | Integrity | Search | Compress |
|---------|----|----|----|----|----|-----------|---------| ---------|
| MINIMAL | ✓  | ✓  | — | — | — | — | — | — |
| INTEGRITY | ✓  | ✓  | — | — | ✓ CRC32 | CRC32 | — | — |
| SEARCHABLE | ✓  | ✓  | ✓ | — | — | — | Index | — |
| ARCHIVED | ✓  | ✓  | — | ✓ | — | — | — | LZ4+LZ77 |
| AUDITED | ✓  | ✓  | — | — | ✓ BLAKE3+Ed25519 | BLAKE3 | — | — |

---

## 12. DETERMINISTIC MODE

**Environment Variable**: `IRONFAMILY_DETERMINISTIC=1`
**Effect**: L0 timestamp_epoch field set to 0x0000000000000000 instead of current time
**Purpose**: Reproducible file generation for testing and vector generation
**Validation**: No impact on reader; zeros are valid timestamp values

---

## 13. UNKNOWNS / NOT VERIFIED

- Large-file behavior (> 1 GB): NOT TESTED
- Stress testing (billions of events): NOT TESTED
- Streaming decompression (L3): NOT VERIFIED (full buffer decompression only)
- Performance scaling with block size: NOT BENCHMARKED at >100 MB scale
- Future version forward-compatibility rules: NOT DEFINED

