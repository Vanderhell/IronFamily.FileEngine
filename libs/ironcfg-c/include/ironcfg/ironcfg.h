/* IRONCFG C99 Library - Public API */

#ifndef IRONCFG_H
#define IRONCFG_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

/* Magic number for IRONCFG files */
#define IRONCFG_MAGIC 0x47464349  /* "ICFG" in little-endian: 0x49 0x43 0x46 0x47 */
#define IRONCFG_VERSION 1

/* Error codes (1-24, 0 = OK) */
typedef enum {
    IRONCFG_OK = 0,
    IRONCFG_TRUNCATED_FILE = 1,
    IRONCFG_INVALID_MAGIC = 2,
    IRONCFG_INVALID_VERSION = 3,
    IRONCFG_INVALID_FLAGS = 4,
    IRONCFG_RESERVED_FIELD_NONZERO = 5,
    IRONCFG_FLAG_MISMATCH = 6,
    IRONCFG_BOUNDS_VIOLATION = 7,
    IRONCFG_ARITHMETIC_OVERFLOW = 8,
    IRONCFG_TRUNCATED_BLOCK = 9,
    IRONCFG_INVALID_SCHEMA = 10,
    IRONCFG_FIELD_ORDER_VIOLATION = 11,
    IRONCFG_INVALID_STRING = 12,
    IRONCFG_INVALID_TYPE_CODE = 13,
    IRONCFG_FIELD_TYPE_MISMATCH = 14,
    IRONCFG_MISSING_REQUIRED_FIELD = 15,
    IRONCFG_UNKNOWN_FIELD = 16,
    IRONCFG_FIELD_COUNT_MISMATCH = 17,
    IRONCFG_ARRAY_TYPE_MISMATCH = 18,
    IRONCFG_NON_MINIMAL_VARUINT = 19,
    IRONCFG_INVALID_FLOAT = 20,
    IRONCFG_RECURSION_LIMIT_EXCEEDED = 21,
    IRONCFG_LIMIT_EXCEEDED = 22,
    IRONCFG_CRC32_MISMATCH = 23,
    IRONCFG_BLAKE3_MISMATCH = 24
} ironcfg_error_code_t;

/* Error structure */
typedef struct {
    ironcfg_error_code_t code;
    uint32_t offset;
} ironcfg_error_t;

/* Validation modes */
typedef enum {
    IRONCFG_VALIDATE_FAST,
    IRONCFG_VALIDATE_STRICT
} ironcfg_validation_mode_t;

/* File header (64 bytes, fixed) */
typedef struct {
    uint32_t magic;
    uint8_t version;
    uint8_t flags;
    uint16_t reserved0;
    uint32_t file_size;
    uint32_t schema_offset;
    uint32_t schema_size;
    uint32_t string_pool_offset;
    uint32_t string_pool_size;
    uint32_t data_offset;
    uint32_t data_size;
    uint32_t crc_offset;
    uint32_t blake3_offset;
    uint32_t reserved1;
    uint8_t reserved2[16];
} ironcfg_header_t;

/* File view (zero-copy) */
typedef struct {
    const uint8_t *buffer;
    size_t buffer_size;
    ironcfg_header_t header;
} ironcfg_view_t;

/* Public API */

/* Open and validate header only. Returns IRONCFG_OK on success. */
ironcfg_error_t ironcfg_open(const uint8_t *buffer, size_t buffer_size,
                             ironcfg_view_t *out_view);

/* Fast validation (O(1)): header and offset checks only */
ironcfg_error_t ironcfg_validate_fast(const uint8_t *buffer, size_t buffer_size);

/* Strict validation (O(n)): full canonical validation */
ironcfg_error_t ironcfg_validate_strict(const uint8_t *buffer, size_t buffer_size);

/* Get root object (zero-copy pointer). Assumes file is already validated. */
ironcfg_error_t ironcfg_get_root(const ironcfg_view_t *view,
                                 const uint8_t **out_data, size_t *out_size);

/* Get schema block (zero-copy pointer) */
ironcfg_error_t ironcfg_get_schema(const ironcfg_view_t *view,
                                   const uint8_t **out_data, size_t *out_size);

/* Get string pool (zero-copy pointer, may be NULL if not present) */
ironcfg_error_t ironcfg_get_string_pool(const ironcfg_view_t *view,
                                        const uint8_t **out_data, size_t *out_size);

/* Get header information */
const ironcfg_header_t *ironcfg_get_header(const ironcfg_view_t *view);

/* Check file properties */
bool ironcfg_has_crc32(const ironcfg_view_t *view);
bool ironcfg_has_blake3(const ironcfg_view_t *view);
bool ironcfg_has_embedded_schema(const ironcfg_view_t *view);
uint32_t ironcfg_get_file_size(const ironcfg_view_t *view);

#endif /* IRONCFG_H */
