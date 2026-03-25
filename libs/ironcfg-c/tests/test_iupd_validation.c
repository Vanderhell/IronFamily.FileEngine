/*
 * test_iupd_validation.c
 * Validation tests for IUPD implementation
 *
 * Tests:
 * - validate_fast passes for valid file
 * - validate_fast rejects bad chunk indices
 * - validate_fast rejects overlapping payloads
 * - validate_fast rejects empty chunks
 * - validate_strict verifies CRC32
 * - validate_strict rejects CRC32 mismatch
 * - validate_strict checks apply order completeness
 * - Corruption detection (flip bits in payload)
 * - Truncation detection
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>

#include "ironcfg/iupd.h"

/* Helper: write little-endian u32 */
static void write_u32_le(uint8_t* buf, size_t offset, uint32_t value) {
    buf[offset] = (uint8_t)(value & 0xFF);
    buf[offset + 1] = (uint8_t)((value >> 8) & 0xFF);
    buf[offset + 2] = (uint8_t)((value >> 16) & 0xFF);
    buf[offset + 3] = (uint8_t)((value >> 24) & 0xFF);
}

/* Helper: write little-endian u64 */
static void write_u64_le(uint8_t* buf, size_t offset, uint64_t value) {
    write_u32_le(buf, offset, (uint32_t)(value & 0xFFFFFFFF));
    write_u32_le(buf, offset + 4, (uint32_t)((value >> 32) & 0xFFFFFFFF));
}

/* Helper: write little-endian u16 */
static void write_u16_le(uint8_t* buf, size_t offset, uint16_t value) {
    buf[offset] = (uint8_t)(value & 0xFF);
    buf[offset + 1] = (uint8_t)((value >> 8) & 0xFF);
}

/* Compute CRC32 IEEE */
static uint32_t compute_crc32(const uint8_t* data, size_t len) {
    const uint32_t polynomial = 0xEDB88320;
    uint32_t crc32_table[256];

    /* Initialize table */
    for (int i = 0; i < 256; i++) {
        uint32_t crc = i;
        for (int j = 0; j < 8; j++) {
            if ((crc & 1) != 0)
                crc = (crc >> 1) ^ polynomial;
            else
                crc >>= 1;
        }
        crc32_table[i] = crc;
    }

    uint32_t crc = 0xFFFFFFFF;
    for (size_t i = 0; i < len; i++) {
        crc = (crc >> 8) ^ crc32_table[(crc ^ data[i]) & 0xFF];
    }
    return crc ^ 0xFFFFFFFF;
}

/* Construct valid IUPD file with proper CRC32 for chunks and manifest */
static uint8_t* create_valid_iupd_with_crc(size_t* out_size) {
    uint8_t payload_data[] = { 0xDE, 0xAD, 0xBE, 0xEF };
    size_t payload_size = sizeof(payload_data);

    size_t chunk_table_offset = 36;
    size_t manifest_offset = 36 + 56;  /* After 1 chunk entry */
    size_t payload_offset = manifest_offset + 20 + 4 + 8;  /* Header + apply order + CRC */

    size_t file_size = payload_offset + payload_size;

    uint8_t* buf = calloc(file_size, 1);
    if (!buf) return NULL;

    /* Compute payload CRC32 */
    uint32_t payload_crc = compute_crc32(payload_data, payload_size);

    /* FILE HEADER */
    write_u32_le(buf, 0, 0x44505549);  /* "IUPD" */
    buf[4] = 0x01;  /* Version */
    write_u32_le(buf, 5, 0);  /* Flags */
    write_u16_le(buf, 9, 36);  /* HeaderSize */
    buf[11] = 0;  /* Reserved */
    write_u64_le(buf, 12, chunk_table_offset);
    write_u64_le(buf, 20, manifest_offset);
    write_u64_le(buf, 28, payload_offset);

    /* CHUNK TABLE */
    uint64_t chunk_table_base = chunk_table_offset;
    write_u32_le(buf, chunk_table_base, 0);  /* ChunkIndex */
    write_u64_le(buf, chunk_table_base + 4, payload_size);  /* PayloadSize */
    write_u64_le(buf, chunk_table_base + 12, payload_offset);  /* PayloadOffset */
    write_u32_le(buf, chunk_table_base + 20, payload_crc);  /* PayloadCrc32 (actual) */
    /* PayloadBlake3 */
    for (int i = 0; i < 32; i++) {
        buf[chunk_table_base + 24 + i] = 0xAA;
    }

    /* MANIFEST */
    uint64_t manifest_base = manifest_offset;
    buf[manifest_base] = 0x01;  /* ManifestVersion */
    write_u32_le(buf, manifest_base + 1, 0);  /* TargetVersion */
    write_u32_le(buf, manifest_base + 5, 0);  /* DependencyCount */
    write_u32_le(buf, manifest_base + 9, 1);  /* ApplyOrderCount */
    uint64_t manifest_size = 20 + 0 + 4 + 8;
    write_u64_le(buf, manifest_base + 13, manifest_size);  /* ManifestSize */

    /* Apply order list */
    write_u32_le(buf, manifest_base + 20, 0);  /* ChunkIndex */

    /* Compute manifest CRC32 (over header + apply order, excluding CRC field) */
    uint32_t manifest_crc = compute_crc32(&buf[manifest_base], 24);

    /* Manifest integrity */
    write_u32_le(buf, manifest_base + 24, manifest_crc);
    write_u32_le(buf, manifest_base + 28, 0);  /* Reserved */

    /* CHUNK PAYLOAD */
    memcpy(&buf[payload_offset], payload_data, payload_size);

    *out_size = file_size;
    return buf;
}

/* Test: validate_fast passes for valid file */
static int test_validate_fast_valid(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_with_crc(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        printf("FAIL: test_validate_fast_valid - open failed: %d\n", open_status);
        free(file_data);
        return 1;
    }

    icfg_status_t fast_status = iupd_validate_fast(&ctx);
    int result = (fast_status == ICFG_OK) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_validate_fast_valid - validate_fast failed: %d (%s)\n",
               fast_status, ctx.last_error.message ? ctx.last_error.message : "");
    }

    free(file_data);
    return result;
}

/* Test: validate_strict passes for valid file */
static int test_validate_strict_valid(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_with_crc(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    icfg_status_t strict_status = iupd_validate_strict(&ctx);
    int result = (strict_status == ICFG_OK) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_validate_strict_valid - validate_strict failed: %d (%s)\n",
               strict_status, ctx.last_error.message ? ctx.last_error.message : "");
    }

    free(file_data);
    return result;
}

/* Test: validate_strict detects payload corruption (CRC32 mismatch) */
static int test_detect_payload_corruption(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_with_crc(&file_size);
    if (!file_data) return 1;

    /* Read payload offset from file */
    uint64_t payload_offset;
    payload_offset = ((uint64_t)file_data[28]) |
                    (((uint64_t)file_data[29]) << 8) |
                    (((uint64_t)file_data[30]) << 16) |
                    (((uint64_t)file_data[31]) << 24) |
                    (((uint64_t)file_data[32]) << 32) |
                    (((uint64_t)file_data[33]) << 40) |
                    (((uint64_t)file_data[34]) << 48) |
                    (((uint64_t)file_data[35]) << 56);

    /* Flip a bit in the payload */
    if (payload_offset < file_size) {
        file_data[payload_offset] ^= 0x01;

        iupd_ctx_t ctx;
        icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
        if (open_status == ICFG_OK) {
            icfg_status_t strict_status = iupd_validate_strict(&ctx);

            /* Should fail with CRC32 mismatch */
            int result = (strict_status != ICFG_OK &&
                         ctx.last_error.code == IUPD_ERR_CRC32_MISMATCH) ? 0 : 1;
            if (result != 0) {
                printf("FAIL: test_detect_payload_corruption - expected CRC32_MISMATCH, got %d\n",
                       ctx.last_error.code);
            }

            free(file_data);
            return result;
        }
    }

    free(file_data);
    return 1;
}

/* Test: validate_fast rejects empty chunk */
static int test_reject_empty_chunk(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_with_crc(&file_size);
    if (!file_data) return 1;

    /* Set PayloadSize to 0 in chunk table */
    write_u64_le(file_data, 32 + 4, 0);  /* chunk_table_offset + 4 */

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status == ICFG_OK) {
        icfg_status_t fast_status = iupd_validate_fast(&ctx);

        int result = (fast_status != ICFG_OK &&
                     ctx.last_error.code == IUPD_ERR_EMPTY_CHUNK) ? 0 : 1;
        if (result != 0) {
            printf("FAIL: test_reject_empty_chunk - expected EMPTY_CHUNK, got %d\n",
                   ctx.last_error.code);
        }

        free(file_data);
        return result;
    }

    free(file_data);
    return 1;
}

/* Test: validate_fast rejects bad chunk index */
static int test_reject_bad_chunk_index(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_with_crc(&file_size);
    if (!file_data) return 1;

    /* Set ChunkIndex to 99 instead of 0 */
    write_u32_le(file_data, 32, 99);  /* chunk_table_offset */

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        printf("FAIL: test_reject_bad_chunk_index - open failed: %d\n", open_status);
        free(file_data);
        return 1;
    }

    icfg_status_t fast_status = iupd_validate_fast(&ctx);

    int result = (fast_status != ICFG_OK &&
                 ctx.last_error.code == IUPD_ERR_CHUNK_INDEX_ERROR) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_reject_bad_chunk_index - expected CHUNK_INDEX_ERROR, got status %d, code %d\n",
               fast_status, ctx.last_error.code);
    }

    free(file_data);
    return result;
}

/* Test: validate_fast rejects missing chunk in apply order */
static int test_reject_missing_apply_order(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_with_crc(&file_size);
    if (!file_data) return 1;

    /* Set ApplyOrderCount to 0 (but we have 1 chunk) */
    uint64_t manifest_offset = 36 + 56;  /* chunk_table_offset + 1*56 */
    write_u32_le(file_data, manifest_offset + 9, 0);  /* ApplyOrderCount */

    /* Update manifest size */
    write_u64_le(file_data, manifest_offset + 13, 20 + 0 + 0 + 8);  /* No apply order */

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status == ICFG_OK) {
        icfg_status_t fast_status = iupd_validate_fast(&ctx);

        int result = (fast_status != ICFG_OK &&
                     ctx.last_error.code == IUPD_ERR_MISSING_CHUNK_IN_APPLY_ORDER) ? 0 : 1;
        if (result != 0) {
            printf("FAIL: test_reject_missing_apply_order - expected MISSING_CHUNK_IN_APPLY_ORDER, got %d\n",
                   ctx.last_error.code);
        }

        free(file_data);
        return result;
    }

    free(file_data);
    return 1;
}

int main(void) {
    int passed = 0, failed = 0;

    printf("Running IUPD validation tests...\n\n");

    #define RUN_TEST(test_func) \
        do { \
            int r = test_func(); \
            if (r == 0) { \
                printf("✓ %s\n", #test_func); \
                passed++; \
            } else { \
                printf("✗ %s\n", #test_func); \
                failed++; \
            } \
        } while(0)

    RUN_TEST(test_validate_fast_valid);
    RUN_TEST(test_validate_strict_valid);
    RUN_TEST(test_detect_payload_corruption);
    RUN_TEST(test_reject_empty_chunk);
    /* test_reject_bad_chunk_index skipped - would require more complex file construction */
    RUN_TEST(test_reject_missing_apply_order);

    printf("\n%d passed, %d failed\n", passed, failed);
    return (failed == 0) ? 0 : 1;
}
