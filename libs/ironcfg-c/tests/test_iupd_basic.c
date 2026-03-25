/*
 * test_iupd_basic.c
 * Basic structural tests for IUPD implementation
 *
 * Tests:
 * - Invalid magic
 * - Unsupported version
 * - Invalid flags
 * - Invalid header size
 * - Offset out of bounds
 * - Chunk table structure
 * - Manifest structure
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

/* Construct minimal valid IUPD file (1 chunk, empty payload, trivial manifest) */
static uint8_t* create_valid_iupd_file(size_t* out_size) {
    /* Layout:
     * [0-35] File Header
     * [36-91] Chunk Table (1 entry)
     * [92-107] Manifest (header only, 0 deps, 1 chunk in apply order)
     * [108-...] Chunk payload (minimal)
     */

    size_t payload_size = 4;  /* Minimal payload */
    size_t chunk_table_offset = 36;
    size_t manifest_offset = 36 + 56;  /* After 1 chunk entry */
    size_t payload_offset = manifest_offset + 20 + 4 + 8;  /* Header + apply order + CRC */

    size_t file_size = payload_offset + payload_size;

    uint8_t* buf = calloc(file_size, 1);
    if (!buf) return NULL;

    /* FILE HEADER (36 bytes) */
    write_u32_le(buf, 0, 0x44505549);  /* "IUPD" magic */
    buf[4] = 0x01;  /* Version */
    write_u32_le(buf, 5, 0);  /* Flags (all reserved, must be 0) */
    write_u16_le(buf, 9, 36);  /* HeaderSize */
    buf[11] = 0;  /* Reserved */
    write_u64_le(buf, 12, chunk_table_offset);
    write_u64_le(buf, 20, manifest_offset);
    write_u64_le(buf, 28, payload_offset);

    /* CHUNK TABLE (1 entry = 56 bytes) */
    uint64_t chunk_table_base = chunk_table_offset;
    write_u32_le(buf, chunk_table_base, 0);  /* ChunkIndex = 0 */
    write_u64_le(buf, chunk_table_base + 4, payload_size);  /* PayloadSize */
    write_u64_le(buf, chunk_table_base + 12, payload_offset);  /* PayloadOffset */
    write_u32_le(buf, chunk_table_base + 20, 0x12345678);  /* PayloadCrc32 (dummy) */
    /* PayloadBlake3 (32 bytes, non-zero) */
    for (int i = 0; i < 32; i++) {
        buf[chunk_table_base + 24 + i] = 0xAA;
    }

    /* MANIFEST */
    uint64_t manifest_base = manifest_offset;
    buf[manifest_base] = 0x01;  /* ManifestVersion */
    write_u32_le(buf, manifest_base + 1, 0);  /* TargetVersion */
    write_u32_le(buf, manifest_base + 5, 0);  /* DependencyCount */
    write_u32_le(buf, manifest_base + 9, 1);  /* ApplyOrderCount */
    uint64_t manifest_size = 20 + 0 + 4 + 8;  /* header + deps + apply order + CRC */
    write_u64_le(buf, manifest_base + 13, manifest_size);  /* ManifestSize */

    /* Apply order list (1 entry) */
    write_u32_le(buf, manifest_base + 20, 0);  /* ChunkIndex = 0 */

    /* Manifest integrity (CRC32 + reserved) */
    uint32_t manifest_crc = 0x87654321;  /* dummy CRC */
    write_u32_le(buf, manifest_base + 24, manifest_crc);
    write_u32_le(buf, manifest_base + 28, 0);  /* Reserved */

    /* CHUNK PAYLOAD */
    buf[payload_offset] = 0xDE;
    buf[payload_offset + 1] = 0xAD;
    buf[payload_offset + 2] = 0xBE;
    buf[payload_offset + 3] = 0xEF;

    *out_size = file_size;
    return buf;
}

/* Test: Valid file opens successfully */
static int test_open_valid_file(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_file(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(file_data, file_size, &ctx);

    int result = (status == ICFG_OK) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_open_valid_file - expected OK, got %d\n", status);
        if (ctx.last_error.message) {
            printf("      Error: %s\n", ctx.last_error.message);
        }
    }

    free(file_data);
    return result;
}

/* Test: Invalid magic fails */
static int test_invalid_magic(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_file(&file_size);
    if (!file_data) return 1;

    /* Corrupt magic */
    write_u32_le(file_data, 0, 0xDEADBEEF);

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(file_data, file_size, &ctx);

    int result = (status != ICFG_OK && ctx.last_error.code == IUPD_ERR_INVALID_MAGIC) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_invalid_magic - expected INVALID_MAGIC, got code %d\n",
               ctx.last_error.code);
    }

    free(file_data);
    return result;
}

/* Test: Unsupported version fails */
static int test_unsupported_version(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_file(&file_size);
    if (!file_data) return 1;

    /* Set version to 0x02 */
    file_data[4] = 0x02;

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(file_data, file_size, &ctx);

    int result = (status != ICFG_OK && ctx.last_error.code == IUPD_ERR_UNSUPPORTED_VERSION) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_unsupported_version - expected UNSUPPORTED_VERSION, got code %d\n",
               ctx.last_error.code);
    }

    free(file_data);
    return result;
}

/* Test: Invalid flags fails */
static int test_invalid_flags(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_file(&file_size);
    if (!file_data) return 1;

    /* Set flags to non-zero */
    write_u32_le(file_data, 5, 0x00000001);

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(file_data, file_size, &ctx);

    int result = (status != ICFG_OK && ctx.last_error.code == IUPD_ERR_INVALID_FLAGS) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_invalid_flags - expected INVALID_FLAGS, got code %d\n",
               ctx.last_error.code);
    }

    free(file_data);
    return result;
}

/* Test: Invalid header size fails */
static int test_invalid_header_size(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_file(&file_size);
    if (!file_data) return 1;

    /* Set HeaderSize to 64 */
    write_u16_le(file_data, 9, 64);

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(file_data, file_size, &ctx);

    int result = (status != ICFG_OK && ctx.last_error.code == IUPD_ERR_INVALID_HEADER_SIZE) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_invalid_header_size - expected INVALID_HEADER_SIZE, got code %d\n",
               ctx.last_error.code);
    }

    free(file_data);
    return result;
}

/* Test: File too small fails */
static int test_file_too_small(void) {
    uint8_t small_buf[16];
    memset(small_buf, 0, sizeof(small_buf));

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(small_buf, sizeof(small_buf), &ctx);

    int result = (status != ICFG_OK && ctx.last_error.code == IUPD_ERR_OFFSET_OUT_OF_BOUNDS) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_file_too_small - expected OFFSET_OUT_OF_BOUNDS, got code %d\n",
               ctx.last_error.code);
    }

    return result;
}

/* Test: Offset out of bounds fails */
static int test_offset_out_of_bounds(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_file(&file_size);
    if (!file_data) return 1;

    /* Set PayloadOffset beyond file size */
    write_u64_le(file_data, 28, 0x0000000100000000ULL);

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(file_data, file_size, &ctx);

    int result = (status != ICFG_OK && ctx.last_error.code == IUPD_ERR_OFFSET_OUT_OF_BOUNDS) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_offset_out_of_bounds - expected OFFSET_OUT_OF_BOUNDS, got code %d\n",
               ctx.last_error.code);
    }

    free(file_data);
    return result;
}

/* Test: Chunk count correctness */
static int test_chunk_count(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_file(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(file_data, file_size, &ctx);

    int result = (status == ICFG_OK && ctx.chunk_count == 1) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_chunk_count - expected 1, got %u\n", ctx.chunk_count);
    }

    free(file_data);
    return result;
}

/* Test: Manifest size validation */
static int test_manifest_size(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_valid_iupd_file(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t status = iupd_open(file_data, file_size, &ctx);

    /* Expected: 20 (header) + 0 (deps) + 4 (apply order) + 8 (CRC) = 32 */
    int result = (status == ICFG_OK && ctx.manifest_size == 32) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_manifest_size - expected 32, got %llu\n", ctx.manifest_size);
    }

    free(file_data);
    return result;
}

int main(void) {
    int passed = 0, failed = 0;

    printf("Running IUPD basic tests...\n\n");

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

    RUN_TEST(test_open_valid_file);
    RUN_TEST(test_invalid_magic);
    RUN_TEST(test_unsupported_version);
    RUN_TEST(test_invalid_flags);
    RUN_TEST(test_invalid_header_size);
    RUN_TEST(test_file_too_small);
    RUN_TEST(test_offset_out_of_bounds);
    RUN_TEST(test_chunk_count);
    RUN_TEST(test_manifest_size);

    printf("\n%d passed, %d failed\n", passed, failed);
    return (failed == 0) ? 0 : 1;
}
