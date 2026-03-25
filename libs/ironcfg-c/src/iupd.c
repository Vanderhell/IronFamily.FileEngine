/*
 * iupd.c
 * IUPD (Update / Patch Container) C99 reader and applier implementation
 *
 * Status: Reference implementation (DESIGN)
 * License: MIT
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>

#include "../include/ironcfg/iupd.h"

/* Forward declaration to avoid implicit declaration on some compilers */
icfg_status_t iupd_get_chunk_entry(const iupd_ctx_t* ctx, uint32_t chunk_index,
                                   iupd_chunk_entry_t* out_entry);

/* ============================================================================
 * CRC32 IEEE Implementation (per spec section 23)
 * ============================================================================ */

static uint32_t iupd_crc32_table[256];
static bool iupd_crc32_table_initialized = false;

static void iupd_crc32_init_table(void) {
    if (iupd_crc32_table_initialized) return;

    const uint32_t polynomial = 0xEDB88320;
    for (int i = 0; i < 256; i++) {
        uint32_t crc = i;
        for (int j = 0; j < 8; j++) {
            if ((crc & 1) != 0)
                crc = (crc >> 1) ^ polynomial;
            else
                crc >>= 1;
        }
        iupd_crc32_table[i] = crc;
    }
    iupd_crc32_table_initialized = true;
}

static uint32_t iupd_crc32_compute(const uint8_t* data, size_t len) {
    iupd_crc32_init_table();
    uint32_t crc = 0xFFFFFFFF;
    for (size_t i = 0; i < len; i++) {
        crc = (crc >> 8) ^ iupd_crc32_table[(crc ^ data[i]) & 0xFF];
    }
    return crc ^ 0xFFFFFFFF;
}

/* ============================================================================
 * Bounds Checking Utilities
 * ============================================================================ */

/* Check if offset + size is within buffer bounds */
static bool iupd_check_bounds(size_t buffer_size, uint64_t offset, size_t size) {
    if (offset > buffer_size) return false;
    if (size > buffer_size - offset) return false;
    return true;
}

/* ============================================================================
 * Little-Endian Reading Functions
 * ============================================================================ */

/* Read little-endian u8 */
static bool iupd_read_u8(const uint8_t* data, size_t data_size,
                         uint64_t offset, uint8_t* out) {
    if (!iupd_check_bounds(data_size, offset, 1)) return false;
    *out = data[offset];
    return true;
}

/* Read little-endian u16 */
static bool iupd_read_u16_le(const uint8_t* data, size_t data_size,
                             uint64_t offset, uint16_t* out) {
    if (!iupd_check_bounds(data_size, offset, 2)) return false;
    *out = ((uint16_t)data[offset]) | (((uint16_t)data[offset + 1]) << 8);
    return true;
}

/* Read little-endian u32 */
static bool iupd_read_u32_le(const uint8_t* data, size_t data_size,
                             uint64_t offset, uint32_t* out) {
    if (!iupd_check_bounds(data_size, offset, 4)) return false;
    *out = ((uint32_t)data[offset]) |
           (((uint32_t)data[offset + 1]) << 8) |
           (((uint32_t)data[offset + 2]) << 16) |
           (((uint32_t)data[offset + 3]) << 24);
    return true;
}

/* Read little-endian u64 */
static bool iupd_read_u64_le(const uint8_t* data, size_t data_size,
                             uint64_t offset, uint64_t* out) {
    if (!iupd_check_bounds(data_size, offset, 8)) return false;
    uint32_t lo, hi;
    if (!iupd_read_u32_le(data, data_size, offset, &lo)) return false;
    if (!iupd_read_u32_le(data, data_size, offset + 4, &hi)) return false;
    *out = ((uint64_t)lo) | (((uint64_t)hi) << 32);
    return true;
}

/* Read 32 bytes (BLAKE3-256) */
static bool iupd_read_blake3(const uint8_t* data, size_t data_size,
                             uint64_t offset, uint8_t* out) {
    if (!iupd_check_bounds(data_size, offset, 32)) return false;
    memcpy(out, &data[offset], 32);
    return true;
}

/* ============================================================================
 * Error Reporting
 * ============================================================================ */

static void iupd_set_error(iupd_ctx_t* ctx, iupd_error_code_t code,
                           uint64_t byte_offset, uint32_t chunk_index,
                           const char* message) {
    ctx->last_error.code = code;
    ctx->last_error.byte_offset = byte_offset;
    ctx->last_error.chunk_index = chunk_index;
    ctx->last_error.message = message;
}

/* ============================================================================
 * Core Implementation
 * ============================================================================ */

icfg_status_t iupd_open(const uint8_t* data, size_t size, iupd_ctx_t* out) {
    if (!data || !out) {
        if (out) {
            iupd_set_error(out, IUPD_ERR_UNKNOWN_ERROR, 0, 0xFFFFFFFF,
                          "Invalid arguments to iupd_open");
        }
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    memset(out, 0, sizeof(*out));
    out->data = data;
    out->size = size;

    /* Check minimum file size */
    if (size < IUPD_FILE_HEADER_SIZE) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, 0, 0xFFFFFFFF,
                      "File too small for header");
        return ICFG_ERR_BOUNDS;
    }

    /* Parse File Header (32 bytes) */
    if (!iupd_read_u32_le(data, size, 0, &out->magic)) {
        iupd_set_error(out, IUPD_ERR_INVALID_MAGIC, 0, 0xFFFFFFFF,
                      "Cannot read magic");
        return ICFG_ERR_MAGIC;
    }

    /* Check magic (IUPD or UPD1) */
    if (out->magic != IUPD_MAGIC_PRIMARY && out->magic != IUPD_MAGIC_ALTERNATE) {
        iupd_set_error(out, IUPD_ERR_INVALID_MAGIC, 0, 0xFFFFFFFF,
                      "Invalid magic number");
        return ICFG_ERR_MAGIC;
    }

    /* Read version */
    if (!iupd_read_u8(data, size, 4, &out->version)) {
        iupd_set_error(out, IUPD_ERR_UNSUPPORTED_VERSION, 4, 0xFFFFFFFF,
                      "Cannot read version");
        return ICFG_ERR_UNSUPPORTED;
    }

    if (out->version != IUPD_VERSION) {
        iupd_set_error(out, IUPD_ERR_UNSUPPORTED_VERSION, 4, 0xFFFFFFFF,
                      "Unsupported version");
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Read flags (must be 0) */
    if (!iupd_read_u32_le(data, size, 5, &out->flags)) {
        iupd_set_error(out, IUPD_ERR_INVALID_FLAGS, 5, 0xFFFFFFFF,
                      "Cannot read flags");
        return ICFG_ERR_BOUNDS;
    }

    if (out->flags != 0) {
        iupd_set_error(out, IUPD_ERR_INVALID_FLAGS, 5, 0xFFFFFFFF,
                      "Non-zero flags not allowed in v1");
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Read header size (must be 32) */
    uint16_t header_size;
    if (!iupd_read_u16_le(data, size, 9, &header_size)) {
        iupd_set_error(out, IUPD_ERR_INVALID_HEADER_SIZE, 9, 0xFFFFFFFF,
                      "Cannot read header size");
        return ICFG_ERR_BOUNDS;
    }

    if (header_size != IUPD_FILE_HEADER_SIZE) {
        iupd_set_error(out, IUPD_ERR_INVALID_HEADER_SIZE, 9, 0xFFFFFFFF,
                      "Header size must be 36");
        return ICFG_ERR_BOUNDS;
    }

    /* Read reserved byte (must be 0) */
    uint8_t reserved;
    if (!iupd_read_u8(data, size, 11, &reserved)) {
        iupd_set_error(out, IUPD_ERR_INVALID_HEADER_SIZE, 11, 0xFFFFFFFF,
                      "Cannot read reserved byte");
        return ICFG_ERR_BOUNDS;
    }

    if (reserved != 0) {
        iupd_set_error(out, IUPD_ERR_INVALID_HEADER_SIZE, 11, 0xFFFFFFFF,
                      "Reserved byte must be 0");
        return ICFG_ERR_BOUNDS;
    }

    /* Read offsets */
    if (!iupd_read_u64_le(data, size, 12, &out->chunk_table_offset)) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, 12, 0xFFFFFFFF,
                      "Cannot read chunk table offset");
        return ICFG_ERR_BOUNDS;
    }

    if (!iupd_read_u64_le(data, size, 20, &out->manifest_offset)) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, 20, 0xFFFFFFFF,
                      "Cannot read manifest offset");
        return ICFG_ERR_BOUNDS;
    }

    if (!iupd_read_u64_le(data, size, 28, &out->payload_offset)) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, 28, 0xFFFFFFFF,
                      "Cannot read payload offset");
        return ICFG_ERR_BOUNDS;
    }

    /* Validate offset ordering */
    if (out->chunk_table_offset < IUPD_FILE_HEADER_SIZE) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, 12, 0xFFFFFFFF,
                      "Chunk table offset before header");
        return ICFG_ERR_BOUNDS;
    }

    if (out->manifest_offset <= out->chunk_table_offset) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, 20, 0xFFFFFFFF,
                      "Manifest offset must be after chunk table");
        return ICFG_ERR_BOUNDS;
    }

    if (out->payload_offset <= out->manifest_offset) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, 28, 0xFFFFFFFF,
                      "Payload offset must be after manifest");
        return ICFG_ERR_BOUNDS;
    }

    if (out->payload_offset > size) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, 28, 0xFFFFFFFF,
                      "Payload offset exceeds file size");
        return ICFG_ERR_BOUNDS;
    }

    /* Calculate chunk table size and count */
    uint64_t chunk_table_size = out->manifest_offset - out->chunk_table_offset;
    if (chunk_table_size % IUPD_CHUNK_ENTRY_SIZE != 0) {
        iupd_set_error(out, IUPD_ERR_INVALID_CHUNK_TABLE_SIZE,
                      out->chunk_table_offset, 0xFFFFFFFF,
                      "Chunk table size not divisible by 56");
        return ICFG_ERR_BOUNDS;
    }

    out->chunk_count = (uint32_t)(chunk_table_size / IUPD_CHUNK_ENTRY_SIZE);

    if (out->chunk_count > IUPD_MAX_CHUNKS) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS,
                      out->chunk_table_offset, 0xFFFFFFFF,
                      "Chunk count exceeds maximum");
        return ICFG_ERR_BOUNDS;
    }

    /* Parse Manifest Header (20 bytes at manifest_offset) */
    if (!iupd_check_bounds(size, out->manifest_offset, 20)) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS,
                      out->manifest_offset, 0xFFFFFFFF,
                      "Manifest header out of bounds");
        return ICFG_ERR_BOUNDS;
    }

    uint8_t manifest_version;
    if (!iupd_read_u8(data, size, out->manifest_offset, &manifest_version)) {
        iupd_set_error(out, IUPD_ERR_INVALID_MANIFEST_VERSION,
                      out->manifest_offset, 0xFFFFFFFF,
                      "Cannot read manifest version");
        return ICFG_ERR_BOUNDS;
    }

    if (manifest_version != IUPD_VERSION) {
        iupd_set_error(out, IUPD_ERR_INVALID_MANIFEST_VERSION,
                      out->manifest_offset, 0xFFFFFFFF,
                      "Unsupported manifest version");
        return ICFG_ERR_BOUNDS;
    }

    /* Manifest is accepted in compact (v1 legacy) and aligned layouts. */
    bool parsed_manifest = false;

    {
        uint32_t dep = 0, apply = 0;
        uint64_t msize = 0;
        if (iupd_read_u32_le(data, size, out->manifest_offset + 5, &dep) &&
            iupd_read_u32_le(data, size, out->manifest_offset + 9, &apply) &&
            iupd_read_u64_le(data, size, out->manifest_offset + 13, &msize)) {
            uint64_t expected = 20 + ((uint64_t)dep * 8) + ((uint64_t)apply * 4) + 8;
            if (msize == expected) {
                out->dependency_count = dep;
                out->apply_order_count = apply;
                out->manifest_size = msize;
                parsed_manifest = true;
            }
        }
    }

    if (!parsed_manifest) {
        uint32_t dep = 0, apply = 0;
        uint64_t msize = 0;
        if (iupd_read_u32_le(data, size, out->manifest_offset + 8, &dep) &&
            iupd_read_u32_le(data, size, out->manifest_offset + 12, &apply) &&
            iupd_read_u64_le(data, size, out->manifest_offset + 16, &msize)) {
            uint64_t expected = 24 + ((uint64_t)dep * 8) + ((uint64_t)apply * 4) + 8;
            if (msize == expected) {
                out->dependency_count = dep;
                out->apply_order_count = apply;
                out->manifest_size = msize;
                parsed_manifest = true;
            }
        }
    }

    if (!parsed_manifest) {
        iupd_set_error(out, IUPD_ERR_MANIFEST_SIZE_MISMATCH,
                      out->manifest_offset, 0xFFFFFFFF,
                      "Manifest size field inconsistent");
        return ICFG_ERR_BOUNDS;
    }

    /* Verify manifest fits within bounds */
    if (!iupd_check_bounds(size, out->manifest_offset, (size_t)out->manifest_size)) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS,
                      out->manifest_offset, 0xFFFFFFFF,
                      "Manifest extends beyond file");
        return ICFG_ERR_BOUNDS;
    }

    if (out->manifest_size > IUPD_MAX_MANIFEST_SIZE) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS,
                      out->manifest_offset, 0xFFFFFFFF,
                      "Manifest size exceeds maximum");
        return ICFG_ERR_BOUNDS;
    }

    /* Read manifest CRC32 (at end of manifest structure) */
    uint64_t crc32_offset = out->manifest_offset + out->manifest_size - 8;
    if (!iupd_read_u32_le(data, size, crc32_offset, &out->manifest_crc32)) {
        iupd_set_error(out, IUPD_ERR_OFFSET_OUT_OF_BOUNDS,
                      crc32_offset, 0xFFFFFFFF,
                      "Cannot read manifest CRC32");
        return ICFG_ERR_BOUNDS;
    }

    return ICFG_OK;
}

icfg_status_t iupd_validate_fast(iupd_ctx_t* ctx) {
    if (!ctx) return ICFG_ERR_INVALID_ARGUMENT;

    uint64_t manifest_footer_and_lists = ((uint64_t)ctx->dependency_count * 8) +
                                         ((uint64_t)ctx->apply_order_count * 4) + 8;
    if (ctx->manifest_size < manifest_footer_and_lists) {
        iupd_set_error(ctx, IUPD_ERR_MANIFEST_SIZE_MISMATCH, ctx->manifest_offset, 0xFFFFFFFF,
                      "Manifest size too small");
        return ICFG_ERR_BOUNDS;
    }
    uint64_t manifest_header_size = ctx->manifest_size - manifest_footer_and_lists;
    if (manifest_header_size < 20 || manifest_header_size > 24) {
        iupd_set_error(ctx, IUPD_ERR_MANIFEST_SIZE_MISMATCH, ctx->manifest_offset, 0xFFFFFFFF,
                      "Invalid manifest header size");
        return ICFG_ERR_BOUNDS;
    }

    /* Check all chunk table entries for contiguous indices and bounds */
    for (uint32_t i = 0; i < ctx->chunk_count; i++) {
        uint64_t entry_offset = ctx->chunk_table_offset + (i * IUPD_CHUNK_ENTRY_SIZE);

        /* Read chunk index */
        uint32_t chunk_index;
        if (!iupd_read_u32_le(ctx->data, ctx->size, entry_offset, &chunk_index)) {
            iupd_set_error(ctx, IUPD_ERR_CHUNK_INDEX_ERROR, entry_offset, i,
                          "Cannot read chunk index");
            return ICFG_ERR_BOUNDS;
        }

        /* Indices must be 0, 1, 2, ... in order */
        if (chunk_index != i) {
            iupd_set_error(ctx, IUPD_ERR_CHUNK_INDEX_ERROR, entry_offset, i,
                          "Chunk indices not in order starting at 0");
            return ICFG_ERR_BOUNDS;
        }

        /* Read payload size and offset */
        uint64_t payload_size, payload_offset;
        if (!iupd_read_u64_le(ctx->data, ctx->size, entry_offset + 4, &payload_size)) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 4, i,
                          "Cannot read payload size");
            return ICFG_ERR_BOUNDS;
        }

        if (!iupd_read_u64_le(ctx->data, ctx->size, entry_offset + 12, &payload_offset)) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 12, i,
                          "Cannot read payload offset");
            return ICFG_ERR_BOUNDS;
        }

        /* Payload size must be > 0 */
        if (payload_size == 0) {
            iupd_set_error(ctx, IUPD_ERR_EMPTY_CHUNK, entry_offset + 4, i,
                          "Chunk payload size must be > 0");
            return ICFG_ERR_BOUNDS;
        }

        /* Check max chunk size */
        if (payload_size > IUPD_MAX_CHUNK_SIZE) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 4, i,
                          "Chunk size exceeds maximum");
            return ICFG_ERR_BOUNDS;
        }

        /* Payload must be within file and start at payload section */
        if (payload_offset < ctx->payload_offset) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 12, i,
                          "Payload offset before payload section");
            return ICFG_ERR_BOUNDS;
        }

        if (payload_offset + payload_size > ctx->size) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 12, i,
                          "Payload extends beyond file");
            return ICFG_ERR_BOUNDS;
        }

        /* Check for overlaps with next chunk */
        if (i + 1 < ctx->chunk_count) {
            uint64_t next_entry_offset = entry_offset + IUPD_CHUNK_ENTRY_SIZE;
            uint64_t next_payload_offset;
            if (!iupd_read_u64_le(ctx->data, ctx->size, next_entry_offset + 12,
                                 &next_payload_offset)) {
                iupd_set_error(ctx, IUPD_ERR_OVERLAPPING_PAYLOADS,
                              next_entry_offset + 12, i + 1,
                              "Cannot read next payload offset");
                return ICFG_ERR_BOUNDS;
            }

            if (payload_offset + payload_size > next_payload_offset) {
                iupd_set_error(ctx, IUPD_ERR_OVERLAPPING_PAYLOADS,
                              entry_offset + 12, i,
                              "Chunk payloads overlap");
                return ICFG_ERR_BOUNDS;
            }
        }
    }

    /* Validate apply order references valid chunks and count */
    uint64_t apply_order_offset = ctx->manifest_offset + manifest_header_size +
                                  ((uint64_t)ctx->dependency_count * 8);
    uint8_t* apply_order_flags = NULL;

    if (ctx->apply_order_count > 0) {
        apply_order_flags = calloc(ctx->chunk_count, sizeof(uint8_t));
        if (!apply_order_flags) {
            iupd_set_error(ctx, IUPD_ERR_UNKNOWN_ERROR, 0, 0xFFFFFFFF,
                          "Memory allocation failed");
            return ICFG_ERR_INVALID_ARGUMENT;
        }
    }

    for (uint32_t i = 0; i < ctx->apply_order_count; i++) {
        uint64_t entry_offset = apply_order_offset + (i * 4);
        uint32_t chunk_index;

        if (!iupd_read_u32_le(ctx->data, ctx->size, entry_offset, &chunk_index)) {
            if (apply_order_flags) free(apply_order_flags);
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset, 0xFFFFFFFF,
                          "Cannot read apply order entry");
            return ICFG_ERR_BOUNDS;
        }

        if (chunk_index >= ctx->chunk_count) {
            if (apply_order_flags) free(apply_order_flags);
            iupd_set_error(ctx, IUPD_ERR_MISSING_CHUNK_IN_APPLY_ORDER,
                          entry_offset, chunk_index,
                          "Apply order references invalid chunk");
            return ICFG_ERR_BOUNDS;
        }

        /* Check for duplicates */
        if (apply_order_flags[chunk_index]) {
            if (apply_order_flags) free(apply_order_flags);
            iupd_set_error(ctx, IUPD_ERR_DUPLICATE_CHUNK_IN_APPLY_ORDER,
                          entry_offset, chunk_index,
                          "Chunk referenced multiple times in apply order");
            return ICFG_ERR_BOUNDS;
        }

        apply_order_flags[chunk_index] = 1;
    }

    /* Check all chunks are referenced in apply order */
    if (ctx->apply_order_count != ctx->chunk_count) {
        if (apply_order_flags) free(apply_order_flags);
        iupd_set_error(ctx, IUPD_ERR_MISSING_CHUNK_IN_APPLY_ORDER,
                      ctx->manifest_offset, 0xFFFFFFFF,
                      "Not all chunks referenced in apply order");
        return ICFG_ERR_BOUNDS;
    }

    for (uint32_t i = 0; i < ctx->chunk_count; i++) {
        if (!apply_order_flags[i]) {
            if (apply_order_flags) free(apply_order_flags);
            iupd_set_error(ctx, IUPD_ERR_MISSING_CHUNK_IN_APPLY_ORDER,
                          ctx->manifest_offset, i,
                          "Chunk not referenced in apply order");
            return ICFG_ERR_BOUNDS;
        }
    }

    if (apply_order_flags) free(apply_order_flags);

    return ICFG_OK;
}

icfg_status_t iupd_validate_strict(iupd_ctx_t* ctx) {
    if (!ctx) return ICFG_ERR_INVALID_ARGUMENT;

    /* First, run fast validation */
    icfg_status_t status = iupd_validate_fast(ctx);
    if (status != ICFG_OK) return status;

    /* Verify CRC32 for each chunk payload */
    for (uint32_t i = 0; i < ctx->chunk_count; i++) {
        uint64_t entry_offset = ctx->chunk_table_offset + (i * IUPD_CHUNK_ENTRY_SIZE);

        uint64_t payload_size, payload_offset;
        uint32_t expected_crc32;

        if (!iupd_read_u64_le(ctx->data, ctx->size, entry_offset + 4, &payload_size)) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 4, i,
                          "Cannot read payload size during CRC check");
            return ICFG_ERR_BOUNDS;
        }

        if (!iupd_read_u64_le(ctx->data, ctx->size, entry_offset + 12, &payload_offset)) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 12, i,
                          "Cannot read payload offset during CRC check");
            return ICFG_ERR_BOUNDS;
        }

        if (!iupd_read_u32_le(ctx->data, ctx->size, entry_offset + 20, &expected_crc32)) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 20, i,
                          "Cannot read payload CRC32");
            return ICFG_ERR_BOUNDS;
        }

        /* Compute CRC32 */
        uint32_t computed_crc32 = iupd_crc32_compute(
            &ctx->data[payload_offset], (size_t)payload_size);

        if (computed_crc32 != expected_crc32) {
            iupd_set_error(ctx, IUPD_ERR_CRC32_MISMATCH, payload_offset, i,
                          "Chunk CRC32 mismatch");
            return ICFG_ERR_CRC;
        }
    }

    /* Verify manifest CRC32 */
    uint64_t manifest_data_size = ctx->manifest_size - 8;  /* Exclude CRC32 field itself */
    uint32_t computed_manifest_crc32 = iupd_crc32_compute(
        &ctx->data[ctx->manifest_offset], (size_t)manifest_data_size);

    if (computed_manifest_crc32 != ctx->manifest_crc32) {
        iupd_set_error(ctx, IUPD_ERR_CRC32_MISMATCH, ctx->manifest_offset, 0xFFFFFFFF,
                      "Manifest CRC32 mismatch");
        return ICFG_ERR_CRC;
    }

    /* TODO: Verify BLAKE3-256 hashes (requires external library or implementation) */
    /* For now, just check that hashes are present (non-zero) if payload exists */
    for (uint32_t i = 0; i < ctx->chunk_count; i++) {
        uint64_t entry_offset = ctx->chunk_table_offset + (i * IUPD_CHUNK_ENTRY_SIZE);
        uint64_t payload_size;

        if (!iupd_read_u64_le(ctx->data, ctx->size, entry_offset + 4, &payload_size)) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 4, i,
                          "Cannot read payload size during BLAKE3 check");
            return ICFG_ERR_BOUNDS;
        }

        if (payload_size > 0) {
            uint8_t blake3_hash[32];
            if (!iupd_read_blake3(ctx->data, ctx->size, entry_offset + 24, blake3_hash)) {
                iupd_set_error(ctx, IUPD_ERR_BLAKE3_MISMATCH, entry_offset + 24, i,
                              "Cannot read BLAKE3 hash");
                return ICFG_ERR_BOUNDS;
            }

            /* Check hash is not all zeros (placeholder validation) */
            bool has_nonzero = false;
            for (int j = 0; j < 32; j++) {
                if (blake3_hash[j] != 0) {
                    has_nonzero = true;
                    break;
                }
            }

            if (!has_nonzero) {
                iupd_set_error(ctx, IUPD_ERR_BLAKE3_MISMATCH, entry_offset + 24, i,
                              "BLAKE3 hash is all zeros");
                return ICFG_ERR_BOUNDS;
            }
        }
    }

    /* Verify dependency graph is acyclic (simple cycle detection) */
    /* For each dependency, verify it references valid chunks */
    uint64_t manifest_footer_and_lists = ((uint64_t)ctx->dependency_count * 8) +
                                         ((uint64_t)ctx->apply_order_count * 4) + 8;
    if (ctx->manifest_size < manifest_footer_and_lists) {
        iupd_set_error(ctx, IUPD_ERR_MANIFEST_SIZE_MISMATCH, ctx->manifest_offset, 0xFFFFFFFF,
                      "Manifest size too small");
        return ICFG_ERR_BOUNDS;
    }
    uint64_t manifest_header_size = ctx->manifest_size - manifest_footer_and_lists;
    uint64_t dependency_offset = ctx->manifest_offset + manifest_header_size;

    for (uint32_t i = 0; i < ctx->dependency_count; i++) {
        uint64_t entry_offset = dependency_offset + (i * 8);
        uint32_t dependent_patch_id, required_patch_id;

        if (!iupd_read_u32_le(ctx->data, ctx->size, entry_offset, &dependent_patch_id)) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset, 0xFFFFFFFF,
                          "Cannot read dependent patch ID");
            return ICFG_ERR_BOUNDS;
        }

        if (!iupd_read_u32_le(ctx->data, ctx->size, entry_offset + 4, &required_patch_id)) {
            iupd_set_error(ctx, IUPD_ERR_OFFSET_OUT_OF_BOUNDS, entry_offset + 4, 0xFFFFFFFF,
                          "Cannot read required patch ID");
            return ICFG_ERR_BOUNDS;
        }

        if (dependent_patch_id >= ctx->chunk_count) {
            iupd_set_error(ctx, IUPD_ERR_INVALID_DEPENDENCY, entry_offset,
                          dependent_patch_id,
                          "Dependent patch ID invalid");
            return ICFG_ERR_BOUNDS;
        }

        if (required_patch_id >= ctx->chunk_count) {
            iupd_set_error(ctx, IUPD_ERR_INVALID_DEPENDENCY, entry_offset + 4,
                          required_patch_id,
                          "Required patch ID invalid");
            return ICFG_ERR_BOUNDS;
        }
    }

    /* TODO: Perform full topological sort for cycle detection */
    /* For now, basic validation complete */

    return ICFG_OK;
}

const iupd_error_t* iupd_get_error(const iupd_ctx_t* ctx) {
    if (!ctx) return NULL;
    return &ctx->last_error;
}

/* ============================================================================
 * Streaming Apply Functions
 * ============================================================================ */

icfg_status_t iupd_apply_begin(const iupd_ctx_t* ctx, iupd_apply_ctx_t* apply_ctx) {
    if (!ctx || !apply_ctx) return ICFG_ERR_INVALID_ARGUMENT;

    memset(apply_ctx, 0, sizeof(*apply_ctx));
    apply_ctx->file_ctx = ctx;
    apply_ctx->apply_order_index = 0;

    return ICFG_OK;
}

icfg_status_t iupd_apply_next(iupd_apply_ctx_t* apply_ctx, iupd_chunk_t* out_chunk) {
    if (!apply_ctx || !out_chunk) return ICFG_ERR_INVALID_ARGUMENT;

    const iupd_ctx_t* ctx = apply_ctx->file_ctx;
    if (!ctx) return ICFG_ERR_INVALID_ARGUMENT;

    /* Check if we've reached the end */
    if (apply_ctx->apply_order_index >= ctx->apply_order_count) {
        return ICFG_ERR_RANGE;  /* No more chunks */
    }

    /* Read chunk index from apply order */
    uint64_t manifest_footer_and_lists = ((uint64_t)ctx->dependency_count * 8) +
                                         ((uint64_t)ctx->apply_order_count * 4) + 8;
    if (ctx->manifest_size < manifest_footer_and_lists) {
        return ICFG_ERR_BOUNDS;
    }
    uint64_t manifest_header_size = ctx->manifest_size - manifest_footer_and_lists;
    uint64_t apply_order_offset = ctx->manifest_offset + manifest_header_size +
                                  ((uint64_t)ctx->dependency_count * 8);
    uint64_t entry_offset = apply_order_offset + (apply_ctx->apply_order_index * 4);

    uint32_t chunk_index;
    if (!iupd_read_u32_le(ctx->data, ctx->size, entry_offset, &chunk_index)) {
        return ICFG_ERR_BOUNDS;
    }

    if (chunk_index >= ctx->chunk_count) {
        return ICFG_ERR_BOUNDS;
    }

    /* Get chunk entry */
    iupd_chunk_entry_t entry;
    icfg_status_t status = iupd_get_chunk_entry(ctx, chunk_index, &entry);
    if (status != ICFG_OK) return status;

    /* Populate output chunk */
    out_chunk->chunk_index = chunk_index;
    out_chunk->payload_ptr = &ctx->data[entry.payload_offset];
    out_chunk->payload_size = entry.payload_size;
    out_chunk->payload_crc32 = entry.payload_crc32;
    out_chunk->payload_blake3 = &ctx->data[ctx->chunk_table_offset +
                                           (chunk_index * IUPD_CHUNK_ENTRY_SIZE) + 24];

    apply_ctx->apply_order_index++;

    return ICFG_OK;
}

void iupd_apply_end(iupd_apply_ctx_t* apply_ctx) {
    if (apply_ctx) {
        memset(apply_ctx, 0, sizeof(*apply_ctx));
    }
}

/* ============================================================================
 * Helper Functions
 * ============================================================================ */

icfg_status_t iupd_get_chunk_entry(const iupd_ctx_t* ctx, uint32_t chunk_index,
                                   iupd_chunk_entry_t* out_entry) {
    if (!ctx || !out_entry) return ICFG_ERR_INVALID_ARGUMENT;

    if (chunk_index >= ctx->chunk_count) return ICFG_ERR_BOUNDS;

    uint64_t entry_offset = ctx->chunk_table_offset + (chunk_index * IUPD_CHUNK_ENTRY_SIZE);

    if (!iupd_read_u32_le(ctx->data, ctx->size, entry_offset, &out_entry->chunk_index)) {
        return ICFG_ERR_BOUNDS;
    }

    if (!iupd_read_u64_le(ctx->data, ctx->size, entry_offset + 4, &out_entry->payload_size)) {
        return ICFG_ERR_BOUNDS;
    }

    if (!iupd_read_u64_le(ctx->data, ctx->size, entry_offset + 12, &out_entry->payload_offset)) {
        return ICFG_ERR_BOUNDS;
    }

    if (!iupd_read_u32_le(ctx->data, ctx->size, entry_offset + 20, &out_entry->payload_crc32)) {
        return ICFG_ERR_BOUNDS;
    }

    if (!iupd_read_blake3(ctx->data, ctx->size, entry_offset + 24, out_entry->payload_blake3)) {
        return ICFG_ERR_BOUNDS;
    }

    return ICFG_OK;
}

icfg_status_t iupd_verify_chunk_crc32(const iupd_ctx_t* ctx, uint32_t chunk_index,
                                      uint32_t expected_crc32) {
    if (!ctx) return ICFG_ERR_INVALID_ARGUMENT;

    if (chunk_index >= ctx->chunk_count) return ICFG_ERR_BOUNDS;

    iupd_chunk_entry_t entry;
    icfg_status_t status = iupd_get_chunk_entry(ctx, chunk_index, &entry);
    if (status != ICFG_OK) return status;

    uint32_t computed_crc32 = iupd_crc32_compute(
        &ctx->data[entry.payload_offset], (size_t)entry.payload_size);

    if (computed_crc32 != expected_crc32) {
        return ICFG_ERR_CRC;
    }

    return ICFG_OK;
}
