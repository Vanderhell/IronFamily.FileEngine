#include "ironfamily/ota_apply.h"
#include "ironfamily/iupd_v2_spec_min.h"
#include "ironfamily/delta_v1_spec_min.h"
#include "ironfamily/iupd_reader.h"
#include "ironfamily/iupd_incremental_metadata.h"
#include "ironfamily/delta2_apply.h"
#include "blake3/blake3.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

#define STREAM_CHUNK_SIZE 65536

static uint64_t read_u64_le(const uint8_t* data) {
    return ((uint64_t)data[0]) | (((uint64_t)data[1]) << 8) |
           (((uint64_t)data[2]) << 16) | (((uint64_t)data[3]) << 24) |
           (((uint64_t)data[4]) << 32) | (((uint64_t)data[5]) << 40) |
           (((uint64_t)data[6]) << 48) | (((uint64_t)data[7]) << 56);
}

static iron_error_t read_bytes(
    const iron_reader_t* r,
    uint64_t file_size,
    uint64_t offset,
    uint32_t len,
    uint8_t* dst
) {
    if (offset + len > file_size) {
        return IRON_E_FORMAT;
    }
    return r->read(r->ctx, offset, dst, len);
}

typedef struct {
    const iron_reader_t* pkg_r;
    uint64_t delta_offset;
    uint64_t delta_size;
} delta_reader_ctx_t;

static iron_error_t delta_read_impl(void* ctx, uint64_t off, uint8_t* dst, uint32_t len) {
    delta_reader_ctx_t* drc = (delta_reader_ctx_t*)ctx;
    if (off + len > drc->delta_size) {
        return IRON_E_FORMAT;
    }
    return drc->pkg_r->read(drc->pkg_r->ctx, drc->delta_offset + off, dst, len);
}

iron_error_t iron_ota_apply_iupd_v2_delta_v1(const iron_ota_apply_ctx_t* ctx)
{
    iron_error_t err;
    uint8_t header_buf[IUPD_V2_HEADER_SIZE];
    uint64_t payload_offset;
    uint64_t delta_size;
    const iron_reader_t* delta_reader_to_use;
    delta_reader_ctx_t delta_ctx;
    iron_reader_t wrapped_delta_reader;

    if (!ctx) {
        return IRON_E_FORMAT;
    }
    if (!ctx->base_r || !ctx->iupd_pkg_r || !ctx->out_w || !ctx->pubkey32) {
        return IRON_E_FORMAT;
    }
    if (ctx->base_size == 0 || ctx->iupd_pkg_size == 0 || ctx->out_size_expected == 0) {
        return IRON_E_FORMAT;
    }

    err = iron_iupd_verify_strict(
        ctx->iupd_pkg_r,
        ctx->iupd_pkg_size,
        ctx->pubkey32,
        ctx->expected_min_update_sequence,
        ctx->out_update_sequence
    );
    if (err != IRON_OK) {
        return err;
    }

    /* Use provided delta reader if available, otherwise extract from IUPD payload */
    if (ctx->delta_r != NULL && ctx->delta_size > 0) {
        /* External delta provided */
        delta_reader_to_use = ctx->delta_r;
        delta_size = ctx->delta_size;
    } else {
        /* Extract delta from IUPD payload */
        err = read_bytes(ctx->iupd_pkg_r, ctx->iupd_pkg_size, 0, IUPD_V2_HEADER_SIZE, header_buf);
        if (err != IRON_OK) {
            return err;
        }

        payload_offset = read_u64_le(&header_buf[IUPD_V2_PAYLOAD_OFFSET_OFFSET]);
        delta_size = ctx->iupd_pkg_size - payload_offset;

        if (delta_size < DIFF_HEADER_SIZE) {
            return IRON_E_FORMAT;
        }

        delta_ctx.pkg_r = ctx->iupd_pkg_r;
        delta_ctx.delta_offset = payload_offset;
        delta_ctx.delta_size = delta_size;

        wrapped_delta_reader.ctx = &delta_ctx;
        wrapped_delta_reader.read = delta_read_impl;

        delta_reader_to_use = &wrapped_delta_reader;
    }

    uint64_t out_bytes_written = 0;
    err = iron_diff_v1_apply(
        ctx->base_r,
        ctx->base_size,
        delta_reader_to_use,
        delta_size,
        ctx->out_w,
        ctx->out_size_expected,
        &out_bytes_written
    );
    if (err != IRON_OK) {
        return err;
    }

    if (ctx->out_r != NULL) {
        uint8_t delta_header[DIFF_HEADER_SIZE];
        err = read_bytes(delta_reader_to_use, delta_size, 0, DIFF_HEADER_SIZE, delta_header);
        if (err != IRON_OK) {
            return err;
        }

        uint8_t expected_target_hash[DIFF_HASH_SIZE];
        memcpy(expected_target_hash, &delta_header[DIFF_TARGET_HASH_OFFSET], DIFF_HASH_SIZE);

        blake3_hasher hasher;
        blake3_hasher_init(&hasher);

        uint8_t chunk[STREAM_CHUNK_SIZE];
        uint64_t pos = 0;

        while (pos < ctx->out_size_expected) {
            uint32_t chunk_len = (uint32_t)(ctx->out_size_expected - pos > STREAM_CHUNK_SIZE ? STREAM_CHUNK_SIZE : ctx->out_size_expected - pos);
            err = ctx->out_r->read(ctx->out_r->ctx, pos, chunk, chunk_len);
            if (err != IRON_OK) {
                return IRON_E_IO;
            }
            blake3_hasher_update(&hasher, chunk, chunk_len);
            pos += chunk_len;
        }

        uint8_t computed_target_hash[DIFF_HASH_SIZE];
        blake3_hasher_finalize(&hasher, computed_target_hash, DIFF_HASH_SIZE);

        if (memcmp(expected_target_hash, computed_target_hash, DIFF_HASH_SIZE) != 0) {
            return IRON_E_DIFF_TARGET_HASH;
        }
    }

    return IRON_OK;
}

/*
 * OTA Apply: Verify IUPD v2 INCREMENTAL + Apply dispatched algorithm
 *
 * Validates IUPD v2 signature, profile (must be INCREMENTAL), and metadata trailer.
 * Dispatches to DELTA_V1 or IRONDEL2 apply based on metadata algorithm ID.
 * Validates base image and optional target image BLAKE3 hashes.
 */
iron_error_t iron_ota_apply_iupd_v2_incremental(const iron_ota_apply_ctx_t* ctx)
{
    iron_error_t err;
    uint8_t header_buf[IUPD_V2_HEADER_SIZE];
    uint64_t payload_offset;
    uint64_t delta_size;
    uint8_t profile;
    iupd_incremental_metadata_t metadata;
    const iron_reader_t* delta_reader_to_use;
    delta_reader_ctx_t delta_ctx;
    iron_reader_t wrapped_delta_reader;

    if (!ctx) {
        return IRON_E_FORMAT;
    }
    if (!ctx->base_r || !ctx->iupd_pkg_r || !ctx->out_w || !ctx->pubkey32) {
        return IRON_E_FORMAT;
    }
    if (ctx->base_size == 0 || ctx->iupd_pkg_size == 0 || ctx->out_size_expected == 0) {
        return IRON_E_FORMAT;
    }

    /* Verify IUPD v2 signature and UpdateSequence */
    err = iron_iupd_verify_strict(
        ctx->iupd_pkg_r,
        ctx->iupd_pkg_size,
        ctx->pubkey32,
        ctx->expected_min_update_sequence,
        ctx->out_update_sequence
    );
    if (err != IRON_OK) {
        return err;
    }

    /* Read header to check profile */
    err = read_bytes(ctx->iupd_pkg_r, ctx->iupd_pkg_size, 0, IUPD_V2_HEADER_SIZE, header_buf);
    if (err != IRON_OK) {
        return err;
    }

    profile = header_buf[IUPD_V2_PROFILE_OFFSET];
    if (profile != IUPD_PROFILE_INCREMENTAL) {
        return IRON_E_PROFILE_NOT_ALLOWED;
    }

    /* Locate and parse INCREMENTAL metadata trailer */
    uint8_t* pkg_data;
    /* Read entire package to locate metadata (allocate buffer) */
    if (ctx->iupd_pkg_size > 10 * 1024 * 1024) {
        /* Package too large to buffer (>10MB), use streaming search */
        /* For now, fail. In production, implement streaming trailer search. */
        return IRON_E_FORMAT;
    }

    pkg_data = (uint8_t*)malloc(ctx->iupd_pkg_size);
    if (!pkg_data) {
        return IRON_E_FORMAT;
    }

    err = ctx->iupd_pkg_r->read(ctx->iupd_pkg_r->ctx, 0, pkg_data, (uint32_t)ctx->iupd_pkg_size);
    if (err != IRON_OK) {
        free(pkg_data);
        return err;
    }

    int64_t metadata_offset = iupd_incremental_metadata_find(pkg_data, ctx->iupd_pkg_size);
    if (metadata_offset < 0) {
        free(pkg_data);
        return IRON_E_SIG_INVALID;  /* Metadata trailer missing */
    }

    uint32_t metadata_len = (uint32_t)(ctx->iupd_pkg_size - metadata_offset);
    if (!iupd_incremental_metadata_parse(&pkg_data[metadata_offset], metadata_len, &metadata)) {
        free(pkg_data);
        return IRON_E_SIG_INVALID;  /* Metadata parsing failed */
    }

    /* Validate algorithm ID (fail-closed on unknown) */
    if (!iupd_algorithm_is_known(metadata.algorithm_id)) {
        free(pkg_data);
        return IRON_E_FORMAT;
    }

    /* Validate base image hash binding */
    if (metadata.base_hash_len == 0) {
        free(pkg_data);
        return IRON_E_SIG_INVALID;
    }

    blake3_hasher hasher;
    blake3_hasher_init(&hasher);

    uint8_t chunk[STREAM_CHUNK_SIZE];
    uint64_t pos = 0;

    while (pos < ctx->base_size) {
        uint32_t chunk_len = (uint32_t)(ctx->base_size - pos > STREAM_CHUNK_SIZE ? STREAM_CHUNK_SIZE : ctx->base_size - pos);
        err = ctx->base_r->read(ctx->base_r->ctx, pos, chunk, chunk_len);
        if (err != IRON_OK) {
            free(pkg_data);
            return IRON_E_IO;
        }
        blake3_hasher_update(&hasher, chunk, chunk_len);
        pos += chunk_len;
    }

    uint8_t computed_base_hash[32];
    blake3_hasher_finalize(&hasher, computed_base_hash, 32);

    if (metadata.base_hash_len != 32 || memcmp(computed_base_hash, metadata.base_hash, 32) != 0) {
        free(pkg_data);
        return IRON_E_DIFF_BASE_HASH;
    }

    /* Extract delta from payload (before metadata trailer) */
    payload_offset = read_u64_le(&header_buf[IUPD_V2_PAYLOAD_OFFSET_OFFSET]);
    delta_size = metadata_offset - payload_offset;  /* Up to metadata trailer */

    if (delta_size < DIFF_HEADER_SIZE) {
        free(pkg_data);
        return IRON_E_FORMAT;
    }

    delta_ctx.pkg_r = ctx->iupd_pkg_r;
    delta_ctx.delta_offset = payload_offset;
    delta_ctx.delta_size = delta_size;

    wrapped_delta_reader.ctx = &delta_ctx;
    wrapped_delta_reader.read = delta_read_impl;
    delta_reader_to_use = &wrapped_delta_reader;

    /* Dispatch to appropriate delta apply algorithm */
    int apply_err;
    if (metadata.algorithm_id == IUPD_ALGORITHM_DELTA_V1) {
        /* Delta v1 apply */
        uint64_t out_bytes_written = 0;
        apply_err = iron_diff_v1_apply(
            ctx->base_r,
            ctx->base_size,
            delta_reader_to_use,
            delta_size,
            ctx->out_w,
            ctx->out_size_expected,
            &out_bytes_written
        );
    } else if (metadata.algorithm_id == IUPD_ALGORITHM_IRONDEL2) {
        /* IRONDEL2 apply */
        apply_err = iron_delta2_apply(
            ctx->base_r,
            ctx->base_size,
            delta_reader_to_use,
            delta_size,
            ctx->out_w,
            ctx->out_size_expected,
            0  /* flags */
        );
    } else {
        /* Should not reach here (already validated above) */
        free(pkg_data);
        return IRON_E_FORMAT;
    }

    if (apply_err != IRON_OK) {
        free(pkg_data);
        return apply_err;
    }

    /* Validate target image hash if present in metadata */
    if (metadata.target_hash_len > 0) {
        if (ctx->out_r == NULL) {
            free(pkg_data);
            return IRON_E_FORMAT;  /* Target hash requires output reader */
        }

        if (metadata.target_hash_len != 32) {
            free(pkg_data);
            return IRON_E_FORMAT;  /* Only 32-byte BLAKE3 hashes supported */
        }

        /* Flush output writer to ensure written data is visible to reader.
         * The writer context is a file_writer_ctx_t struct containing FILE*.
         * This is necessary because the writer uses buffered I/O (fwrite) while
         * the reader uses separate file handles. Without flushing, the reader
         * sees stale data. */
        if (ctx->out_w && ctx->out_w->ctx) {
            /* DEFECT FIX: Correctly extract FILE* from file_writer_ctx_t */
            /* The writer context is NOT a FILE* directly, it contains one. */
            typedef struct {
                FILE* fp;
            } file_writer_ctx_t;
            file_writer_ctx_t* writer_ctx = (file_writer_ctx_t*)ctx->out_w->ctx;
            if (writer_ctx && writer_ctx->fp) {
                fflush(writer_ctx->fp);
            }
        }

        blake3_hasher_init(&hasher);
        pos = 0;

        while (pos < ctx->out_size_expected) {
            uint32_t chunk_len = (uint32_t)(ctx->out_size_expected - pos > STREAM_CHUNK_SIZE ? STREAM_CHUNK_SIZE : ctx->out_size_expected - pos);
            err = ctx->out_r->read(ctx->out_r->ctx, pos, chunk, chunk_len);
            if (err != IRON_OK) {
                free(pkg_data);
                return IRON_E_IO;
            }
            blake3_hasher_update(&hasher, chunk, chunk_len);
            pos += chunk_len;
        }

        uint8_t computed_target_hash[32];
        blake3_hasher_finalize(&hasher, computed_target_hash, 32);

        if (memcmp(computed_target_hash, metadata.target_hash, 32) != 0) {
            free(pkg_data);
            return IRON_E_DIFF_TARGET_HASH;
        }
    }

    free(pkg_data);
    return IRON_OK;
}
