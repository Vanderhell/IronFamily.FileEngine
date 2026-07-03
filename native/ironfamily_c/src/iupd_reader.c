/*
 * IUPD v2 Strict Verifier Implementation (C99)
 *
 * Fail-closed verification of IUPD v2 update packages.
 * Enforces: signature, UpdateSequence, DoS limits, profile whitelist.
 */

#include "ironfamily/iupd_reader.h"
#include "ironfamily/iupd_v2_spec_min.h"
#include "ed25519/ed25519.h"
#include "blake3/blake3.h"
#include <string.h>
#include <stdio.h>
#include <stdarg.h>
#include <limits.h>

/* Debug trace function (test-only, disabled by default) */
#ifdef IRONFAMILY_TRACE
static void trace_printf(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    vfprintf(stderr, fmt, args);
    va_end(args);
    fflush(stderr);
}
#else
#define trace_printf(...) do {} while(0)
#endif

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

static uint16_t read_u16_le(const uint8_t* data) {
    return ((uint16_t)data[0]) | (((uint16_t)data[1]) << 8);
}

static int checked_add_u64(uint64_t a, uint64_t b, uint64_t* out) {
    if (UINT64_MAX - a < b) {
        return 0;
    }
    *out = a + b;
    return 1;
}

static int checked_mul_u64(uint64_t a, uint64_t b, uint64_t* out) {
    if (a != 0 && UINT64_MAX / a < b) {
        return 0;
    }
    *out = a * b;
    return 1;
}

/* Helper: Read via reader interface with bounds checking */
static iron_error_t read_bytes(
    const iron_reader_t* r,
    uint64_t file_size,
    uint64_t offset,
    uint32_t len,
    uint8_t* dst
) {
    uint64_t end_offset;
    if (r == NULL || r->read == NULL) {
        return IRON_E_INVALID_ARGUMENT;
    }
    if (len > 0 && dst == NULL) {
        return IRON_E_INVALID_ARGUMENT;
    }
    if (!checked_add_u64(offset, len, &end_offset) || end_offset > file_size) {
        return IRON_E_FORMAT;
    }
    return r->read(r->ctx, offset, dst, len);
}

static iron_error_t hash_region_blake3(
    const iron_reader_t* r,
    uint64_t file_size,
    uint64_t offset,
    uint64_t len,
    uint8_t out_hash[32]
) {
    uint8_t scratch[512];
    uint64_t remaining = len;
    uint64_t cursor = offset;
    blake3_hasher hasher;

    if (out_hash == NULL) {
        return IRON_E_INVALID_ARGUMENT;
    }
    if (!checked_add_u64(offset, len, &cursor) || cursor > file_size) {
        return IRON_E_FORMAT;
    }

    cursor = offset;
    blake3_hasher_init(&hasher);
    while (remaining > 0) {
        uint32_t chunk = remaining > sizeof(scratch) ? (uint32_t)sizeof(scratch) : (uint32_t)remaining;
        iron_error_t err = read_bytes(r, file_size, cursor, chunk, scratch);
        if (err != IRON_OK) {
            return err;
        }
        blake3_hasher_update(&hasher, scratch, chunk);
        cursor += chunk;
        remaining -= chunk;
    }
    blake3_hasher_finalize(&hasher, out_hash, 32);
    return IRON_OK;
}

static iron_error_t validate_chunk_table(
    const iron_reader_t* r,
    uint64_t file_size,
    uint64_t chunk_table_offset,
    uint64_t manifest_offset,
    uint64_t payload_offset,
    uint64_t chunk_count
) {
    uint64_t expected_chunk_table_size;
    uint8_t entry_buf[IUPD_CHUNK_ENTRY_SIZE];
    uint32_t previous_index = 0;
    int have_previous_index = 0;

    if (!checked_mul_u64(chunk_count, IUPD_CHUNK_ENTRY_SIZE, &expected_chunk_table_size)) {
        return IRON_E_DOS_LIMIT;
    }
    if (chunk_table_offset > manifest_offset || manifest_offset - chunk_table_offset != expected_chunk_table_size) {
        return IRON_E_FORMAT;
    }

    for (uint64_t i = 0; i < chunk_count; i++) {
        uint64_t entry_offset;
        uint64_t payload_end;
        uint32_t chunk_index;
        uint64_t payload_size;
        uint64_t entry_payload_offset;

        if (!checked_mul_u64(i, IUPD_CHUNK_ENTRY_SIZE, &entry_offset) ||
            !checked_add_u64(chunk_table_offset, entry_offset, &entry_offset)) {
            return IRON_E_DOS_LIMIT;
        }

        if (read_bytes(r, file_size, entry_offset, IUPD_CHUNK_ENTRY_SIZE, entry_buf) != IRON_OK) {
            return IRON_E_FORMAT;
        }

        chunk_index = read_u32_le(&entry_buf[0]);
        payload_size = read_u64_le(&entry_buf[4]);
        entry_payload_offset = read_u64_le(&entry_buf[12]);

        if (have_previous_index && chunk_index <= previous_index) {
            return IRON_E_FORMAT;
        }
        previous_index = chunk_index;
        have_previous_index = 1;

        if (payload_size > IUPD_MAX_CHUNK_SIZE) {
            return IRON_E_DOS_LIMIT;
        }
        if (entry_payload_offset < payload_offset) {
            return IRON_E_FORMAT;
        }
        if (!checked_add_u64(entry_payload_offset, payload_size, &payload_end) || payload_end > file_size) {
            return IRON_E_FORMAT;
        }
    }

    return IRON_OK;
}

/*
 * Main verification function
 */
iron_error_t iron_iupd_verify_strict(
    const iron_reader_t* r,
    uint64_t file_size,
    const uint8_t ed25519_pubkey[32],
    uint64_t expected_min_update_sequence,
    uint64_t* out_update_sequence
) {
    uint8_t header_buf[IUPD_V2_HEADER_SIZE];
    uint8_t manifest_header_buf[IUPD_MANIFEST_HEADER_SIZE];
    uint8_t sig_len_buf[4];
    uint8_t signature_buf[IUPD_SIGNATURE_LENGTH];
    uint8_t witness_hash_buf[IUPD_WITNESS_HASH_LENGTH];
    uint8_t trailer_buf[IUPD_UPDATESEQ_TRAILER_SIZE];
    uint8_t manifest_hash[32];
    uint64_t manifest_end;
    uint64_t signature_end;
    uint64_t signed_region_size;
    uint64_t extracted_sequence = 0;
    iron_error_t err;

    if (r == NULL || r->read == NULL || ed25519_pubkey == NULL || out_update_sequence == NULL) {
        return IRON_E_INVALID_ARGUMENT;
    }

    *out_update_sequence = 0;

    trace_printf("[TRACE] === IUPD v2 Verification Start ===\n");
    trace_printf("[TRACE] file_size=%llu\n", (unsigned long long)file_size);
    trace_printf("[TRACE] expected_min_update_sequence=%llu\n", (unsigned long long)expected_min_update_sequence);

    /* === GATE 1: File size check === */
    if (file_size < IUPD_V2_HEADER_SIZE) {
        trace_printf("[TRACE] FAIL: file_size < HEADER_SIZE\n");
        return IRON_E_FORMAT;
    }

    /* === GATE 2: Read and parse header === */
    err = read_bytes(r, file_size, 0, IUPD_V2_HEADER_SIZE, header_buf);
    if (err != IRON_OK) return err;

    /* Check magic */
    uint32_t magic = read_u32_le(&header_buf[IUPD_V2_MAGIC_OFFSET]);
    if (magic != IUPD_MAGIC) {
        return IRON_E_FORMAT;
    }

    /* Check version */
    uint8_t version = header_buf[IUPD_V2_VERSION_OFFSET];
    trace_printf("[TRACE] version=0x%02x\n", version);
    if (version != IUPD_VERSION_V2) {
        trace_printf("[TRACE] FAIL: unsupported version\n");
        return IRON_E_UNSUPPORTED_VERSION;
    }

    /* === GATE 3: Profile whitelist === */
    uint8_t profile = header_buf[IUPD_V2_PROFILE_OFFSET];
    const char* profile_name = (profile == IUPD_PROFILE_SECURE) ? "SECURE" :
                               (profile == IUPD_PROFILE_OPTIMIZED) ? "OPTIMIZED" :
                               (profile == IUPD_PROFILE_INCREMENTAL) ? "INCREMENTAL" : "OTHER";
    trace_printf("[TRACE] profile=0x%02x (%s)\n", profile, profile_name);
    if (profile != IUPD_PROFILE_SECURE && profile != IUPD_PROFILE_OPTIMIZED && profile != IUPD_PROFILE_INCREMENTAL) {
        trace_printf("[TRACE] FAIL: profile not allowed\n");
        return IRON_E_PROFILE_NOT_ALLOWED;
    }

    /* Check flags (only WITNESS_ENABLED allowed) */
    uint32_t flags = read_u32_le(&header_buf[IUPD_V2_FLAGS_OFFSET]);
    if ((flags & ~IUPD_V2_FLAGS_WITNESS_ENABLED) != 0) {
        return IRON_E_FORMAT;
    }

    /* Check header size */
    uint16_t header_size = read_u16_le(&header_buf[IUPD_V2_HEADER_SIZE_OFFSET]);
    if (header_size != IUPD_V2_HEADER_SIZE) {
        return IRON_E_FORMAT;
    }

    /* Check reserved byte */
    if (header_buf[IUPD_V2_RESERVED_OFFSET] != 0) {
        return IRON_E_FORMAT;
    }

    /* Parse offsets */
    uint64_t chunk_table_offset = read_u64_le(&header_buf[IUPD_V2_CHUNK_TABLE_OFFSET_OFFSET]);
    uint64_t manifest_offset = read_u64_le(&header_buf[IUPD_V2_MANIFEST_OFFSET_OFFSET]);
    uint64_t payload_offset = read_u64_le(&header_buf[IUPD_V2_PAYLOAD_OFFSET_OFFSET]);

    trace_printf("[TRACE] chunk_table_offset=%llu, manifest_offset=%llu, payload_offset=%llu\n",
                (unsigned long long)chunk_table_offset, (unsigned long long)manifest_offset,
                (unsigned long long)payload_offset);

    /* Basic range checks */
    if (chunk_table_offset < IUPD_V2_HEADER_SIZE || chunk_table_offset > file_size) {
        trace_printf("[TRACE] FAIL: chunk_table_offset invalid\n");
        return IRON_E_DOS_LIMIT;
    }
    if (manifest_offset < chunk_table_offset || manifest_offset > file_size) {
        trace_printf("[TRACE] FAIL: manifest_offset invalid\n");
        return IRON_E_FORMAT;
    }
    if (payload_offset < manifest_offset || payload_offset > file_size) {
        trace_printf("[TRACE] FAIL: payload_offset invalid\n");
        return IRON_E_FORMAT;
    }

    /* === GATE 4: Manifest size (DoS limit) === */
    err = read_bytes(r, file_size, manifest_offset, IUPD_MANIFEST_HEADER_SIZE, manifest_header_buf);
    if (err != IRON_OK) {
        trace_printf("[TRACE] FAIL: could not read manifest header\n");
        return err;
    }

    uint64_t manifest_size = read_u64_le(&manifest_header_buf[16]);
    trace_printf("[TRACE] manifest_size=%llu (MAX=%llu)\n", (unsigned long long)manifest_size,
                (unsigned long long)IUPD_MAX_MANIFEST_SIZE);
    if (manifest_size < IUPD_MANIFEST_HEADER_SIZE + IUPD_MANIFEST_CRCRESV_SIZE) {
        trace_printf("[TRACE] FAIL: manifest_size too small\n");
        return IRON_E_FORMAT;
    }
    if (manifest_size > IUPD_MAX_MANIFEST_SIZE) {
        trace_printf("[TRACE] FAIL: manifest_size > MAX (DOS_LIMIT)\n");
        return IRON_E_DOS_LIMIT;
    }
    if (!checked_add_u64(manifest_offset, manifest_size, &manifest_end) || manifest_end > file_size) {
        trace_printf("[TRACE] FAIL: manifest extends past file\n");
        return IRON_E_DOS_LIMIT;
    }

    /* === GATE 5: Chunk count (DoS limit) === */
    uint64_t chunk_table_size = manifest_offset - chunk_table_offset;
    if (chunk_table_size % IUPD_CHUNK_ENTRY_SIZE != 0) {
        trace_printf("[TRACE] FAIL: chunk_table_size not multiple of entry size\n");
        return IRON_E_FORMAT;
    }
    uint64_t chunk_count = chunk_table_size / IUPD_CHUNK_ENTRY_SIZE;
    trace_printf("[TRACE] chunk_count=%llu (MAX=%llu)\n", (unsigned long long)chunk_count,
                (unsigned long long)IUPD_MAX_CHUNKS);
    if (chunk_count > IUPD_MAX_CHUNKS) {
        trace_printf("[TRACE] FAIL: chunk_count > MAX (DOS_LIMIT)\n");
        return IRON_E_DOS_LIMIT;
    }

    err = validate_chunk_table(r, file_size, chunk_table_offset, manifest_offset, payload_offset, chunk_count);
    if (err != IRON_OK) {
        trace_printf("[TRACE] FAIL: chunk table validation failed\n");
        return err;
    }

    /* === GATE 7: UpdateSequence trailer validation (BEFORE signature) === */
    /* Validate early to catch anti-replay violations before expensive signature checks */
    trace_printf("[TRACE] trailer_offset check: payload_offset=%llu, TRAILER_SIZE=%u\n",
                (unsigned long long)payload_offset, IUPD_UPDATESEQ_TRAILER_SIZE);
    if (payload_offset >= IUPD_UPDATESEQ_TRAILER_SIZE) {
        uint64_t trailer_offset = payload_offset - IUPD_UPDATESEQ_TRAILER_SIZE;
        trace_printf("[TRACE] trailer_offset=%llu\n", (unsigned long long)trailer_offset);

        err = read_bytes(r, file_size, trailer_offset, IUPD_UPDATESEQ_TRAILER_SIZE, trailer_buf);
        if (err != IRON_OK) {
            if (expected_min_update_sequence > 0) {
                trace_printf("[TRACE] FAIL: trailer read failed while anti-replay required\n");
                return IRON_E_SEQ_INVALID;
            }
        } else if (memcmp(trailer_buf, IUPD_UPDATESEQ_MAGIC_STR, 8) == 0) {
            uint32_t trailer_len = read_u32_le(&trailer_buf[8]);
            uint8_t trailer_version = trailer_buf[12];

            trace_printf("[TRACE] trailer found: len=%u, version=%u\n", trailer_len, trailer_version);

            if (trailer_len != IUPD_UPDATESEQ_TRAILER_SIZE || trailer_version != IUPD_UPDATESEQ_VERSION) {
                trace_printf("[TRACE] FAIL: trailer format invalid (SEQ_INVALID)\n");
                return IRON_E_SEQ_INVALID;
            }

            extracted_sequence = read_u64_le(&trailer_buf[13]);
            if (extracted_sequence < expected_min_update_sequence) {
                trace_printf("[TRACE] FAIL: sequence < expected_min (SEQ_INVALID)\n");
                return IRON_E_SEQ_INVALID;
            }
        } else if (expected_min_update_sequence > 0) {
            trace_printf("[TRACE] FAIL: trailer missing while anti-replay required\n");
            return IRON_E_SEQ_INVALID;
        }
    } else if (expected_min_update_sequence > 0) {
        trace_printf("[TRACE] FAIL: payload_offset too small for mandatory trailer\n");
        return IRON_E_SEQ_INVALID;
    }

    /* === GATE 8: Signature verification === */
    /* Signature is at: manifest_offset + manifest_size
     * Format: [length:4][signature:64][witness:32]
     */
    uint64_t sig_footer_offset = manifest_end;
    trace_printf("[TRACE] sig_footer_offset=%llu\n", (unsigned long long)sig_footer_offset);

    err = read_bytes(r, file_size, sig_footer_offset, 4, sig_len_buf);
    if (err != IRON_OK) {
        trace_printf("[TRACE] FAIL: could not read signature length\n");
        return err;
    }

    uint32_t sig_len = read_u32_le(sig_len_buf);
    trace_printf("[TRACE] sig_len=%u (expected=%u)\n", sig_len, IUPD_SIGNATURE_LENGTH);
    if (sig_len != IUPD_SIGNATURE_LENGTH) {
        trace_printf("[TRACE] FAIL: signature length mismatch (SIG_INVALID)\n");
        return IRON_E_SIG_INVALID;
    }

    err = read_bytes(r, file_size, sig_footer_offset + 4, IUPD_SIGNATURE_LENGTH, signature_buf);
    if (err != IRON_OK) {
        trace_printf("[TRACE] FAIL: could not read signature bytes\n");
        return err;
    }

    /* Read manifest data to be signed (exclude last 8 bytes: CRC32 + reserved) */
    signed_region_size = manifest_size - IUPD_MANIFEST_CRCRESV_SIZE;
    trace_printf("[TRACE] signed_region_size=%llu (manifest_size=%llu - 8)\n",
                (unsigned long long)signed_region_size, (unsigned long long)manifest_size);
    if (signed_region_size == 0) {
        trace_printf("[TRACE] FAIL: signed_region_size is zero\n");
        return IRON_E_FORMAT;
    }
    err = hash_region_blake3(r, file_size, manifest_offset, signed_region_size, manifest_hash);
    if (err != IRON_OK) {
        trace_printf("[TRACE] FAIL: could not hash manifest data\n");
        return err;
    }

    trace_printf("[TRACE] manifest_hash (hex): ");
    for (int i = 0; i < 32; i++) {
        trace_printf("%02x", manifest_hash[i]);
    }
    trace_printf("\n");

    /* Verify signature using Ed25519 over the hash
     * ed25519_verify returns 1 on success, 0 on failure */
    int verify_result = ed25519_verify(signature_buf, manifest_hash, 32, ed25519_pubkey);
    trace_printf("[TRACE] ed25519_verify returned: %d\n", verify_result);
    if (!verify_result) {
        trace_printf("[TRACE] FAIL: signature verification failed (SIG_INVALID)\n");
        return IRON_E_SIG_INVALID;
    }

    if ((flags & IUPD_V2_FLAGS_WITNESS_ENABLED) != 0) {
        if (!checked_add_u64(sig_footer_offset, 4 + IUPD_SIGNATURE_LENGTH + IUPD_WITNESS_HASH_LENGTH, &signature_end) ||
            signature_end > file_size) {
            trace_printf("[TRACE] FAIL: witness footer missing\n");
            return IRON_E_SIG_INVALID;
        }
        err = read_bytes(r, file_size, sig_footer_offset + 4 + IUPD_SIGNATURE_LENGTH, IUPD_WITNESS_HASH_LENGTH, witness_hash_buf);
        if (err != IRON_OK) {
            trace_printf("[TRACE] FAIL: could not read witness hash\n");
            return err;
        }
        if (memcmp(witness_hash_buf, manifest_hash, IUPD_WITNESS_HASH_LENGTH) != 0) {
            trace_printf("[TRACE] FAIL: witness hash mismatch\n");
            return IRON_E_SIG_INVALID;
        }
    }

    if (expected_min_update_sequence > 0) {
        trace_printf("[TRACE] FAIL: authenticated sequence binding not present\n");
        return IRON_E_SEQ_INVALID;
    }

    *out_update_sequence = extracted_sequence;

    trace_printf("[TRACE] === ALL GATES PASSED, RETURNING OK ===\n");
    return IRON_OK;
}
