/* IRONCFG C99 Deterministic Encoder */

#ifndef IRONCFG_ENCODE_H
#define IRONCFG_ENCODE_H

#include "ironcfg.h"
#include <stdint.h>
#include <stddef.h>

/* Primitive value types */
typedef enum {
    IRONCFG_VAL_NULL = 0,
    IRONCFG_VAL_BOOL,
    IRONCFG_VAL_I64,
    IRONCFG_VAL_U64,
    IRONCFG_VAL_F64,
    IRONCFG_VAL_STRING,
    IRONCFG_VAL_BYTES,
    IRONCFG_VAL_ARRAY,
    IRONCFG_VAL_OBJECT
} ironcfg_value_type_t;

/* Forward declare value structure */
typedef struct ironcfg_value ironcfg_value_t;

/* Object field */
typedef struct {
    uint32_t field_id;
    const char *field_name;
    uint32_t field_name_len;
    uint8_t field_type;
    uint8_t is_required;
} ironcfg_field_def_t;

/* Schema definition */
typedef struct {
    ironcfg_field_def_t *fields;
    uint32_t field_count;
} ironcfg_schema_t;

/* Boolean value */
typedef struct {
    bool value;
} ironcfg_bool_t;

/* Integer values */
typedef struct {
    int64_t value;
} ironcfg_i64_t;

typedef struct {
    uint64_t value;
} ironcfg_u64_t;

/* Float value (with -0 normalization) */
typedef struct {
    double value;
} ironcfg_f64_t;

/* String value */
typedef struct {
    const uint8_t *data;
    uint32_t len;
} ironcfg_string_t;

/* Bytes value */
typedef struct {
    const uint8_t *data;
    uint32_t len;
} ironcfg_bytes_t;

/* Array value */
typedef struct {
    ironcfg_value_t *elements;
    uint32_t element_count;
    uint8_t element_type;
} ironcfg_array_t;

/* Object value */
typedef struct {
    ironcfg_value_t *field_values;
    uint32_t field_count;
    ironcfg_schema_t *schema;
} ironcfg_object_t;

/* Polymorphic value */
struct ironcfg_value {
    ironcfg_value_type_t type;
    union {
        ironcfg_bool_t bool_val;
        ironcfg_i64_t i64_val;
        ironcfg_u64_t u64_val;
        ironcfg_f64_t f64_val;
        ironcfg_string_t string_val;
        ironcfg_bytes_t bytes_val;
        ironcfg_array_t array_val;
        ironcfg_object_t object_val;
    } data;
};

/* Encode context */
typedef struct {
    uint8_t *buffer;
    size_t buffer_size;
    size_t offset;
    ironcfg_error_t last_error;
    bool has_crc32;
    bool has_blake3;
} ironcfg_encode_ctx_t;

/* Public API */

/**
 * Encode a value to buffer with CRC32 and/or BLAKE3
 */
ironcfg_error_t ironcfg_encode(
    const ironcfg_value_t *root,
    ironcfg_schema_t *schema,
    bool compute_crc32,
    bool compute_blake3,
    uint8_t *out_buffer,
    size_t buffer_size,
    size_t *out_encoded_size);

/**
 * Calculate minimal VarUInt encoding size
 */
uint32_t ironcfg_varuint_size(uint64_t value);

/**
 * Encode VarUInt to buffer at offset
 */
void ironcfg_encode_varuint(uint8_t *buffer, size_t offset, uint64_t value, uint32_t *out_size);

/**
 * Normalize float (-0.0 -> +0.0)
 */
double ironcfg_normalize_float(double value);

/**
 * Check if float is NaN
 */
bool ironcfg_is_nan(double value);

#endif /* IRONCFG_ENCODE_H */
