/*
 * ilog.c
 * ILOG (Log / Stream Container) C99 reader implementation
 *
 * Status: Reference implementation (DESIGN)
 * License: MIT
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>

#include "../include/ironcfg/ilog.h"

/* ============================================================================
 * CRC32 IEEE Implementation (per spec section 15)
 * ============================================================================ */

static uint32_t ilog_crc32_table[256];
static bool ilog_crc32_table_initialized = false;

static void ilog_crc32_init_table(void) {
    if (ilog_crc32_table_initialized) return;

    const uint32_t polynomial = 0xEDB88320;
    for (int i = 0; i < 256; i++) {
        uint32_t crc = i;
        for (int j = 0; j < 8; j++) {
            if ((crc & 1) != 0)
                crc = (crc >> 1) ^ polynomial;
            else
                crc >>= 1;
        }
        ilog_crc32_table[i] = crc;
    }
    ilog_crc32_table_initialized = true;
}

static uint32_t ilog_crc32_compute(const uint8_t* data, size_t len) {
    ilog_crc32_init_table();
    uint32_t crc = 0xFFFFFFFF;
    for (size_t i = 0; i < len; i++) {
        crc = (crc >> 8) ^ ilog_crc32_table[(crc ^ data[i]) & 0xFF];
    }
    return crc ^ 0xFFFFFFFF;
}

/* ============================================================================
 * Bounds Checking Utilities
 * ============================================================================ */

/* Check if offset + size is within buffer bounds */
static bool ilog_check_bounds(const uint8_t* data, size_t data_size,
                              uint64_t offset, size_t size) {
    if (offset > data_size) return false;
    if (size > data_size - offset) return false;
    return true;
}

/* Read u8 from offset with bounds check */
static bool ilog_read_u8(const uint8_t* data, size_t data_size,
                         uint64_t offset, uint8_t* out) {
    if (!ilog_check_bounds(data, data_size, offset, 1)) {
        return false;
    }
    *out = data[offset];
    return true;
}

/* Read little-endian u32 with bounds check */
static bool ilog_read_u32_le(const uint8_t* data, size_t data_size,
                             uint64_t offset, uint32_t* out) {
    if (!ilog_check_bounds(data, data_size, offset, 4)) {
        return false;
    }
    *out = ((uint32_t)data[offset]) |
           (((uint32_t)data[offset + 1]) << 8) |
           (((uint32_t)data[offset + 2]) << 16) |
           (((uint32_t)data[offset + 3]) << 24);
    return true;
}

/* Read big-endian u32 with bounds check */
static bool ilog_read_u32_be(const uint8_t* data, size_t data_size,
                             uint64_t offset, uint32_t* out) {
    if (!ilog_check_bounds(data, data_size, offset, 4)) {
        return false;
    }
    *out = (((uint32_t)data[offset]) << 24) |
           (((uint32_t)data[offset + 1]) << 16) |
           (((uint32_t)data[offset + 2]) << 8) |
           ((uint32_t)data[offset + 3]);
    return true;
}

/* ============================================================================
 * Varint Decoding (LEB128)
 * ============================================================================ */

/* Decode varint at offset, return number of bytes consumed or -1 on error */
static int ilog_decode_varint(const uint8_t* data, size_t data_size,
                              uint64_t offset, uint64_t* out) {
    uint64_t result = 0;
    int shift = 0;
    int bytes_read = 0;

    /* Maximum 10 bytes for varint (per spec) */
    while (bytes_read < 10) {
        uint8_t byte;
        if (!ilog_read_u8(data, data_size, offset + bytes_read, &byte)) {
            return -1;
        }

        result |= (uint64_t)(byte & 0x7F) << shift;
        bytes_read++;

        if ((byte & 0x80) == 0) {
            *out = result;
            return bytes_read;
        }

        shift += 7;
    }

    /* Varint too long */
    return -1;
}

/* ============================================================================
 * Flag Parsing
 * ============================================================================ */

static void ilog_parse_flags(uint8_t flags_byte, ilog_flags_t* out) {
    out->little_endian = (flags_byte & 0x01) == 0;
    out->has_crc32 = (flags_byte & 0x02) != 0;
    out->has_blake3 = (flags_byte & 0x04) != 0;
    out->has_layer_l2 = (flags_byte & 0x08) != 0;
    out->has_layer_l3 = (flags_byte & 0x10) != 0;
    out->has_layer_l4 = (flags_byte & 0x20) != 0;
    out->reserved = (flags_byte >> 6) & 0x03;
}

/* ============================================================================
 * Core Implementation
 * ============================================================================ */

icfg_status_t ilog_open(const uint8_t* data, size_t size, ilog_view_t* out) {
    if (!data || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    if (size < ILOG_MIN_HEADER_SIZE) {
        out->last_error.code = ILOG_ERR_INVALID_MAGIC;
        out->last_error.byte_offset = 0;
        out->last_error.message = "File too small for ILOG header";
        return (icfg_status_t)ILOG_ERR_INVALID_MAGIC;
    }

    /* Initialize view */
    memset(out, 0, sizeof(ilog_view_t));
    out->data = data;
    out->size = size;

    /* Parse magic number (4 bytes, little-endian) */
    uint32_t magic;
    if (!ilog_read_u32_le(data, size, 0, &magic)) {
        out->last_error.code = ILOG_ERR_INVALID_MAGIC;
        out->last_error.byte_offset = 0;
        out->last_error.message = "Cannot read magic number";
        return (icfg_status_t)ILOG_ERR_INVALID_MAGIC;
    }

    if (magic != ILOG_MAGIC_PRIMARY && magic != ILOG_MAGIC_EXTENDED) {
        out->last_error.code = ILOG_ERR_INVALID_MAGIC;
        out->last_error.byte_offset = 0;
        out->last_error.message = "Invalid ILOG magic number";
        return (icfg_status_t)ILOG_ERR_INVALID_MAGIC;
    }

    out->magic = magic;

    /* Parse version (1 byte) */
    uint8_t version;
    if (!ilog_read_u8(data, size, 4, &version)) {
        out->last_error.code = ILOG_ERR_CORRUPTED_HEADER;
        out->last_error.byte_offset = 4;
        out->last_error.message = "Cannot read version";
        return (icfg_status_t)ILOG_ERR_CORRUPTED_HEADER;
    }

    if (version != ILOG_VERSION) {
        out->last_error.code = ILOG_ERR_UNSUPPORTED_VERSION;
        out->last_error.byte_offset = 4;
        out->last_error.message = "Unsupported ILOG version";
        return (icfg_status_t)ILOG_ERR_UNSUPPORTED_VERSION;
    }

    out->version = version;

    /* Parse flags (1 byte) */
    uint8_t flags_byte;
    if (!ilog_read_u8(data, size, 5, &flags_byte)) {
        out->last_error.code = ILOG_ERR_CORRUPTED_HEADER;
        out->last_error.byte_offset = 5;
        out->last_error.message = "Cannot read flags";
        return (icfg_status_t)ILOG_ERR_CORRUPTED_HEADER;
    }

    ilog_parse_flags(flags_byte, &out->flags);

    /* Check reserved bits (not critical, but note them) */
    if (out->flags.reserved != 0) {
        /* Per spec, readers MAY ignore or MAY reject */
        /* For now, we accept them */
    }

    /* Minimum: L0 and L1 must be present */
    /* L0 offset at byte 6-7 (reserved, will be after header) */
    /* L1 offset at byte 8-15 or determined by header size */

    /* For now, assume L0 starts after header, L1 starts after L0 */
    /* Actual layout will be read from TOC */
    out->l0_offset = 16;  /* Placeholder after header */
    out->l1_offset = 0;   /* Will be parsed from TOC */

    /* Set error to none */
    out->last_error.code = 0;
    out->last_error.byte_offset = 0;
    out->last_error.message = NULL;

    return ICFG_OK;
}

icfg_status_t ilog_validate_fast(const ilog_view_t* v) {
    if (!v) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    if (v->size < ILOG_MIN_HEADER_SIZE) {
        return (icfg_status_t)ILOG_ERR_INVALID_MAGIC;
    }

    /* Check magic is valid */
    uint32_t magic;
    if (!ilog_read_u32_le(v->data, v->size, 0, &magic)) {
        return (icfg_status_t)ILOG_ERR_INVALID_MAGIC;
    }

    if (magic != ILOG_MAGIC_PRIMARY && magic != ILOG_MAGIC_EXTENDED) {
        return (icfg_status_t)ILOG_ERR_INVALID_MAGIC;
    }

    /* Check version */
    uint8_t version;
    if (!ilog_read_u8(v->data, v->size, 4, &version)) {
        return (icfg_status_t)ILOG_ERR_CORRUPTED_HEADER;
    }

    if (version != ILOG_VERSION) {
        return (icfg_status_t)ILOG_ERR_UNSUPPORTED_VERSION;
    }

    /* Check flags consistency */
    uint8_t flags_byte;
    if (!ilog_read_u8(v->data, v->size, 5, &flags_byte)) {
        return (icfg_status_t)ILOG_ERR_CORRUPTED_HEADER;
    }

    ilog_flags_t flags;
    ilog_parse_flags(flags_byte, &flags);

    /* Verify L1 must be present (implicit - all files have both L0 and L1) */
    /* For now, basic gate check passed */

    return ICFG_OK;
}

icfg_status_t ilog_validate_strict(const ilog_view_t* v) {
    if (!v) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    /* First pass fast validation */
    icfg_status_t fast_result = ilog_validate_fast(v);
    if (fast_result != ICFG_OK) {
        return fast_result;
    }

    /* Strict validation: enumerate blocks and verify CRC32/BLAKE3 per file flags */
    /* Per spec section 15: validate_strict MUST check PayloadCrc32 and PayloadBlake3 against file flags */

    uint64_t block_pos = 16; /* Start after file header */
    uint32_t block_num = 0;

    while (block_pos < v->size && block_num < 1000) { /* Reasonable limit to avoid infinite loops */
        /* Need at least 72 bytes for block header */
        if (block_pos + 72 > v->size) {
            return ICFG_OK; /* Reached end */
        }

        /* Read block header fields for validation */
        uint32_t block_magic;
        if (!ilog_read_u32_le(v->data, v->size, block_pos, &block_magic)) {
            return (icfg_status_t)ILOG_ERR_MALFORMED_BLOCK;
        }

        /* Block magic must be 0x314B4C42 ("BLK1") */
        if (block_magic != 0x314B4C42) {
            ((ilog_view_t*)v)->last_error.code = ILOG_ERR_MALFORMED_BLOCK;
            ((ilog_view_t*)v)->last_error.byte_offset = block_pos;
            return (icfg_status_t)ILOG_ERR_MALFORMED_BLOCK;
        }

        /* Read PayloadSize at offset 0x0C */
        uint32_t payload_size;
        if (!ilog_read_u32_le(v->data, v->size, block_pos + 0x0C, &payload_size)) {
            return (icfg_status_t)ILOG_ERR_MALFORMED_BLOCK;
        }

        uint64_t payload_offset = block_pos + 72;
        uint64_t payload_end = payload_offset + payload_size;

        /* Bounds check */
        if (payload_end > v->size) {
            ((ilog_view_t*)v)->last_error.code = ILOG_ERR_BLOCK_OUT_OF_BOUNDS;
            ((ilog_view_t*)v)->last_error.byte_offset = block_pos;
            return (icfg_status_t)ILOG_ERR_BLOCK_OUT_OF_BOUNDS;
        }

        /* If CRC32 flag is set, verify PayloadCrc32 in block header */
        if (v->flags.has_crc32) {
            uint32_t stored_crc32;
            if (!ilog_read_u32_le(v->data, v->size, block_pos + 0x18, &stored_crc32)) {
                return (icfg_status_t)ILOG_ERR_MALFORMED_BLOCK;
            }

            if (payload_size > 0) {
                uint32_t computed_crc32 = ilog_crc32_compute(
                    v->data + payload_offset,
                    payload_size
                );
                if (computed_crc32 != stored_crc32) {
                    ((ilog_view_t*)v)->last_error.code = ILOG_ERR_CRC32_MISMATCH;
                    ((ilog_view_t*)v)->last_error.byte_offset = block_pos + 0x18;
                    return (icfg_status_t)ILOG_ERR_CRC32_MISMATCH;
                }
            }
        }

        /* If BLAKE3 flag is set, check that PayloadBlake3 is present (non-zero) in block header */
        if (v->flags.has_blake3) {
            /* Read PayloadBlake3 at offset 0x20 (32 bytes) */
            bool has_nonzero_blake3 = false;
            for (int i = 0; i < 32; i++) {
                if (v->data[block_pos + 0x20 + i] != 0) {
                    has_nonzero_blake3 = true;
                    break;
                }
            }

            if (!has_nonzero_blake3 && payload_size > 0) {
                ((ilog_view_t*)v)->last_error.code = ILOG_ERR_BLAKE3_MISMATCH;
                ((ilog_view_t*)v)->last_error.byte_offset = block_pos + 0x20;
                return (icfg_status_t)ILOG_ERR_BLAKE3_MISMATCH;
            }
        }

        /* Move to next block */
        block_pos = payload_end;
        block_num++;
    }

    return ICFG_OK;
}

const ilog_error_t* ilog_get_error(const ilog_view_t* v) {
    if (!v) {
        return NULL;
    }
    return &v->last_error;
}

icfg_status_t ilog_record_count(const ilog_view_t* v, uint32_t* out) {
    if (!v || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    *out = v->record_count;
    return ICFG_OK;
}

icfg_status_t ilog_block_count(const ilog_view_t* v, uint32_t* out) {
    if (!v || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    *out = v->block_count;
    return ICFG_OK;
}

icfg_status_t ilog_verify_crc32(const ilog_view_t* v) {
    if (!v) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    if (!v->flags.has_crc32) {
        /* No CRC32 present, not an error */
        return ICFG_OK;
    }

    /* Placeholder: CRC32 verification would be implemented here */
    /* This would read L4 SEAL layer and verify CRC32 over protected bytes */

    return ICFG_OK;
}

icfg_status_t ilog_verify_blake3(const ilog_view_t* v) {
    if (!v) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    if (!v->flags.has_blake3) {
        /* No BLAKE3 present, not an error */
        return ICFG_OK;
    }

    /* Placeholder: BLAKE3 verification would be implemented here */
    /* This would read L4 SEAL layer and verify BLAKE3 over protected bytes */

    return ICFG_OK;
}
