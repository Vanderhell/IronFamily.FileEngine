/* IRONCFG C99 - Full validation (strict mode) */

#include "ironcfg/ironcfg.h"
#include <string.h>
#include <stdint.h>

/* CRC32 table for IEEE 802.3 polynomial (0xEDB88320) */
static const uint32_t CRC32_TABLE[256] = {
    0x00000000, 0x77073096, 0xee0e612c, 0x990951ba,
    0x076dc419, 0x706af48f, 0xe963a535, 0x9e6495a3,
    0x0edb8832, 0x79dcb8a4, 0xe0d5e91e, 0x97d2d988,
    0x09b64c2b, 0x7eb17cbd, 0xe7b82d07, 0x90bf1d91,
    0x1db71642, 0x6ab020f2, 0xf3b97148, 0x84be41de,
    0x1adad47d, 0x6ddde4eb, 0xf4d4b551, 0x83d385c7,
    0x136c9856, 0x646ba8c0, 0xfd62f97a, 0x8a65c9ec,
    0x14015c4f, 0x63066cd9, 0xfa44e5d6, 0x8d079fd5,
    0x3b6e20c8, 0x4c69105e, 0xd56041e4, 0xa2677172,
    0x3c03e4d1, 0x4b04d447, 0xd20d85fd, 0xa50ab56b,
    0x35b5a8fa, 0x42b2986c, 0xdbbbc9d6, 0xacbcf940,
    0x32d86ce3, 0x45df5c75, 0xdcd60dcf, 0xabd13d59,
    0x26d930ac, 0x51de003a, 0xc8d75180, 0xbfd06116,
    0x21b4f4b5, 0x56b3c423, 0xcfba9599, 0xb8bda50f,
    0x2802b89e, 0x5f058808, 0xc60cd9b2, 0xb10be924,
    0x2f6f7c87, 0x58684c11, 0xc1611dab, 0xb6662d3d,
    0x76dc4190, 0x01db7106, 0x98d220bc, 0xefd5102a,
    0x71b18589, 0x06b6b51f, 0x9fbfe4a5, 0xe8b8d433,
    0x7807c9a2, 0x0f00f934, 0x9609a88e, 0xe10e9818,
    0x7f6a0dbb, 0x086d3d2d, 0x91646c97, 0xe6635c01,
    0x6b6b51f4, 0x1c6c6162, 0x856534d8, 0xf262004e,
    0x6c0695ed, 0x1b01a57b, 0x8208f4c1, 0xf50fc457,
    0x65b0d9c6, 0x12b7e950, 0x8bbeb8ea, 0xfcb9887c,
    0x62dd1ddf, 0x15da2d49, 0x8cd37cf3, 0xfbd44c65,
    0x4db26158, 0x3ab551ce, 0xa3bc0074, 0xd4bb30e2,
    0x4adfa541, 0x3dd895d7, 0xa4d1c46d, 0xd3d6f4fb,
    0x4369e96a, 0x346ed9fc, 0xad678846, 0xda60b8d0,
    0x44042d73, 0x33031de5, 0xaa0a4c5f, 0xdd0d7cc9,
    0x5005713c, 0x270241aa, 0xbe0b1010, 0xc90c2086,
    0x5a6bda7b, 0x2d6d6a3e, 0xb40ead84, 0xc34a36c4,
    0x5ae39d55, 0x2d6d6a3e, 0xb40ead84, 0xc34a36c4,
    0x5aae09e3, 0x2d6d6a3e, 0xb40ead84, 0xc34a36c4
};

/* Compute CRC32 IEEE 802.3 */
static uint32_t crc32_ieee(const uint8_t *data, size_t len) {
    uint32_t crc = 0xFFFFFFFF;
    for (size_t i = 0; i < len; i++) {
        uint8_t byte = data[i];
        crc = (crc >> 8) ^ CRC32_TABLE[(crc ^ byte) & 0xFF];
    }
    return crc ^ 0xFFFFFFFF;
}

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

ironcfg_error_t ironcfg_validate_strict(const uint8_t *buffer, size_t buffer_size) {
    ironcfg_error_t error = { IRONCFG_OK, 0 };
    ironcfg_view_t view;

    /* Step 1-12: Fast validation */
    error = ironcfg_open(buffer, buffer_size, &view);
    if (error.code != IRONCFG_OK) {
        return error;
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

    uint32_t prev_field_id = UINT32_MAX;

    for (uint32_t i = 0; i < field_count; i++) {
        if (offset >= schema_end) {
            error.code = IRONCFG_TRUNCATED_BLOCK;
            error.offset = schema_offset + view.header.schema_size;
            return error;
        }

        /* Read field ID (varuint) */
        uint32_t field_id = varuint_decode32(buffer, buffer_size, &offset, &error.code);
        if (error.code != IRONCFG_OK) {
            error.offset = offset;
            return error;
        }

        /* Check fieldId ascending (skip for first field) */
        if (i > 0 && field_id <= prev_field_id) {
            error.code = IRONCFG_FIELD_ORDER_VIOLATION;
            error.offset = offset;
            return error;
        }

        /* Read field type (1 byte) */
        if (offset >= buffer_size) {
            error.code = IRONCFG_BOUNDS_VIOLATION;
            error.offset = offset;
            return error;
        }

        uint8_t field_type = buffer[offset];
        offset++;

        /* Field type must be 0-6 for scalar, or >= 0x1C for compound */
        if (field_type <= 6) {
            /* Scalar type - no name, no is_required */
        } else if (field_type >= 0x1C) {
            /* Compound type - read field name */
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

            /* For Array types (0x30), skip element schema validation for now */
            if (field_type == 0x30) {
                /* Element schema would be encoded here recursively */
                /* Skip it for fast validation */
            }
        } else {
            /* Invalid field type (7-27) */
            error.code = IRONCFG_INVALID_SCHEMA;
            error.offset = offset - 1;
            return error;
        }

        prev_field_id = field_id;
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

        const uint8_t *prev_string = NULL;
        size_t prev_string_len = 0;

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
            if (prev_string != NULL) {
                int cmp = memcmp(prev_string, string_data,
                                prev_string_len < string_len ? prev_string_len : string_len);
                if (cmp > 0 || (cmp == 0 && prev_string_len >= string_len)) {
                    error.code = IRONCFG_INVALID_SCHEMA;
                    error.offset = offset;
                    return error;
                }
            }

            prev_string = string_data;
            prev_string_len = string_len;
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

    /* Step 16+: CRC32 validation */
    if ((view.header.flags & 0x01) != 0) {
        if (view.header.crc_offset >= buffer_size || view.header.crc_offset + 4 > buffer_size) {
            error.code = IRONCFG_BOUNDS_VIOLATION;
            error.offset = 36;
            return error;
        }

        uint32_t computed_crc = crc32_ieee(buffer, view.header.crc_offset);
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

    return error;
}
