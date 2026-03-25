#ifndef IRONCFG_IUPD_H
#define IRONCFG_IUPD_H

#include <stdint.h>
#include <stddef.h>
#include "ironcfg_common.h"

#ifdef __cplusplus
extern "C" {
#endif

/* IUPD Constants */
#define IUPD_MAGIC_PRIMARY 0x44505549U   /* "IUPD" in little-endian */
#define IUPD_MAGIC_ALTERNATE 0x31445055U /* "UPD1" in little-endian */
#define IUPD_VERSION 0x01
#define IUPD_FILE_HEADER_SIZE 36
#define IUPD_CHUNK_ENTRY_SIZE 56
#define IUPD_MANIFEST_HEADER_SIZE 24

#define IUPD_MAX_CHUNKS 1000000UL
#define IUPD_MAX_CHUNK_SIZE (1UL << 30)  /* 1 GB */
#define IUPD_MAX_MANIFEST_SIZE (100UL << 20)  /* 100 MB */

/* IUPD Error Codes (per spec section 25) */
typedef enum {
    IUPD_ERR_OK = 0x0000,
    IUPD_ERR_INVALID_MAGIC = 0x0001,
    IUPD_ERR_UNSUPPORTED_VERSION = 0x0002,
    IUPD_ERR_INVALID_FLAGS = 0x0003,
    IUPD_ERR_INVALID_HEADER_SIZE = 0x0004,
    IUPD_ERR_OFFSET_OUT_OF_BOUNDS = 0x0005,
    IUPD_ERR_INVALID_CHUNK_TABLE_SIZE = 0x0006,
    IUPD_ERR_CHUNK_INDEX_ERROR = 0x0007,
    IUPD_ERR_OVERLAPPING_PAYLOADS = 0x0008,
    IUPD_ERR_EMPTY_CHUNK = 0x0009,
    IUPD_ERR_INVALID_MANIFEST_VERSION = 0x000A,
    IUPD_ERR_MANIFEST_SIZE_MISMATCH = 0x000B,
    IUPD_ERR_CRC32_MISMATCH = 0x000C,
    IUPD_ERR_BLAKE3_MISMATCH = 0x000D,
    IUPD_ERR_CYCLIC_DEPENDENCY = 0x000E,
    IUPD_ERR_INVALID_DEPENDENCY = 0x000F,
    IUPD_ERR_MISSING_CHUNK_IN_APPLY_ORDER = 0x0010,
    IUPD_ERR_DUPLICATE_CHUNK_IN_APPLY_ORDER = 0x0011,
    IUPD_ERR_MISSING_CHUNK = 0x0012,
    IUPD_ERR_APPLY_ERROR = 0x0013,
    IUPD_ERR_UNKNOWN_ERROR = 0x0014
} iupd_error_code_t;

/* IUPD Error Structure */
typedef struct {
    iupd_error_code_t code;
    uint64_t byte_offset;
    uint32_t chunk_index;  /* Only valid if code is chunk-related */
    const char *message;
} iupd_error_t;

/* Chunk Entry from Chunk Table (56 bytes) */
typedef struct {
    uint32_t chunk_index;      /* 0-3: ChunkIndex */
    uint64_t payload_size;     /* 4-11: PayloadSize */
    uint64_t payload_offset;   /* 12-19: PayloadOffset */
    uint32_t payload_crc32;    /* 20-23: PayloadCrc32 */
    uint8_t payload_blake3[32]; /* 24-55: PayloadBlake3 */
} iupd_chunk_entry_t;

/* IUPD Reader Context */
typedef struct {
    const uint8_t *data;
    size_t size;

    /* Parsed header fields */
    uint32_t magic;
    uint8_t version;
    uint32_t flags;
    uint16_t header_size;
    uint8_t reserved;
    uint64_t chunk_table_offset;
    uint64_t manifest_offset;
    uint64_t payload_offset;

    /* Parsed metadata */
    uint32_t chunk_count;
    uint32_t dependency_count;
    uint32_t apply_order_count;
    uint64_t manifest_size;
    uint32_t manifest_crc32;

    /* Last error */
    iupd_error_t last_error;
} iupd_ctx_t;

/* Chunk during apply (spans data buffer) */
typedef struct {
    uint32_t chunk_index;
    const uint8_t *payload_ptr;
    uint64_t payload_size;
    uint32_t payload_crc32;
    const uint8_t *payload_blake3;
} iupd_chunk_t;

/* IUPD Apply Context */
typedef struct {
    const iupd_ctx_t *file_ctx;
    uint32_t apply_order_index;
} iupd_apply_ctx_t;

/* IUPD Reader Functions */

/**
 * iupd_open - Parse and open an IUPD file for reading.
 * @data: Binary data buffer containing IUPD file
 * @size: Size of data buffer in bytes
 * @out: Output reader context (must not be NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   Non-zero on error (error is stored in out->last_error)
 *
 * On success, *out is initialized and can be used for validation and apply.
 */
icfg_status_t iupd_open(const uint8_t *data, size_t size, iupd_ctx_t *out);

/**
 * iupd_validate_fast - Perform fast structural validation.
 * @ctx: Reader context from iupd_open()
 *
 * Returns:
 *   ICFG_OK on success (valid), non-zero on error
 *
 * Checks file structure, offsets, chunk table format, and manifest bounds.
 * Does NOT verify CRC32 or BLAKE3.
 * Error details are stored in ctx->last_error.
 */
icfg_status_t iupd_validate_fast(iupd_ctx_t *ctx);

/**
 * iupd_validate_strict - Perform full integrity validation.
 * @ctx: Reader context from iupd_open()
 *
 * Returns:
 *   ICFG_OK on success (fully valid), non-zero on error
 *
 * Performs all fast checks plus CRC32 verification for all chunks
 * and manifest, and BLAKE3 verification for all chunks.
 * Error details are stored in ctx->last_error.
 */
icfg_status_t iupd_validate_strict(iupd_ctx_t *ctx);

/**
 * iupd_apply_begin - Start streaming apply of patches.
 * @ctx: Reader context from iupd_open()
 * @out_applier: Output applier context (must not be NULL)
 *
 * Returns:
 *   ICFG_OK on success, non-zero on error
 *
 * Initializes applier for streaming through chunks in apply order.
 * Must call iupd_apply_next() to iterate chunks.
 */
icfg_status_t iupd_apply_begin(const iupd_ctx_t *ctx, iupd_apply_ctx_t *out_applier);

/**
 * iupd_apply_next - Get next chunk in apply order.
 * @applier: Applier context from iupd_apply_begin()
 * @out_chunk: Output chunk struct (must not be NULL)
 *
 * Returns:
 *   ICFG_OK if chunk returned successfully
 *   ICFG_ERR_RANGE if end of apply order (normal end)
 *   Other icfg_status_t on error
 *
 * Chunks are yielded in the order specified by the manifest.
 * After reaching the end, further calls return ICFG_ERR_RANGE.
 */
icfg_status_t iupd_apply_next(iupd_apply_ctx_t *applier, iupd_chunk_t *out_chunk);

/**
 * iupd_apply_end - Clean up apply context.
 * @applier: Applier context from iupd_apply_begin()
 *
 * Closes and cleans up the apply context.
 */
void iupd_apply_end(iupd_apply_ctx_t *applier);

#ifdef __cplusplus
}
#endif

#endif /* IRONCFG_IUPD_H */
