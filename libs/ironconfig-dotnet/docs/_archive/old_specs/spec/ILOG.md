> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# ILOG â€” Log / Stream Container Format

## 1. Identity

ILOG is the log and stream container engine within the IRONCFG family.

ILOG magic numbers:
- Primary: `ILOG` (0x49 0x4C 0x4F 0x47)
- Extended: `BLK1` (0x42 0x4C 0x4B 0x31)

## 2. Status

Status: **DESIGN**

This specification is a design document. The format is under active development and subject to change.

## 3. Engine Type

ILOG is a **Log / Stream Container** engine.

ILOG MUST support sequential, append-optimized storage of structured events, records, and log entries.

## 4. Layer Model

ILOG MUST organize data into exactly five conceptual layers:

**L0 â€” DATA Layer**
- Purpose: Raw event and record payload storage
- Role: Core data representation
- Presence: MANDATORY

**L1 â€” TOC Layer**
- Purpose: Table of Contents; structural navigation
- Role: Segment and block inventory
- Presence: MANDATORY

**L2 â€” INDEX Layer**
- Purpose: Searchable indices over data
- Role: Optional acceleration structures
- Presence: OPTIONAL

**L3 â€” ARCHIVE Layer**
- Purpose: Historical and cold data organization
- Role: Optional compression and retention
- Presence: OPTIONAL

**L4 â€” SEAL Layer**
- Purpose: Integrity and authenticity verification
- Role: Optional cryptographic and checksumming mechanisms
- Presence: OPTIONAL

Layers are **conceptual only**. No binary layout, byte order, offsets, or field positioning is defined by the layer model.

L0 and L1 MUST be present in all ILOG files.

L2, L3, and L4 MUST be optional. Readers MUST accept files missing any or all optional layers. Unknown future layers MUST be safely ignored.

## 5. Profile Model

An ILOG profile is a named combination of layers selected for a specific use case.

Profiles are defined as sets of layers:

```
Profile = { L0, L1 } âŞ { L2, L3, L4 } âŠ† P({L0, L1, L2, L3, L4})
```

Where:
- L0 and L1 MUST be present in all profiles
- L2, L3, L4 MUST be optional in any profile

Profiles MUST NOT be encoded in file bytes. Profile identity is determined by which layers are physically present in the file.

Profiles are identified only by which layers are present in the file. No profile identifier field or flag MUST be encoded in the file format itself.

## 6. File Structure Overview

An ILOG file MUST consist of a sequence of logical regions:

1. File header and magic number identification
2. Mandatory layer content (L0, L1)
3. Optional layer content (L2, L3, L4 in any combination)
4. End-of-file marker or sentinel

An ILOG file MUST support sequential reading. The file structure MUST allow a reader to:

- Identify the file as ILOG via magic number
- Locate and parse mandatory layers L0 and L1
- Optionally locate and parse any present optional layers L2, L3, L4
- Safely skip or ignore unknown future layer types
- Validate file integrity if integrity layers are present

The organization of layers within the file is not defined by this normative specification. Binary layout, byte ordering, and segment boundaries are defined in later specifications.

## 7. Layer Presence Rules

### Mandatory Layers

L0 (DATA) and L1 (TOC) MUST be present in every valid ILOG file.

A reader MUST reject any file missing L0 or L1 as malformed.

### Optional Layers

L2 (INDEX), L3 (ARCHIVE), and L4 (SEAL) MUST be optional.

A file MAY contain any subset of {L2, L3, L4}, including the empty set.

A reader MUST accept files missing any or all optional layers.

A reader MUST NOT require the presence of L2, L3, or L4 to read L0 or L1 data.

### Future Layers

Unknown layer types MAY be defined in future ILOG revisions (L5, L6, etc.).

A reader MUST safely skip or ignore any layer of an unknown type without failing.

A reader MAY provide a warning or informational message about unknown layers, but MUST NOT treat their presence as an error.

## 8. L0 â€” DATA Layer (Normative)

### Purpose

L0 is the DATA layer. L0 MUST contain the primary event and record payloads that form the core content of the ILOG file.

### Presence and Obligation

L0 MUST be present in every valid ILOG file.

If L0 is absent, the file MUST be rejected as invalid.

### Core Obligations

L0 MUST define a sequence of records or events.

Each record or event in L0 MUST have a well-defined structure specified by a schema.

L0 MUST provide a means to enumerate all records in order.

L0 MUST support either:
- Fixed-size record layout, or
- Variable-size records with explicit length or delimiter boundaries

### Invariants

L0 MUST NOT be empty. An ILOG file MUST contain at least one record in L0.

All records in L0 MUST conform to the same schema definition.

L0 data MUST be immutable once written. Subsequent writes MUST append new records, not modify existing ones.

The ordering of records in L0 MUST be preserved and retrievable.

### Reader Behavior

A reader MUST be able to iterate over all records in L0 sequentially.

A reader MUST decode each record according to the schema definition.

A reader MUST validate that each record conforms to its declared schema.

A reader MUST handle any schema version or encoding specified by the file.

## 9. L1 â€” TOC Layer (Normative)

### Purpose

L1 is the Table of Contents (TOC) layer. L1 MUST provide structural metadata and navigation information for the ILOG file.

### Presence and Obligation

L1 MUST be present in every valid ILOG file.

If L1 is absent, the file MUST be rejected as invalid.

### Core Obligations

L1 MUST document the structure and organization of the file.

L1 MUST identify which layers are present and their locations within the file.

L1 MUST provide a segment or block inventory that allows a reader to locate L0 data and other layer content.

L1 MUST specify the schema version and record structure definition used in L0.

L1 MUST document any encoding schemes, compression methods, or special representations used in L0.

### Invariants

L1 MUST contain complete and accurate information about the file's contents. All facts stated in L1 MUST be true for the file as a whole.

L1 layer presence declarations MUST match the actual presence of layers in the file. If L1 declares a layer present, that layer MUST exist and be readable. If L1 declares a layer absent, that layer MUST NOT be present.

L1 schema definitions MUST be sufficient for a reader to decode all records in L0 without additional external information.

### Reader Behavior

A reader MUST parse L1 before attempting to read L0.

A reader MUST use L1 metadata to identify the schema and encoding of L0 records.

A reader MUST check L1's layer presence declarations to determine which optional layers (L2, L3, L4) may be present.

A reader MUST reject the file if L1 is malformed or internally inconsistent.

### L1 TOC Payload Binary Format

The L1_TOC block payload MUST have the following structure:

- TocVersion (u8): MUST be 0x01
- LayerCount (u32): Number of layer entries (little-endian)
- [LayerCount entries], each:
  - LayerType (u16): From section 14 registry (0x0001=L0_DATA, 0x0002=L1_TOC, 0x0003=L2_INDEX, 0x0004=L3_ARCHIVE, 0x0005=L4_SEAL) (little-endian)
  - BlockCount (u32): Number of blocks of this type (little-endian)
  - Flags (u32): Layer-specific flags; future use; readers MUST ignore unknown bits (little-endian)
  - Reserved (u64): MUST be 0 (little-endian)

Total entry size: 2 + 4 + 4 + 8 = 18 bytes per entry.

**Constraints:**
- LayerCount MUST be >= 1
- Entries for LayerType 0x0001 (L0_DATA) and 0x0002 (L1_TOC) MUST be present
- Block counts MUST match the actual number of blocks of each type in the file
- Readers MUST validate that BlockCount matches file contents

## 10. L2 â€” INDEX Layer (Normative)

### Purpose

L2 is the INDEX layer. L2 MAY provide searchable indices, acceleration structures, or lookup tables over L0 data to enable efficient queries.

### Presence and Obligation

L2 is OPTIONAL. A file MAY omit L2 entirely.

A reader MUST NOT require L2 to be present in order to read L0 or L1.

If L2 is present, it MUST be declared in L1's layer presence metadata.

### Core Obligations (if present)

If L2 is present, it MUST define at least one index over L0 data.

L2 indices MUST be consistent with the records in L0. Index entries MUST refer to valid records, and index values MUST be correctly computed from the indexed records.

L2 MUST provide a schema or specification for each index it contains, allowing a reader to interpret and use the index.

L2 indices MUST support lookup, range queries, or other query patterns appropriate to their type.

### Invariants

L2 indices MUST be read-only. Once constructed, indices MUST NOT be modified in place.

L2 content MUST remain consistent with L0. If L0 is appended to, L2 indices MUST either be updated consistently or marked as stale.

Unknown index types in L2 MUST be safely skipped by readers.

### Reader Behavior

A reader MAY ignore L2 entirely and process only L0 and L1.

A reader MAY use L2 indices if present to accelerate queries or access patterns.

A reader that uses L2 indices MUST validate that indices are consistent with L0 before relying on them.

If L2 is present but corrupted, a reader MAY fall back to reading L0 directly.

## 11. L3 â€” ARCHIVE Layer (Normative)

### Purpose

L3 is the ARCHIVE layer. L3 MAY provide compressed, deduplicated, or aged data storage for historical or cold data within the ILOG file.

### Presence and Obligation

L3 is OPTIONAL. A file MAY omit L3 entirely.

A reader MUST NOT require L3 to be present in order to read L0 or L1.

If L3 is present, it MUST be declared in L1's layer presence metadata.

### Core Obligations (if present)

If L3 is present, it MUST document the compression, deduplication, or retention policy applied to archived records.

L3 MUST provide a specification for decoding archived content, including any transformation or compression scheme used.

L3 content MUST be logically convertible back to original L0 record form without loss of information.

### Invariants

L3 data MUST be read-only once written.

L3 MUST NOT contain records that are also present in L0. L3 and L0 content MUST be disjoint or explicitly coordinated.

Unknown compression or archival schemes in L3 MUST be safely skipped by readers.

### Reader Behavior

A reader MAY ignore L3 entirely.

A reader that processes L3 MUST decompress or transform archived records according to the specification in L1 or L3.

A reader MUST NOT require access to L3 to read active records in L0.

If L3 is present but unreadable, a reader MAY proceed with L0 and L1 only.

## 12. L4 â€” SEAL Layer (Normative)

### Purpose

L4 is the SEAL layer. L4 MAY provide integrity verification, authenticity guarantees, or cryptographic signatures over the ILOG file.

### Presence and Obligation

L4 is OPTIONAL. A file MAY omit L4 entirely.

A reader MUST NOT require L4 to be present in order to read L0 or L1.

If L4 is present, it MUST be declared in L1's layer presence metadata.

### Core Obligations (if present)

If L4 is present, it MUST specify one or more integrity schemes: checksums, message authentication codes, or digital signatures.

L4 MUST document which portions of the file are protected by each integrity scheme (L0, L1, L2, L3, or combinations).

L4 MUST provide enough information for a reader to verify the integrity claims without external secrets or configuration.

L4 integrity values MUST be deterministically computable from the protected file content.

### Invariants

L4 seals MUST be read-only. Once written, integrity values MUST NOT be recomputed or modified.

L4 MUST NOT encode secret key material in the file itself. Verification MUST use only public information.

An ILOG file MAY have multiple independent seals in L4, each covering different content or using different algorithms.

### Reader Behavior

A reader MAY choose to verify L4 seals or to ignore them.

A reader MUST NOT require L4 seals to be valid in order to read L0, L1, L2, or L3.

A reader that verifies L4 seals MUST follow the specification provided by L4 to perform verification.

If L4 seals are present and invalid, a reader MAY:
- Reject the file as corrupted
- Warn the user and proceed anyway
- Provide the raw data with a corruption flag

A reader MUST NOT silently accept invalid seals.

### L4 SEAL Payload Binary Format

The L4_SEAL block payload MUST have the following structure for hash-based integrity:

- SealVersion (u8): MUST be 0x01
- SealType (u8): MUST be 0x00 (hash-based seal; other types reserved for future use)
- CoverageType (u8):
  - 0x00 = All blocks (L0 + L1 + L2 + L3, excluding this L4 block itself)
  - 0x01 = L0 and L1 only
  - 0x02 = Reserved for future use
- Reserved (u8): MUST be 0
- FileBlake3 [32]: BLAKE3-256 of the concatenation of:
  - All block headers (72 bytes each) for blocks covered by this seal (in file order, excluding L4 header)
  - All block payloads for those blocks (in file order)
- OptionalSignatureLength (u32): Length of optional signature; MAY be 0 (little-endian)
- [OptionalSignatureLength bytes]: Signature bytes (if present; format reserved for future use)

**Constraints:**
- Multiple L4 blocks MAY exist; each is processed independently
- Readers in validate_fast MUST skip L4 blocks
- Readers in validate_strict with Flags.BLAKE3=1 MUST validate FileBlake3
- Readers MAY ignore signature bytes in this version

## 13. File Header Layout

### Binary Header Format (Deterministic)

Every ILOG file MUST begin with a fixed 16-byte header, all multi-byte values in little-endian:

| Offset | Field | Type | Size | Semantics |
|--------|-------|------|------|-----------|
| 0x00 | Magic | ASCII | 4 | MUST be "ILOG" (0x49 0x4C 0x4F 0x47) or "BLK1" (0x42 0x4C 0x4B 0x31) |
| 0x04 | Version | u8 | 1 | MUST be 0x01. Readers MUST reject higher versions. |
| 0x05 | Flags | u8 | 1 | Global file properties (see Flags Field section) |
| 0x06 | Reserved0 | u16 | 2 | MUST be 0x0000 |
| 0x08 | TocBlockOffset | u64 | 8 | Absolute file byte offset to L1 TOC block. MUST be >= 16. MUST point to first L1_TOC block. |

### Endianness

All multi-byte integers in the file MUST be encoded in little-endian byte order unless explicitly stated otherwise.

Single-byte values have no endianness.

### Flags Field

The flags byte (offset 0x05) MUST encode global file properties:

- Bit 0: Endianness flag (reserved; MUST be 0 for this spec version; future versions MAY use 1 for big-endian)
- Bit 1: CRC32 integrity. MUST be 1 if any block has a non-zero PayloadCrc32 field, 0 otherwise.
- Bit 2: BLAKE3 integrity. MUST be 1 if any block has a non-zero PayloadBlake3 field, 0 otherwise.
- Bit 3: L2 (INDEX) layer present. MUST be 1 if any block with BlockType=0x0003 (L2_INDEX) exists, 0 otherwise.
- Bit 4: L3 (ARCHIVE) layer present. MUST be 1 if any block with BlockType=0x0004 (L3_ARCHIVE) exists, 0 otherwise.
- Bit 5: L4 (SEAL) layer present. MUST be 1 if any block with BlockType=0x0005 (L4_SEAL) exists, 0 otherwise.
- Bits 6-7: Reserved. MUST be 0.

Readers MUST validate that flag declarations match actual layer presence.

## 14. Segment / Block Model (Binary Framing)

### File Layout

An ILOG file MUST have the following binary structure:

```
[File Header (16 bytes)]
[Block 0]
[Block 1]
...
[Block N]
```

Where:
- File Header is exactly 16 bytes (offset 0x00â€“0x0F)
- Blocks follow immediately after the header (starting at offset 0x10)
- Blocks MUST be contiguous with no gaps
- Block K ends where Block K+1 begins
- No padding, alignment, or reserved space between blocks

### Block Structure

Each block MUST have the following physical structure:

```
[Block Header (fixed 72 bytes)]
[Block Payload (0 or more bytes)]
```

Block header and payload MUST have no padding between them.

Total block size in file = 72 + PayloadSize (as defined in block header).

### Block Sequencing

Readers MUST enumerate blocks by:
1. Start at file offset 0x10 (immediately after file header)
2. Read 72-byte block header at current offset
3. Extract PayloadSize field
4. Block data = 72 + PayloadSize bytes
5. Next block starts at (current offset + 72 + PayloadSize)
6. Continue until reaching end of file

### Block Type Registry

All blocks in an ILOG file MUST have a BlockType (u16) indicating their purpose and layer:

| BlockType | Value | Layer | Semantics |
|-----------|-------|-------|-----------|
| L0_DATA | 0x0001 | L0 | Event/record payload stream |
| L1_TOC | 0x0002 | L1 | Table of contents metadata |
| L2_INDEX | 0x0003 | L2 | Search indices (optional) |
| L3_ARCHIVE | 0x0004 | L3 | Compressed/archived data (optional) |
| L4_SEAL | 0x0005 | L4 | Integrity seals (optional) |

All other BlockType values are reserved for future use. Readers encountering unknown BlockType MUST skip the block without error using PayloadSize.

### Mandatory Block Requirements

- L1_TOC MUST appear exactly once in the file.
- L0_DATA MUST appear at least once.
- L0_DATA and L1_TOC MUST be present for the file to be valid.
- Readers do not need to enforce any particular order of blocks.

## 15. Fixed Block Header Layout (72 Bytes, Binary Format)

### Block Header Structure

All blocks MUST have a fixed 72-byte header, all integers in little-endian:

| Offset (hex) | Field | Type | Size | Semantics |
|--------------|-------|------|------|-----------|
| 0x00 | BlockMagic | u32 | 4 | MUST equal 0x314B4C42 ("BLK1" in little-endian bytes) |
| 0x04 | BlockType | u16 | 2 | Layer/content type from section 14 registry |
| 0x06 | BlockFlags | u16 | 2 | Block-specific flags; MUST be 0 |
| 0x08 | HeaderSize | u16 | 2 | MUST be 72 |
| 0x0A | Reserved0 | u16 | 2 | MUST be 0 |
| 0x0C | PayloadSize | u32 | 4 | Payload byte count; MAY be 0 |
| 0x10 | Sequence | u64 | 8 | Sequential ID, starting at 0, strictly increasing |
| 0x18 | PayloadCrc32 | u32 | 4 | IEEE CRC32 of payload bytes |
| 0x1C | HeaderCrc32 | u32 | 4 | IEEE CRC32 of bytes [0x00â€“0x1B]; computed with this field set to 0 |
| 0x20 | PayloadBlake3 | [32] | 32 | BLAKE3-256 of payload bytes |
| 0x40 | Reserved1 | [8] | 8 | MUST be all 0x00 |

**Total: 72 bytes (0x48)**

### Header Validation

- validate_fast MUST check: BlockMagic, HeaderSize, HeaderCrc32 (over bytes 0â€“0x1B)
- validate_strict MUST check all of fast, plus: PayloadCrc32 and PayloadBlake3 against file flags (Flags.CRC32, Flags.BLAKE3)
- All Reserved fields MUST be zero; non-zero values cause validation failure

## 16. Endianness Rules

### Fixed Little-Endian

All ILOG files in this version (v0.01) MUST use little-endian byte order for all multi-byte values.

This includes:
- All u16, u32, u64 integers
- All i32, i64 signed integers
- All f32, f64 floating-point values
- All offsets and sizes
- All checksums (CRC32) and hashes (BLAKE3)

Single-byte values (u8) have no endianness.

Readers MUST reject files that declare big-endian (Flags bit 0 = 1) in this specification version.

## 17. Varint Encoding Rules

### Varint Definition

A varint (variable-length integer) is a compact encoding of unsigned integers.

Varint MUST use LEB128 (Little Endian Base 128) encoding:

- Each byte contains 7 bits of data and 1 continuation bit (MSB)
- If MSB = 1, more bytes follow
- If MSB = 0, this is the final byte
- Bytes are read in order from least significant to most significant

### Varint Constraints

Varint encoding MUST support values from 0 to 2^63 - 1.

Varint encodings MUST be canonical. An integer MUST NOT be encoded using more bytes than necessary.

Readers MUST reject varint sequences that encode the same value in multiple valid byte counts. For example, the value 127 MUST be encoded as 1 byte (0x7F), not as 2 bytes (0xFF 0x00).

### Varint Applications

Varints MUST be used for:
- Record counts and field counts
- String lengths
- Array lengths
- Sparse field indices
- All context-dependent size and count fields

Fixed-size integers MUST NOT be encoded as varints.

## 18. ZigZag Encoding Rules

### ZigZag Definition

ZigZag encoding MUST be used to compactly represent signed integers that may be negative.

ZigZag MUST encode signed integers as unsigned integers using the formula:
```
encoded = (n << 1) ^ (n >> (bits - 1))
```

For decoding:
```
decoded = (encoded >> 1) ^ -(encoded & 1)
```

Where `bits` is the bit width of the original signed integer (16, 32, 64).

### ZigZag Applications

ZigZag encoding MUST be applied before Varint encoding for signed integer fields.

Signed integers MUST be encoded as:
1. Apply ZigZag transformation to signed value
2. Encode result as varint

ZigZag MUST be used for:
- Timestamp deltas
- Coordinate differences
- All signed integer values in log records
- Any field where negative values are expected

## 19. Dictionary Encoding Rules

### Dictionary Definition

Dictionary encoding MUST be a table of distinct values referenced by index.

A dictionary MUST be stored as:
- Dictionary header (type, entry count)
- Dictionary entries (variable-length values or fixed references)

### Dictionary Use Cases

Dictionary encoding MUST be used for:
- Repeated string values (keys, event types, host names)
- Repeated categorical values (severity levels, status codes)
- Common constants or enumerations

### Dictionary Consistency

All dictionary references in a block MUST resolve to valid entries in the dictionary.

Readers MUST reject files with out-of-bounds dictionary references.

Dictionary indices MUST be encoded as varints.

### Dictionary Atomicity

A dictionary MUST be self-contained within its block or layer. References across layers or blocks MUST NOT use dictionary indices.

## 20. Event Stream Encoding Rules

### Event Definition

An event MUST be a single record or log entry containing:
- Event timestamp (mandatory)
- Event fields (as defined by schema)
- Event metadata (sequence number, flags, etc.)

### Event Order

Events MUST be stored in strictly chronological or sequence order.

Events with the same timestamp MUST be ordered by their sequence number or block position.

Readers MUST preserve event order when decoding.

### Event Encoding

Each event MUST be encoded with:
1. Length or delimiter to mark event boundaries
2. Timestamp or timestamp delta (relative to previous event or epoch)
3. Schema version or field descriptor
4. Field values in schema order
5. Optional event flags (truncated, partial, etc.)

### Delta Encoding for Timestamps

Timestamps MUST be delta-encoded relative to the previous event's timestamp.

The first event in a segment MUST encode an absolute timestamp.

Subsequent events MUST encode only the delta (difference) from the previous timestamp.

Timestamp deltas MUST be ZigZag-encoded then Varint-encoded.

### L0 DATA Payload Binary Format

The L0_DATA block payload MUST be a deterministic event stream with the following structure:

- StreamVersion (u8): MUST be 0x01
- EventCount (u32): Number of events (little-endian)
- TimestampEpoch (u64): Base timestamp for first event (little-endian, UNIX milliseconds)
- [EventCount events], each:
  - EventLength (varint): Length of event data in bytes (excludes this length field)
  - TimestampDelta (ZigZag varint): Delta from previous timestamp (first event uses 0; only deltas stored)
  - EventTypeId (varint): Application-defined event type ID
  - FieldCount (varint): Number of fields in this event
  - [FieldCount fields], each:
    - FieldId (varint): Application-defined field ID
    - WireType (u8): Encoding: 0=int varint, 1=sint ZigZag varint, 2=bytes length-delimited
    - Value:
      - WireType 0: unsigned varint (u64)
      - WireType 1: signed ZigZag varint (s64)
      - WireType 2: length varint + raw bytes

**Determinism constraints:**
- Fields within each event MUST be sorted by ascending FieldId
- Events MUST appear in file order (no reordering)
- TimestampEpoch MUST be set to the first event's absolute timestamp

## 21. Exception / Error Record Encoding

### Error Record Structure

An error or exceptional record MUST be distinguishable from normal events by its type indicator.

Error records MUST contain:
- Error code (non-zero integer)
- Byte offset in source (where the error was detected)
- Error description or reason code
- Optional context (related record ID, layer, etc.)

### Error Codes

Error codes MUST be non-zero positive integers.

Error code 0 MUST be reserved and indicate success or no error.

Error codes MUST be stable and standardized across all implementations.

### Byte Offset Tracking

The byte offset MUST be the file byte position where the error was detected or where validation failed.

Byte offsets MUST be exact. Off-by-one errors MUST NOT occur.

Readers MUST provide byte offsets in error messages.

## 22. Determinism Rules

### Deterministic Encoding

An ILOG encoder MUST produce identical byte sequences for identical input records.

The same records encoded multiple times MUST produce exactly the same bytes.

### Field Order Determinism

Records MUST be encoded with fields in a canonical order as defined by schema. Field order MUST NOT vary based on input or runtime state.

### Map/Dictionary Determinism

If a record contains maps or dictionaries, entries MUST be sorted deterministically:
- By key (lexicographic order for strings)
- By insertion order (if explicitly defined)
- By value hash (if defined in schema)

Readers MUST NOT assume any particular map order.

### Floating-Point Canonicalization

All floating-point values MUST be canonicalized:
- NaN values MUST be encoded as a single canonical bit pattern (implementation-defined, but consistent)
- Positive zero (+0.0) and negative zero (-0.0) MUST be treated identically
- No subnormal or denormalized floats MUST be produced

### No Compression Non-Determinism

Compression algorithms MUST be deterministic. Compressed blocks produced by the same compressor with the same settings MUST produce identical output.

Variable-compression settings MUST NOT be allowed if they produce non-deterministic output.

## 23. Integrity Rules

### CRC32 IEEE Support

CRC32 IEEE MUST be supported as a mandatory integrity mechanism.

CRC32 values MUST be computed using the standard IEEE polynomial (0x1EDC6F41).

CRC32 MUST cover one of:
1. Entire L0 data payload
2. Entire L1 metadata
3. All layers (L0 + L1 + L2 + L3)
4. Per-block basis (each block has its own CRC32)

Scope MUST be declared in L1 or L4 metadata.

CRC32 values MUST be stored as 32-bit integers in the declared endianness.

Readers MUST validate CRC32 values if the Flags field indicates they are present.

### BLAKE3 Support (Strict Mode)

BLAKE3 hashing MUST be supported as an optional, strong integrity mechanism.

BLAKE3 output MUST be 32 bytes (256 bits).

BLAKE3 MUST be computed over:
1. Entire file (from magic number to end of L3, excluding L4)
2. Specific layer(s)
3. Per-segment basis

Scope MUST be declared in L4 metadata.

BLAKE3 hashes MUST be stored as binary 32-byte values.

Readers in strict mode MUST validate BLAKE3 hashes if present.

### Multiple Seals

An ILOG file MAY contain multiple independent integrity seals in L4.

Each seal MUST declare:
- Algorithm (CRC32, BLAKE3, or other)
- Scope (which bytes/layers are protected)
- The integrity value itself

## 24. Validation Rules

### Two-Mode Validation

Readers MUST support two validation modes:

**validate_fast:**
- Quick gate validation performed on file open
- Checks magic number, version, flags consistency
- Verifies header structure
- Checks L1 is present and readable
- Returns success or failure with basic error only
- MUST complete in O(1) or O(header size) time

**validate_strict:**
- Full integrity and consistency validation
- Verifies all blocks are well-formed
- Validates all field values against schema
- Checks all indices are consistent with data
- Verifies all integrity seals (CRC32, BLAKE3)
- Returns success or detailed error with byte offset
- MAY require O(file size) time

### Validation Sequencing

Validation MUST be performed in order:
1. Magic number and version check
2. Flags consistency check
3. Header structural integrity
4. L1 (TOC) presence and basic readability
5. (fast mode stops here)
6. L0 record enumeration and schema validation
7. Optional layer presence validation
8. Integrity seal verification
9. Cross-layer consistency checks

### Record Validation

Each record in L0 MUST be validated against its declared schema:
- All mandatory fields present
- All field values within declared types and ranges
- No out-of-bounds references (strings, dictionaries, indices)

## 25. Limits and DoS Policy

### Mandatory Limits

An ILOG file MUST enforce the following limits:

- **Maximum file size**: 2^63 - 1 bytes (signed 64-bit limit)
- **Maximum record count**: 2^32 - 1 records per L0 segment
- **Maximum string length**: 2^31 - 1 bytes per string
- **Maximum dictionary entries**: 2^16 - 1 entries per dictionary
- **Maximum block size**: 2^32 - 1 bytes per block
- **Maximum nesting depth**: 64 levels (for nested records or structures)
- **Maximum field count per record**: 256 fields
- **Maximum schema version**: 255

Readers MUST reject files exceeding these limits.

### DoS Prevention

Readers MUST implement the following DoS prevention measures:

- Varint decoding MUST stop after reading 10 bytes. Values requiring more than 10 bytes MUST be rejected.
- Dictionary lookups MUST be O(1) or O(log n). Hash table probing MUST be bounded.
- Block enumeration MUST track visited blocks to prevent infinite loops.
- Deflate/compression decompression MUST be bounded by output size limit or iteration count.

### Recursion Guards

If a record format permits nesting or recursion, the maximum nesting depth MUST be enforced.

Stack-based readers MUST use explicit depth counters.

Readers using recursion MUST implement stack guards or convert to iterative parsing.

## 26. Error Model

### Error Code Definitions

Readers MUST report errors with a standard error code and byte offset.

Standard error codes:

- **0x0001**: Invalid magic number or file signature
- **0x0002**: Unsupported version
- **0x0003**: Corrupted header
- **0x0004**: Missing mandatory layer (L0 or L1)
- **0x0005**: Malformed block header
- **0x0006**: Block payload out of bounds
- **0x0007**: Invalid block type
- **0x0008**: Schema validation failed
- **0x0009**: Out-of-bounds field reference
- **0x000A**: Dictionary lookup failed
- **0x000B**: Varint decoding failed
- **0x000C**: CRC32 mismatch
- **0x000D**: BLAKE3 mismatch
- **0x000E**: Compression decompression failed
- **0x000F**: Record truncated or incomplete
- **0x0010**: Nested depth limit exceeded
- **0x0011**: File size limit exceeded
- **0x0012**: Record count limit exceeded
- **0x0013**: String length limit exceeded
- **0x0014**: Unknown critical flag set

### Error Reporting

Every error MUST include:

1. **Error code**: Standardized code from the error code registry
2. **Byte offset**: Exact byte position in file where error detected (0-based)
3. **Message**: Human-readable description (optional in strict parsing, required for user output)
4. **Context**: Additional metadata (layer, block type, field name if applicable)

### Error Return Semantics

Readers MUST distinguish between:
- **Fatal errors**: File is invalid, cannot be read further
- **Recoverable errors**: Specific record is invalid, but file can continue
- **Warnings**: Non-critical issues (unknown layer, unverified seal)

Recovery behavior MUST be defined per error code:
- **Fatal**: Stop processing, return error to caller
- **Recoverable**: Skip record, log error, continue to next record
- **Warning**: Log but continue processing

### Recovery Policy

A reader MAY attempt recovery from specific errors:
- Skipping truncated records
- Ignoring unrecognized optional layers
- Continuing with unverified seals in non-strict mode

Recovery MUST be safe (never read out of bounds or dereference invalid pointers).

A reader MUST NOT silently drop errors. Errors MUST be reported to the caller via error code and offset, even if recovery succeeds.

## 27. Test Vector Canonical Datasets (Normative)

### Reserved IDs for Test Vectors

The following identifiers MUST be used exclusively for ironcert golden vector generation and MUST NOT be treated as application schema:

- **EventTypeId range**: 1â€“4 (reserved)
- **FieldId range**: 1â€“3 (reserved)

### Canonical Timestamp Epoch

All golden vectors MUST use:
- **TimestampEpoch = 0 (u64 little-endian)**

This is the base timestamp in UNIX milliseconds for the first event in every golden dataset.

### Datasets and Event Counts

Golden vectors MUST have exactly the following event counts:

| Dataset | EventCount | Generation |
|---------|-----------|-----------|
| golden_small | 3 | Explicit enumeration required |
| golden_medium | 30 | Formula or explicit |
| golden_large | 300 | Formula or explicit |
| golden_mega | 3000 | Formula or explicit |

### Canonical Event Generation

For each event index i (0-based), the encoder MUST generate:

**Event[i]:**
- EventTypeId (varint): `(i mod 4) + 1` (values 1, 2, 3, or 4)
- TimestampDelta (ZigZag varint): Encode the signed value `(i)` using ZigZag per spec section 18
- FieldCount (varint): 3
- Fields (in ascending FieldId order):
  - **Field 1**: FieldId=1, WireType=0 (varint), Value=(i encoded as u64 varint)
  - **Field 2**: FieldId=2, WireType=1 (ZigZag varint), Value=`-(i + 1)` (encoded as ZigZag s64 varint)
  - **Field 3**: FieldId=3, WireType=2 (bytes), Value=6-byte sequence: `0x00 0x01 0x02 0x03 0xFE 0xFF`

### Explicit golden_small Events

For golden_small (3 events), the concrete values MUST be:

**Event 0:**
- EventTypeId: 1
- TimestampDelta: 0 (ZigZag encoded)
- Field 1: Value = 0
- Field 2: Value = -1
- Field 3: Value = [0x00, 0x01, 0x02, 0x03, 0xFE, 0xFF]

**Event 1:**
- EventTypeId: 2
- TimestampDelta: 1 (ZigZag encoded)
- Field 1: Value = 1
- Field 2: Value = -2
- Field 3: Value = [0x00, 0x01, 0x02, 0x03, 0xFE, 0xFF]

**Event 2:**
- EventTypeId: 3
- TimestampDelta: 2 (ZigZag encoded)
- Field 1: Value = 2
- Field 2: Value = -3
- Field 3: Value = [0x00, 0x01, 0x02, 0x03, 0xFE, 0xFF]

## 28. Golden Manifest Semantics (Normative)

Each golden vector directory (golden_small, golden_medium, golden_large, golden_mega) MUST contain a manifest.json file with the following EXACT structure and field meanings:

```json
{
  "engine": "ilog",
  "version": 1,
  "dataset": "<dataset_name>",
  "expected_fast": "OK",
  "expected_strict": "OK",
  "expected_events": <event_count>,
  "expected_crc32": "<hex_string>",
  "expected_blake3": "<hex_string>"
}
```

### Field Definitions

- **engine**: MUST be the string `"ilog"` (case-sensitive)
- **version**: MUST be the integer `1`
- **dataset**: MUST be one of: `"small"`, `"medium"`, `"large"`, `"mega"` (case-sensitive)
- **expected_fast**: MUST be the string `"OK"` (case-sensitive)
- **expected_strict**: MUST be the string `"OK"` (case-sensitive)
- **expected_events**: MUST be the integer event count for this dataset (3, 30, 300, or 3000)
- **expected_crc32**: MUST be an 8-character lowercase hexadecimal string (0â€“9, aâ€“f) representing the IEEE CRC32 of the PAYLOAD bytes (PayloadCrc32 field) of the first L0_DATA block in the file
- **expected_blake3**: MUST be a 64-character lowercase hexadecimal string representing the BLAKE3-256 hash of the PAYLOAD bytes (PayloadBlake3 field) of the first L0_DATA block in the file

### Manifest Generation Rules

When ironcert generates a golden vector:

1. Create the vector binary file: `vectors/small/ilog/golden_<dataset>/expected/ilog.ilog`
2. Compute L0_DATA block payload CRC32 and BLAKE3
3. Write manifest.json with values from Section 27 dataset definition
4. All hex values MUST be lowercase
5. No trailing whitespace; use standard JSON formatting

## 29. ironcert Generation Contract (Normative)

### ironcert generate ilog Command

ILOG test vector generation MUST be integrated into ironcert as:

```bash
ironcert generate ilog
```

### Generation Behavior

When executed, `ironcert generate ilog` MUST:

1. Generate exactly four canonical golden vectors using Section 27 datasets (small, medium, large, mega)
2. For each dataset, write exactly one binary file: `vectors/small/ilog/golden_<name>/expected/ilog.ilog`
3. For each dataset, write/update the corresponding `vectors/small/ilog/golden_<name>/manifest.json` with fields from Section 28
4. Use deterministic encoding per spec/ILOG.md sections 13â€“26 with no deviation
5. NOT mark any vector as SKIP after generation completes
6. Report success/failure to stdout; exit code 0 on success, non-zero on failure

### Generation Requirements

- Generation MUST be deterministic: repeated execution produces identical byte sequences
- Generation MUST use only the canonical datasets defined in Section 27
- Generation MUST NOT invent new event structures or field values outside Section 27
- Generation MUST produce files that pass `ironcert validate ilog --strict`
- CRC32 and BLAKE3 values MUST be computed per section 15 (block header validation rules)

### ironcert vectors Integration

After generation completes, `ironcert vectors ilog` MUST:

1. List the generated vectors (no longer marked SKIP)
2. Validate each against the expected values in manifest.json
3. Report pass/fail status per vector

Vectors MUST NOT remain marked SKIP in vectors/small/manifest.json after generation.
