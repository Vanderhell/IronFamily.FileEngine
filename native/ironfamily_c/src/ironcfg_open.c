/* IRONCFG C99 - File open and header validation */

#include "ironcfg/ironcfg.h"
#include <string.h>

/* Little-endian byte reading */
static uint32_t read_le32(const uint8_t *data) {
    return ((uint32_t)data[0] << 0) |
           ((uint32_t)data[1] << 8) |
           ((uint32_t)data[2] << 16) |
           ((uint32_t)data[3] << 24);
}

static uint16_t read_le16(const uint8_t *data) {
    return ((uint16_t)data[0] << 0) |
           ((uint16_t)data[1] << 8);
}

/* Validation helpers */
static bool is_offset_valid(uint32_t offset, uint32_t size, uint32_t file_size) {
    /* Check: 0 < offset */
    if (offset == 0) return false;

    /* Check: offset < file_size */
    if (offset > file_size) return false;

    /* Check: size > 0 */
    if (size == 0) return false;

    /* Check: offset + size <= file_size (no overflow) */
    if (size > file_size - offset) return false;

    return true;
}

static bool offsets_monotonic(uint32_t off1, uint32_t size1,
                              uint32_t off2, uint32_t size2,
                              uint32_t off3, uint32_t size3,
                              uint32_t off4, uint32_t size4) {
    uint32_t end1 = off1 + size1;
    uint32_t end2 = (off2 > 0) ? (off2 + size2) : end1;
    uint32_t end3 = (off3 > 0) ? (off3 + size3) : end2;
    uint32_t end4 = (off4 > 0) ? (off4 + size4) : end3;

    return end1 <= off2 && off2 <= end2 &&
           end2 <= off3 && off3 <= end3 &&
           end3 <= off4 && off4 <= end4;
}

ironcfg_error_t ironcfg_open(const uint8_t *buffer, size_t buffer_size,
                             ironcfg_view_t *out_view) {
    ironcfg_error_t error = { IRONCFG_OK, 0 };

    /* Step 1: File size >= 64 bytes */
    if (buffer_size < 64) {
        error.code = IRONCFG_TRUNCATED_FILE;
        error.offset = 0;
        return error;
    }

    /* Step 2: Magic = "ICFG" */
    uint32_t magic = read_le32(buffer + 0);
    if (magic != IRONCFG_MAGIC) {
        error.code = IRONCFG_INVALID_MAGIC;
        error.offset = 0;
        return error;
    }

    /* Step 3: Version = 1 or 2 (backward compatible) */
    uint8_t version = buffer[4];
    if (version != 1 && version != 2) {
        error.code = IRONCFG_INVALID_VERSION;
        error.offset = 4;
        return error;
    }

    /* Step 4: Flags bits 3-7 = 0 */
    uint8_t flags = buffer[5];
    if (flags & 0xF8) {  /* bits 3-7 */
        error.code = IRONCFG_INVALID_FLAGS;
        error.offset = 5;
        return error;
    }

    /* Step 5: reserved0 = 0x0000 */
    uint16_t reserved0 = read_le16(buffer + 6);
    if (reserved0 != 0) {
        error.code = IRONCFG_RESERVED_FIELD_NONZERO;
        error.offset = 6;
        return error;
    }

    /* Read all offsets and sizes */
    uint32_t file_size = read_le32(buffer + 8);
    uint32_t schema_offset = read_le32(buffer + 12);
    uint32_t schema_size = read_le32(buffer + 16);
    uint32_t string_pool_offset = read_le32(buffer + 20);
    uint32_t string_pool_size = read_le32(buffer + 24);
    uint32_t data_offset = read_le32(buffer + 28);
    uint32_t data_size = read_le32(buffer + 32);
    uint32_t crc_offset = read_le32(buffer + 36);
    uint32_t blake3_offset = read_le32(buffer + 40);
    uint32_t reserved1 = read_le32(buffer + 44);

    /* Step 6: reserved1 = 0x00000000 */
    if (reserved1 != 0) {
        error.code = IRONCFG_RESERVED_FIELD_NONZERO;
        error.offset = 44;
        return error;
    }

    /* Step 7: reserved2 = all 0x00 */
    for (int i = 0; i < 16; i++) {
        if (buffer[48 + i] != 0) {
            error.code = IRONCFG_RESERVED_FIELD_NONZERO;
            error.offset = 48;
            return error;
        }
    }

    /* Step 8: CRC flag ↔ crcOffset consistency */
    bool crc_flag = (flags & 0x01) != 0;
    if (crc_flag && crc_offset == 0) {
        error.code = IRONCFG_FLAG_MISMATCH;
        error.offset = 5;
        return error;
    }
    if (!crc_flag && crc_offset != 0) {
        error.code = IRONCFG_FLAG_MISMATCH;
        error.offset = 5;
        return error;
    }

    /* Step 9: BLAKE3 flag ↔ blake3Offset consistency */
    bool blake3_flag = (flags & 0x02) != 0;
    if (blake3_flag && blake3_offset == 0) {
        error.code = IRONCFG_FLAG_MISMATCH;
        error.offset = 5;
        return error;
    }
    if (!blake3_flag && blake3_offset != 0) {
        error.code = IRONCFG_FLAG_MISMATCH;
        error.offset = 5;
        return error;
    }

    /* Step 10: Offset monotonicity */
    uint32_t expected_file_size = data_offset + data_size;
    if (crc_flag) expected_file_size += 4;
    if (blake3_flag) expected_file_size += 32;

    /* Check ordering: schema < schema+size <= pool < pool+size <= data < data+size <= crc <= blake3 <= file_end */
    if (schema_offset == 0 || schema_size == 0) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 12;
        return error;
    }

    if (data_offset == 0 || data_size == 0) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 28;
        return error;
    }

    uint32_t schema_end = schema_offset + schema_size;
    uint32_t pool_start = (string_pool_offset > 0) ? string_pool_offset : data_offset;
    uint32_t pool_end = (string_pool_offset > 0) ? (string_pool_offset + string_pool_size) : data_offset;

    if (!(schema_offset < schema_end && schema_end <= pool_start &&
          pool_start <= pool_end && pool_end <= data_offset &&
          data_offset < data_offset + data_size)) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 12;
        return error;
    }

    if (crc_offset > 0 && !(data_offset + data_size <= crc_offset)) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 36;
        return error;
    }

    if (blake3_offset > 0 && !(crc_offset > 0 ? (crc_offset + 4 <= blake3_offset) : (data_offset + data_size <= blake3_offset))) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 40;
        return error;
    }

    /* Step 11: File size match */
    if (file_size != expected_file_size) {
        error.code = IRONCFG_BOUNDS_VIOLATION;
        error.offset = 8;
        return error;
    }

    if (buffer_size != file_size) {
        error.code = IRONCFG_TRUNCATED_FILE;
        error.offset = 0;
        return error;
    }

    /* All checks passed, populate view */
    out_view->buffer = buffer;
    out_view->buffer_size = buffer_size;
    out_view->header.magic = magic;
    out_view->header.version = version;
    out_view->header.flags = flags;
    out_view->header.reserved0 = reserved0;
    out_view->header.file_size = file_size;
    out_view->header.schema_offset = schema_offset;
    out_view->header.schema_size = schema_size;
    out_view->header.string_pool_offset = string_pool_offset;
    out_view->header.string_pool_size = string_pool_size;
    out_view->header.data_offset = data_offset;
    out_view->header.data_size = data_size;
    out_view->header.crc_offset = crc_offset;
    out_view->header.blake3_offset = blake3_offset;
    out_view->header.reserved1 = reserved1;
    memcpy(out_view->header.reserved2, buffer + 48, 16);

    return error;
}

ironcfg_error_t ironcfg_validate_fast(const uint8_t *buffer, size_t buffer_size) {
    ironcfg_view_t view;
    return ironcfg_open(buffer, buffer_size, &view);
}
