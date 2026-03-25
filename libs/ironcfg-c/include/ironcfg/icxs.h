/*
 * icxs.h
 * ICXS (IronConfig X Schema) zero-copy C99 reader
 *
 * Status: Production-ready
 * License: MIT
 *
 * Features:
 * - Zero-copy: No allocations required for parsing/access
 * - O(1) field access: Constant-time field lookup by ID
 * - Type-safe: Dedicated getter for each type
 * - Bounds-safe: All reads validated against buffer size
 * - CRC validation: Optional integrity checking
 */

#ifndef IRONCFG_ICXS_H
#define IRONCFG_ICXS_H

#include "ironcfg_common.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================================
 * Types
 * ============================================================================ */

/* Opaque view of ICXS file (zero-copy) */
typedef struct {
    const uint8_t* data;
    size_t size;
    uint32_t header_size;
    uint32_t schema_block_offset;
    uint32_t schema_fields_offset;  /* Offset where field definitions start (after varint field_count) */
    uint32_t data_block_offset;
    uint32_t crc_offset;
    uint32_t record_count;
    uint32_t record_stride;
    uint32_t field_count;
    bool has_crc;
} icxs_view_t;

/* Single record view (zero-copy) */
typedef struct {
    const icxs_view_t* view;
    uint32_t index;
} icxs_record_t;

/* Field metadata */
typedef struct {
    uint32_t id;
    uint8_t type;      /* 1=i64, 2=u64, 3=f64, 4=bool, 5=str */
    uint32_t offset;   /* Byte offset in fixed region */
} icxs_field_t;

/* ============================================================================
 * Core Functions
 * ============================================================================ */

/**
 * Open and parse ICXS file
 *
 * Args:
 *   data: Pointer to ICXS file bytes
 *   size: Size of data buffer
 *   out: Output view (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_MAGIC: Invalid magic
 *   ICFG_ERR_BOUNDS: File too small or offsets invalid
 *   ICFG_ERR_SCHEMA: Schema parsing error
 */
icfg_status_t icxs_open(const uint8_t* data, size_t size, icxs_view_t* out);

/**
 * Validate ICXS file (including CRC if enabled)
 *
 * Args:
 *   v: View from icxs_open()
 *
 * Returns:
 *   ICFG_OK if valid
 *   ICFG_ERR_CRC: CRC mismatch (file corrupted)
 *   ICFG_ERR_BOUNDS: Bounds validation failed
 */
icfg_status_t icxs_validate(const icxs_view_t* v);

/**
 * Get record count
 *
 * Args:
 *   v: View from icxs_open()
 *   out: Output (must be non-NULL)
 *
 * Returns: ICFG_OK
 */
icfg_status_t icxs_record_count(const icxs_view_t* v, uint32_t* out);

/**
 * Get record at index
 *
 * Args:
 *   v: View from icxs_open()
 *   index: Record index (0-based)
 *   out_rec: Output record view (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: index >= record_count
 */
icfg_status_t icxs_get_record(const icxs_view_t* v, uint32_t index, icxs_record_t* out_rec);

/* ============================================================================
 * Field Access (Type-safe)
 * ============================================================================ */

/**
 * Get int64 field
 *
 * Args:
 *   r: Record from icxs_get_record()
 *   field_id: Field ID from schema
 *   out: Output (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: field_id not found
 *   ICFG_ERR_TYPE: Field is not i64
 */
icfg_status_t icxs_get_i64(const icxs_record_t* r, uint32_t field_id, int64_t* out);

/**
 * Get uint64 field
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: field_id not found
 *   ICFG_ERR_TYPE: Field is not u64
 */
icfg_status_t icxs_get_u64(const icxs_record_t* r, uint32_t field_id, uint64_t* out);

/**
 * Get float64 field
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: field_id not found
 *   ICFG_ERR_TYPE: Field is not f64
 */
icfg_status_t icxs_get_f64(const icxs_record_t* r, uint32_t field_id, double* out);

/**
 * Get bool field
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: field_id not found
 *   ICFG_ERR_TYPE: Field is not bool
 */
icfg_status_t icxs_get_bool(const icxs_record_t* r, uint32_t field_id, bool* out);

/**
 * Get string field (zero-copy, no allocation)
 *
 * Args:
 *   r: Record from icxs_get_record()
 *   field_id: Field ID from schema
 *   out_ptr: Output pointer to UTF-8 bytes in buffer (may be NULL if string is empty)
 *   out_len: Output length in bytes (may be NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: field_id not found
 *   ICFG_ERR_TYPE: Field is not string
 *
 * Note: Returned pointer is valid only while original buffer is valid.
 *       No UTF-8 validation performed; caller must validate if needed.
 */
icfg_status_t icxs_get_str(const icxs_record_t* r, uint32_t field_id,
                           const uint8_t** out_ptr, uint32_t* out_len);

/* ============================================================================
 * Schema Access (Internal)
 * ============================================================================ */

/**
 * Get field metadata by ID (for advanced usage)
 *
 * Args:
 *   v: View from icxs_open()
 *   field_id: Field ID to lookup
 *   out: Output field metadata (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: field_id not found
 */
icfg_status_t icxs_schema_get_field(const icxs_view_t* v, uint32_t field_id, icxs_field_t* out);

/**
 * Get schema hash (SHA-256 first 16 bytes)
 *
 * Args:
 *   v: View from icxs_open()
 *   out: Output hash (must be >= 16 bytes)
 *
 * Returns: ICFG_OK
 */
icfg_status_t icxs_schema_hash(const icxs_view_t* v, uint8_t* out);

#ifdef __cplusplus
}
#endif

#endif /* IRONCFG_ICXS_H */
