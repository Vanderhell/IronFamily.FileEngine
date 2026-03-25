/*
 * Simple test for just success_05_delta_v1_no_target
 * to diagnose the exact failure
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#include "ironfamily/ota_apply.h"
#include "ironfamily/iupd_errors.h"
#include "file_reader.h"

typedef struct {
    FILE* fp;
} file_writer_ctx_t;

static iron_error_t file_write_impl(void* ctx, uint64_t off, const uint8_t* src, uint32_t len) {
    file_writer_ctx_t* fw = (file_writer_ctx_t*)ctx;
    if (fseek(fw->fp, (long)off, SEEK_SET) != 0) return 1;
    size_t written = fwrite(src, 1, len, fw->fp);
    if (written != len) return 1;
    return 0;
}

static iron_error_t file_read_impl(void* ctx, uint64_t off, uint8_t* dst, uint32_t len) {
    file_reader_ctx_t* fr = (file_reader_ctx_t*)ctx;
    if (fseek(fr->fp, (long)off, SEEK_SET) != 0) return IRON_E_IO;
    size_t read = fread(dst, 1, len, fr->fp);
    if (read != len) return IRON_E_IO;
    return IRON_OK;
}

static uint64_t get_file_size(FILE* fp) {
    if (fseek(fp, 0, SEEK_END) != 0) return 0;
    long size = ftell(fp);
    fseek(fp, 0, SEEK_SET);
    return (uint64_t)size;
}

int main(void) {
    printf("=== SUCCESS_05 SIMPLE TEST ===\n\n");

    const char* base_path = "artifacts/_dev/exec_07_incremental_full_package_vector_parity/incremental_vectors/success_05_delta_v1_no_target/base.bin";
    const char* pkg_path = "artifacts/_dev/exec_07_incremental_full_package_vector_parity/incremental_vectors/success_05_delta_v1_no_target/package.iupd";
    const char* target_path = "artifacts/_dev/exec_07_incremental_full_package_vector_parity/incremental_vectors/success_05_delta_v1_no_target/target.bin";

    /* Open files */
    file_reader_ctx_t base_ctx, pkg_ctx;
    iron_reader_t base_reader, pkg_reader;
    if (!file_reader_open(base_path, &base_ctx, &base_reader)) {
        printf("ERROR: Cannot open base\n");
        return 1;
    }
    if (!file_reader_open(pkg_path, &pkg_ctx, &pkg_reader)) {
        printf("ERROR: Cannot open package\n");
        return 1;
    }

    uint64_t base_size = get_file_size(base_ctx.fp);
    uint64_t pkg_size = get_file_size(pkg_ctx.fp);

    printf("Base size: %llu\n", base_size);
    printf("Package size: %llu\n", pkg_size);

    /* Create output writer */
    FILE* out_fp = fopen("tmptest.bin", "w+b");
    if (!out_fp) {
        printf("ERROR: Cannot create output\n");
        return 1;
    }

    file_writer_ctx_t writer_ctx = {out_fp};
    iron_writer_t writer = {.ctx = &writer_ctx, .write = file_write_impl};

    /* Create output reader (will try after close) */
    file_reader_ctx_t out_rctx;
    iron_reader_t out_reader = {.ctx = &out_rctx, .read = file_read_impl};

    /* Set up apply context */
    uint8_t pubkey32[32] = {
        0x3f, 0x77, 0x08, 0xd5, 0xf5, 0xcc, 0x2b, 0xc6,
        0x33, 0xb5, 0x9d, 0x2b, 0x3a, 0x2e, 0xd9, 0x2e,
        0x74, 0x79, 0x22, 0x0c, 0x6f, 0x08, 0xad, 0xe2,
        0x08, 0xbe, 0xbc, 0xd8, 0x58, 0x0a, 0xb9, 0x3b
    };

    uint64_t target_size = get_file_size(fopen(target_path, "rb"));
    uint64_t out_seq = 0;

    iron_ota_apply_ctx_t ctx = {
        .base_r = &base_reader,
        .base_size = base_size,
        .iupd_pkg_r = &pkg_reader,
        .iupd_pkg_size = pkg_size,
        .delta_r = NULL,
        .delta_size = 0,
        .out_w = &writer,
        .out_r = &out_reader,  /* Will be NULL for target hash validation since there's no target hash */
        .out_size_expected = target_size,
        .pubkey32 = pubkey32,
        .expected_min_update_sequence = 0,
        .out_update_sequence = &out_seq
    };

    printf("\nCalling apply...\n");
    iron_error_t err = iron_ota_apply_iupd_v2_incremental(&ctx);
    printf("Apply result: %s\n", iron_error_str(err));

    if (err != IRON_OK) {
        printf("FAILED: Apply returned error\n");
        fclose(out_fp);
        remove("tmptest.bin");
        return 1;
    }

    /* Close writer and verify output */
    fclose(out_fp);
    uint64_t out_size = get_file_size(fopen("tmptest.bin", "rb"));
    printf("Output size: %llu (expected %llu)\n", out_size, target_size);

    if (out_size != target_size) {
        printf("FAILED: Output size mismatch\n");
        remove("tmptest.bin");
        return 1;
    }

    printf("\nSUCCESS!\n");
    remove("tmptest.bin");
    return 0;
}
