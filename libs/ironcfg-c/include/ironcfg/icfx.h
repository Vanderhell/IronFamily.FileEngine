/*
 * icfx.h
 * ICFX (IronConfig X) zero-copy C99 reader
 *
 * Status: Production-ready
 * License: MIT
 *
 * Features:
 * - Zero-copy: TLV-based binary JSON
 * - Optional indexed objects for O(1) key lookup
 * - Type-safe getters for primitives
 * - Depth limits to prevent stack exhaustion
 * - CRC32 optional integrity checking
 */

#ifndef IRONCFG_ICFX_H
#define IRONCFG_ICFX_H

#include "ironcfg_common.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================================
 * Types
 * ============================================================================ */

/* Value kind (discriminant from TLV) */
typedef enum {
    ICFX_NULL = 0x00,
    ICFX_FALSE = 0x01,
    ICFX_TRUE = 0x02,
    ICFX_I64 = 0x10,
    ICFX_U64 = 0x11,
    ICFX_F64 = 0x12,
    ICFX_STRING = 0x20,
    ICFX_BYTES = 0x21,
    ICFX_STR_ID = 0x22,
    ICFX_ARRAY = 0x30,
    ICFX_OBJECT = 0x40,
    ICFX_INDEXED_OBJECT = 0x41,
    ICFX_INVALID = 0xFF,
} icfx_kind_t;

/* Opaque view of ICFX file */
typedef struct {
    const uint8_t* data;
    size_t size;
    uint32_t payload_offset;
    uint32_t dictionary_offset;
    uint32_t vsp_offset;
    uint32_t crc_offset;
    bool has_crc;
    bool has_vsp;
    bool has_index;
} icfx_view_t;

/* Value reference (offset + view) */
typedef struct {
    const icfx_view_t* view;
    uint32_t offset;
} icfx_value_t;

/* Array iteration state (opaque) */
typedef struct {
    const icfx_view_t* view;
    uint32_t offset;    /* Current element offset */
    uint32_t count;     /* Total elements */
    uint32_t index;     /* Current index */
} icfx_array_iter_t;

/* ============================================================================
 * Core Functions
 * ============================================================================ */

/**
 * Open and parse ICFX file
 *
 * Args:
 *   data: Pointer to ICFX file bytes
 *   size: Size of data buffer
 *   out: Output view (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_MAGIC: Invalid magic
 *   ICFG_ERR_BOUNDS: File too small or offsets invalid
 */
icfg_status_t icfx_open(const uint8_t* data, size_t size, icfx_view_t* out);

/**
 * Validate ICFX file (including CRC if enabled)
 *
 * Args:
 *   v: View from icfx_open()
 *
 * Returns:
 *   ICFG_OK if valid
 *   ICFG_ERR_CRC: CRC mismatch
 */
icfg_status_t icfx_validate(const icfx_view_t* v);

/**
 * Get root value
 *
 * Args:
 *   v: View from icfx_open()
 *
 * Returns: Root value (always valid)
 */
icfx_value_t icfx_root(const icfx_view_t* v);

/* ============================================================================
 * Value Inspection
 * ============================================================================ */

/**
 * Get value kind (type discriminant)
 *
 * Args:
 *   val: Value
 *
 * Returns: icfx_kind_t (ICFX_NULL, ICFX_I64, etc.)
 */
icfx_kind_t icfx_kind(const icfx_value_t* val);

/**
 * Check if value is null
 */
bool icfx_is_null(const icfx_value_t* val);

/**
 * Check if value is bool
 */
bool icfx_is_bool(const icfx_value_t* val);

/**
 * Check if value is number (i64, u64, f64)
 */
bool icfx_is_number(const icfx_value_t* val);

/**
 * Check if value is string (inline string or string ID)
 */
bool icfx_is_string(const icfx_value_t* val);

/**
 * Check if value is array
 */
bool icfx_is_array(const icfx_value_t* val);

/**
 * Check if value is object
 */
bool icfx_is_object(const icfx_value_t* val);

/* ============================================================================
 * Primitive Getters (No Allocation)
 * ============================================================================ */

/**
 * Get bool value
 *
 * Returns:
 *   true if ICFX_TRUE, false if ICFX_FALSE or other
 */
bool icfx_get_bool(const icfx_value_t* val);

/**
 * Get int64 value
 *
 * Args:
 *   val: Value (must be ICFX_I64)
 *   out: Output (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_TYPE: Value is not i64
 */
icfg_status_t icfx_get_i64(const icfx_value_t* val, int64_t* out);

/**
 * Get uint64 value
 */
icfg_status_t icfx_get_u64(const icfx_value_t* val, uint64_t* out);

/**
 * Get float64 value
 */
icfg_status_t icfx_get_f64(const icfx_value_t* val, double* out);

/**
 * Get string value (zero-copy, returns pointer+length)
 *
 * Args:
 *   val: Value (must be ICFX_STRING or ICFX_STR_ID)
 *   out_ptr: Output pointer to UTF-8 bytes (may be NULL)
 *   out_len: Output length in bytes (may be NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_TYPE: Value is not a string
 */
icfg_status_t icfx_get_str(const icfx_value_t* val,
                           const uint8_t** out_ptr, uint32_t* out_len);

/**
 * Get bytes value (zero-copy)
 */
icfg_status_t icfx_get_bytes(const icfx_value_t* val,
                             const uint8_t** out_ptr, uint32_t* out_len);

/* ============================================================================
 * Array Access
 * ============================================================================ */

/**
 * Get array length
 *
 * Args:
 *   val: Value (must be ICFX_ARRAY)
 *   out: Output length (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_TYPE: Value is not an array
 */
icfg_status_t icfx_array_len(const icfx_value_t* val, uint32_t* out);

/**
 * Get array element by index
 *
 * Args:
 *   val: Value (must be ICFX_ARRAY)
 *   index: Element index
 *   out: Output element value (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_TYPE: Value is not an array
 *   ICFG_ERR_RANGE: index out of bounds
 */
icfg_status_t icfx_array_get(const icfx_value_t* val, uint32_t index, icfx_value_t* out);

/* ============================================================================
 * Object Access
 * ============================================================================ */

/**
 * Get object field count
 *
 * Args:
 *   val: Value (must be object or indexed object)
 *   out: Output count (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_TYPE: Value is not an object
 */
icfg_status_t icfx_obj_len(const icfx_value_t* val, uint32_t* out);

/**
 * Get object field by key ID (fast for indexed objects, linear scan for regular objects)
 *
 * Args:
 *   val: Value (must be object or indexed object)
 *   key_id: Key ID (index in dictionary)
 *   out: Output value (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_TYPE: Value is not an object
 *   ICFG_ERR_RANGE: key_id not found
 */
icfg_status_t icfx_obj_try_get_by_keyid(const icfx_value_t* val, uint32_t key_id,
                                        icfx_value_t* out);

/**
 * Enumerate object fields
 *
 * Get field at index (0 to field_count-1)
 *
 * Args:
 *   val: Value (must be object)
 *   index: Field index
 *   out_key_id: Output key ID (may be NULL)
 *   out_value: Output value (may be NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: index out of bounds
 */
icfg_status_t icfx_obj_enum(const icfx_value_t* val, uint32_t index,
                            uint32_t* out_key_id, icfx_value_t* out_value);

/* ============================================================================
 * Dictionary Access (Advanced)
 * ============================================================================ */

/**
 * Get key string by ID
 *
 * Args:
 *   view: View from icfx_open()
 *   key_id: Key ID
 *   out_ptr: Output pointer to UTF-8 key (may be NULL)
 *   out_len: Output length (may be NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_RANGE: key_id out of bounds
 */
icfg_status_t icfx_dict_get_key(const icfx_view_t* view, uint32_t key_id,
                                const uint8_t** out_ptr, uint32_t* out_len);

#ifdef __cplusplus
}
#endif

#endif /* IRONCFG_ICFX_H */
