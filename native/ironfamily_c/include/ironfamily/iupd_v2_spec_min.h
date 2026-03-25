/*
 * IUPD v2 Minimal Format Specification for C99 Verifier
 * Extracted from C# IronConfig IUPD reference implementation
 *
 * This header defines ONLY the minimum fields and byte offsets needed
 * to implement strict verification gates (fail-closed semantics).
 *
 * Source: libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdReader.cs
 * and libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdWriter.cs
 */

#ifndef IUPD_V2_SPEC_MIN_H
#define IUPD_V2_SPEC_MIN_H

#include <stdint.h>

/* === MAGIC AND VERSION === */
#define IUPD_MAGIC 0x44505549              /* "IUPD" in little-endian */
#define IUPD_VERSION_V2 0x02
#define IUPD_VERSION_V1 0x01

/* === PROFILE BYTE (V2 offset 5) === */
#define IUPD_PROFILE_MINIMAL   0x00
#define IUPD_PROFILE_FAST      0x01
#define IUPD_PROFILE_SECURE    0x02        /* Allowed for v2 strict verifier */
#define IUPD_PROFILE_OPTIMIZED 0x03        /* Allowed for v2 strict verifier */
#define IUPD_PROFILE_INCREMENTAL 0x04     /* Allowed for v2 strict verifier (patch-bound) */

/* === FILE HEADER STRUCTURE (V2: 37 bytes) === */
/* Layout for IUPD v2:
 * [0-3]   Magic (u32 LE)
 * [4]     Version (u8, must be 0x02)
 * [5]     Profile (u8)
 * [6-9]   Flags (u32 LE, only bit 0 WITNESS_ENABLED allowed)
 * [10-11] Header size (u16 LE, must be 37)
 * [12]    Reserved (u8, must be 0)
 * [13-20] ChunkTableOffset (u64 LE)
 * [21-28] ManifestOffset (u64 LE)
 * [29-36] PayloadOffset (u64 LE)
 */
#define IUPD_V2_HEADER_SIZE 37
#define IUPD_V2_MAGIC_OFFSET 0
#define IUPD_V2_VERSION_OFFSET 4
#define IUPD_V2_PROFILE_OFFSET 5
#define IUPD_V2_FLAGS_OFFSET 6
#define IUPD_V2_HEADER_SIZE_OFFSET 10
#define IUPD_V2_RESERVED_OFFSET 12
#define IUPD_V2_CHUNK_TABLE_OFFSET_OFFSET 13
#define IUPD_V2_MANIFEST_OFFSET_OFFSET 21
#define IUPD_V2_PAYLOAD_OFFSET_OFFSET 29

#define IUPD_V2_FLAGS_WITNESS_ENABLED 0x00000001

/* === MANIFEST STRUCTURE === */
/* Manifest header (24 bytes):
 * [0-3]   DependencyCount (u32 LE)
 * [4-7]   ApplyOrderCount (u32 LE)
 * [8-11]  ManifestCrc32 (u32 LE)
 * [12-15] (reserved/unused)
 * [16-23] ManifestSize (u64 LE, part of the total manifest)
 *
 * Manifest body:
 * DependencyCount * 8 bytes (each: [0-3] from, [4-7] to)
 * ApplyOrderCount * 4 bytes (each: chunk index u32 LE)
 * Last 8 bytes: CRC32 (4) + Reserved (4)
 */
#define IUPD_MANIFEST_HEADER_SIZE 24
#define IUPD_MANIFEST_CRCRESV_SIZE 8   /* Last 8 bytes of manifest (CRC32 + reserved) */

/* === CHUNK TABLE STRUCTURE === */
/* Each chunk entry: 56 bytes
 * Not needed for strict verification, but defined for completeness
 */
#define IUPD_CHUNK_ENTRY_SIZE 56

/* === SIGNATURE FOOTER (after manifest) === */
/* Format:
 * [0-3]   SignatureLength (u32 LE, must be 64)
 * [4-67]  Signature (64 bytes, Ed25519)
 * [68-99] WitnessHash (32 bytes, BLAKE3-256, optional for witness flag)
 */
#define IUPD_SIGNATURE_LENGTH 64
#define IUPD_WITNESS_HASH_LENGTH 32

/* === UPDATESEQUENCE TRAILER (optional, before payloads) === */
/* Format (21 bytes total):
 * [0-7]   Magic ("IUPDDEL1" in ASCII)
 * [8-11]  Length (u32 LE, must be 21)
 * [12]    Version (u8, must be 1)
 * [13-20] Sequence (u64 LE)
 */
#define IUPD_UPDATESEQ_MAGIC_STR "IUPDSEQ1"
#define IUPD_UPDATESEQ_TRAILER_SIZE 21
#define IUPD_UPDATESEQ_VERSION 1

/* === DoS LIMITS === */
#define IUPD_MAX_CHUNKS (1000000UL)
#define IUPD_MAX_CHUNK_SIZE (1UL << 30)    /* 1 GB */
#define IUPD_MAX_MANIFEST_SIZE (100UL << 20) /* 100 MB */

/* === ED25519 CONSTANTS === */
#define ED25519_PUBLIC_KEY_SIZE 32
#define ED25519_SIGNATURE_SIZE 64

#endif /* IUPD_V2_SPEC_MIN_H */
