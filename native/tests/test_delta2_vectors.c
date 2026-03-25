/*
 * IRONDEL2 (Delta v2) Vector Test Suite
 *
 * Tests the IRONDEL2 Delta v2 apply implementation against golden vectors.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "ironfamily/delta2_apply.h"
#include "ironfamily/iupd_errors.h"
#include "ironfamily/delta2_spec_min.h"
#include "ironfamily/crc32.h"
#include "blake3/blake3.h"
#include "file_reader.h"

/* Forward declarations */
int create_file_writer(const char* path, iron_writer_t* writer);
void close_file_writer(iron_writer_t* writer);

/* Helper: Get file size */
static uint64_t get_file_size(const char* path) {
    FILE* fp = fopen(path, "rb");
    if (!fp) return 0;
    if (fseek(fp, 0, SEEK_END) != 0) {
        fclose(fp);
        return 0;
    }
    long size = ftell(fp);
    fclose(fp);
    return (size < 0) ? 0 : (uint64_t)size;
}

/* File reader implementation */
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

/* Open file for reading */
static int open_file_reader(const char* path, file_reader_ctx_t* ctx, iron_reader_t* reader) {
    ctx->fp = fopen(path, "rb");
    if (!ctx->fp) {
        return 0;
    }

    reader->ctx = ctx;
    reader->read = file_read_impl;

    return 1;
}

/* Close file reader */
static void close_file_reader(file_reader_ctx_t* ctx) {
    if (ctx && ctx->fp) {
        fclose(ctx->fp);
        ctx->fp = NULL;
    }
}

/* Compare files byte-by-byte */
static int compare_files_streaming(const char* file1, const char* file2) {
    FILE *fp1 = fopen(file1, "rb"), *fp2 = fopen(file2, "rb");
    if (!fp1 || !fp2) {
        if (fp1) fclose(fp1);
        if (fp2) fclose(fp2);
        return 0;
    }

    uint8_t buf1[65536], buf2[65536];
    int result = 1;

    while (1) {
        size_t read1 = fread(buf1, 1, sizeof(buf1), fp1);
        size_t read2 = fread(buf2, 1, sizeof(buf2), fp2);

        if (read1 != read2 || (read1 > 0 && memcmp(buf1, buf2, read1) != 0)) {
            result = 0;
            break;
        }

        if (read1 == 0) {
            break;
        }
    }

    fclose(fp1);
    fclose(fp2);
    return result;
}

/* Test counters */
static int tests_passed = 0;
static int tests_failed = 0;

/* Test POSITIVE: Apply golden vector case_01 */
static void test_delta2_case_01(void) {
    printf("\n[DELTA2] Test: case_01 (golden vector)\n");

    const char* base_path = "artifacts/vectors/v1/delta2/v1/case_01.base.bin";
    const char* patch_path = "artifacts/vectors/v1/delta2/v1/case_01.patch2.bin";
    const char* expected_path = "artifacts/vectors/v1/delta2/v1/case_01.out.bin";
    const char* output_path = "artifacts/vectors/v1/delta2/v1/out_case_01.bin";

    /* Check files exist */
    FILE* f = fopen(base_path, "rb");
    if (!f) {
        printf("  ❌ FAIL: Base file not found: %s\n", base_path);
        printf("     Run: dotnet run --project tools/IronFamily.Vectors -- --out artifacts/vectors/v1 --force\n");
        tests_failed++;
        return;
    }
    fclose(f);

    f = fopen(patch_path, "rb");
    if (!f) {
        printf("  ❌ FAIL: Patch file not found: %s\n", patch_path);
        tests_failed++;
        return;
    }
    fclose(f);

    f = fopen(expected_path, "rb");
    if (!f) {
        printf("  ❌ FAIL: Expected output file not found: %s\n", expected_path);
        tests_failed++;
        return;
    }
    fclose(f);

    /* Get file sizes */
    uint64_t base_size = get_file_size(base_path);
    uint64_t patch_size = get_file_size(patch_path);
    uint64_t expected_size = get_file_size(expected_path);

    printf("  base_size: %llu, patch_size: %llu, expected_out_size: %llu\n",
           (unsigned long long)base_size, (unsigned long long)patch_size, (unsigned long long)expected_size);

    /* Open files for reading/writing */
    file_reader_ctx_t base_ctx = {0};
    file_reader_ctx_t patch_ctx = {0};
    iron_reader_t base_reader = {0};
    iron_reader_t patch_reader = {0};
    iron_writer_t out_writer = {0};

    if (!open_file_reader(base_path, &base_ctx, &base_reader)) {
        printf("  ❌ FAIL: Cannot open base file\n");
        tests_failed++;
        return;
    }

    if (!open_file_reader(patch_path, &patch_ctx, &patch_reader)) {
        printf("  ❌ FAIL: Cannot open patch file\n");
        close_file_reader(&base_ctx);
        tests_failed++;
        return;
    }

    if (create_file_writer(output_path, &out_writer)) {
        printf("  ❌ FAIL: Cannot open output file for writing\n");
        close_file_reader(&base_ctx);
        close_file_reader(&patch_ctx);
        tests_failed++;
        return;
    }

    /* Apply patch */
    int err = iron_delta2_apply(
        &base_reader, base_size,
        &patch_reader, patch_size,
        &out_writer, expected_size,
        0
    );

    close_file_reader(&base_ctx);
    close_file_reader(&patch_ctx);
    close_file_writer(&out_writer);

    if (err != IRON_OK) {
        printf("  ❌ FAIL: iron_delta2_apply returned error %d\n", err);
        tests_failed++;
        return;
    }

    /* Compare output to expected */
    if (!compare_files_streaming(output_path, expected_path)) {
        printf("  ❌ FAIL: Output does not match expected\n");
        tests_failed++;
        return;
    }

    printf("  ✅ PASS: Output matches expected (size: %llu bytes)\n", (unsigned long long)expected_size);
    tests_passed++;
}

/* Test NEGATIVE: Corrupted magic */
static void test_delta2_bad_magic(void) {
    printf("\n[DELTA2] Test: corrupted magic\n");

    const char* base_path = "artifacts/vectors/v1/delta2/v1/case_01.base.bin";
    const char* patch_path = "artifacts/vectors/v1/delta2/v1/case_01.patch2.bin";
    const char* output_path = "artifacts/vectors/v1/delta2/v1/out_bad_magic.bin";

    FILE* f = fopen(patch_path, "rb");
    if (!f) {
        printf("  ⊘ SKIP: Patch file not found\n");
        return;
    }
    fclose(f);

    uint64_t base_size = get_file_size(base_path);
    uint64_t patch_size = get_file_size(patch_path);

    /* Create corrupted patch in memory */
    uint8_t* patch_data = (uint8_t*)malloc(patch_size);
    if (!patch_data) {
        printf("  ❌ FAIL: Memory allocation error\n");
        tests_failed++;
        return;
    }

    FILE* fp = fopen(patch_path, "rb");
    if (!fp || fread(patch_data, 1, patch_size, fp) != patch_size) {
        printf("  ❌ FAIL: Cannot read patch file\n");
        if (fp) fclose(fp);
        free(patch_data);
        tests_failed++;
        return;
    }
    fclose(fp);

    /* Corrupt the magic */
    memcpy(patch_data, "BADMAGI!", 8);

    /* Write corrupted patch to temp file */
    const char* temp_patch = "artifacts/vectors/v1/delta2/v1/temp_bad_magic.bin";
    fp = fopen(temp_patch, "wb");
    if (!fp || fwrite(patch_data, 1, patch_size, fp) != patch_size) {
        printf("  ❌ FAIL: Cannot write temp patch file\n");
        if (fp) fclose(fp);
        free(patch_data);
        tests_failed++;
        return;
    }
    fclose(fp);

    /* Try to apply */
    file_reader_ctx_t base_ctx = {0};
    file_reader_ctx_t patch_ctx = {0};
    iron_reader_t base_reader = {0};
    iron_reader_t patch_reader = {0};
    iron_writer_t out_writer = {0};

    if (!open_file_reader(base_path, &base_ctx, &base_reader) ||
        !open_file_reader(temp_patch, &patch_ctx, &patch_reader) ||
        create_file_writer(output_path, &out_writer)) {
        printf("  ❌ FAIL: Cannot open files\n");
        close_file_reader(&base_ctx);
        close_file_reader(&patch_ctx);
        close_file_writer(&out_writer);
        free(patch_data);
        tests_failed++;
        return;
    }

    int err = iron_delta2_apply(
        &base_reader, base_size,
        &patch_reader, patch_size,
        &out_writer, get_file_size(base_path),
        0
    );

    close_file_reader(&base_ctx);
    close_file_reader(&patch_ctx);
    close_file_writer(&out_writer);
    free(patch_data);

    if (err == IRON_E_FORMAT) {
        printf("  ✅ PASS: Correctly rejected corrupted magic (error %d)\n", err);
        tests_passed++;
    } else {
        printf("  ❌ FAIL: Expected IRON_E_FORMAT (%d) but got %d\n", IRON_E_FORMAT, err);
        tests_failed++;
    }
}

int main(void) {
    printf("=== IRONDEL2 (Delta v2) Apply Tests ===\n");

    /* Positive test */
    test_delta2_case_01();

    /* Negative tests */
    test_delta2_bad_magic();

    /* Summary */
    printf("\n=== Summary ===\n");
    printf("Passed: %d\n", tests_passed);
    printf("Failed: %d\n", tests_failed);

    return tests_failed > 0 ? 1 : 0;
}
