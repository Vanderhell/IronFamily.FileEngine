/*
 * File Writer Implementation for Tests
 *
 * Simple callback-based writer that writes to a file.
 */

#include "ironfamily/diff_apply.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct {
    FILE* fp;
} file_writer_ctx_t;

/* Writer callback implementation */
static int file_write_impl(void* ctx, uint64_t off, const uint8_t* src, uint32_t len) {
    file_writer_ctx_t* fw = (file_writer_ctx_t*)ctx;

    if (fseek(fw->fp, (long)off, SEEK_SET) != 0) {
        return 1;  /* Error */
    }

    size_t written = fwrite(src, 1, len, fw->fp);
    if (written != len) {
        return 1;  /* Error */
    }

    return 0;  /* Success */
}

/* Helper to create a file writer */
int create_file_writer(const char* path, iron_writer_t* writer) {
    file_writer_ctx_t* ctx = (file_writer_ctx_t*)malloc(sizeof(file_writer_ctx_t));
    if (!ctx) {
        return 1;  /* Allocation failed */
    }

    ctx->fp = fopen(path, "w+b");
    if (!ctx->fp) {
        free(ctx);
        return 1;  /* Cannot open file */
    }

    writer->ctx = ctx;
    writer->write = file_write_impl;

    return 0;  /* Success */
}

/* Helper to close and free writer */
void close_file_writer(iron_writer_t* writer) {
    if (writer && writer->ctx) {
        file_writer_ctx_t* ctx = (file_writer_ctx_t*)writer->ctx;
        if (ctx->fp) {
            fclose(ctx->fp);
        }
        free(ctx);
        writer->ctx = NULL;
    }
}
