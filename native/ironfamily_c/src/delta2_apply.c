/*
 * IRONDEL2 (Delta v2) Apply Implementation (C99)
 *
 * Content-Defined Chunking (CDC) delta with COPY/LIT operations.
 * Applies patch to base file, streaming output with fail-closed validation.
 */

#include "ironfamily/delta2_apply.h"
#include "ironfamily/delta2_spec_min.h"
#include "ironfamily/crc32.h"
#include "ironfamily/iupd_errors.h"
#include "blake3/blake3.h"
#include <string.h>
#include <stdio.h>

#define STREAM_CHUNK_SIZE 65536

/* Helper: Read little-endian integers */
static uint32_t read_u32_le(const uint8_t* data) {
    return ((uint32_t)data[0]) | (((uint32_t)data[1]) << 8) |
           (((uint32_t)data[2]) << 16) | (((uint32_t)data[3]) << 24);
}

static uint64_t read_u64_le(const uint8_t* data) {
    return ((uint64_t)data[0]) | (((uint64_t)data[1]) << 8) |
           (((uint64_t)data[2]) << 16) | (((uint64_t)data[3]) << 24) |
           (((uint64_t)data[4]) << 32) | (((uint64_t)data[5]) << 40) |
           (((uint64_t)data[6]) << 48) | (((uint64_t)data[7]) << 56);
}

/* Helper: Overflow-safe addition */
static int check_overflow_add_u64(uint64_t a, uint64_t b, uint64_t limit) {
    return (a > limit || b > limit - a) ? 1 : 0;
}

/* Helper: Read from reader with bounds checking */
static int read_bytes(
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

/* Stream copy from source to destination */
static int stream_copy(
    const iron_reader_t* src_r,
    uint64_t src_size,
    uint64_t src_offset,
    uint64_t len,
    const iron_writer_t* dst_w,
    uint64_t dst_offset
) {
    uint8_t chunk[STREAM_CHUNK_SIZE];

    while (len > 0) {
        uint32_t chunk_len = (uint32_t)(len > STREAM_CHUNK_SIZE ? STREAM_CHUNK_SIZE : len);

        int err = read_bytes(src_r, src_size, src_offset, chunk_len, chunk);
        if (err != IRON_OK) {
            return err;
        }

        int write_err = dst_w->write(dst_w->ctx, dst_offset, chunk, chunk_len);
        if (write_err != 0) {
            return IRON_E_IO;
        }

        src_offset += chunk_len;
        dst_offset += chunk_len;
        len -= chunk_len;
    }

    return IRON_OK;
}

/*
 * IRONDEL2 Apply: Validate header, verify base hash, apply ops, verify output hash.
 */
int iron_delta2_apply(
    const iron_reader_t* base_r,
    uint64_t base_size,
    const iron_reader_t* patch_r,
    uint64_t patch_size,
    const iron_writer_t* out_w,
    uint64_t out_size_expected,
    uint32_t flags
) {
    int err;

    /* === GATE 1: Patch size sanity === */
    if (patch_size > DELTA2_MAX_OUTPUT_SIZE) {
        return IRON_E_DIFF_LIMITS;
    }

    if (patch_size < DELTA2_HEADER_SIZE) {
        return IRON_E_FORMAT;
    }

    if (flags != 0) {
        return IRON_E_FORMAT;
    }

    /* === GATE 2: Read and validate header === */
    uint8_t header_buf[DELTA2_HEADER_SIZE];
    err = read_bytes(patch_r, patch_size, 0, DELTA2_HEADER_SIZE, header_buf);
    if (err != IRON_OK) {
        return err;
    }

    /* Check magic */
    if (strncmp((const char*)header_buf, DELTA2_MAGIC, 8) != 0) {
        return IRON_E_FORMAT;
    }

    /* Check version */
    uint8_t version = header_buf[DELTA2_VERSION_OFFSET];
    if (version != DELTA2_VERSION) {
        return IRON_E_FORMAT;
    }

    /* Check flags */
    uint8_t flags_byte = header_buf[DELTA2_FLAGS_OFFSET];
    if (flags_byte != DELTA2_FLAGS) {
        return IRON_E_FORMAT;
    }

    /* Check reserved */
    uint16_t reserved = read_u32_le(&header_buf[DELTA2_RESERVED_OFFSET]) & 0xFFFF;
    if (reserved != DELTA2_RESERVED) {
        return IRON_E_FORMAT;
    }

    /* Extract base length */
    uint64_t base_len_expected = read_u64_le(&header_buf[DELTA2_BASE_LEN_OFFSET]);
    if (base_len_expected != base_size) {
        return IRON_E_FORMAT;
    }

    /* Extract target length */
    uint64_t target_len = read_u64_le(&header_buf[DELTA2_TARGET_LEN_OFFSET]);
    if (target_len != out_size_expected) {
        return IRON_E_FORMAT;
    }

    /* Extract hashes */
    uint8_t base_hash_expected[DELTA2_HASH_SIZE];
    uint8_t target_hash_expected[DELTA2_HASH_SIZE];
    memcpy(base_hash_expected, &header_buf[DELTA2_BASE_HASH_OFFSET], DELTA2_HASH_SIZE);
    memcpy(target_hash_expected, &header_buf[DELTA2_TARGET_HASH_OFFSET], DELTA2_HASH_SIZE);

    /* Extract op count */
    uint32_t op_count = read_u32_le(&header_buf[DELTA2_OP_COUNT_OFFSET]);
    if (op_count == 0 || op_count > DELTA2_MAX_OPS) {
        return IRON_E_DIFF_LIMITS;
    }

    /* === GATE 3: Verify header CRC32 === */
    uint32_t header_crc32_expected = read_u32_le(&header_buf[DELTA2_HEADER_CRC32_OFFSET]);
    uint8_t header_for_crc[DELTA2_HEADER_SIZE];
    memcpy(header_for_crc, header_buf, DELTA2_HEADER_SIZE);
    /* Zero out the CRC32 field for computation */
    memset(&header_for_crc[DELTA2_HEADER_CRC32_OFFSET], 0, 4);
    /* Compute CRC32 over first 96 bytes only (with CRC field zeroed) */
    uint32_t header_crc32_computed = iron_crc32(header_for_crc, 96);
    if (header_crc32_computed != header_crc32_expected) {
        return IRON_E_FORMAT;
    }

    /* === GATE 4: Verify base hash (fail-closed) === */
    uint8_t base_hash_computed[DELTA2_HASH_SIZE];
    {
        blake3_hasher hasher;
        blake3_hasher_init(&hasher);

        uint8_t chunk[STREAM_CHUNK_SIZE];
        uint64_t pos = 0;

        while (pos < base_size) {
            uint32_t chunk_len = (uint32_t)(base_size - pos > STREAM_CHUNK_SIZE ? STREAM_CHUNK_SIZE : base_size - pos);
            err = read_bytes(base_r, base_size, pos, chunk_len, chunk);
            if (err != IRON_OK) {
                return err;
            }
            blake3_hasher_update(&hasher, chunk, chunk_len);
            pos += chunk_len;
        }

        blake3_hasher_finalize(&hasher, base_hash_computed, DELTA2_HASH_SIZE);
    }

    if (memcmp(base_hash_expected, base_hash_computed, DELTA2_HASH_SIZE) != 0) {
        return IRON_E_DIFF_BASE_HASH;
    }

    /* === GATE 5: Process op stream === */
    uint64_t patch_pos = DELTA2_HEADER_SIZE;
    uint64_t out_pos = 0;  /* Current output position */
    uint8_t op_chunk[STREAM_CHUNK_SIZE];
    blake3_hasher out_hasher;
    blake3_hasher_init(&out_hasher);

    for (uint32_t op_idx = 0; op_idx < op_count; op_idx++) {
        /* Read opcode */
        if (patch_pos >= patch_size) {
            return IRON_E_FORMAT;
        }

        uint8_t opcode_buf;
        err = read_bytes(patch_r, patch_size, patch_pos, 1, &opcode_buf);
        if (err != IRON_OK) {
            return err;
        }
        uint8_t opcode = opcode_buf;
        patch_pos++;

        if (opcode == DELTA2_OP_COPY) {
            /* COPY: base_offset (u64) + length (u32) = 12 bytes */
            if (patch_pos + 12 > patch_size) {
                return IRON_E_FORMAT;
            }

            uint8_t copy_header[12];
            err = read_bytes(patch_r, patch_size, patch_pos, 12, copy_header);
            if (err != IRON_OK) {
                return err;
            }

            uint64_t base_offset = read_u64_le(&copy_header[0]);
            uint32_t copy_len = read_u32_le(&copy_header[8]);

            patch_pos += 12;

            /* Validate COPY bounds */
            if (copy_len == 0 || copy_len > DELTA2_MAX_COPY_LEN) {
                return IRON_E_FORMAT;
            }

            if (check_overflow_add_u64(base_offset, copy_len, base_size)) {
                return IRON_E_DIFF_OUT_OF_RANGE;
            }

            if (check_overflow_add_u64(out_pos, copy_len, target_len)) {
                return IRON_E_FORMAT;
            }

            /* Copy from base to output */
            uint32_t copied = 0;
            while (copied < copy_len) {
                uint32_t chunk_len = (uint32_t)(copy_len - copied > STREAM_CHUNK_SIZE ? STREAM_CHUNK_SIZE : copy_len - copied);

                err = read_bytes(base_r, base_size, base_offset + copied, chunk_len, op_chunk);
                if (err != IRON_OK) {
                    return err;
                }

                int write_err = out_w->write(out_w->ctx, out_pos + copied, op_chunk, chunk_len);
                if (write_err != 0) {
                    return IRON_E_IO;
                }

                blake3_hasher_update(&out_hasher, op_chunk, chunk_len);
                copied += chunk_len;
            }

            out_pos += copy_len;

        } else if (opcode == DELTA2_OP_LIT) {
            /* LIT: length (u32) + data */
            if (patch_pos + 4 > patch_size) {
                return IRON_E_FORMAT;
            }

            uint8_t lit_len_buf[4];
            err = read_bytes(patch_r, patch_size, patch_pos, 4, lit_len_buf);
            if (err != IRON_OK) {
                return err;
            }

            uint32_t lit_len = read_u32_le(lit_len_buf);
            patch_pos += 4;

            /* Validate LIT length */
            if (lit_len == 0 || lit_len > DELTA2_MAX_LIT_LEN) {
                return IRON_E_FORMAT;
            }

            if (check_overflow_add_u64(patch_pos, lit_len, patch_size)) {
                return IRON_E_FORMAT;
            }

            if (check_overflow_add_u64(out_pos, lit_len, target_len)) {
                return IRON_E_FORMAT;
            }

            /* Copy literal data from patch to output */
            uint32_t copied = 0;
            while (copied < lit_len) {
                uint32_t chunk_len = (uint32_t)(lit_len - copied > STREAM_CHUNK_SIZE ? STREAM_CHUNK_SIZE : lit_len - copied);

                err = read_bytes(patch_r, patch_size, patch_pos + copied, chunk_len, op_chunk);
                if (err != IRON_OK) {
                    return err;
                }

                int write_err = out_w->write(out_w->ctx, out_pos + copied, op_chunk, chunk_len);
                if (write_err != 0) {
                    return IRON_E_IO;
                }

                blake3_hasher_update(&out_hasher, op_chunk, chunk_len);
                copied += chunk_len;
            }

            out_pos += lit_len;
            patch_pos += lit_len;

        } else {
            /* Unknown opcode */
            return IRON_E_FORMAT;
        }
    }

    /* === GATE 6: Verify output length === */
    if (out_pos != target_len) {
        return IRON_E_FORMAT;
    }

    /* === GATE 7: Verify output hash === */
    uint8_t target_hash_computed[DELTA2_HASH_SIZE];
    blake3_hasher_finalize(&out_hasher, target_hash_computed, DELTA2_HASH_SIZE);

    if (memcmp(target_hash_expected, target_hash_computed, DELTA2_HASH_SIZE) != 0) {
        return IRON_E_FORMAT;
    }

    return IRON_OK;
}
