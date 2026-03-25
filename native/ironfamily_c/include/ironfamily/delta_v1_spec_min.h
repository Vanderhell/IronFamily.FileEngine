/*
 * IUPD Delta v1 Minimal Format Specification for C99 Apply
 * Extracted from C# IupdDeltaV1 reference implementation
 *
 * Source: libs/ironconfig-dotnet/src/IronConfig/Iupd/Delta/IupdDeltaV1.cs
 *
 * Fixed-chunk deterministic delta compression (4096-byte chunks).
 * Fail-closed: base hash verified before, target hash verified after applying.
 */

#ifndef DELTA_V1_SPEC_MIN_H
#define DELTA_V1_SPEC_MIN_H

#include <stdint.h>

/* === MAGIC, VERSION, CHUNK SIZE === */
#define DIFF_MAGIC "IUPDDEL1"              /* 8 bytes ASCII */
#define DIFF_VERSION 1                     /* u32, little-endian */
#define DIFF_CHUNK_SIZE 4096               /* Fixed chunk size */
#define DIFF_HASH_SIZE 32                  /* BLAKE3-256 */

/* === HEADER STRUCTURE (96 bytes fixed) === */
/* Layout:
 * [0-7]    Magic "IUPDDEL1" (8 bytes ASCII)
 * [8-11]   Version (u32 LE, must be 1)
 * [12-15]  ChunkSize (u32 LE, must be 4096)
 * [16-23]  TargetLength (u64 LE)
 * [24-55]  BaseHash (32 bytes, BLAKE3-256)
 * [56-87]  TargetHash (32 bytes, BLAKE3-256)
 * [88-91]  EntryCount (u32 LE)
 * [92-95]  Reserved (u32 LE, must be 0)
 */
#define DIFF_HEADER_SIZE 96
#define DIFF_MAGIC_OFFSET 0
#define DIFF_VERSION_OFFSET 8
#define DIFF_CHUNK_SIZE_OFFSET 12
#define DIFF_TARGET_LEN_OFFSET 16
#define DIFF_BASE_HASH_OFFSET 24
#define DIFF_TARGET_HASH_OFFSET 56
#define DIFF_ENTRY_COUNT_OFFSET 88
#define DIFF_RESERVED_OFFSET 92

/* === ENTRY RECORDS (variable, starting at offset 96) === */
/* Each entry (variable length):
 * [0-3]    ChunkIndex (u32 LE)
 * [4-7]    DataLen (u32 LE, 1 to 4096)
 * [8...]   Data (DataLen bytes)
 */
#define DIFF_ENTRY_HEADER_SIZE 8

/* === DoS LIMITS === */
#define DIFF_MAX_ENTRIES 1000000                /* Max changed chunks */
#define DIFF_MAX_PATCH_BYTES 512000000          /* 512 MB */
#define DIFF_MAX_OUTPUT_SIZE (4UL << 30)        /* 4 GB default output */

#endif /* DELTA_V1_SPEC_MIN_H */
