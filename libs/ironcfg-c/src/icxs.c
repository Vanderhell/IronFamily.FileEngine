/*
 * icxs.c
 * ICXS (IronConfig X Schema) zero-copy C99 reader implementation
 *
 * Status: Production-ready
 * License: MIT
 */

#include "../include/ironcfg/icxs.h"
#include <string.h>

#define ICXS_MAGIC "ICXS"
#define ICXS_HEADER_SIZE 64
#define ICXS_MAGIC_SIZE 4
#define ICXS_SCHEMA_HASH_SIZE 16

/* Field types (must match spec) */
#define ICXS_TYPE_I64 1
#define ICXS_TYPE_U64 2
#define ICXS_TYPE_F64 3
#define ICXS_TYPE_BOOL 4
#define ICXS_TYPE_STR 5

/* Helper: Safe read with bounds check */
static inline bool safe_read_u32_le(const uint8_t* data, size_t size, size_t offset, uint32_t* out) {
    if (offset + 4 > size) return false;
    *out = icfg_read_u32_le(data, offset);
    return true;
}

static inline bool safe_read_u8(const uint8_t* data, size_t size, size_t offset, uint8_t* out) {
    if (offset >= size) return false;
    *out = data[offset];
    return true;
}

static inline bool safe_read_bytes(const uint8_t* data, size_t size, size_t offset, size_t len,
                                   const uint8_t** out_ptr) {
    if (offset + len > size) return false;
    *out_ptr = &data[offset];
    return true;
}

/* Parse varint u32 from buffer, return bytes consumed (0 on error) */
static size_t parse_varint_u32(const uint8_t* data, size_t offset, size_t size, uint32_t* out) {
    uint32_t value = 0;
    size_t shift = 0;
    size_t bytes_consumed = 0;

    while (offset < size && bytes_consumed < 5) {
        uint8_t byte = data[offset++];
        bytes_consumed++;
        value |= ((uint32_t)(byte & 0x7F)) << shift;
        if ((byte & 0x80) == 0) {
            *out = value;
            return bytes_consumed;
        }
        shift += 7;
    }

    return 0; /* Error: varint too long or incomplete */
}

/* ============================================================================
 * Core API
 * ============================================================================ */

icfg_status_t icxs_open(const uint8_t* data, size_t size, icxs_view_t* out) {
    if (!data || !out || size < ICXS_HEADER_SIZE) {
        return ICFG_ERR_BOUNDS;
    }

    /* Validate magic */
    if (memcmp(data, ICXS_MAGIC, ICXS_MAGIC_SIZE) != 0) {
        return ICFG_ERR_MAGIC;
    }

    /* Check version */
    if (data[4] != 0) {
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Parse header */
    uint8_t flags = data[5];
    bool has_crc = (flags & 0x01) != 0;

    uint32_t schema_block_offset = icfg_read_u32_le(data, 24);
    uint32_t data_block_offset = icfg_read_u32_le(data, 28);
    uint32_t crc_offset = icfg_read_u32_le(data, 32);

    /* Validate offsets */
    if (schema_block_offset < ICXS_HEADER_SIZE || schema_block_offset >= size ||
        data_block_offset < schema_block_offset || data_block_offset >= size) {
        return ICFG_ERR_BOUNDS;
    }

    if (has_crc && (crc_offset == 0 || crc_offset >= size || crc_offset + 4 > size)) {
        return ICFG_ERR_BOUNDS;
    }

    /* Parse schema block (field count as varint) */
    size_t field_count_len = parse_varint_u32(data, schema_block_offset, size, &out->field_count);
    if (field_count_len == 0) {
        return ICFG_ERR_BOUNDS;
    }
    uint32_t schema_fields_offset = schema_block_offset + field_count_len;

    /* Parse data block header */
    if (!safe_read_u32_le(data, size, data_block_offset, &out->record_count)) {
        return ICFG_ERR_BOUNDS;
    }

    if (!safe_read_u32_le(data, size, data_block_offset + 4, &out->record_stride)) {
        return ICFG_ERR_BOUNDS;
    }

    /* Validate record stride safety */
    if (out->record_stride == 0 ||
        out->record_stride > 1000000 || /* Sanity limit */
        out->record_count > 1000000) {    /* Sanity limit */
        return ICFG_ERR_BOUNDS;
    }

    /* Check fixed region bounds */
    uint32_t fixed_region_size = out->record_count * out->record_stride;
    uint32_t fixed_region_offset = data_block_offset + 8;
    if (fixed_region_offset + fixed_region_size > size) {
        return ICFG_ERR_BOUNDS;
    }

    /* Populate view */
    out->data = data;
    out->size = size;
    out->header_size = ICXS_HEADER_SIZE;
    out->schema_block_offset = schema_block_offset;
    out->schema_fields_offset = schema_fields_offset;
    out->data_block_offset = data_block_offset;
    out->crc_offset = crc_offset;
    out->has_crc = has_crc;

    return ICFG_OK;
}

icfg_status_t icxs_validate(const icxs_view_t* v) {
    if (!v || !v->data) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    /* Check CRC if enabled */
    if (v->has_crc && v->crc_offset > 0) {
        /* Read stored CRC */
        if (v->crc_offset + 4 > v->size) {
            return ICFG_ERR_BOUNDS;
        }

        uint32_t stored_crc = icfg_read_u32_le(v->data, v->crc_offset);

        /* Compute CRC over [0 .. crc_offset) */
        uint32_t computed_crc = icfg_crc32(v->data, v->crc_offset);

        if (computed_crc != stored_crc) {
            return ICFG_ERR_CRC;
        }
    }

    return ICFG_OK;
}

icfg_status_t icxs_record_count(const icxs_view_t* v, uint32_t* out) {
    if (!v || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }
    *out = v->record_count;
    return ICFG_OK;
}

icfg_status_t icxs_get_record(const icxs_view_t* v, uint32_t index, icxs_record_t* out_rec) {
    if (!v || !out_rec) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    if (index >= v->record_count) {
        return ICFG_ERR_RANGE;
    }

    out_rec->view = v;
    out_rec->index = index;

    return ICFG_OK;
}

/* ============================================================================
 * Schema Lookup (Internal)
 * ============================================================================ */

static icfg_status_t find_field(const icxs_view_t* v, uint32_t field_id, icxs_field_t* out_field) {
    /* Parse schema block */
    const uint8_t* data = v->data;
    size_t size = v->size;
    size_t offset = v->schema_fields_offset;  /* Start at actual field definitions (after varint field_count) */

    uint32_t field_offset_accum = 0;

    for (uint32_t i = 0; i < v->field_count; i++) {
        if (!safe_read_u32_le(data, size, offset, &out_field->id)) {
            return ICFG_ERR_BOUNDS;
        }
        offset += 4;

        if (!safe_read_u8(data, size, offset, &out_field->type)) {
            return ICFG_ERR_BOUNDS;
        }
        offset += 1;

        /* Check type is valid */
        if (out_field->type < 1 || out_field->type > 5) {
            return ICFG_ERR_SCHEMA;
        }

        out_field->offset = field_offset_accum;

        /* Update accumulator based on field type */
        switch (out_field->type) {
            case ICXS_TYPE_I64:
            case ICXS_TYPE_U64:
            case ICXS_TYPE_F64:
                field_offset_accum += 8;
                break;
            case ICXS_TYPE_BOOL:
                field_offset_accum += 1;
                break;
            case ICXS_TYPE_STR:
                field_offset_accum += 4;
                break;
            default:
                return ICFG_ERR_SCHEMA;
        }

        if (out_field->id == field_id) {
            return ICFG_OK;
        }
    }

    return ICFG_ERR_RANGE; /* Field not found */
}

/* ============================================================================
 * Field Access
 * ============================================================================ */

icfg_status_t icxs_get_i64(const icxs_record_t* r, uint32_t field_id, int64_t* out) {
    if (!r || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    icxs_field_t field;
    icfg_status_t status = find_field(r->view, field_id, &field);
    if (status != ICFG_OK) {
        return status;
    }

    if (field.type != ICXS_TYPE_I64) {
        return ICFG_ERR_TYPE;
    }

    /* Read field from fixed region */
    uint32_t fixed_region_offset = r->view->data_block_offset + 8;
    uint32_t field_addr = fixed_region_offset + r->index * r->view->record_stride + field.offset;

    if (field_addr + 8 > r->view->size) {
        return ICFG_ERR_BOUNDS;
    }

    *out = icfg_read_i64_le(r->view->data, field_addr);
    return ICFG_OK;
}

icfg_status_t icxs_get_u64(const icxs_record_t* r, uint32_t field_id, uint64_t* out) {
    if (!r || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    icxs_field_t field;
    icfg_status_t status = find_field(r->view, field_id, &field);
    if (status != ICFG_OK) {
        return status;
    }

    if (field.type != ICXS_TYPE_U64) {
        return ICFG_ERR_TYPE;
    }

    uint32_t fixed_region_offset = r->view->data_block_offset + 8;
    uint32_t field_addr = fixed_region_offset + r->index * r->view->record_stride + field.offset;

    if (field_addr + 8 > r->view->size) {
        return ICFG_ERR_BOUNDS;
    }

    *out = icfg_read_u64_le(r->view->data, field_addr);
    return ICFG_OK;
}

icfg_status_t icxs_get_f64(const icxs_record_t* r, uint32_t field_id, double* out) {
    if (!r || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    icxs_field_t field;
    icfg_status_t status = find_field(r->view, field_id, &field);
    if (status != ICFG_OK) {
        return status;
    }

    if (field.type != ICXS_TYPE_F64) {
        return ICFG_ERR_TYPE;
    }

    uint32_t fixed_region_offset = r->view->data_block_offset + 8;
    uint32_t field_addr = fixed_region_offset + r->index * r->view->record_stride + field.offset;

    if (field_addr + 8 > r->view->size) {
        return ICFG_ERR_BOUNDS;
    }

    *out = icfg_read_f64_le(r->view->data, field_addr);
    return ICFG_OK;
}

icfg_status_t icxs_get_bool(const icxs_record_t* r, uint32_t field_id, bool* out) {
    if (!r || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    icxs_field_t field;
    icfg_status_t status = find_field(r->view, field_id, &field);
    if (status != ICFG_OK) {
        return status;
    }

    if (field.type != ICXS_TYPE_BOOL) {
        return ICFG_ERR_TYPE;
    }

    uint32_t fixed_region_offset = r->view->data_block_offset + 8;
    uint32_t field_addr = fixed_region_offset + r->index * r->view->record_stride + field.offset;

    if (field_addr >= r->view->size) {
        return ICFG_ERR_BOUNDS;
    }

    *out = r->view->data[field_addr] != 0;
    return ICFG_OK;
}

icfg_status_t icxs_get_str(const icxs_record_t* r, uint32_t field_id,
                           const uint8_t** out_ptr, uint32_t* out_len) {
    if (!r) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    icxs_field_t field;
    icfg_status_t status = find_field(r->view, field_id, &field);
    if (status != ICFG_OK) {
        return status;
    }

    if (field.type != ICXS_TYPE_STR) {
        return ICFG_ERR_TYPE;
    }

    /* Read string offset from fixed region */
    uint32_t fixed_region_offset = r->view->data_block_offset + 8;
    uint32_t field_addr = fixed_region_offset + r->index * r->view->record_stride + field.offset;

    if (field_addr + 4 > r->view->size) {
        return ICFG_ERR_BOUNDS;
    }

    uint32_t str_offset = icfg_read_u32_le(r->view->data, field_addr);

    /* Find variable region offset (right after fixed region) */
    uint32_t var_region_offset = fixed_region_offset + r->view->record_count * r->view->record_stride;

    /* Read string length */
    uint32_t str_addr = var_region_offset + str_offset;
    if (str_addr + 4 > r->view->size) {
        return ICFG_ERR_BOUNDS;
    }

    uint32_t str_len = icfg_read_u32_le(r->view->data, str_addr);

    /* Validate string bounds */
    if (str_addr + 4 + str_len > r->view->size) {
        return ICFG_ERR_BOUNDS;
    }

    if (out_ptr) {
        *out_ptr = &r->view->data[str_addr + 4];
    }

    if (out_len) {
        *out_len = str_len;
    }

    return ICFG_OK;
}

icfg_status_t icxs_schema_get_field(const icxs_view_t* v, uint32_t field_id, icxs_field_t* out) {
    if (!v || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    return find_field(v, field_id, out);
}

icfg_status_t icxs_schema_hash(const icxs_view_t* v, uint8_t* out) {
    if (!v || !out) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    memcpy(out, &v->data[8], ICXS_SCHEMA_HASH_SIZE);
    return ICFG_OK;
}
