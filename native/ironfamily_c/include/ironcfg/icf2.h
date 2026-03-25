/*
 * icf2.h
 * ICF2 (IronConfig Columnar Format) zero-copy C99 reader
 *
 * Status: Production-ready
 * License: MIT
 *
 * Features:
 * - Zero-copy: No allocations required for parsing/access
 * - Bounds-safe: All reads validated against buffer size
 * - CRC validation: Optional integrity checking
 * - Read-only: No encoding support (use .NET for encoding)
 */

#ifndef IRONCFG_ICF2_H
#define IRONCFG_ICF2_H

#include "ironcfg_common.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================================
 * Constants
 * ============================================================================ */

#define ICF2_MAGIC 0x32464349U         /* "ICF2" in little-endian */
#define ICF2_HEADER_SIZE 64
#define ICF2_VERSION 0

/* ============================================================================
 * Types
 * ============================================================================ */

/* Opaque view of ICF2 file (zero-copy) */
typedef struct {
    const uint8_t* data;
    size_t size;

    /* Header fields */
    uint32_t file_size;
    uint32_t prefix_dict_offset;
    uint32_t prefix_dict_size;
    uint32_t schema_offset;
    uint32_t schema_size;
    uint32_t columns_offset;
    uint32_t columns_size;
    uint32_t row_index_offset;
    uint32_t row_index_size;
    uint32_t payload_offset;
    uint32_t payload_size;
    uint32_t crc_offset;
    uint32_t blake3_offset;

    /* Flags */
    bool has_crc32;
    bool has_blake3;
    bool has_prefix_dict;
    bool has_columns;

    /* Parsed schema */
    uint32_t row_count;
    uint32_t field_count;
} icf2_view_t;

/* ============================================================================
 * Core Functions
 * ============================================================================ */

/**
 * Open and parse ICF2 file
 *
 * Args:
 *   data: Pointer to ICF2 file bytes
 *   size: Size of data buffer
 *   out: Output view (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_MAGIC: Invalid magic bytes
 *   ICFG_ERR_BOUNDS: File too small or offsets invalid
 *   ICFG_ERR_SCHEMA: Schema parsing error
 *   ICFG_ERR_UNSUPPORTED: Unsupported version or reserved flags
 */
icfg_status_t icf2_open(const uint8_t* data, size_t size, icf2_view_t* out);

/**
 * Validate ICF2 file (including CRC if enabled)
 *
 * Args:
 *   v: View from icf2_open()
 *
 * Returns:
 *   ICFG_OK if valid
 *   ICFG_ERR_CRC: CRC mismatch (file corrupted)
 *   ICFG_ERR_BOUNDS: Bounds validation failed
 */
icfg_status_t icf2_validate(const icf2_view_t* v);

/**
 * Get row count
 *
 * Args:
 *   v: View from icf2_open()
 *   out: Output (must be non-NULL)
 *
 * Returns: ICFG_OK
 */
icfg_status_t icf2_row_count(const icf2_view_t* v, uint32_t* out);

/**
 * Get field count
 *
 * Args:
 *   v: View from icf2_open()
 *   out: Output (must be non-NULL)
 *
 * Returns: ICFG_OK
 */
icfg_status_t icf2_field_count(const icf2_view_t* v, uint32_t* out);

#ifdef __cplusplus
}
#endif

#endif /* IRONCFG_ICF2_H */
