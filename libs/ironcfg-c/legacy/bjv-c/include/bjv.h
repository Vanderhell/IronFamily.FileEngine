#ifndef BJV_H
#define BJV_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================================
 * Error codes
 * ============================================================================
 */

typedef enum {
    BJV_OK = 0,
    BJV_ERR_FORMAT = 1,           /* Bad magic, bad header */
    BJV_ERR_BOUNDS = 2,           /* Read past EOF */
    BJV_ERR_UNSUPPORTED = 3,      /* Unknown type/flag */
    BJV_ERR_CANONICAL = 4,        /* Non-minimal encoding, unsorted, NaN, etc */
    BJV_ERR_OVERFLOW = 5          /* Integer overflow */
} bjv_err_t;

/* ============================================================================
 * Types
 * ============================================================================
 */

/* TLV type IDs */
typedef enum {
    BJV_NULL = 0x00,
    BJV_FALSE = 0x01,
    BJV_TRUE = 0x02,
    BJV_I64 = 0x10,
    BJV_U64 = 0x11,
    BJV_F64 = 0x12,
    BJV_STRING = 0x20,
    BJV_BYTES = 0x21,
    BJV_STR_ID = 0x22,
    BJV_ARRAY = 0x30,
    BJV_OBJECT = 0x40
} bjv_type_t;

/* Slice: pointer + length (no zero-termination) */
typedef struct {
    const uint8_t* ptr;
    size_t len;
} bjv_slice_t;

/* Forward declarations */
typedef struct bjv_doc bjv_doc_t;

/* Value reference: points into a document */
typedef struct {
    const bjv_doc_t* doc;
    uint32_t off;  /* Absolute offset to type byte */
} bjv_val_t;

/* ============================================================================
 * Document (internal state for parsed BJV)
 * ============================================================================
 */

typedef struct bjv_doc {
    const uint8_t* data;
    size_t size;

    /* Header fields (offsets are absolute in data) */
    uint32_t dict_off;
    uint32_t vsp_off;
    uint32_t root_off;
    uint32_t crc_off;

    /* Flags */
    bool has_crc;
    bool has_vsp;
    bool is_bjv4;      /* true=BJV4 (u32 keyId), false=BJV2 (u16 keyId) */

    /* Dictionary (parsed) */
    uint32_t dict_count;
    const uint8_t* dict_data;     /* Points to dict_off in data */

    /* VSP (parsed, optional) */
    uint32_t vsp_count;
    const uint8_t* vsp_data;      /* Points to vsp_off in data, or NULL */
} bjv_doc_t;

/* ============================================================================
 * API: Open document
 * ============================================================================
 */

/**
 * Parse BJV header and dictionary. Does NOT validate root.
 * Accepts BJV2 or BJV4.
 * Returns BJV_OK on success, error code otherwise.
 */
bjv_err_t bjv_open(const void* data, size_t size, bjv_doc_t* out_doc);

/**
 * Get root value reference.
 */
bjv_val_t bjv_root(const bjv_doc_t* doc);

/**
 * Get type byte of a value.
 */
uint8_t bjv_type(bjv_val_t v);

/* ============================================================================
 * API: Getters (primitives)
 * ============================================================================
 */

/**
 * Get I64 value.
 * Returns true if type is BJV_I64, false otherwise (does not modify out).
 */
bool bjv_get_i64(bjv_val_t v, int64_t* out);

/**
 * Get U64 value.
 * Returns true if type is BJV_U64, false otherwise.
 */
bool bjv_get_u64(bjv_val_t v, uint64_t* out);

/**
 * Get F64 value.
 * Returns true if type is BJV_F64, false otherwise.
 * Note: parser already rejected NaN and -0.0 during parsing.
 */
bool bjv_get_f64(bjv_val_t v, double* out);

/**
 * Get boolean value (checks for BJV_TRUE or BJV_FALSE).
 * Returns true if bool, false otherwise.
 */
bool bjv_get_bool(bjv_val_t v, bool* out);

/**
 * Check for NULL value.
 * Returns true if type is BJV_NULL, false otherwise.
 */
bool bjv_get_null(bjv_val_t v);

/**
 * Get string value (BJV_STRING or BJV_STR_ID).
 * For BJV_STR_ID, dereferences from VSP.
 * Returns true on success, false if not a string type.
 */
bool bjv_get_string(bjv_val_t v, bjv_slice_t* out_utf8);

/**
 * Get bytes value (BJV_BYTES).
 * Returns true on success, false if not bytes type.
 */
bool bjv_get_bytes(bjv_val_t v, bjv_slice_t* out_bytes);

/* ============================================================================
 * API: Array access
 * ============================================================================
 */

/**
 * Get array element count.
 * Returns true if v is BJV_ARRAY, false otherwise.
 */
bool bjv_arr_count(bjv_val_t v, uint32_t* out_count);

/**
 * Get array element by index.
 * Requires idx < count.
 * Returns true on success, false on bounds or not array.
 */
bool bjv_arr_get(bjv_val_t v, uint32_t idx, bjv_val_t* out_elem);

/* ============================================================================
 * API: Object access
 * ============================================================================
 */

/**
 * Get object key count.
 * Returns true if v is BJV_OBJECT, false otherwise.
 */
bool bjv_obj_count(bjv_val_t v, uint32_t* out_count);

/**
 * Get object value by keyId (fast, no string comparison).
 * Requires keyId to exist in object (sorted lookup).
 * Returns true on success, false if keyId not found or not object.
 */
bool bjv_obj_get_by_keyid(bjv_val_t v, uint32_t keyId, bjv_val_t* out_val);

/* ============================================================================
 * API: Dictionary access
 * ============================================================================
 */

/**
 * Find keyId by key string.
 * Performs binary search on sorted dictionary.
 * Returns true if found, false otherwise.
 */
bool bjv_keyid_find(const bjv_doc_t* doc, const char* key_utf8, size_t key_len, uint32_t* out_keyid);

/**
 * Get key string by keyId.
 * Returns true if keyId is valid, false otherwise.
 */
bool bjv_keyid_to_key(const bjv_doc_t* doc, uint32_t keyId, bjv_slice_t* out_key_utf8);

/* ============================================================================
 * API: Validation
 * ============================================================================
 */

/**
 * Recursively validate root value (canonical checks).
 * Enforces depth limit, checks all invariants.
 * max_depth: max nesting allowed (recommend 64).
 * Returns BJV_OK if valid, error code otherwise.
 */
bjv_err_t bjv_validate_root(const bjv_doc_t* doc, uint32_t max_depth);

#ifdef __cplusplus
}
#endif

#endif /* BJV_H */
