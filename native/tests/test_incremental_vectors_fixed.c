/*
 * EXEC_07 PHASE 5: Native C INCREMENTAL Vector Lifecycle Test (FIXED)
 *
 * FIX: Open output file for reading AFTER writer closes,
 * OR use write-and-read approach with same FILE* in w+b mode.
 *
 * Executes iron_ota_apply_iupd_v2_incremental() against generated INCREMENTAL
 * package vectors to achieve parity with .NET implementation.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#include "ironfamily/iupd_errors.h"
#include "ironfamily/ota_apply.h"
#include "file_reader.h"

/* Forward declarations for file operations */
int create_file_writer(const char* path, iron_writer_t* writer);
void close_file_writer(iron_writer_t* writer);

#define VECTORS_BASE_PATH "artifacts/_dev/exec_07_incremental_full_package_vector_parity/incremental_vectors"
#define MAX_VECTORS 10

typedef struct
{
    const char* vector_id;
    const char* type;
    const char* algorithm;
} VectorMeta;

typedef struct
{
    const char* vector_id;
    const char* type;
    int success;
    const char* error_message;
    int package_size;
    int base_size;
    int target_size;
    const char* error_code;
} TestResult;

static VectorMeta vectors[MAX_VECTORS] = {
    {"success_01_delta_v1_simple", "success", "DELTA_V1"},
    {"success_02_irondel2_simple", "success", "IRONDEL2"},
    {"success_03_delta_v1_medium", "success", "DELTA_V1"},
    {"success_04_irondel2_medium", "success", "IRONDEL2"},
    {"success_05_delta_v1_no_target", "success", "DELTA_V1"},
    {"refusal_01_wrong_base_hash", "refusal", "DELTA_V1"},
    {"refusal_02_unknown_algorithm", "refusal", "UNKNOWN"},
    {"refusal_03_corrupted_crc32", "refusal", "DELTA_V1"},
    {"refusal_04_target_hash_mismatch", "refusal", "DELTA_V1"},
    {"refusal_05_missing_metadata", "refusal", "OPTIMIZED"},
};

static TestResult results[MAX_VECTORS];
static int result_count = 0;

/* Get file size */
static uint64_t get_file_size(FILE* fp)
{
    if (fseek(fp, 0, SEEK_END) != 0)
        return 0;
    long size = ftell(fp);
    fseek(fp, 0, SEEK_SET);
    return (uint64_t)size;
}

/* Compare two files byte by byte */
static int compare_files(FILE* f1, FILE* f2, uint64_t size)
{
    uint8_t buf1[4096], buf2[4096];
    uint64_t remaining = size;

    while (remaining > 0)
    {
        uint32_t to_read = (uint32_t)(remaining > sizeof(buf1) ? sizeof(buf1) : remaining);
        size_t r1 = fread(buf1, 1, to_read, f1);
        size_t r2 = fread(buf2, 1, to_read, f2);

        if (r1 != r2 || (r1 > 0 && memcmp(buf1, buf2, r1) != 0))
            return 0;

        remaining -= r1;
    }

    return 1;
}

/* Custom reader that uses a FILE* opened in w+b mode (dual I/O) */
static iron_error_t dual_file_read_impl(void* ctx, uint64_t off, uint8_t* dst, uint32_t len)
{
    FILE* fp = (FILE*)ctx;
    if (fseek(fp, (long)off, SEEK_SET) != 0) {
        return IRON_E_IO;
    }
    size_t read = fread(dst, 1, len, fp);
    if (read != len) {
        return IRON_E_IO;
    }
    return IRON_OK;
}

static void test_vector(const VectorMeta* meta)
{
    TestResult result = {0};
    result.vector_id = meta->vector_id;
    result.type = meta->type;

    /* Construct paths */
    char pkg_path[512], base_path[512], target_path[512];
    snprintf(pkg_path, sizeof(pkg_path), "%s/%s/package.iupd", VECTORS_BASE_PATH, meta->vector_id);
    snprintf(base_path, sizeof(base_path), "%s/%s/base.bin", VECTORS_BASE_PATH, meta->vector_id);
    snprintf(target_path, sizeof(target_path), "%s/%s/target.bin", VECTORS_BASE_PATH, meta->vector_id);

    /* Open input files */
    file_reader_ctx_t pkg_ctx, base_ctx;
    iron_reader_t pkg_reader, base_reader;

    if (!file_reader_open(pkg_path, &pkg_ctx, &pkg_reader))
    {
        result.success = 0;
        result.error_message = "Failed to open package file";
        goto done;
    }

    if (!file_reader_open(base_path, &base_ctx, &base_reader))
    {
        result.success = 0;
        result.error_message = "Failed to open base file";
        file_reader_close(&pkg_ctx);
        goto done;
    }

    /* Get sizes */
    uint64_t pkg_size = get_file_size(pkg_ctx.fp);
    uint64_t base_size = get_file_size(base_ctx.fp);

    result.package_size = (int)pkg_size;
    result.base_size = (int)base_size;

    /* Open target file for comparison */
    FILE* target_fp = fopen(target_path, "rb");
    if (!target_fp)
    {
        result.success = 0;
        result.error_message = "Failed to open target file";
        file_reader_close(&pkg_ctx);
        file_reader_close(&base_ctx);
        goto done;
    }

    uint64_t target_size = get_file_size(target_fp);
    result.target_size = (int)target_size;

    if (pkg_size == 0 || base_size == 0 || target_size == 0)
    {
        result.success = 0;
        result.error_message = "Invalid file sizes";
        fclose(target_fp);
        file_reader_close(&pkg_ctx);
        file_reader_close(&base_ctx);
        goto done;
    }

    /* Create output file writer (opened in w+b mode for dual I/O) */
    char output_temp[] = "tmpXXXXXX";
    FILE* out_fp = fopen(output_temp, "w+b");
    if (!out_fp)
    {
        result.success = 0;
        result.error_message = "Failed to create output file";
        fclose(target_fp);
        file_reader_close(&pkg_ctx);
        file_reader_close(&base_ctx);
        goto done;
    }

    /* Create writer and reader from same file handle (w+b allows both) */
    typedef struct { FILE* fp; } file_writer_ctx_t;
    file_writer_ctx_t writer_ctx = {out_fp};

    iron_writer_t output_writer;
    output_writer.ctx = &writer_ctx;
    output_writer.write = NULL;  /* Will set below with proper cast */

    /* Define write callback */
    static iron_error_t write_impl(void* ctx, uint64_t off, const uint8_t* src, uint32_t len) {
        FILE* fp = *(FILE**)ctx;
        if (fseek(fp, (long)off, SEEK_SET) != 0) return 1;
        size_t w = fwrite(src, 1, len, fp);
        return (w == len) ? 0 : 1;
    }
    output_writer.write = write_impl;

    /* Create reader from same file handle */
    iron_reader_t output_reader;
    output_reader.ctx = out_fp;
    output_reader.read = dual_file_read_impl;

    /* Benchmark public key */
    uint8_t pubkey32[32] = {
        0x3f, 0x77, 0x08, 0xd5, 0xf5, 0xcc, 0x2b, 0xc6,
        0x33, 0xb5, 0x9d, 0x2b, 0x3a, 0x2e, 0xd9, 0x2e,
        0x74, 0x79, 0x22, 0x0c, 0x6f, 0x08, 0xad, 0xe2,
        0x08, 0xbe, 0xbc, 0xd8, 0x58, 0x0a, 0xb9, 0x3b
    };

    uint64_t out_update_sequence = 0;
    iron_ota_apply_ctx_t ctx = {0};
    ctx.base_r = &base_reader;
    ctx.base_size = base_size;
    ctx.iupd_pkg_r = &pkg_reader;
    ctx.iupd_pkg_size = pkg_size;
    ctx.delta_r = NULL;  /* Delta is embedded in package */
    ctx.delta_size = 0;
    ctx.out_w = &output_writer;
    ctx.out_r = &output_reader;
    ctx.out_size_expected = target_size;
    ctx.pubkey32 = pubkey32;
    ctx.expected_min_update_sequence = 0;
    ctx.out_update_sequence = &out_update_sequence;

    /* Call apply */
    iron_error_t apply_error = iron_ota_apply_iupd_v2_incremental(&ctx);

    if (strcmp(meta->type, "success") == 0)
    {
        /* Success vector */
        if (apply_error != 0)
        {
            result.success = 0;
            result.error_message = "ApplyIncremental failed";
            result.error_code = iron_error_str(apply_error);
            goto done_with_fp;
        }

        result.success = 1;
    }
    else
    {
        /* Refusal vector - should fail */
        if (apply_error == 0)
        {
            result.success = 0;
            result.error_message = "Should have been rejected";
            goto done_with_fp;
        }

        result.success = 1;
        result.error_code = iron_error_str(apply_error);
    }

    /* Compare output with target if success */
    if (result.success && strcmp(meta->type, "success") == 0)
    {
        fseek(out_fp, 0, SEEK_SET);
        fseek(target_fp, 0, SEEK_SET);

        if (!compare_files(out_fp, target_fp, target_size))
        {
            result.success = 0;
            result.error_message = "Output content mismatch";
        }
    }

done_with_fp:
    /* Cleanup */
    file_reader_close(&pkg_ctx);
    file_reader_close(&base_ctx);
    if (out_fp) fclose(out_fp);
    if (target_fp) fclose(target_fp);
    remove(output_temp);
    return;

done:
    results[result_count++] = result;
    return;
}

/* Write JSON results */
static void write_json_results(const char* filename)
{
    FILE* f = fopen(filename, "w");
    if (!f) return;

    fprintf(f, "[\n");
    for (int i = 0; i < result_count; i++)
    {
        TestResult* r = &results[i];
        fprintf(f, "  {\n");
        fprintf(f, "    \"VectorId\": \"%s\",\n", r->vector_id);
        fprintf(f, "    \"Type\": \"%s\",\n", r->type);
        fprintf(f, "    \"Success\": %s,\n", r->success ? "true" : "false");
        if (r->error_message)
            fprintf(f, "    \"ErrorMessage\": \"%s\",\n", r->error_message);
        else
            fprintf(f, "    \"ErrorMessage\": null,\n");
        fprintf(f, "    \"PackageSize\": %d,\n", r->package_size);
        fprintf(f, "    \"BaseSize\": %d,\n", r->base_size);
        fprintf(f, "    \"TargetSize\": %d,\n", r->target_size);
        if (r->error_code)
            fprintf(f, "    \"ErrorCode\": \"%s\"\n", r->error_code);
        else
            fprintf(f, "    \"ErrorCode\": null\n");
        fprintf(f, "  }%s\n", i < result_count - 1 ? "," : "");
    }
    fprintf(f, "]\n");
    fclose(f);
}

int main(void)
{
    printf("=== EXEC_07 PHASE 5: Native C INCREMENTAL Lifecycle Test ===\n\n");

    for (int i = 0; i < MAX_VECTORS; i++)
    {
        test_vector(&vectors[i]);
        printf("%s %s\n", results[i].success ? "✓" : "✗", vectors[i].vector_id);
        result_count++;
    }

    int success_count = 0;
    for (int i = 0; i < result_count; i++)
        if (results[i].success)
            success_count++;

    printf("\n📊 Results: %d/%d pass\n", success_count, result_count);
    write_json_results("native_lifecycle_results.json");
    printf("📄 Output: native_lifecycle_results.json\n");

    return success_count == result_count ? 0 : 1;
}
