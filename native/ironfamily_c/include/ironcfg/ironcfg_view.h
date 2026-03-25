#ifndef IRONCFG_VIEW_H
#define IRONCFG_VIEW_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#include "ironcfg_validate.h"

/* Path element types */
typedef enum {
    IRONCFG_PATH_INDEX,
    IRONCFG_PATH_KEY,
} ironcfg_path_type_t;

/* Path element */
typedef struct {
    ironcfg_path_type_t type;
    union {
        uint32_t index;
        struct {
            const char* key;
            size_t key_len;
        } key_val;
    } value;
} ironcfg_path_elem_t;

/* Value getters (zero-copy) */

/* Get boolean value (0x01 false, 0x02 true) */
ironcfg_error_t ironcfg_get_bool(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    bool* out_value);

/* Get signed 64-bit integer */
ironcfg_error_t ironcfg_get_i64(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    int64_t* out_value);

/* Get unsigned 64-bit integer */
ironcfg_error_t ironcfg_get_u64(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    uint64_t* out_value);

/* Get 64-bit float */
ironcfg_error_t ironcfg_get_f64(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    double* out_value);

/* Get string (zero-copy pointer + length) */
ironcfg_error_t ironcfg_get_string(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    const uint8_t** out_data, size_t* out_len);

/* Get bytes (zero-copy pointer + length) */
ironcfg_error_t ironcfg_get_bytes(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    const uint8_t** out_data, size_t* out_len);

/* Get array length */
ironcfg_error_t ironcfg_get_array_length(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    uint32_t* out_length);

/* Get object field count */
ironcfg_error_t ironcfg_get_object_field_count(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    uint32_t* out_count);

#endif // IRONCFG_VIEW_H
