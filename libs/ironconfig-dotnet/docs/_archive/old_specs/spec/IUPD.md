> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IUPD — Update / Patch Container Format

## 1. Identity

IUPD is the update and patch container engine within the IRONCFG family.

IUPD magic numbers:
- Primary: `IUPD` (0x49 0x55 0x50 0x44)
- Alternate: `UPD1` (0x55 0x50 0x44 0x31)

## 2. Status

Status: **DESIGN**

This specification is a design document. The format is under active development and subject to change.

## 3. Engine Type

IUPD is an **Update / Patch Container** engine.

IUPD MUST support:
- Structured patch manifests describing updates and patches
- Explicit dependency graphs between patch operations
- Content-addressed storage of chunks via cryptographic hashes
- Streaming application of patches without requiring entire package to be loaded into memory
- Deterministic encoding (same input always produces identical bytes)

## 4. Goals (Normative)

IUPD MUST achieve the following objectives:

### 4.1 Deterministic Output
The format MUST produce identical bytes for identical inputs across all platforms and implementations. All encoding operations MUST be fully specified with no randomization or non-deterministic behavior.

### 4.2 Content-Addressed Chunks
IUPD MUST support content-addressed storage where patch chunks and data blocks are identified by cryptographic hash (content hash = identity).

### 4.3 Patch Manifests with Dependency Graph
IUPD MUST support explicit patch manifests that specify:
- Patches to be applied
- Dependencies between patches (must not be cyclic)
- Prerequisites and version constraints
- Deterministic dependency resolution

### 4.4 Streaming Apply
IUPD readers and writers MUST support applying patches in a streaming fashion. The entire package MUST NOT need to be loaded into memory before applying patches. Implementations MUST be able to apply patches incrementally as bytes are read.

### 4.5 Data Integrity
IUPD MUST support two levels of integrity checking:
- Quick corruption detection via IEEE CRC32 on patch payloads
- Strong verification via BLAKE3-256 hash on patch content

### 4.6 Denial-of-Service Limits
IUPD implementations MUST enforce strict bounds and limits to prevent DoS attacks:
- Maximum patch payload size limits
- Maximum manifest size limits
- Maximum number of patches per manifest
- Maximum dependency graph depth
- Bounds checking on all offset and size fields

## 5. Non-Goals (Normative)

IUPD explicitly MUST NOT support:

### 5.1 Encryption in Version 1
IUPD v1 MUST NOT include encryption mechanisms. Encryption MUST be deferred to future versions or handled externally via container wrapping (e.g., via BJX encrypted container).

### 5.2 Non-Deterministic Chunking
IUPD MUST NOT employ heuristics that change chunk boundaries based on data content or external state. Chunking MUST be fully deterministic and specified. Rolling hashes and similarity-based chunking are explicitly excluded.

### 5.3 Randomization
IUPD encoders MUST NOT use any random operations. All choices in encoding (including byte order, flag settings, field ordering) MUST be fully specified and reproducible.

## 6. Terminology (Normative)

**Patch**
A patch is a named, ordered sequence of changes to be applied to a target state. Each patch has a unique identifier and explicit version.

**Manifest**
A manifest is the top-level structure that lists patches to be applied, their dependencies, and metadata.

**Chunk**
A chunk is a unit of data storage within a patch, identified by its content hash.

**Content Hash**
A deterministic hash (BLAKE3-256) of chunk content. Content hash serves as the chunk's identity and storage key.

**Payload**
The raw bytes of a patch or chunk, excluding headers and metadata.

**Dependency**
A directed edge in the patch dependency graph indicating that one patch MUST be applied before another.

**Streaming**
The ability to apply patches incrementally, reading and processing bytes sequentially without buffering the entire payload in memory.

## 7. Status Gates (Normative)

IUPD MUST pass the following certification gates before proceeding to CERTIFIED status:

| Gate | Requirement |
|------|-------------|
| **Spec Lock** | Specification document MUST be finalized and frozen |
| **Golden Vectors** | At least four (4) golden test vectors representing distinct use cases |
| **Reference Implementations** | Both C99 and .NET implementations with exact parity |
| **Parity Testing** | All golden vectors MUST produce identical results across implementations |
| **Determinism Validation** | Multiple encodes of same input MUST produce identical bytes |
| **Error Handling** | Corruption detection and error reporting MUST match specification |
| **Bounds Testing** | Invalid offsets, sizes, and payloads MUST be rejected safely |
| **Benchmarking** | Performance MUST be measured and documented |
| **Fuzzing** | At least 10,000 malformed vectors MUST be safely rejected |

## 8. Patch Model (Normative)

A patch MUST be a self-contained update unit that can be independently identified, verified, and applied.

**Patch Identity**
- Each patch MUST have a unique identifier within its container.
- Patch identifier MUST be deterministically derived or explicitly specified.
- Patch identifier MUST remain stable across encodings.

**Patch Semantics**
- A patch MUST describe the target state or set of operations to apply.
- A patch MUST NOT describe procedural transformation steps; rather, it MUST declare the desired end state.
- A patch MAY depend on other patches via explicit dependency references.
- Patch dependencies MUST form a directed acyclic graph (DAG). Cyclic dependencies MUST be rejected as invalid.

**Streaming Applicability**
- A patch MUST be applicable in a streaming fashion without requiring the entire patch to be buffered in memory.
- A patch's chunks MUST be referenced in a predictable order to enable streaming processing.

## 9. Chunk Model (Normative)

A chunk MUST be an indivisible unit of data storage and identity within IUPD.

**Content Addressing**
- Chunk identity MUST be derived from the content of the chunk via cryptographic hash.
- The content hash MUST be BLAKE3-256 (32 bytes).
- The same chunk data MUST always produce the identical content hash across all platforms and implementations.
- Chunk identity MUST be deterministic: no randomness or external state MAY affect chunk hashing.

**Chunk Boundaries**
- Chunk boundaries MUST be deterministic. Given identical input data, the same sequence of chunks MUST be produced.
- Chunk boundaries MUST NOT depend on external factors such as timestamps, random values, or environment-specific state.
- Chunking algorithms MUST be fully specified and reproducible across all implementations.

**Chunk Data Integrity**
- Each chunk's payload MUST have associated integrity metadata (both CRC32 and BLAKE3).
- CRC32 MUST use the IEEE polynomial (0xEDB88320) for fast corruption detection.
- BLAKE3-256 MUST be used for strong, cryptographically secure verification.

## 10. Manifest Model (Normative)

A manifest MUST be the top-level structure that describes the complete set of changes to apply and their ordering.

**Manifest Contents**
- A manifest MUST enumerate all chunks required for the update.
- A manifest MUST define the apply order for patches and chunks.
- A manifest MUST encode the complete dependency graph between patches.
- A manifest MUST include metadata (version, timestamp if applicable, compatibility information).

**Manifest Canonicalization**
- A manifest MUST be canonical and deterministic. The same logical manifest MUST always encode to identical bytes.
- All ordered lists in the manifest MUST have a stable, reproducible ordering rule.
- All references (to chunks, patches, dependencies) MUST use deterministic identifiers.

**Manifest Validation**
- A manifest MUST be validation before use. Validation MUST check:
  - All referenced chunks are present or referenced correctly.
  - All dependencies are acyclic and consistent.
  - All size and count fields are within allowed limits.

## 11. Apply Model (Normative)

Applying a manifest MUST describe the process of taking patches from the container and delivering them to a target.

**Streaming Apply**
- Patches MUST be applyable in a streaming fashion.
- A reader MUST NOT be required to load the entire patch into memory before starting to apply it.
- Apply operations MUST be able to process chunks as they become available.

**Integrity Verification During Apply**
- Before using a chunk, the reader MUST verify its integrity (both CRC32 and BLAKE3 if integrity is required).
- If integrity verification fails, apply MUST fail with a clear error indicating which chunk failed and why.
- Failed apply MUST not corrupt the target state.

**Error Handling During Apply**
- If a required chunk is missing, apply MUST fail with a specific error (missing chunk error).
- If a chunk is invalid, apply MUST fail with a corruption error.
- If dependencies cannot be satisfied, apply MUST fail with a dependency error.

**Apply Resumption**
- After a failed or interrupted apply, applying the same manifest again MUST be possible.
- Readers MAY implement checkpointing to resume partial applies, but this is not mandatory.

## 12. Determinism Rules (Normative)

Determinism is mandatory for IUPD. All encoding and canonicalization operations MUST be fully deterministic.

**Identical Inputs, Identical Outputs**
- For any given input (patches, chunks, dependencies), encoding MUST always produce identical bytes across any number of encodings, any platform, any implementation language.
- Determinism MUST apply to both the manifest and the chunk data.

**Ordering Canonicalization**
- All ordered sequences (chunk lists, dependency lists, patch lists) MUST use a canonical, deterministically computable ordering rule.
- Ordering rules MUST NOT depend on system time, random values, memory addresses, or any non-deterministic source.

**Hash Stability**
- Cryptographic hashes (BLAKE3-256, CRC32) MUST be platform-independent and produce identical values on all platforms.
- Hash computation MUST not depend on endianness, pointer size, or compiler optimizations (though correct implementations will naturally match).

**Forbidden Non-Deterministic Operations**
- Timestamps or wall-clock time MUST NOT be included in canonical encoding (timestamps MAY be in metadata but MUST NOT affect chunk identity or patch ordering).
- Random or pseudo-random values MUST NOT be used in any encoding operation.
- Environment-dependent values (paths, system info, locale) MUST NOT affect encoding.

## 13. Integrity Rules (Normative)

IUPD MUST provide integrity verification at two levels: fast and strong.

**IEEE CRC32 (Fast Corruption Detection)**
- CRC32 checksums MUST use the IEEE polynomial (0xEDB88320).
- CRC32 MUST be computed over chunk payloads and manifest data.
- CRC32 MUST be used to detect accidental corruption (bit flips, transmission errors).
- CRC32 alone MUST NOT be used for authenticity verification (use BLAKE3 for that).

**BLAKE3-256 (Strong Verification)**
- BLAKE3-256 hashes MUST be computed over all chunk payloads.
- BLAKE3-256 hashes MUST be used as content addresses (chunk identity).
- BLAKE3-256 hashes MUST be verified during apply to ensure chunk integrity.
- BLAKE3-256 computations MUST be deterministic and identical across all platforms and implementations.

**Manifest Integrity**
- The manifest itself MUST have integrity protection. Manifest fields, structure, and metadata MUST be verifiable against tampering.
- Manifest integrity verification MUST be part of strict validation.

## 14. Validation Modes (Normative)

IUPD MUST support two validation modes with different rigor levels.

**validate_fast (Quick Structural Check)**
- validate_fast MUST check basic structural validity:
  - Magic number is correct (IUPD or UPD1).
  - File header is well-formed and within expected size.
  - Manifest is present and has valid size fields.
  - No out-of-bounds references.
  - All required fields are present.
- validate_fast MUST complete in O(1) or O(log n) time relative to file size.
- validate_fast MUST NOT compute cryptographic hashes or read all payload data.

**validate_strict (Full Integrity Check)**
- validate_strict MUST perform all fast validation checks.
- validate_strict MUST additionally verify:
  - All CRC32 checksums match computed values.
  - All BLAKE3-256 hashes match computed values.
  - Dependency graph is acyclic and complete.
  - All references resolve to valid, present chunks.
  - No chunks are missing or inaccessible.
- validate_strict MUST complete even if corruption is found (report all errors).

**Failure Determinism**
- Validation MUST be deterministic. The same input MUST always produce the same validation result (pass or fail with identical errors).

## 15. Limits and DoS Policy (Normative)

IUPD implementations MUST enforce explicit limits to prevent Denial-of-Service attacks.

**Chunk Limits**
- Maximum chunk count per manifest: MUST be enforced (suggested: 1,000,000 chunks).
- Maximum chunk payload size: MUST be enforced (suggested: 1 GB per chunk).
- Total manifest payload size: MUST be enforced (suggested: 100 GB).

**Manifest Limits**
- Maximum manifest header size: MUST be enforced (suggested: 100 MB).
- Maximum number of patches per manifest: MUST be enforced (suggested: 10,000 patches).

**Dependency Limits**
- Maximum dependency graph depth: MUST be enforced (suggested: 1,000 levels).
- Maximum fan-in (predecessors) per patch: MUST be enforced (suggested: 10,000 predecessors).
- Cyclic dependency detection: MUST reject any cycle immediately.

**Size and Offset Limits**
- All offset and size fields MUST be bounds-checked.
- Any offset or size that exceeds the file size or manifest bounds MUST be rejected.
- Negative sizes or invalid offsets MUST be rejected.

**Reader Behavior**
- Readers MUST enforce these limits and reject any input exceeding them.
- Readers MUST provide clear error messages indicating which limit was exceeded.
- Readers MUST fail safely without buffer overflows or resource exhaustion.

## 16. Error Model (Normative)

IUPD implementations MUST use a stable, well-defined error model.

**Error Codes**
- All error conditions MUST have stable, numeric error codes.
- Error codes MUST be consistent across all implementations (C, .NET, and others).
- Error codes MUST be documented and unchanging.

**Error Information**
- Each error MUST include:
  - Numeric error code.
  - Human-readable error message.
  - Byte offset or logical reference where the error occurred (e.g., which chunk, which dependency).
  - Context (what was being checked when the error occurred).

**Error Categories**
- **Structural Errors**: File format violated (bad magic, truncated header, invalid size).
- **Corruption Errors**: Data integrity check failed (CRC32 mismatch, BLAKE3 mismatch).
- **Dependency Errors**: Dependency graph invalid (cyclic, missing, unresolvable).
- **Limit Errors**: Exceeds resource limits (too many chunks, chunk too large, etc.).
- **Apply Errors**: Failed during apply phase (missing chunk, incompatibility, etc.).

**Deterministic Error Reporting**
- The same invalid input MUST always produce the same error code and message.
- Error messages MUST NOT vary based on system state or runtime conditions.
- Error offsets and references MUST be deterministic.

## 17. File Structure Overview (Normative)

An IUPD file MUST consist of exactly four contiguous sections in the following order:

1. **File Header** (fixed size, defined in section 18)
2. **Chunk Table** (variable size, defined in section 19)
3. **Manifest** (variable size, defined in section 20)
4. **Chunk Payloads** (variable size, defined in section 21)

**Contiguity and Offsets**
- All sections MUST be contiguous with no gaps or padding between sections.
- All offsets in the file MUST be absolute byte offsets from the start of the file (byte 0).
- The file MUST contain no data outside these four sections.
- The end of the file MUST be at the end of the last chunk payload.

**Section Boundaries**
- Chunk Table MUST start at offset given in FileHeader.ChunkTableOffset.
- Manifest MUST start at offset given in FileHeader.ManifestOffset.
- Chunk Payloads MUST start at offset given in FileHeader.PayloadOffset.
- Each section's size MUST be computable from the start offset of the next section (or file size for the last section).

## 18. File Header Layout (Normative)

The File Header MUST be exactly 36 bytes and MUST be located at byte offset 0 of the file.

**Header Structure (little-endian):**

| Offset | Field | Type | Size | Description |
|--------|-------|------|------|-------------|
| 0 | Magic | ASCII | 4 | MUST be "IUPD" (0x49 0x55 0x50 0x44) |
| 4 | Version | u8 | 1 | MUST be 0x01 for IUPD v1 |
| 5 | Flags | u32 | 4 | Bit flags (see below) |
| 9 | HeaderSize | u16 | 2 | MUST be 36 (header is always 36 bytes) |
| 11 | Reserved | u8 | 1 | MUST be 0x00 |
| 12 | ChunkTableOffset | u64 | 8 | Byte offset to Chunk Table start |
| 20 | ManifestOffset | u64 | 8 | Byte offset to Manifest start |
| 28 | PayloadOffset | u64 | 8 | Byte offset to Chunk Payloads start |

**Total: 36 bytes**

**Endianness**
- All multi-byte fields MUST use little-endian byte order.
- All readers MUST validate endianness by checking magic number.

**Flags Field (u32)**
- Bit 0: Reserved (MUST be 0)
- Bit 1: Reserved (MUST be 0)
- Bit 2: Reserved (MUST be 0)
- Bits 3-31: Reserved (MUST be 0)
- All flags MUST be 0 in v1. Future versions MAY define additional flags.

**Offset Validation**
- ChunkTableOffset MUST be >= 36 (after header).
- ManifestOffset MUST be > ChunkTableOffset (manifest after chunk table).
- PayloadOffset MUST be > ManifestOffset (payloads after manifest).
- PayloadOffset MUST be <= file size.

## 19. Chunk Table Layout (Normative)

The Chunk Table MUST be located at the offset specified in FileHeader.ChunkTableOffset.

**Chunk Table Structure**
- The Chunk Table MUST consist of a sequence of Chunk Entry records.
- Each Chunk Entry MUST be exactly 56 bytes.
- Chunk Entries MUST be stored in ascending order by ChunkIndex (0, 1, 2, ...).
- ChunkIndex values MUST be zero-based and contiguous (no gaps, no duplicates).

**Chunk Entry Structure (56 bytes, little-endian):**

| Offset | Field | Type | Size | Description |
|--------|-------|------|------|-------------|
| 0 | ChunkIndex | u32 | 4 | Zero-based index (0, 1, 2, ...) |
| 4 | PayloadSize | u64 | 8 | Byte size of chunk payload |
| 12 | PayloadOffset | u64 | 8 | Byte offset where chunk payload starts |
| 20 | PayloadCrc32 | u32 | 4 | IEEE CRC32 of payload bytes |
| 24 | PayloadBlake3 | u8[32] | 32 | BLAKE3-256 hash of payload |

**Total per entry: 56 bytes**

**Chunk Table Count**
- The number of entries in the Chunk Table MUST be determinable from: (ManifestOffset - ChunkTableOffset) / 56
- This MUST result in an integer (no partial entries).
- If division is not exact, the file MUST be rejected as malformed.

**Chunk Index Ordering**
- ChunkIndex values in successive entries MUST be 0, 1, 2, 3, ... up to (ChunkCount - 1).
- Any missing, duplicated, or out-of-order ChunkIndex MUST be rejected as invalid.

**Payload Offset and Size Validation**
- Each PayloadOffset MUST be >= PayloadOffset (from FileHeader).
- Each PayloadOffset + PayloadSize MUST be <= next chunk's PayloadOffset (or file size for last chunk).
- Overlapping payload ranges MUST be rejected.
- PayloadSize MUST be > 0 (empty chunks MUST NOT be encoded).

## 20. Manifest Encoding (Normative)

The Manifest MUST be located at the offset specified in FileHeader.ManifestOffset.

**Manifest Structure**
- Manifest MUST be a binary-encoded structure containing:
  - Manifest Header (fixed 24 bytes)
  - Dependency List (variable size)
  - Apply Order List (variable size)
  - Manifest Integrity (8 bytes for CRC32 and BLAKE3)

**Manifest Header (24 bytes, little-endian):**

| Offset | Field | Type | Size | Description |
|--------|-------|------|------|-------------|
| 0 | ManifestVersion | u8 | 1 | MUST be 0x01 |
| 1 | Reserved | u8[3] | 3 | MUST be 0 |
| 4 | TargetVersion | u32 | 4 | Target system version identifier |
| 8 | DependencyCount | u32 | 4 | Number of dependencies |
| 12 | ApplyOrderCount | u32 | 4 | Number of chunks in apply order |
| 16 | ManifestSize | u64 | 8 | Total manifest size (including this header and all following data) |

**Total: 24 bytes**

**Dependency List Encoding**
- Immediately follows Manifest Header.
- Each dependency entry MUST be 8 bytes (little-endian):
  - DependentPatchId (u32): Patch that depends on another
  - RequiredPatchId (u32): Patch that must be applied first
- Total size: DependencyCount * 8 bytes
- Dependency list MUST form a directed acyclic graph (DAG). Cycles MUST be rejected.

**Apply Order List Encoding**
- Immediately follows Dependency List.
- Each entry MUST be 4 bytes (little-endian):
  - ChunkIndex (u32): Index of chunk to apply next
- Total size: ApplyOrderCount * 4 bytes
- Apply order MUST reference valid chunk indices (0 to ChunkCount-1).
- All chunks MUST be referenced exactly once in apply order.

**Manifest Integrity (8 bytes)**
- Immediately follows Apply Order List.
- First 4 bytes: ManifestCrc32 (u32, little-endian) - IEEE CRC32 of all preceding manifest data (header + dependencies + apply order)
- Next 4 bytes: Reserved (MUST be 0)

**Manifest Size Validation**
- ManifestSize MUST equal: 24 + (DependencyCount * 8) + (ApplyOrderCount * 4) + 8
- ManifestSize MUST match (PayloadOffset - ManifestOffset).
- If mismatch, file MUST be rejected as malformed.

**Determinism**
- Manifest encoding MUST be canonical. Same logical manifest MUST always encode to identical bytes.
- Dependency list and apply order MUST be in canonical order (implementation-defined but reproducible).

## 21. Chunk Payload Encoding (Normative)

Chunk payloads MUST be located at the offset specified in FileHeader.PayloadOffset.

**Payload Layout**
- Chunk payloads MUST be stored sequentially in the order defined by Chunk Table.
- Each chunk MUST occupy exactly PayloadSize bytes as specified in its Chunk Table entry.
- No compression, encryption, or transformation MUST be applied to payloads in v1.
- Payloads are raw data bytes as-is.

**Payload Boundaries**
- First chunk starts at PayloadOffset.
- Each subsequent chunk starts immediately after the previous chunk (no gaps, no padding).
- Last chunk ends at file end.

**Payload Storage**
- Chunk payloads MUST match the byte counts specified in Chunk Table (PayloadSize).
- If actual payload size differs from PayloadSize, file MUST be rejected.

## 22. Apply Order and Streaming Rules (Normative)

Applying patches MUST follow the order and rules defined by the manifest.

**Apply Sequence**
- Patches MUST be applied in the order specified by Manifest.ApplyOrderList.
- Each entry in ApplyOrderList specifies a ChunkIndex to apply next.
- Chunks MUST be applied sequentially in this order.

**Streaming Apply**
- Readers MUST support streaming apply without loading entire file into memory.
- After reading Manifest.ApplyOrderList, readers MUST be able to:
  - Seek to first chunk in apply order
  - Read and apply chunk
  - Verify chunk integrity (CRC32, BLAKE3)
  - Move to next chunk in apply order
- Readers MUST NOT require loading all chunks before starting apply.

**Apply Resumption**
- After interruption or failure, applying the same manifest again MUST be possible.
- Readers MAY implement checkpointing by recording which ChunkIndex was last applied.
- On resume, reader MUST verify already-applied chunks have not changed (by re-checking CRC32).
- Resuming MUST always be safe and MUST NOT corrupt target state.

**Dependency Checking**
- Before applying a chunk, reader MUST verify all its dependencies are satisfied.
- Dependencies MUST be tracked and validated during apply.
- If a dependency is violated, apply MUST fail immediately with a dependency error.

## 23. Integrity Encoding (Normative)

Two levels of integrity checking MUST be supported in IUPD v1.

**CRC32 IEEE Integrity**
- Each chunk's PayloadCrc32 MUST be computed using the IEEE polynomial (0xEDB88320).
- CRC32 computation MUST be deterministic and produce identical results on all platforms.
- CRC32 MUST be stored in Chunk Table entry (PayloadCrc32 field).
- CRC32 MUST be verified during validate_fast and validate_strict.

**BLAKE3-256 Integrity**
- Each chunk's PayloadBlake3 MUST be computed using BLAKE3-256 (32-byte output).
- BLAKE3-256 computation MUST be deterministic and produce identical results on all platforms.
- BLAKE3-256 MUST be stored in Chunk Table entry (PayloadBlake3 field).
- BLAKE3-256 MUST be verified during validate_strict (not validate_fast).

**Manifest Integrity**
- Manifest MUST include ManifestCrc32 (last 4 bytes of Manifest structure).
- ManifestCrc32 MUST be IEEE CRC32 of entire manifest excluding the CRC32 field itself.
- Manifest integrity MUST be verified during validate_fast and validate_strict.

**Per-Chunk Verification**
- Each chunk CRC32 and BLAKE3 MUST be independently verifiable without accessing other chunks.
- This enables streaming verification without buffering entire file.

## 24. Validation Rules (Normative)

IUPD implementations MUST support two validation modes with precise rules.

**validate_fast (Quick Structural Check)**

validate_fast MUST check:
- File size >= 36 (minimum header size)
- Magic is "IUPD"
- Version is 0x01
- Flags are 0x00000000
- HeaderSize is 36
- ChunkTableOffset >= 36
- ManifestOffset > ChunkTableOffset
- PayloadOffset > ManifestOffset
- PayloadOffset <= file size
- (ManifestOffset - ChunkTableOffset) is divisible by 56
- ManifestVersion is 0x01
- ManifestSize is consistent with manifest bounds
- All Chunk Table entries have ascending ChunkIndex starting at 0
- All payload offsets are in bounds
- No overlapping payload ranges
- All chunk PayloadSize > 0

validate_fast MUST NOT:
- Compute CRC32 or BLAKE3 hashes
- Read entire chunk payloads
- Validate dependency graph

**validate_strict (Full Integrity Check)**

validate_strict MUST perform all validate_fast checks, then additionally:
- Compute IEEE CRC32 for each chunk payload and verify against PayloadCrc32
- Compute BLAKE3-256 for each chunk payload and verify against PayloadBlake3
- Compute CRC32 for manifest and verify against ManifestCrc32
- Verify dependency graph is acyclic (no cycles)
- Verify all dependencies reference valid patches
- Verify apply order is complete (all chunks referenced exactly once)

validate_strict MUST complete even if errors are found (report all errors, not just first).

**Failure Behavior**
- Both validation modes MUST be deterministic. Same input MUST always produce same result.
- If validation fails, error code and byte offset MUST be reported.
- Partial corruption MUST not prevent error detection (detect all possible errors).

## 25. Error Codes (Normative)

IUPD implementations MUST use the following stable numeric error codes.

**Error Code Definitions (little-endian u16)**

| Code | Name | Meaning |
|------|------|---------|
| 0x0001 | InvalidMagic | File magic is not "IUPD" |
| 0x0002 | UnsupportedVersion | Version byte is not 0x01 |
| 0x0003 | InvalidFlags | Flags field is not 0x00000000 |
| 0x0004 | InvalidHeaderSize | HeaderSize is not 36 |
| 0x0005 | OffsetOutOfBounds | Offset exceeds file size |
| 0x0006 | InvalidChunkTableSize | Chunk table size not divisible by 56 |
| 0x0007 | ChunkIndexError | ChunkIndex not in ascending order from 0 |
| 0x0008 | OverlappingPayloads | Chunk payloads overlap |
| 0x0009 | EmptyChunk | Chunk PayloadSize is 0 |
| 0x000A | InvalidManifestVersion | ManifestVersion is not 0x01 |
| 0x000B | ManifestSizeMismatch | ManifestSize inconsistent with offsets |
| 0x000C | Crc32Mismatch | IEEE CRC32 does not match (chunk or manifest) |
| 0x000D | Blake3Mismatch | BLAKE3-256 does not match (chunk) |
| 0x000E | CyclicDependency | Dependency graph contains cycle |
| 0x000F | InvalidDependency | Dependency references nonexistent patch |
| 0x0010 | MissingChunkInApplyOrder | Chunk not referenced in apply order |
| 0x0011 | DuplicateChunkInApplyOrder | Chunk referenced multiple times in apply order |
| 0x0012 | MissingChunk | Required chunk not found in file |
| 0x0013 | ApplyError | Error during patch apply phase |
| 0x0014 | UnknownError | Unknown or unclassified error |

**Error Information**

Each error MUST include:
- Numeric error code (u16)
- Byte offset or logical reference (u64, or u32 chunk index for chunk-related errors)
- Human-readable error message (for debugging)
- Context (which operation failed: validation, apply, etc.)

**Error Reporting Requirements**

- Same invalid input MUST always produce same error code.
- Byte offsets MUST be absolute file offsets (or -1 if not applicable).
- Chunk-related errors MUST report ChunkIndex.
- Dependency-related errors MUST report patch IDs.
- Errors MUST NOT include timestamps, random values, or non-deterministic information.

---

**Specification Version:** 1.0 (DESIGN)
**Last Updated:** 2026-01-17
**Status:** DESIGN — Under active development
