/*
 * IRONDEL2 (Delta v2) Minimal Format Specification for C99 Apply
 * Extracted from C# IronDel2 reference implementation
 *
 * Source: libs/ironconfig-dotnet/src/IronConfig/Iupd/Delta/IupdDeltaV2Cdc.cs
 * Spec: specs/IRONDEL2_SPEC_MIN.md
 *
 * Content-Defined Chunking (CDC) delta format with COPY/LIT operations.
 * Fail-closed: base hash verified before, output hash verified after applying.
 */

#ifndef DELTA2_SPEC_MIN_H
#define DELTA2_SPEC_MIN_H

#include <stdint.h>

/* === MAGIC, VERSION === */
#define DELTA2_MAGIC "IRONDEL2"          /* 8 bytes ASCII */
#define DELTA2_VERSION 0x01               /* u8 */
#define DELTA2_FLAGS 0x00                 /* u8, must be 0x00 */
#define DELTA2_RESERVED 0x0000            /* u16, must be 0x0000 */
#define DELTA2_HASH_SIZE 32               /* BLAKE3-256 */

/* === HEADER STRUCTURE (Fixed 100 bytes) === */
/* Layout:
 * [0-7]    Magic "IRONDEL2" (8 bytes ASCII)
 * [8]      Version (u8, must be 0x01)
 * [9]      Flags (u8, must be 0x00)
 * [10-11]  Reserved (u16 LE, must be 0x0000)
 * [12-19]  BaseLen (u64 LE, length of base input)
 * [20-27]  TargetLen (u64 LE, expected length of output)
 * [28-59]  BaseHash (32 bytes, BLAKE3-256 of base)
 * [60-91]  TargetHash (32 bytes, BLAKE3-256 of expected output)
 * [92-95]  OpCount (u32 LE, number of COPY/LIT operations)
 * [96-99]  HeaderCrc32 (u32 LE, CRC32 of header with [96:100] = [0,0,0,0])
 */
#define DELTA2_HEADER_SIZE 100
#define DELTA2_MAGIC_OFFSET 0
#define DELTA2_VERSION_OFFSET 8
#define DELTA2_FLAGS_OFFSET 9
#define DELTA2_RESERVED_OFFSET 10
#define DELTA2_BASE_LEN_OFFSET 12
#define DELTA2_TARGET_LEN_OFFSET 20
#define DELTA2_BASE_HASH_OFFSET 28
#define DELTA2_TARGET_HASH_OFFSET 60
#define DELTA2_OP_COUNT_OFFSET 92
#define DELTA2_HEADER_CRC32_OFFSET 96

/* === OPCODES === */
#define DELTA2_OP_COPY 0x01
#define DELTA2_OP_LIT 0x02

/* === OPERATION STREAM (Variable, starts at offset 100) === */
/* COPY Operation (13 bytes):
 * [0]      Opcode (u8, 0x01)
 * [1-8]    BaseOffset (u64 LE, byte offset in base to copy from)
 * [9-12]   Length (u32 LE, number of bytes to copy)
 */
#define DELTA2_COPY_OP_SIZE 13
#define DELTA2_COPY_BASE_OFFSET_OFFSET 1
#define DELTA2_COPY_LENGTH_OFFSET 9

/* LIT Operation (5 + length bytes):
 * [0]      Opcode (u8, 0x02)
 * [1-4]    Length (u32 LE, number of literal bytes)
 * [5...]   Data (length bytes, literal data to append)
 */
#define DELTA2_LIT_HEADER_SIZE 5
#define DELTA2_LIT_LENGTH_OFFSET 1

/* === DoS LIMITS === */
#define DELTA2_MAX_OPS 20000000           /* Maximum operations per patch */
#define DELTA2_MAX_COPY_LEN (1UL << 30)   /* 1 GB per COPY operation */
#define DELTA2_MAX_LIT_LEN (1UL << 30)    /* 1 GB per LIT operation */
#define DELTA2_MAX_OUTPUT_SIZE (4294967295UL) /* 4 GB max output */

#endif /* DELTA2_SPEC_MIN_H */
