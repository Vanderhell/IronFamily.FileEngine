/* IRONCFG C99 - Read-only view and access */

#include "ironcfg/ironcfg.h"

/* Get a zero-copy pointer to the root object data */
ironcfg_error_t ironcfg_get_root(const ironcfg_view_t *view,
                                 const uint8_t **out_data, size_t *out_size) {
    ironcfg_error_t error = { IRONCFG_OK, 0 };

    if (view == NULL || out_data == NULL || out_size == NULL) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 0;
        return error;
    }

    /* Root data starts at data_offset and extends for data_size bytes */
    uint32_t root_offset = view->header.data_offset;
    uint32_t root_size = view->header.data_size;

    if (root_offset > view->buffer_size) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = root_offset;
        return error;
    }

    if (root_size > view->buffer_size - root_offset) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = root_offset;
        return error;
    }

    *out_data = view->buffer + root_offset;
    *out_size = root_size;

    return error;
}

/* Get a zero-copy pointer to the schema block */
ironcfg_error_t ironcfg_get_schema(const ironcfg_view_t *view,
                                   const uint8_t **out_data, size_t *out_size) {
    ironcfg_error_t error = { IRONCFG_OK, 0 };

    if (view == NULL || out_data == NULL || out_size == NULL) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 0;
        return error;
    }

    uint32_t schema_offset = view->header.schema_offset;
    uint32_t schema_size = view->header.schema_size;

    if (schema_offset > view->buffer_size) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = schema_offset;
        return error;
    }

    if (schema_size > view->buffer_size - schema_offset) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = schema_offset;
        return error;
    }

    *out_data = view->buffer + schema_offset;
    *out_size = schema_size;

    return error;
}

/* Get a zero-copy pointer to the string pool (if present) */
ironcfg_error_t ironcfg_get_string_pool(const ironcfg_view_t *view,
                                        const uint8_t **out_data, size_t *out_size) {
    ironcfg_error_t error = { IRONCFG_OK, 0 };

    if (view == NULL || out_data == NULL || out_size == NULL) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 0;
        return error;
    }

    uint32_t pool_offset = view->header.string_pool_offset;
    uint32_t pool_size = view->header.string_pool_size;

    /* String pool is optional (may be absent) */
    if (pool_offset == 0) {
        *out_data = NULL;
        *out_size = 0;
        return error;
    }

    if (pool_offset > view->buffer_size) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = pool_offset;
        return error;
    }

    if (pool_size > view->buffer_size - pool_offset) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = pool_offset;
        return error;
    }

    *out_data = view->buffer + pool_offset;
    *out_size = pool_size;

    return error;
}

/* Get header information (always available after open) */
const ironcfg_header_t *ironcfg_get_header(const ironcfg_view_t *view) {
    if (view == NULL) return NULL;
    return &view->header;
}

/* Check if file has CRC32 */
bool ironcfg_has_crc32(const ironcfg_view_t *view) {
    if (view == NULL) return false;
    return (view->header.flags & 0x01) != 0;
}

/* Check if file has BLAKE3 */
bool ironcfg_has_blake3(const ironcfg_view_t *view) {
    if (view == NULL) return false;
    return (view->header.flags & 0x02) != 0;
}

/* Check if schema is embedded */
bool ironcfg_has_embedded_schema(const ironcfg_view_t *view) {
    if (view == NULL) return false;
    return (view->header.flags & 0x04) != 0;
}

/* Get file size */
uint32_t ironcfg_get_file_size(const ironcfg_view_t *view) {
    if (view == NULL) return 0;
    return view->header.file_size;
}

/* ============================================================================
 * Value Extraction API (zero-copy, deterministic path traversal)
 * ============================================================================ */

#include <string.h>

#define MAX_NESTING 128

/* Helper: Decode little-endian 64-bit unsigned integer */
static uint64_t decode_u64(const uint8_t* data) {
    return ((uint64_t)data[0])
        | (((uint64_t)data[1]) << 8)
        | (((uint64_t)data[2]) << 16)
        | (((uint64_t)data[3]) << 24)
        | (((uint64_t)data[4]) << 32)
        | (((uint64_t)data[5]) << 40)
        | (((uint64_t)data[6]) << 48)
        | (((uint64_t)data[7]) << 56);
}

/* Helper: Decode little-endian 64-bit signed integer */
static int64_t decode_i64(const uint8_t* data) {
    return (int64_t)decode_u64(data);
}

/* Helper: Decode little-endian 64-bit float */
static double decode_f64(const uint8_t* data) {
    uint64_t bits = decode_u64(data);
    double value;
    memcpy(&value, &bits, 8);
    return value;
}

/* Helper: Decode VarUInt from buffer, return bytes consumed */
static ironcfg_error_t decode_varuint32(
    const uint8_t* buffer, size_t buffer_size, size_t offset,
    uint32_t* out_value, size_t* out_bytes)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };

    if (offset >= buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }

    uint8_t b = buffer[offset];
    if ((b & 0x80) == 0) {
        *out_value = b;
        *out_bytes = 1;
        return err;
    }

    if (offset + 1 >= buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }
    uint8_t b1 = buffer[offset + 1];
    if ((b1 & 0x80) == 0) {
        *out_value = (uint32_t)((b & 0x7F) | ((b1 & 0x7F) << 7));
        *out_bytes = 2;
        return err;
    }

    if (offset + 2 >= buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }
    uint8_t b2 = buffer[offset + 2];
    if ((b2 & 0x80) == 0) {
        *out_value = (uint32_t)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14));
        *out_bytes = 3;
        return err;
    }

    if (offset + 3 >= buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }
    uint8_t b3 = buffer[offset + 3];
    if ((b3 & 0x80) == 0) {
        *out_value = (uint32_t)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14) | ((b3 & 0x7F) << 21));
        *out_bytes = 4;
        return err;
    }

    if (offset + 4 >= buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }
    uint8_t b4 = buffer[offset + 4];
    *out_value = (uint32_t)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14) | ((b3 & 0x7F) << 21) | ((b4 & 0x0F) << 28));
    *out_bytes = 5;
    return err;
}

/* Forward declaration */
static ironcfg_error_t skip_value(
    const uint8_t* buffer, size_t buffer_size, size_t offset,
    uint8_t type_code, size_t* out_next_offset);

/* Find value by path: returns offset to type code and the type code itself */
static ironcfg_error_t find_value_by_path(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    size_t* out_offset, uint8_t* out_type_code)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_offset = 0;
    *out_type_code = 0;

    if (path == NULL || path_len == 0) {
        /* No path: return root value offset */
        if (view->header.data_offset >= buffer_size) {
            err.code = IRONCFG_BOUNDS_VIOLATION;
            err.offset = view->header.data_offset;
            return err;
        }
        *out_offset = view->header.data_offset;
        *out_type_code = buffer[view->header.data_offset];
        return err;
    }

    /* Start at root */
    size_t current_offset = view->header.data_offset;
    uint8_t current_type = buffer[view->header.data_offset];

    if (current_type != 0x40) {
        err.code = IRONCFG_TYPE_MISMATCH;
        err.offset = current_offset;
        return err;
    }

    /* Traverse path */
    for (size_t i = 0; i < path_len; i++) {
        if (i > MAX_NESTING) {
            err.code = IRONCFG_LIMIT_EXCEEDED;
            err.offset = current_offset;
            return err;
        }

        ironcfg_path_type_t path_type = path[i].type;

        if (path_type == IRONCFG_PATH_KEY) {
            /* Current must be object */
            if (current_type != 0x40) {
                err.code = IRONCFG_FIELD_TYPE_MISMATCH;
                err.offset = current_offset;
                return err;
            }

            /* Read object field count */
            /* Note: current_offset points to the type code, so skip it first */
            size_t count_offset = current_offset + 1;
            uint32_t field_count;
            size_t count_bytes;
            ironcfg_error_t count_err = decode_varuint32(buffer, buffer_size, count_offset, &field_count, &count_bytes);
            if (count_err.code != IRONCFG_OK) {
                return count_err;
            }

            size_t field_offset = count_offset + count_bytes;
            const char* search_key = path[i].value.key_val.key;
            size_t search_key_len = path[i].value.key_val.key_len;

            /* Find matching field */
            bool found = false;
            for (uint32_t f = 0; f < field_count; f++) {
                /* Read field ID */
                uint32_t field_id;
                size_t id_bytes;
                ironcfg_error_t id_err = decode_varuint32(buffer, buffer_size, field_offset, &field_id, &id_bytes);
                if (id_err.code != IRONCFG_OK) {
                    return id_err;
                }
                field_offset += id_bytes;

                /* Read field type */
                if (field_offset >= buffer_size) {
                    err.code = IRONCFG_BOUNDS_VIOLATION;
                    err.offset = field_offset;
                    return err;
                }
                uint8_t field_type = buffer[field_offset];
                field_offset++;

                /* Check if field name is written in data (for types >= 0x1C) */
                if (field_type >= 0x1C) {
                    /* Read field name length */
                    uint32_t name_len;
                    size_t name_len_bytes;
                    ironcfg_error_t name_err = decode_varuint32(buffer, buffer_size, field_offset, &name_len, &name_len_bytes);
                    if (name_err.code != IRONCFG_OK) {
                        return name_err;
                    }
                    field_offset += name_len_bytes;

                    /* Bounds check field name */
                    if (field_offset + name_len > buffer_size) {
                        err.code = IRONCFG_BOUNDS_VIOLATION;
                        err.offset = field_offset;
                        return err;
                    }

                    /* Compare field name */
                    if (name_len == search_key_len &&
                        memcmp(&buffer[field_offset], search_key, name_len) == 0) {
                        current_offset = field_offset + name_len;
                        current_type = field_type;
                        found = true;
                        break;
                    }

                    field_offset += name_len;

                    /* Skip the field value after the name */
                    size_t next_offset;
                    ironcfg_error_t skip_err = skip_value(buffer, buffer_size, field_offset, field_type, &next_offset);
                    if (skip_err.code != IRONCFG_OK) {
                        return skip_err;
                    }
                    field_offset = next_offset;
                } else {
                    /* Non-compound, no field name: skip field value directly */
                    size_t next_offset;
                    ironcfg_error_t skip_err = skip_value(buffer, buffer_size, field_offset, field_type, &next_offset);
                    if (skip_err.code != IRONCFG_OK) {
                        return skip_err;
                    }
                    field_offset = next_offset;
                }
            }

            if (!found) {
                err.code = IRONCFG_UNKNOWN_FIELD;
                err.offset = current_offset;
                return err;
            }
        } else if (path_type == IRONCFG_PATH_INDEX) {
            /* Current must be array */
            if (current_type != 0x30) {
                err.code = IRONCFG_FIELD_TYPE_MISMATCH;
                err.offset = current_offset;
                return err;
            }

            /* Note: current_offset points to the type code, so skip it first */
            size_t len_offset = current_offset + 1;
            uint32_t array_len;
            size_t len_bytes;
            ironcfg_error_t len_err = decode_varuint32(buffer, buffer_size, len_offset, &array_len, &len_bytes);
            if (len_err.code != IRONCFG_OK) {
                return len_err;
            }

            uint32_t target_index = path[i].value.index;
            if (target_index >= array_len) {
                err.code = IRONCFG_BOUNDS_VIOLATION;
                err.offset = current_offset;
                return err;
            }

            size_t elem_offset = len_offset + len_bytes;

            /* Skip to desired element */
            for (uint32_t e = 0; e < target_index; e++) {
                if (elem_offset >= buffer_size) {
                    err.code = IRONCFG_BOUNDS_VIOLATION;
                    err.offset = elem_offset;
                    return err;
                }
                uint8_t elem_type = buffer[elem_offset];

                size_t next_offset;
                ironcfg_error_t skip_err = skip_value(buffer, buffer_size, elem_offset, elem_type, &next_offset);
                if (skip_err.code != IRONCFG_OK) {
                    return skip_err;
                }
                elem_offset = next_offset;
            }

            if (elem_offset >= buffer_size) {
                err.code = IRONCFG_BOUNDS_VIOLATION;
                err.offset = elem_offset;
                return err;
            }
            current_offset = elem_offset;
            current_type = buffer[elem_offset];
        } else {
            err.code = IRONCFG_INVALID_TYPE_CODE;
            err.offset = current_offset;
            return err;
        }
    }

    *out_offset = current_offset;
    *out_type_code = current_type;
    return err;
}

/* Skip value at given offset and return next offset */
static ironcfg_error_t skip_value(
    const uint8_t* buffer, size_t buffer_size, size_t offset,
    uint8_t type_code, size_t* out_next_offset)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_next_offset = offset;

    switch (type_code) {
        case 0x00:
        case 0x01:
        case 0x02:
            *out_next_offset = offset;
            return err;

        case 0x10:
        case 0x11:
        case 0x12:
            if (offset + 8 > buffer_size) {
                err.code = IRONCFG_BOUNDS_VIOLATION;
                err.offset = offset;
                return err;
            }
            *out_next_offset = offset + 8;
            return err;

        case 0x20:
        case 0x22: {
            uint32_t len;
            size_t len_bytes;
            ironcfg_error_t len_err = decode_varuint32(buffer, buffer_size, offset, &len, &len_bytes);
            if (len_err.code != IRONCFG_OK) {
                return len_err;
            }
            if (offset + len_bytes + len > buffer_size) {
                err.code = IRONCFG_BOUNDS_VIOLATION;
                err.offset = offset;
                return err;
            }
            *out_next_offset = offset + len_bytes + len;
            return err;
        }

        case 0x21: {
            uint32_t id;
            size_t id_bytes;
            ironcfg_error_t id_err = decode_varuint32(buffer, buffer_size, offset, &id, &id_bytes);
            if (id_err.code != IRONCFG_OK) {
                return id_err;
            }
            *out_next_offset = offset + id_bytes;
            return err;
        }

        case 0x30:
        case 0x40: {
            uint32_t count;
            size_t count_bytes;
            ironcfg_error_t count_err = decode_varuint32(buffer, buffer_size, offset, &count, &count_bytes);
            if (count_err.code != IRONCFG_OK) {
                return count_err;
            }

            size_t elem_offset = offset + count_bytes;
            for (uint32_t i = 0; i < count; i++) {
                if (elem_offset >= buffer_size) {
                    err.code = IRONCFG_BOUNDS_VIOLATION;
                    err.offset = elem_offset;
                    return err;
                }

                uint8_t elem_type = buffer[elem_offset];
                if (type_code == 0x40) {
                    /* Object: skip fieldId, type */
                    uint32_t field_id;
                    size_t id_bytes;
                    ironcfg_error_t id_err = decode_varuint32(buffer, buffer_size, elem_offset, &field_id, &id_bytes);
                    if (id_err.code != IRONCFG_OK) {
                        return id_err;
                    }
                    elem_offset += id_bytes;

                    if (elem_offset >= buffer_size) {
                        err.code = IRONCFG_BOUNDS_VIOLATION;
                        err.offset = elem_offset;
                        return err;
                    }
                    elem_type = buffer[elem_offset];
                    elem_offset++;

                    /* Skip field name if present (for types >= 0x1C) */
                    if (elem_type >= 0x1C) {
                        uint32_t name_len;
                        size_t name_bytes;
                        ironcfg_error_t name_err = decode_varuint32(buffer, buffer_size, elem_offset, &name_len, &name_bytes);
                        if (name_err.code != IRONCFG_OK) {
                            return name_err;
                        }
                        elem_offset += name_bytes + name_len;
                    }

                    /* For non-compound types, also skip the field value */
                    if (elem_type < 0x30) {
                        size_t next_offset;
                        ironcfg_error_t skip_err = skip_value(buffer, buffer_size, elem_offset, elem_type, &next_offset);
                        if (skip_err.code != IRONCFG_OK) {
                            return skip_err;
                        }
                        elem_offset = next_offset;
                    }
                } else {
                    elem_offset++;
                }

                /* Skip element value (for arrays and as fallback for compound object fields) */
                if (type_code == 0x30 || elem_type >= 0x30) {
                    size_t next_offset;
                    ironcfg_error_t skip_err = skip_value(buffer, buffer_size, elem_offset, elem_type, &next_offset);
                    if (skip_err.code != IRONCFG_OK) {
                        return skip_err;
                    }
                    elem_offset = next_offset;
                }
            }

            *out_next_offset = elem_offset;
            return err;
        }

        default:
            err.code = IRONCFG_INVALID_TYPE_CODE;
            err.offset = offset;
            return err;
    }
}

/* Public API: Get boolean value */
ironcfg_error_t ironcfg_get_bool(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    bool* out_value)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_value = false;

    size_t offset;
    uint8_t type_code;
    ironcfg_error_t find_err = find_value_by_path(buffer, buffer_size, view, path, path_len, &offset, &type_code);
    if (find_err.code != IRONCFG_OK) {
        return find_err;
    }

    if (type_code == 0x01) {
        *out_value = false;
        return err;
    }
    if (type_code == 0x02) {
        *out_value = true;
        return err;
    }
    err.code = IRONCFG_TYPE_MISMATCH;
    err.offset = offset;
    return err;
}

/* Public API: Get signed 64-bit integer */
ironcfg_error_t ironcfg_get_i64(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    int64_t* out_value)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_value = 0;

    size_t offset;
    uint8_t type_code;
    ironcfg_error_t find_err = find_value_by_path(buffer, buffer_size, view, path, path_len, &offset, &type_code);
    if (find_err.code != IRONCFG_OK) {
        return find_err;
    }

    if (type_code != 0x10) {
        err.code = IRONCFG_TYPE_MISMATCH;
        err.offset = offset;
        return err;
    }
    if (offset + 8 > buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }

    *out_value = decode_i64(&buffer[offset]);
    return err;
}

/* Public API: Get unsigned 64-bit integer */
ironcfg_error_t ironcfg_get_u64(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    uint64_t* out_value)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_value = 0;

    size_t offset;
    uint8_t type_code;
    ironcfg_error_t find_err = find_value_by_path(buffer, buffer_size, view, path, path_len, &offset, &type_code);
    if (find_err.code != IRONCFG_OK) {
        return find_err;
    }

    if (type_code != 0x11) {
        err.code = IRONCFG_TYPE_MISMATCH;
        err.offset = offset;
        return err;
    }
    if (offset + 8 > buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }

    *out_value = decode_u64(&buffer[offset]);
    return err;
}

/* Public API: Get 64-bit float */
ironcfg_error_t ironcfg_get_f64(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    double* out_value)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_value = 0.0;

    size_t offset;
    uint8_t type_code;
    ironcfg_error_t find_err = find_value_by_path(buffer, buffer_size, view, path, path_len, &offset, &type_code);
    if (find_err.code != IRONCFG_OK) {
        return find_err;
    }

    if (type_code != 0x12) {
        err.code = IRONCFG_TYPE_MISMATCH;
        err.offset = offset;
        return err;
    }
    if (offset + 8 > buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }

    *out_value = decode_f64(&buffer[offset]);
    return err;
}

/* Public API: Get string (zero-copy pointer + length) */
ironcfg_error_t ironcfg_get_string(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    const uint8_t** out_data, size_t* out_len)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_data = NULL;
    *out_len = 0;

    size_t offset;
    uint8_t type_code;
    ironcfg_error_t find_err = find_value_by_path(buffer, buffer_size, view, path, path_len, &offset, &type_code);
    if (find_err.code != IRONCFG_OK) {
        return find_err;
    }

    if (type_code != 0x20) {
        err.code = IRONCFG_TYPE_MISMATCH;
        err.offset = offset;
        return err;
    }

    uint32_t str_len;
    size_t len_bytes;
    ironcfg_error_t len_err = decode_varuint32(buffer, buffer_size, offset, &str_len, &len_bytes);
    if (len_err.code != IRONCFG_OK) {
        return len_err;
    }

    offset += len_bytes;
    if (offset + str_len > buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }

    *out_data = &buffer[offset];
    *out_len = str_len;
    return err;
}

/* Public API: Get bytes (zero-copy pointer + length) */
ironcfg_error_t ironcfg_get_bytes(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    const uint8_t** out_data, size_t* out_len)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_data = NULL;
    *out_len = 0;

    size_t offset;
    uint8_t type_code;
    ironcfg_error_t find_err = find_value_by_path(buffer, buffer_size, view, path, path_len, &offset, &type_code);
    if (find_err.code != IRONCFG_OK) {
        return find_err;
    }

    if (type_code != 0x22) {
        err.code = IRONCFG_TYPE_MISMATCH;
        err.offset = offset;
        return err;
    }

    uint32_t blob_len;
    size_t len_bytes;
    ironcfg_error_t len_err = decode_varuint32(buffer, buffer_size, offset, &blob_len, &len_bytes);
    if (len_err.code != IRONCFG_OK) {
        return len_err;
    }

    offset += len_bytes;
    if (offset + blob_len > buffer_size) {
        err.code = IRONCFG_BOUNDS_VIOLATION;
        err.offset = offset;
        return err;
    }

    *out_data = &buffer[offset];
    *out_len = blob_len;
    return err;
}

/* Public API: Get array length */
ironcfg_error_t ironcfg_get_array_length(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    uint32_t* out_length)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_length = 0;

    size_t offset;
    uint8_t type_code;
    ironcfg_error_t find_err = find_value_by_path(buffer, buffer_size, view, path, path_len, &offset, &type_code);
    if (find_err.code != IRONCFG_OK) {
        return find_err;
    }

    if (type_code != 0x30) {
        err.code = IRONCFG_TYPE_MISMATCH;
        err.offset = offset;
        return err;
    }

    uint32_t length;
    size_t len_bytes;
    ironcfg_error_t len_err = decode_varuint32(buffer, buffer_size, offset, &length, &len_bytes);
    if (len_err.code != IRONCFG_OK) {
        return len_err;
    }

    *out_length = length;
    return err;
}

/* Public API: Get object field count */
ironcfg_error_t ironcfg_get_object_field_count(
    const uint8_t* buffer, size_t buffer_size,
    const ironcfg_view_t* view,
    const ironcfg_path_elem_t* path, size_t path_len,
    uint32_t* out_count)
{
    ironcfg_error_t err = { IRONCFG_OK, 0 };
    *out_count = 0;

    size_t offset;
    uint8_t type_code;
    ironcfg_error_t find_err = find_value_by_path(buffer, buffer_size, view, path, path_len, &offset, &type_code);
    if (find_err.code != IRONCFG_OK) {
        return find_err;
    }

    if (type_code != 0x40) {
        err.code = IRONCFG_TYPE_MISMATCH;
        err.offset = offset;
        return err;
    }

    uint32_t count;
    size_t count_bytes;
    ironcfg_error_t count_err = decode_varuint32(buffer, buffer_size, offset, &count, &count_bytes);
    if (count_err.code != IRONCFG_OK) {
        return count_err;
    }

    *out_count = count;
    return err;
}
