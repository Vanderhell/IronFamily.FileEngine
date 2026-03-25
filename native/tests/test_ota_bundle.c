/*
 * OTA Bundle Integration Test
 *
 * Tests the high-level OTA API:
 * - Verify IUPD v2 package
 * - Apply embedded Delta v1
 * - Verify output hash (fail-closed)
 * - Return UpdateSequence for persistence
 *
 * Uses separate vectors: IUPD v2 package + Delta v1 patch
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "ironfamily/ota_apply.h"
#include "ironfamily/iupd_errors.h"
#include "ironfamily/delta_v1_spec_min.h"
#include "blake3/blake3.h"
#include "file_reader.h"

/* Forward declarations for file operations */
int create_file_writer(const char* path, iron_writer_t* writer);
void close_file_writer(iron_writer_t* writer);

/* Helper: Compute BLAKE3 hash of a file */
static int compute_file_hash(const char* path, uint8_t* hash_out) {
    FILE* fp = fopen(path, "rb");
    if (!fp) {
        return 0;  /* File missing */
    }

    blake3_hasher hasher;
    blake3_hasher_init(&hasher);

    uint8_t chunk[65536];
    size_t bytes_read;
    while ((bytes_read = fread(chunk, 1, sizeof(chunk), fp)) > 0) {
        blake3_hasher_update(&hasher, chunk, bytes_read);
    }

    blake3_hasher_finalize(&hasher, hash_out, 32);
    fclose(fp);
    return 1;
}

/* File read callback */
static iron_error_t file_read_impl(void* ctx, uint64_t off, uint8_t* dst, uint32_t len) {
    file_reader_ctx_t* fr = (file_reader_ctx_t*)ctx;

    if (fseek(fr->fp, (long)off, SEEK_SET) != 0) {
        return IRON_E_IO;
    }

    size_t read = fread(dst, 1, len, fr->fp);
    if (read != len) {
        return IRON_E_IO;
    }

    return IRON_OK;
}

/* Stream comparison: verify output matches expected without loading full file */
#define COMPARE_CHUNK_SIZE 65536

static int compare_files_streaming(const char* file1, const char* file2) {
    FILE *fp1 = fopen(file1, "rb"), *fp2 = fopen(file2, "rb");
    if (!fp1 || !fp2) {
        if (fp1) fclose(fp1);
        if (fp2) fclose(fp2);
        return 0;  /* Files missing or cannot open */
    }

    uint8_t buf1[COMPARE_CHUNK_SIZE], buf2[COMPARE_CHUNK_SIZE];
    int result = 1;  /* Assume equal */

    while (1) {
        size_t read1 = fread(buf1, 1, COMPARE_CHUNK_SIZE, fp1);
        size_t read2 = fread(buf2, 1, COMPARE_CHUNK_SIZE, fp2);

        if (read1 != read2 || (read1 > 0 && memcmp(buf1, buf2, read1) != 0)) {
            result = 0;  /* Files differ */
            break;
        }

        if (read1 == 0) {
            /* Both files ended */
            break;
        }
    }

    fclose(fp1);
    fclose(fp2);
    return result;
}

/* Test case structure */
typedef struct {
    const char* name;
    const char* base_path;
    const char* iupd_path;
    const char* delta_path;
    const char* expected_path;
    const char* pubkey_hex;  /* Test key in hex */
} test_case_t;

int main(void) {
    int passed = 0, failed = 0;

    printf("========================================\n");
    printf("OTA Bundle Integration Test Suite\n");
    printf("========================================\n\n");

    /* Test case: OTA with verified delta v1 */
    /* Note: Delta is provided separately, not embedded in IUPD */
    test_case_t test_case = {
        "bundle_01_verified_delta",
        "artifacts/vectors/v1/diff/v1/case_01.base.bin",
        "artifacts/vectors/v1/iupd/v2/secure_ok_01.iupd",
        "artifacts/vectors/v1/diff/v1/case_01.patch.bin",
        "artifacts/vectors/v1/diff/v1/case_01.out.bin",
        ""  /* Will use fixed test key */
    };

    printf("[1/1] Testing %s\n", test_case.name);
    printf("      Base:     %s\n", test_case.base_path);
    printf("      IUPD:     %s\n", test_case.iupd_path);
    printf("      Delta:    %s\n", test_case.delta_path);
    printf("      Expected: %s\n\n", test_case.expected_path);

    /* Check if expected output exists */
    FILE *expected_fp = fopen(test_case.expected_path, "rb");
    if (!expected_fp) {
        printf("      ❌ FAIL: Expected output file missing\n");
        printf("             Run: dotnet run --project tools/IronFamily.Vectors -- --out artifacts/vectors/v1 --force\n");
        printf("             to generate base and output files.\n\n");
        failed++;
        return 1;
    }

    /* Get expected output size */
    fseek(expected_fp, 0, SEEK_END);
    uint64_t expected_size = ftell(expected_fp);
    fseek(expected_fp, 0, SEEK_SET);
    fclose(expected_fp);

    /* Open base file */
    FILE *base_fp = fopen(test_case.base_path, "rb");
    if (!base_fp) {
        printf("      ❌ FAIL: Base file missing\n");
        failed++;
        return 1;
    }

    /* Get base size */
    fseek(base_fp, 0, SEEK_END);
    uint64_t base_size = ftell(base_fp);
    fseek(base_fp, 0, SEEK_SET);

    /* Open IUPD package file */
    FILE *iupd_fp = fopen(test_case.iupd_path, "rb");
    if (!iupd_fp) {
        printf("      ❌ FAIL: IUPD package file missing\n");
        fclose(base_fp);
        failed++;
        return 1;
    }

    /* Get IUPD size */
    fseek(iupd_fp, 0, SEEK_END);
    uint64_t iupd_size = ftell(iupd_fp);
    fseek(iupd_fp, 0, SEEK_SET);

    /* Open delta file */
    FILE *delta_fp = fopen(test_case.delta_path, "rb");
    if (!delta_fp) {
        printf("      ❌ FAIL: Delta file missing\n");
        fclose(base_fp);
        fclose(iupd_fp);
        failed++;
        return 1;
    }

    /* Get delta size */
    fseek(delta_fp, 0, SEEK_END);
    uint64_t delta_size = ftell(delta_fp);
    fseek(delta_fp, 0, SEEK_SET);

    /* Create output writer */
    iron_writer_t output_writer;
    const char* output_temp = "artifacts/vectors/v1/diff/v1/bundle_01.temp.bin";
    if (create_file_writer(output_temp, &output_writer) != 0) {
        printf("      ❌ FAIL: Cannot create output file\n\n");
        fclose(base_fp);
        fclose(iupd_fp);
        failed++;
        return 1;
    }

    /* Set up readers */
    file_reader_ctx_t base_ctx = { base_fp };
    iron_reader_t base_reader = { &base_ctx, file_read_impl };

    file_reader_ctx_t iupd_ctx = { iupd_fp };
    iron_reader_t iupd_reader = { &iupd_ctx, file_read_impl };

    file_reader_ctx_t delta_ctx_reader = { delta_fp };
    iron_reader_t delta_reader = { &delta_ctx_reader, file_read_impl };

    /* Test Ed25519 public key (derived from BenchSeed32) */
    uint8_t pubkey32[32] = {
        0x3f, 0x77, 0x08, 0xd5, 0xf5, 0xcc, 0x2b, 0xc6,
        0x33, 0xb5, 0x9d, 0x2b, 0x3a, 0x2e, 0xd9, 0x2e,
        0x74, 0x79, 0x22, 0x0c, 0x6f, 0x08, 0xad, 0xe2,
        0x08, 0xbe, 0xbc, 0xd8, 0x58, 0x0a, 0xb9, 0x3b
    };

    /* Apply OTA: verify IUPD v2 + apply delta v1 */
    uint64_t out_update_sequence = 0;
    iron_ota_apply_ctx_t ctx = {0};
    ctx.base_r = &base_reader;
    ctx.base_size = base_size;
    ctx.iupd_pkg_r = &iupd_reader;
    ctx.iupd_pkg_size = iupd_size;
    ctx.delta_r = &delta_reader;
    ctx.delta_size = delta_size;
    ctx.out_w = &output_writer;
    ctx.out_r = NULL;
    ctx.out_size_expected = expected_size;
    ctx.pubkey32 = pubkey32;
    ctx.expected_min_update_sequence = 0;
    ctx.out_update_sequence = &out_update_sequence;

    int apply_result = iron_ota_apply_iupd_v2_delta_v1(&ctx);

    close_file_writer(&output_writer);
    fclose(base_fp);
    fclose(iupd_fp);
    fclose(delta_fp);

    if (apply_result != IRON_OK) {
        printf("      ❌ FAIL: OTA apply returned error %d\n", apply_result);
        printf("             Error: %s\n\n", iron_error_str((iron_error_t)apply_result));
        failed++;
        return 1;
    }

    printf("      UpdateSequence: %llu\n", (unsigned long long)out_update_sequence);

    /* Verify output file matches expected */
    if (compare_files_streaming(output_temp, test_case.expected_path)) {
        printf("      ✅ PASS\n\n");
        passed++;
        /* Clean up temp file */
        remove(output_temp);
    } else {
        printf("      ❌ FAIL: Output does not match expected\n\n");
        failed++;
    }

    printf("========================================\n");
    printf("Results: %d passed, %d failed out of 1\n", passed, failed);
    printf("========================================\n");

    return (failed == 0) ? 0 : 1;
}
