/* IRONCFG C99 - Full validation (strict mode) */

#include "ironcfg/ironcfg.h"
#include "ironcfg/ironcfg_common.h"
#include <string.h>
#include <stdint.h>

/* VarUInt decoder (returns value, updates offset, sets error on invalid) */
static uint32_t varuint_decode32(const uint8_t *buffer, size_t buffer_size,
                                  size_t *offset, ironcfg_error_code_t *error) {
    uint32_t result = 0;
    uint32_t shift = 0;
    int byte_count = 0;

    while (*offset < buffer_size && byte_count < 5) {
        uint8_t byte = buffer[*offset];
        (*offset)++;
        byte_count++;

        if (shift >= 32) {
            *error = IRONCFG_NON_MINIMAL_VARUINT;
            return 0;
        }

        uint32_t mask = ((uint32_t)(byte & 0x7F)) << shift;
        if (result > UINT32_MAX - mask) {
            *error = IRONCFG_ARITHMETIC_OVERFLOW;
            return 0;
        }
        result += mask;

        if ((byte & 0x80) == 0) {
            *error = IRONCFG_OK;
            return result;
        }
        shift += 7;
    }

    *error = IRONCFG_BOUNDS_VIOLATION;
    return 0;
}

/* UTF-8 validation */
static bool is_valid_utf8(const uint8_t *data, size_t len) {
    size_t i = 0;
    while (i < len) {
        uint8_t byte = data[i];
        if ((byte & 0x80) == 0) {
            /* Single-byte: 0xxxxxxx */
            i++;
        } else if ((byte & 0xE0) == 0xC0) {
            /* Two-byte: 110xxxxx 10xxxxxx */
            if (i + 1 >= len || (data[i + 1] & 0xC0) != 0x80) return false;
            i += 2;
        } else if ((byte & 0xF0) == 0xE0) {
            /* Three-byte: 1110xxxx 10xxxxxx 10xxxxxx */
            if (i + 2 >= len || (data[i + 1] & 0xC0) != 0x80 ||
                (data[i + 2] & 0xC0) != 0x80) return false;
            i += 3;
        } else if ((byte & 0xF8) == 0xF0) {
            /* Four-byte: 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx */
            if (i + 3 >= len || (data[i + 1] & 0xC0) != 0x80 ||
                (data[i + 2] & 0xC0) != 0x80 || (data[i + 3] & 0xC0) != 0x80) return false;
            i += 4;
        } else {
            /* Invalid start byte */
            return false;
        }
    }
    return true;
}

/* Check if float is NaN (bit-level check) */
static bool is_nan_float(const uint8_t *data) {
    uint32_t bits_lo = ((uint32_t)data[0]) | (((uint32_t)data[1]) << 8) |
                        (((uint32_t)data[2]) << 16) | (((uint32_t)data[3]) << 24);
    uint32_t bits_hi = ((uint32_t)data[4]) | (((uint32_t)data[5]) << 8) |
                        (((uint32_t)data[6]) << 16) | (((uint32_t)data[7]) << 24);
    uint64_t bits = ((uint64_t)bits_hi << 32) | bits_lo;

    uint32_t exponent = (bits >> 52) & 0x7FF;
    uint64_t mantissa = bits & 0xFFFFFFFFFFFFFULL;

    return (exponent == 0x7FF) && (mantissa != 0);
}

static bool is_valid_schema_field_type(uint8_t field_type) {
    switch (field_type) {
        case 0x00: /* null */
        case 0x01: /* bool false */
        case 0x02: /* bool true */
        case 0x10: /* i64 */
        case 0x11: /* u64 */
        case 0x12: /* f64 */
        case 0x20: /* string inline */
        case 0x21: /* string id */
        case 0x22: /* bytes */
        case 0x30: /* array */
        case 0x40: /* object */
            return true;
        default:
            return false;
    }
}

ironcfg_error_t ironcfg_validate_strict(const uint8_t *buffer, size_t buffer_size) {
    ironcfg_error_t error = { IRONCFG_OK, 0 };
    ironcfg_view_t view;

    /* Step 1-12: Fast validation */
    error = ironcfg_open(buffer, buffer_size, &view);
    if (error.code != IRONCFG_OK) {
        return error;
    }

    /* Validate CRC32 early so payload corruption reports CRC32_MISMATCH first. */
    if ((view.header.flags & 0x01) != 0) {
        if (view.header.crc_offset >= buffer_size || view.header.crc_offset + 4 > buffer_size) {
            error.code = IRONCFG_BOUNDS_VIOLATION;
            error.offset = 36;
            return error;
        }

        uint32_t computed_crc = icfg_crc32(buffer, view.header.crc_offset);
        uint32_t stored_crc = ((uint32_t)buffer[view.header.crc_offset]) |
                              (((uint32_t)buffer[view.header.crc_offset + 1]) << 8) |
                              (((uint32_t)buffer[view.header.crc_offset + 2]) << 16) |
                              (((uint32_t)buffer[view.header.crc_offset + 3]) << 24);
        if (computed_crc != stored_crc) {
            error.code = IRONCFG_CRC32_MISMATCH;
            error.offset = view.header.crc_offset;
            return error;
        }
    }

    /* Step 13+: Schema validation */
    size_t schema_offset = view.header.schema_offset;
    size_t schema_end = schema_offset + view.header.schema_size;

    if (schema_offset >= buffer_size || schema_end > buffer_size) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 16;
        return error;
    }

    size_t offset = schema_offset;
    uint32_t field_count = varuint_decode32(buffer, buffer_size, &offset, &error.code);
    if (error.code != IRONCFG_OK) {
        error.offset = schema_offset;
        return error;
    }

    if (field_count > 65536) {
        error.code = IRONCFG_LIMIT_EXCEEDED;
        error.offset = schema_offset;
        return error;
    }

    uint32_t prev_field_id = 0;
    const uint8_t *prev_field_name = NULL;
    size_t prev_field_name_len = 0;

    for (uint32_t i = 0; i < field_count; i++) {
        if (offset >= schema_end) {
            error.code = IRONCFG_TRUNCATED_BLOCK;
            error.offset = schema_offset + view.header.schema_size;
            return error;
        }

        uint32_t field_id = varuint_decode32(buffer, buffer_size, &offset, &error.code);
        if (error.code != IRONCFG_OK) {
            error.offset = offset;
            return error;
        }

        /* Check fieldId ascending */
        if (i > 0 && field_id < prev_field_id) {
            error.code = IRONCFG_FIELD_ORDER_VIOLATION;
            error.offset = offset;
            return error;
        }

        uint32_t field_name_len = varuint_decode32(buffer, buffer_size, &offset, &error.code);
        if (error.code != IRONCFG_OK) {
            error.offset = offset;
            return error;
        }

        if (field_name_len > 16 * 1024 * 1024) {
            error.code = IRONCFG_LIMIT_EXCEEDED;
            error.offset = offset;
            return error;
        }

        if (offset + field_name_len > buffer_size) {
            error.code = IRONCFG_BOUNDS_VIOLATION;
            error.offset = offset;
            return error;
        }

        const uint8_t *field_name = buffer + offset;
        offset += field_name_len;

        /* Validate UTF-8 */
        if (!is_valid_utf8(field_name, field_name_len)) {
            error.code = IRONCFG_INVALID_STRING;
            error.offset = offset;
            return error;
        }

        /* Check field names sorted lexicographically */
        if (prev_field_name != NULL) {
            int cmp = memcmp(prev_field_name, field_name,
                            prev_field_name_len < field_name_len ? prev_field_name_len : field_name_len);
            if (cmp > 0 || (cmp == 0 && prev_field_name_len >= field_name_len)) {
                error.code = IRONCFG_INVALID_SCHEMA;
                error.offset = offset;
                return error;
            }
        }

        if (offset >= buffer_size) {
            error.code = IRONCFG_BOUNDS_VIOLATION;
            error.offset = offset;
            return error;
        }

        uint8_t field_type = buffer[offset];
        offset++;

        if (!is_valid_schema_field_type(field_type)) {
            error.code = IRONCFG_INVALID_SCHEMA;
            error.offset = offset - 1;
            return error;
        }

        if (offset >= buffer_size) {
            error.code = IRONCFG_BOUNDS_VIOLATION;
            error.offset = offset;
            return error;
        }

        uint8_t is_required = buffer[offset];
        offset++;

        if (is_required != 0x00 && is_required != 0x01) {
            error.code = IRONCFG_INVALID_SCHEMA;
            error.offset = offset - 1;
            return error;
        }

        prev_field_id = field_id;
        prev_field_name = field_name;
        prev_field_name_len = field_name_len;
    }

    /* Step 14+: String pool validation (if present) */
    if (view.header.string_pool_offset > 0) {
        size_t pool_offset = view.header.string_pool_offset;
        size_t pool_end = pool_offset + view.header.string_pool_size;

        if (pool_offset >= buffer_size || pool_end > buffer_size) {
            error.code = IRONCFG_BOUNDS_VIOLATION;
            error.offset = 20;
            return error;
        }

        offset = pool_offset;
        uint32_t string_count = varuint_decode32(buffer, buffer_size, &offset, &error.code);
        if (error.code != IRONCFG_OK) {
            error.offset = pool_offset;
            return error;
        }

        if (string_count > 1000000) {
            error.code = IRONCFG_LIMIT_EXCEEDED;
            error.offset = pool_offset;
            return error;
        }

        prev_field_name = NULL;
        prev_field_name_len = 0;

        for (uint32_t i = 0; i < string_count; i++) {
            if (offset >= pool_end) {
                error.code = IRONCFG_TRUNCATED_BLOCK;
                error.offset = pool_end;
                return error;
            }

            uint32_t string_len = varuint_decode32(buffer, buffer_size, &offset, &error.code);
            if (error.code != IRONCFG_OK) {
                error.offset = offset;
                return error;
            }

            if (string_len > 16 * 1024 * 1024) {
                error.code = IRONCFG_LIMIT_EXCEEDED;
                error.offset = offset;
                return error;
            }

            if (offset + string_len > buffer_size) {
                error.code = IRONCFG_BOUNDS_VIOLATION;
                error.offset = offset;
                return error;
            }

            const uint8_t *string_data = buffer + offset;
            offset += string_len;

            /* Validate UTF-8 */
            if (!is_valid_utf8(string_data, string_len)) {
                error.code = IRONCFG_INVALID_STRING;
                error.offset = offset;
                return error;
            }

            /* Check strings sorted lexicographically */
            if (prev_field_name != NULL) {
                int cmp = memcmp(prev_field_name, string_data,
                                prev_field_name_len < string_len ? prev_field_name_len : string_len);
                if (cmp > 0 || (cmp == 0 && prev_field_name_len >= string_len)) {
                    error.code = IRONCFG_INVALID_SCHEMA;
                    error.offset = offset;
                    return error;
                }
            }

            prev_field_name = string_data;
            prev_field_name_len = string_len;
        }
    }

    /* Step 15+: Data block basic validation */
    size_t data_offset = view.header.data_offset;
    size_t data_end = data_offset + view.header.data_size;

    if (data_offset >= buffer_size || data_end > buffer_size) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 28;
        return error;
    }

    if (data_end < data_offset) {
        error.code = IRONCFG_ARITHMETIC_OVERFLOW;
        error.offset = 28;
        return error;
    }

    /* Root must be object type (0x40) */
    if (buffer[data_offset] != 0x40) {
        error.code = IRONCFG_INVALID_TYPE_CODE;
        error.offset = data_offset;
        return error;
    }

    return error;
}
