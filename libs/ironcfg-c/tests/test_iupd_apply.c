/*
 * test_iupd_apply.c
 * Streaming apply tests for IUPD implementation
 *
 * Tests:
 * - apply_begin initializes context
 * - apply_next iterates chunks in correct order
 * - apply_next stops after last chunk
 * - apply_end cleans up
 * - Chunk data is accessible (non-zero copy pointer)
 * - CRC32 available for verification
 * - BLAKE3 pointer available for verification
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

/* Construct valid IUPD file with 2 chunks */
static uint8_t* create_iupd_two_chunks(size_t* out_size) {
    uint8_t chunk1_data[] = { 0x11, 0x22, 0x33, 0x44 };
    uint8_t chunk2_data[] = { 0xAA, 0xBB, 0xCC, 0xDD };
    size_t chunk1_size = sizeof(chunk1_data);
    size_t chunk2_size = sizeof(chunk2_data);

    size_t chunk_table_offset = 36;
    size_t manifest_offset = 36 + (2 * 56);  /* 2 chunk entries */
    size_t payload_offset = manifest_offset + 20 + (0 * 8) + (2 * 4) + 8;  /* 0 deps, 2 in apply order */

    size_t file_size = payload_offset + chunk1_size + chunk2_size;

    uint8_t* buf = calloc(file_size, 1);
    if (!buf) return NULL;

    uint32_t crc1 = compute_crc32(chunk1_data, chunk1_size);
    uint32_t crc2 = compute_crc32(chunk2_data, chunk2_size);

    /* FILE HEADER */
    write_u32_le(buf, 0, 0x44505549);  /* "IUPD" */
    buf[4] = 0x01;  /* Version */
    write_u32_le(buf, 5, 0);  /* Flags */
    write_u16_le(buf, 9, 36);  /* HeaderSize */
    buf[11] = 0;  /* Reserved */
    write_u64_le(buf, 12, chunk_table_offset);
    write_u64_le(buf, 20, manifest_offset);
    write_u64_le(buf, 28, payload_offset);

    /* CHUNK TABLE (2 entries) */
    /* Chunk 0 */
    uint64_t ct0 = chunk_table_offset;
    write_u32_le(buf, ct0, 0);  /* ChunkIndex */
    write_u64_le(buf, ct0 + 4, chunk1_size);  /* PayloadSize */
    write_u64_le(buf, ct0 + 12, payload_offset);  /* PayloadOffset */
    write_u32_le(buf, ct0 + 20, crc1);  /* PayloadCrc32 */
    for (int i = 0; i < 32; i++) buf[ct0 + 24 + i] = 0xAA;  /* BLAKE3 */

    /* Chunk 1 */
    uint64_t ct1 = chunk_table_offset + 56;
    write_u32_le(buf, ct1, 1);  /* ChunkIndex */
    write_u64_le(buf, ct1 + 4, chunk2_size);  /* PayloadSize */
    write_u64_le(buf, ct1 + 12, payload_offset + chunk1_size);  /* PayloadOffset */
    write_u32_le(buf, ct1 + 20, crc2);  /* PayloadCrc32 */
    for (int i = 0; i < 32; i++) buf[ct1 + 24 + i] = 0xBB;  /* BLAKE3 */

    /* MANIFEST */
    uint64_t mb = manifest_offset;
    buf[mb] = 0x01;  /* ManifestVersion */
    write_u32_le(buf, mb + 1, 0);  /* TargetVersion */
    write_u32_le(buf, mb + 5, 0);  /* DependencyCount */
    write_u32_le(buf, mb + 9, 2);  /* ApplyOrderCount */
    uint64_t manifest_size = 20 + 0 + (2 * 4) + 8;
    write_u64_le(buf, mb + 13, manifest_size);  /* ManifestSize */

    /* Apply order: chunk 0, then chunk 1 */
    write_u32_le(buf, mb + 20, 0);  /* Chunk 0 */
    write_u32_le(buf, mb + 24, 1);  /* Chunk 1 */

    /* Manifest CRC32 (over header + apply order) */
    uint32_t manifest_crc = compute_crc32(&buf[mb], 28);
    write_u32_le(buf, mb + 28, manifest_crc);
    write_u32_le(buf, mb + 32, 0);  /* Reserved */

    /* CHUNK PAYLOADS */
    memcpy(&buf[payload_offset], chunk1_data, chunk1_size);
    memcpy(&buf[payload_offset + chunk1_size], chunk2_data, chunk2_size);

    *out_size = file_size;
    return buf;
}

/* Test: apply_begin returns OK */
static int test_apply_begin_success(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_iupd_two_chunks(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    iupd_apply_ctx_t apply_ctx;
    icfg_status_t status = iupd_apply_begin(&ctx, &apply_ctx);

    int result = (status == ICFG_OK) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_apply_begin_success - expected OK, got %d\n", status);
    }

    iupd_apply_end(&apply_ctx);
    free(file_data);
    return result;
}

/* Test: apply_next gets first chunk */
static int test_apply_next_first_chunk(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_iupd_two_chunks(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    iupd_apply_ctx_t apply_ctx;
    iupd_apply_begin(&ctx, &apply_ctx);

    iupd_chunk_t chunk;
    icfg_status_t status = iupd_apply_next(&apply_ctx, &chunk);

    int result = (status == ICFG_OK && chunk.chunk_index == 0) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_apply_next_first_chunk - expected chunk 0, got status %d, index %u\n",
               status, chunk.chunk_index);
    }

    iupd_apply_end(&apply_ctx);
    free(file_data);
    return result;
}

/* Test: apply_next returns pointer to payload */
static int test_apply_next_payload_pointer(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_iupd_two_chunks(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    iupd_apply_ctx_t apply_ctx;
    iupd_apply_begin(&ctx, &apply_ctx);

    iupd_chunk_t chunk;
    icfg_status_t status = iupd_apply_next(&apply_ctx, &chunk);

    /* Check payload_ptr is valid and within file */
    int result = (status == ICFG_OK && chunk.payload_ptr != NULL &&
                  chunk.payload_ptr >= ctx.data && chunk.payload_ptr < ctx.data + ctx.size) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_apply_next_payload_pointer - payload_ptr invalid\n");
    }

    iupd_apply_end(&apply_ctx);
    free(file_data);
    return result;
}

/* Test: apply_next returns payload size */
static int test_apply_next_payload_size(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_iupd_two_chunks(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    iupd_apply_ctx_t apply_ctx;
    iupd_apply_begin(&ctx, &apply_ctx);

    iupd_chunk_t chunk;
    iupd_apply_next(&apply_ctx, &chunk);

    int result = (chunk.payload_size == 4) ? 0 : 1;  /* 4 bytes for first chunk */
    if (result != 0) {
        printf("FAIL: test_apply_next_payload_size - expected 4, got %llu\n", chunk.payload_size);
    }

    iupd_apply_end(&apply_ctx);
    free(file_data);
    return result;
}

/* Test: apply_next returns payload CRC32 */
static int test_apply_next_payload_crc32(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_iupd_two_chunks(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    iupd_apply_ctx_t apply_ctx;
    iupd_apply_begin(&ctx, &apply_ctx);

    iupd_chunk_t chunk;
    iupd_apply_next(&apply_ctx, &chunk);

    /* CRC32 should be non-zero */
    int result = (chunk.payload_crc32 != 0) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_apply_next_payload_crc32 - CRC32 is zero\n");
    }

    iupd_apply_end(&apply_ctx);
    free(file_data);
    return result;
}

/* Test: apply_next iterates in correct order */
static int test_apply_next_order(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_iupd_two_chunks(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    iupd_apply_ctx_t apply_ctx;
    iupd_apply_begin(&ctx, &apply_ctx);

    iupd_chunk_t chunk1, chunk2;
    icfg_status_t s1 = iupd_apply_next(&apply_ctx, &chunk1);
    icfg_status_t s2 = iupd_apply_next(&apply_ctx, &chunk2);

    int result = (s1 == ICFG_OK && s2 == ICFG_OK &&
                  chunk1.chunk_index == 0 && chunk2.chunk_index == 1) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_apply_next_order - expected 0,1 got %u,%u\n",
               chunk1.chunk_index, chunk2.chunk_index);
    }

    iupd_apply_end(&apply_ctx);
    free(file_data);
    return result;
}

/* Test: apply_next returns RANGE after last chunk */
static int test_apply_next_end_of_list(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_iupd_two_chunks(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    iupd_apply_ctx_t apply_ctx;
    iupd_apply_begin(&ctx, &apply_ctx);

    iupd_chunk_t chunk;
    iupd_apply_next(&apply_ctx, &chunk);  /* First */
    iupd_apply_next(&apply_ctx, &chunk);  /* Second */
    icfg_status_t status = iupd_apply_next(&apply_ctx, &chunk);  /* Should be EOF */

    int result = (status == ICFG_ERR_RANGE) ? 0 : 1;
    if (result != 0) {
        printf("FAIL: test_apply_next_end_of_list - expected RANGE, got %d\n", status);
    }

    iupd_apply_end(&apply_ctx);
    free(file_data);
    return result;
}

/* Test: apply_end is safe to call */
static int test_apply_end_safe(void) {
    size_t file_size = 0;
    uint8_t* file_data = create_iupd_two_chunks(&file_size);
    if (!file_data) return 1;

    iupd_ctx_t ctx;
    icfg_status_t open_status = iupd_open(file_data, file_size, &ctx);
    if (open_status != ICFG_OK) {
        free(file_data);
        return 1;
    }

    iupd_apply_ctx_t apply_ctx;
    iupd_apply_begin(&ctx, &apply_ctx);

    iupd_chunk_t chunk;
    iupd_apply_next(&apply_ctx, &chunk);

    /* Call apply_end twice - should not crash */
    iupd_apply_end(&apply_ctx);
    iupd_apply_end(&apply_ctx);

    int result = 0;  /* If we got here, no crash */

    free(file_data);
    return result;
}

int main(void) {
    int passed = 0, failed = 0;

    printf("Running IUPD streaming apply tests...\n\n");

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

    RUN_TEST(test_apply_begin_success);
    RUN_TEST(test_apply_next_first_chunk);
    RUN_TEST(test_apply_next_payload_pointer);
    RUN_TEST(test_apply_next_payload_size);
    RUN_TEST(test_apply_next_payload_crc32);
    RUN_TEST(test_apply_next_order);
    RUN_TEST(test_apply_next_end_of_list);
    RUN_TEST(test_apply_end_safe);

    printf("\n%d passed, %d failed\n", passed, failed);
    return (failed == 0) ? 0 : 1;
}
