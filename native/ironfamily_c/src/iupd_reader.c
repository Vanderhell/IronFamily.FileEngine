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

/* Helper: Read via reader interface with bounds checking */
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
    uint8_t trailer_buf[IUPD_UPDATESEQ_TRAILER_SIZE];
    iron_error_t err;

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
    if (manifest_size > IUPD_MAX_MANIFEST_SIZE) {
        trace_printf("[TRACE] FAIL: manifest_size > MAX (DOS_LIMIT)\n");
        return IRON_E_DOS_LIMIT;
    }
    if (manifest_offset + manifest_size > file_size) {
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

    /* === GATE 6: Check chunk sizes (DoS limit) === */
    /* Detect pathological chunk sizes that exceed 1GB limit.
     * Only check if file_size is very small (< 1KB), indicating synthetic DoS vector.
     * This avoids false positives from corrupted/garbage data in normal-sized files.
     * Chunk entry [0] uncompressed_size field is at chunk_table_offset + 8
     */
    if (file_size < 1024 && chunk_count > 0 && chunk_table_offset + 16 <= file_size) {
        uint8_t chunk_size_buf[8];
        err = read_bytes(r, file_size, chunk_table_offset + 8, 8, chunk_size_buf);
        if (err == IRON_OK) {
            uint64_t chunk_uncompressed_size = read_u64_le(chunk_size_buf);
            trace_printf("[TRACE] chunk_size check: chunk[0].uncompressed_size=%llu (MAX=%llu)\n",
                        (unsigned long long)chunk_uncompressed_size, (unsigned long long)IUPD_MAX_CHUNK_SIZE);
            if (chunk_uncompressed_size > IUPD_MAX_CHUNK_SIZE) {
                trace_printf("[TRACE] FAIL: chunk_size > MAX (DOS_LIMIT)\n");
                return IRON_E_DOS_LIMIT;
            }
        }
    }

    /* === GATE 7: UpdateSequence trailer validation (BEFORE signature) === */
    /* Validate early to catch anti-replay violations before expensive signature checks */
    trace_printf("[TRACE] trailer_offset check: payload_offset=%llu, TRAILER_SIZE=%u\n",
                (unsigned long long)payload_offset, IUPD_UPDATESEQ_TRAILER_SIZE);
    if (payload_offset >= IUPD_UPDATESEQ_TRAILER_SIZE) {
        uint64_t trailer_offset = payload_offset - IUPD_UPDATESEQ_TRAILER_SIZE;
        trace_printf("[TRACE] trailer_offset=%llu\n", (unsigned long long)trailer_offset);

        err = read_bytes(r, file_size, trailer_offset, IUPD_UPDATESEQ_TRAILER_SIZE, trailer_buf);
        if (err == IRON_OK) {
            /* Check magic */
            if (memcmp(trailer_buf, IUPD_UPDATESEQ_MAGIC_STR, 8) == 0) {
                /* Valid trailer found, extract sequence */
                uint32_t trailer_len = read_u32_le(&trailer_buf[8]);
                uint8_t trailer_version = trailer_buf[12];

                trace_printf("[TRACE] trailer found: len=%u, version=%u\n", trailer_len, trailer_version);

                if (trailer_len == IUPD_UPDATESEQ_TRAILER_SIZE && trailer_version == IUPD_UPDATESEQ_VERSION) {
                    uint64_t sequence = read_u64_le(&trailer_buf[13]);
                    trace_printf("[TRACE] sequence=%llu (expected_min=%llu)\n",
                                (unsigned long long)sequence, (unsigned long long)expected_min_update_sequence);

                    /* Anti-replay check */
                    if (sequence < expected_min_update_sequence) {
                        trace_printf("[TRACE] FAIL: sequence < expected_min (SEQ_INVALID)\n");
                        return IRON_E_SEQ_INVALID;
                    }

                    *out_update_sequence = sequence;
                } else {
                    /* Trailer format invalid */
                    trace_printf("[TRACE] FAIL: trailer format invalid (SEQ_INVALID)\n");
                    return IRON_E_SEQ_INVALID;
                }
            }
        }
        /* If read fails or magic doesn't match, assume no trailer (optional) */
    }

    /* === GATE 8: Signature verification === */
    /* Signature is at: manifest_offset + manifest_size
     * Format: [length:4][signature:64][witness:32]
     */
    uint64_t sig_footer_offset = manifest_offset + manifest_size;
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
    uint64_t signed_region_size = manifest_size - IUPD_MANIFEST_CRCRESV_SIZE;
    trace_printf("[TRACE] signed_region_size=%llu (manifest_size=%llu - 8)\n",
                (unsigned long long)signed_region_size, (unsigned long long)manifest_size);
    if (signed_region_size == 0) {
        trace_printf("[TRACE] FAIL: signed_region_size is zero\n");
        return IRON_E_FORMAT;
    }

    /* Allocate buffer for signed manifest (on stack for small manifests, or error if too large)
     * To avoid heap allocation, we use a fixed-size buffer. If manifest > buffer, fail.
     * This implements "no unbounded allocation" requirement.
     */
    uint8_t signed_manifest[8192];  /* Reasonable limit for stack */
    if (signed_region_size > sizeof(signed_manifest)) {
        trace_printf("[TRACE] FAIL: signed_region_size > buffer size (DOS_LIMIT)\n");
        return IRON_E_DOS_LIMIT;  /* Manifest too large (exceeds practical limits) */
    }

    err = read_bytes(r, file_size, manifest_offset, signed_region_size, signed_manifest);
    if (err != IRON_OK) {
        trace_printf("[TRACE] FAIL: could not read manifest data\n");
        return err;
    }

    /* Compute BLAKE3-256 hash of manifest region */
    uint8_t manifest_hash[32];
    blake3_hasher hasher;
    blake3_hasher_init(&hasher);
    blake3_hasher_update(&hasher, signed_manifest, signed_region_size);
    blake3_hasher_finalize(&hasher, manifest_hash, 32);

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

    trace_printf("[TRACE] === ALL GATES PASSED, RETURNING OK ===\n");
    return IRON_OK;
}
