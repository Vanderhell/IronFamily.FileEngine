/*
 * File-based reader implementation for testing
 */

#include "file_reader.h"
#include <stdlib.h>
#include <string.h>

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

int file_reader_open(const char* path, file_reader_ctx_t* ctx, iron_reader_t* reader) {
    ctx->fp = fopen(path, "rb");
    if (!ctx->fp) {
        return 0;
    }

    reader->ctx = ctx;
    reader->read = file_read_impl;

    return 1;
}

void file_reader_close(file_reader_ctx_t* ctx) {
    if (ctx->fp) {
        fclose(ctx->fp);
        ctx->fp = NULL;
    }
}
