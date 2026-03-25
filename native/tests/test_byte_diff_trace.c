/*
 * EXEC_10B Byte-Level Diff Trace Test
 *
 * Applies one DELTA_V1 and one IRONDEL2 vector, saves output to file for byte-diff analysis
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#include "ironfamily/iupd_errors.h"
#include "ironfamily/ota_apply.h"
#include "file_reader.h"

int create_file_writer(const char* path, iron_writer_t* writer);
void close_file_writer(iron_writer_t* writer);

uint64_t get_file_size(FILE* fp) {
    if (fseek(fp, 0, SEEK_END) != 0) return 0;
    long size = ftell(fp);
    fseek(fp, 0, SEEK_SET);
    return (uint64_t)size;
}

static void test_delta_v1_simple() {
    printf("\n=== DELTA_V1 Byte-Level Trace: success_01_delta_v1_simple ===\n");

    const char* BASE_PATH = "incremental_vectors/success_01_delta_v1_simple";
    char pkg_path[256], base_path[256], target_path[256], output_path[256];
    snprintf(pkg_path, sizeof(pkg_path), "%s/package.iupd", BASE_PATH);
    snprintf(base_path, sizeof(base_path), "%s/base.bin", BASE_PATH);
    snprintf(target_path, sizeof(target_path), "%s/target.bin", BASE_PATH);
    snprintf(output_path, sizeof(output_path), "exec_10b_delta_v1_output.bin");

    // Open files
    file_reader_ctx_t pkg_ctx, base_ctx;
    iron_reader_t pkg_reader, base_reader;

    if (!file_reader_open(pkg_path, &pkg_ctx, &pkg_reader)) {
        printf("ERROR: Cannot open package\n");
        return;
    }
    if (!file_reader_open(base_path, &base_ctx, &base_reader)) {
        printf("ERROR: Cannot open base\n");
        file_reader_close(&pkg_ctx);
        return;
    }

    uint64_t pkg_size = get_file_size(pkg_ctx.fp);
    uint64_t base_size = get_file_size(base_ctx.fp);

    printf("Package size: %lu\n", pkg_size);
    printf("Base size: %lu\n", base_size);

    // Create output writer
    iron_writer_t output_writer;
    if (create_file_writer(output_path, &output_writer) != 0) {
        printf("ERROR: Cannot create output file\n");
        file_reader_close(&pkg_ctx);
        file_reader_close(&base_ctx);
        return;
    }

    // Create output reader
    FILE* output_fp = (FILE*)output_writer.ctx;
    file_reader_ctx_t output_rctx;
    iron_reader_t output_reader;
    if (!file_reader_open(output_path, &output_rctx, &output_reader)) {
        memset(&output_reader, 0, sizeof(output_reader));
    }

    // Dummy public key
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
    ctx.out_w = &output_writer;
    ctx.out_r = &output_reader;
    ctx.out_size_expected = 1024;  // Target size
    ctx.pubkey32 = pubkey32;
    ctx.expected_min_update_sequence = 0;
    ctx.out_update_sequence = &out_update_sequence;

    iron_error_t err = iron_ota_apply_iupd_v2_incremental(&ctx);
    printf("Apply result: %d (%s)\n", err, err == 0 ? "OK" : "ERROR");

    // Close writer to flush
    close_file_writer(&output_writer);

    // Now read the output and compare with target
    FILE* out_fp = fopen(output_path, "rb");
    FILE* target_fp = fopen(target_path, "rb");

    if (!out_fp || !target_fp) {
        printf("ERROR: Cannot open output or target files for comparison\n");
        if (out_fp) fclose(out_fp);
        if (target_fp) fclose(target_fp);
        if (output_reader.ctx) file_reader_close(&output_rctx);
        file_reader_close(&pkg_ctx);
        file_reader_close(&base_ctx);
        return;
    }

    // Read both files
    uint8_t out_buf[2048], target_buf[2048];
    size_t out_len = fread(out_buf, 1, sizeof(out_buf), out_fp);
    size_t target_len = fread(target_buf, 1, sizeof(target_buf), target_fp);

    printf("Output size: %zu bytes\n", out_len);
    printf("Target size: %zu bytes\n", target_len);

    // Find first mismatch
    int first_mismatch = -1;
    for (int i = 0; i < (int)target_len && i < (int)out_len; i++) {
        if (out_buf[i] != target_buf[i]) {
            first_mismatch = i;
            break;
        }
    }

    if (first_mismatch >= 0) {
        printf("\nFIRST MISMATCH at offset %d (0x%x)\n", first_mismatch, first_mismatch);
        printf("  Expected: 0x%02x\n", target_buf[first_mismatch]);
        printf("  Got:      0x%02x\n", out_buf[first_mismatch]);

        // Show context
        int start = (first_mismatch > 16) ? first_mismatch - 16 : 0;
        int end = (first_mismatch + 16 < (int)target_len) ? first_mismatch + 16 : target_len;

        printf("\nExpected bytes [%d-%d]:\n  ", start, end-1);
        for (int i = start; i < end; i++) {
            printf("%02x ", target_buf[i]);
        }
        printf("\n");

        printf("Got bytes [%d-%d]:\n  ", start, end-1);
        for (int i = start; i < (int)out_len && i < end; i++) {
            printf("%02x ", out_buf[i]);
        }
        printf("\n");
    } else if (out_len != target_len) {
        printf("Size mismatch: expected %zu, got %zu\n", target_len, out_len);
    } else {
        printf("Output matches target exactly!\n");
    }

    fclose(out_fp);
    fclose(target_fp);
    if (output_reader.ctx) file_reader_close(&output_rctx);
    file_reader_close(&pkg_ctx);
    file_reader_close(&base_ctx);
}

int main(void) {
    test_delta_v1_simple();
    return 0;
}
