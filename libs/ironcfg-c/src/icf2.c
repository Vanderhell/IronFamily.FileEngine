/*
 * icf2.c
 * ICF2 (IronConfig Columnar Format) zero-copy C99 reader implementation
 *
 * Status: Production-ready
 * License: MIT
 */

#include "../include/ironcfg/icf2.h"
#include "../include/ironcfg/ironcfg_common.h"
#include <string.h>

/* ============================================================================
 * VarUInt Decoding (LEB128)
 * ============================================================================ */

/**
 * Decode variable-length unsigned integer (LEB128)
 * Args:
 *   data: Pointer to buffer
 *   offset: Read position
 *   size: Buffer size
 *   out_value: Output value
 *   out_bytes_read: Number of bytes consumed
 * Returns: ICFG_OK or ICFG_ERR_BOUNDS
 */
static icfg_status_t decode_varuint(
    const uint8_t* data,
    size_t offset,
    size_t size,
    uint32_t* out_value,
    size_t* out_bytes_read)
{
    if (offset >= size)
        return ICFG_ERR_BOUNDS;

    uint32_t value = 0;
    size_t shift = 0;
    size_t bytes_read = 0;

    while (bytes_read < 5) {  /* Max 5 bytes for 32-bit value */
        if (offset + bytes_read >= size)
            return ICFG_ERR_BOUNDS;

        uint8_t byte = data[offset + bytes_read];
        bytes_read++;

        value |= (uint32_t)(byte & 0x7F) << shift;

        if ((byte & 0x80) == 0) {
            *out_value = value;
            *out_bytes_read = bytes_read;
            return ICFG_OK;
        }

        shift += 7;
    }

    /* Too many bytes */
    return ICFG_ERR_BOUNDS;
}

/* ============================================================================
 * Core Functions
 * ============================================================================ */

icfg_status_t icf2_open(const uint8_t* data, size_t size, icf2_view_t* out)
{
    if (!data || !out)
        return ICFG_ERR_INVALID_ARGUMENT;

    if (size < ICF2_HEADER_SIZE)
        return ICFG_ERR_BOUNDS;

    memset(out, 0, sizeof(*out));
    out->data = data;
    out->size = size;

    /* ========================================================================
     * Parse Header (64 bytes)
     * ======================================================================== */

    /* Magic (offset 0, 4 bytes) */
    uint32_t magic = icfg_read_u32_le(data, 0);
    if (magic != ICF2_MAGIC)
        return ICFG_ERR_MAGIC;

    /* Version (offset 4, 1 byte) */
    uint8_t version = data[4];
    if (version != ICF2_VERSION)
        return ICFG_ERR_UNSUPPORTED;

    /* Flags (offset 5, 1 byte) */
    uint8_t flags = data[5];
    out->has_crc32 = (flags & 0x01) != 0;
    out->has_blake3 = (flags & 0x02) != 0;
    out->has_prefix_dict = (flags & 0x04) != 0;
    out->has_columns = (flags & 0x08) != 0;

    /* Check reserved bits (4-7) must be 0 */
    if ((flags & 0xF0) != 0)
        return ICFG_ERR_UNSUPPORTED;

    /* Parse offsets and sizes */
    out->file_size = icfg_read_u32_le(data, 8);
    out->prefix_dict_offset = icfg_read_u32_le(data, 12);
    out->prefix_dict_size = icfg_read_u32_le(data, 16);
    out->schema_offset = icfg_read_u32_le(data, 20);
    out->schema_size = icfg_read_u32_le(data, 24);
    out->columns_offset = icfg_read_u32_le(data, 28);
    out->columns_size = icfg_read_u32_le(data, 32);
    out->row_index_offset = icfg_read_u32_le(data, 36);
    out->row_index_size = icfg_read_u32_le(data, 40);
    out->payload_offset = icfg_read_u32_le(data, 44);
    out->payload_size = icfg_read_u32_le(data, 48);
    out->crc_offset = icfg_read_u32_le(data, 52);
    out->blake3_offset = icfg_read_u32_le(data, 56);

    /* ========================================================================
     * Validate Offsets (Bounds Checking)
     * ======================================================================== */

    uint32_t prev_end = ICF2_HEADER_SIZE;

    if (out->has_prefix_dict) {
        if (out->prefix_dict_offset < prev_end)
            return ICFG_ERR_BOUNDS;
        if (out->prefix_dict_offset + out->prefix_dict_size > size)
            return ICFG_ERR_BOUNDS;
        prev_end = out->prefix_dict_offset + out->prefix_dict_size;
    }

    /* Schema is always present */
    if (out->schema_offset < prev_end)
        return ICFG_ERR_BOUNDS;
    if (out->schema_offset + out->schema_size > size)
        return ICFG_ERR_BOUNDS;
    prev_end = out->schema_offset + out->schema_size;

    if (out->has_columns) {
        if (out->columns_offset < prev_end)
            return ICFG_ERR_BOUNDS;
        if (out->columns_offset + out->columns_size > size)
            return ICFG_ERR_BOUNDS;
        prev_end = out->columns_offset + out->columns_size;
    }

    if (out->row_index_size > 0) {
        if (out->row_index_offset < prev_end)
            return ICFG_ERR_BOUNDS;
        if (out->row_index_offset + out->row_index_size > size)
            return ICFG_ERR_BOUNDS;
        prev_end = out->row_index_offset + out->row_index_size;
    }

    if (out->payload_size > 0) {
        if (out->payload_offset < prev_end)
            return ICFG_ERR_BOUNDS;
        if (out->payload_offset + out->payload_size > size)
            return ICFG_ERR_BOUNDS;
        prev_end = out->payload_offset + out->payload_size;
    }

    if (out->has_crc32) {
        if (out->crc_offset < prev_end)
            return ICFG_ERR_BOUNDS;
        if (out->crc_offset + 4 > size)
            return ICFG_ERR_BOUNDS;
        prev_end = out->crc_offset + 4;
    }

    if (out->has_blake3) {
        if (out->blake3_offset < prev_end)
            return ICFG_ERR_BOUNDS;
        if (out->blake3_offset + 32 > size)
            return ICFG_ERR_BOUNDS;
    }

    /* ========================================================================
     * Parse Schema (VarUInt row_count, field_count, then field definitions)
     * ======================================================================== */

    size_t schema_pos = 0;
    uint32_t row_count = 0;
    uint32_t field_count = 0;
    size_t bytes_read = 0;

    /* Read row count */
    icfg_status_t rc = decode_varuint(data, out->schema_offset + schema_pos,
                                       out->schema_offset + out->schema_size,
                                       &row_count, &bytes_read);
    if (rc != ICFG_OK)
        return rc;
    schema_pos += bytes_read;

    /* Read field count */
    rc = decode_varuint(data, out->schema_offset + schema_pos,
                        out->schema_offset + out->schema_size,
                        &field_count, &bytes_read);
    if (rc != ICFG_OK)
        return rc;
    schema_pos += bytes_read;

    out->row_count = row_count;
    out->field_count = field_count;

    return ICFG_OK;
}

icfg_status_t icf2_validate(const icf2_view_t* v)
{
    if (!v || !v->data)
        return ICFG_ERR_INVALID_ARGUMENT;

    /* Validate CRC if present */
    if (v->has_crc32) {
        if (v->crc_offset + 4 > v->size)
            return ICFG_ERR_BOUNDS;

        /* Read stored CRC */
        uint32_t stored_crc = icfg_read_u32_le(v->data, v->crc_offset);

        /* Compute CRC over data (0 to crc_offset) */
        uint32_t computed_crc = icfg_crc32(v->data, v->crc_offset);

        if (stored_crc != computed_crc)
            return ICFG_ERR_CRC;
    }

    /* Validate basic structure */
    if (v->schema_offset + v->schema_size > v->size)
        return ICFG_ERR_BOUNDS;

    if (v->has_columns && (v->columns_offset + v->columns_size > v->size))
        return ICFG_ERR_BOUNDS;

    return ICFG_OK;
}

icfg_status_t icf2_row_count(const icf2_view_t* v, uint32_t* out)
{
    if (!v || !out)
        return ICFG_ERR_INVALID_ARGUMENT;

    *out = v->row_count;
    return ICFG_OK;
}

icfg_status_t icf2_field_count(const icf2_view_t* v, uint32_t* out)
{
    if (!v || !out)
        return ICFG_ERR_INVALID_ARGUMENT;

    *out = v->field_count;
    return ICFG_OK;
}
