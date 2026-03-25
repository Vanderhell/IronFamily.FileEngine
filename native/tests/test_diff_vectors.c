/*
 * IUPD Delta v1 Vector Test Suite
 *
 * Tests the IUPD Delta v1 apply implementation against golden vectors.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "ironfamily/diff_apply.h"
#include "ironfamily/iupd_errors.h"
#include "ironfamily/delta_v1_spec_min.h"
#include "blake3/blake3.h"
#include "file_reader.h"

/* Forward declaration for file_writer (from file_writer.c) */
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

/* File reader implementation for tests */
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

typedef struct {
    const char* name;
    const char* base_path;
    const char* patch_path;
    const char* expected_path;
} test_case_t;

int main(void) {
    int passed = 0, failed = 0;

    printf("========================================\n");
    printf("DiffPack v1 Vector Test Suite\n");
    printf("========================================\n\n");

    /* Test case: case_01 */
    test_case_t test_case = {
        "case_01",
        "artifacts/vectors/v1/diff/v1/case_01.base.bin",
        "artifacts/vectors/v1/diff/v1/case_01.patch.bin",
        "artifacts/vectors/v1/diff/v1/case_01.out.bin"
    };

    printf("[1/1] Testing %s\n", test_case.name);
    printf("      Base:     %s\n", test_case.base_path);
    printf("      Patch:    %s\n", test_case.patch_path);
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
        printf("             Run: dotnet run --project tools/IronFamily.Vectors -- --out artifacts/vectors/v1 --force\n");
        failed++;
        return 1;
    }

    /* Get base size */
    fseek(base_fp, 0, SEEK_END);
    uint64_t base_size = ftell(base_fp);
    fseek(base_fp, 0, SEEK_SET);

    /* Open patch file */
    FILE *patch_fp = fopen(test_case.patch_path, "rb");
    if (!patch_fp) {
        printf("      ❌ FAIL: Patch file missing\n");
        fclose(base_fp);
        failed++;
        return 1;
    }

    /* Get patch size */
    fseek(patch_fp, 0, SEEK_END);
    uint64_t patch_size = ftell(patch_fp);
    fseek(patch_fp, 0, SEEK_SET);

    /* Set up readers */
    file_reader_ctx_t base_ctx = { base_fp };
    iron_reader_t base_reader = { &base_ctx, file_read_impl };

    file_reader_ctx_t patch_ctx = { patch_fp };
    iron_reader_t patch_reader = { &patch_ctx, file_read_impl };

    /* Create output writer */
    iron_writer_t output_writer;
    const char* output_temp = "artifacts/vectors/v1/diff/v1/case_01.temp.bin";
    if (create_file_writer(output_temp, &output_writer) != 0) {
        printf("      ❌ FAIL: Cannot create output file\n\n");
        fclose(base_fp);
        fclose(patch_fp);
        failed++;
        return 1;
    }

    /* Apply patch */
    uint64_t out_bytes_written = 0;
    int apply_result = iron_diff_v1_apply(
        &base_reader,
        base_size,
        &patch_reader,
        patch_size,
        &output_writer,
        expected_size,
        &out_bytes_written
    );

    close_file_writer(&output_writer);
    fclose(base_fp);
    fclose(patch_fp);

    if (apply_result != IRON_OK) {
        printf("      ❌ FAIL: Apply returned error %d\n\n", apply_result);
        failed++;
        return 1;
    }

    if (out_bytes_written != expected_size) {
        printf("      ❌ FAIL: Output size mismatch (got %llu, expected %llu)\n\n",
               (unsigned long long)out_bytes_written, (unsigned long long)expected_size);
        failed++;
        return 1;
    }

    /* Verify output hash against patch header */
    /* Read patch header to extract target hash */
    FILE* patch_fp_verify = fopen(test_case.patch_path, "rb");
    if (!patch_fp_verify) {
        printf("      ❌ FAIL: Cannot re-open patch file for hash verification\n\n");
        failed++;
        return 1;
    }

    uint8_t header_buf[96];
    if (fread(header_buf, 1, 96, patch_fp_verify) != 96) {
        printf("      ❌ FAIL: Cannot read patch header\n\n");
        fclose(patch_fp_verify);
        failed++;
        return 1;
    }
    fclose(patch_fp_verify);

    /* Extract expected target hash from patch header (offset 56) */
    uint8_t expected_target_hash[32];
    memcpy(expected_target_hash, &header_buf[56], 32);

    /* Compute output hash */
    uint8_t computed_output_hash[32];
    if (!compute_file_hash(output_temp, computed_output_hash)) {
        printf("      ❌ FAIL: Cannot read output file for hash verification\n\n");
        failed++;
        return 1;
    }

    /* Verify hash */
    if (memcmp(expected_target_hash, computed_output_hash, 32) != 0) {
        printf("      ❌ FAIL: Output hash mismatch\n");
        printf("             Expected: ");
        for (int i = 0; i < 32; i++) printf("%02x", expected_target_hash[i]);
        printf("\n");
        printf("             Got:      ");
        for (int i = 0; i < 32; i++) printf("%02x", computed_output_hash[i]);
        printf("\n\n");
        failed++;
        return 1;
    }

    /* Compare output with expected */
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
