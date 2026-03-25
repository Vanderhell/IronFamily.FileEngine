/*
 * IUPD INCREMENTAL Metadata Trailer Implementation (C99)
 */

#include "ironfamily/iupd_incremental_metadata.h"
#include "ironfamily/crc32.h"
#include <string.h>

/* Helper: Read little-endian integers */
static uint32_t read_u32_le(const uint8_t* data) {
    return ((uint32_t)data[0]) | (((uint32_t)data[1]) << 8) |
           (((uint32_t)data[2]) << 16) | (((uint32_t)data[3]) << 24);
}

/*
 * Parse INCREMENTAL metadata trailer from buffer.
 * Structure (variable-length):
 * [0-7]    Magic: "IUPDINC1"
 * [8-11]   Length (u32 LE)
 * [12]     Version (u8)
 * [13]     AlgorithmId (u8)
 * [14]     BaseHashLength (u8)
 * [15+]    BaseHash (BaseHashLength bytes)
 * [...]    TargetHashLength (u8)
 * [...]    TargetHash (TargetHashLength bytes, optional)
 * [...]    CRC32 (u32 LE)
 */
bool iupd_incremental_metadata_parse(
    const uint8_t* trailer_data,
    uint32_t trailer_len,
    iupd_incremental_metadata_t* out_metadata
) {
    if (!trailer_data || !out_metadata || trailer_len < IUPD_INC_TRAILER_MIN_SIZE) {
        return false;
    }

    memset(out_metadata, 0, sizeof(*out_metadata));

    /* Verify magic */
    if (memcmp(trailer_data, IUPD_INC_MAGIC_STR, 8) != 0) {
        return false;
    }

    /* Read and verify length */
    uint32_t declared_len = read_u32_le(&trailer_data[8]);
    if (declared_len != trailer_len) {
        return false;
    }

    /* Read and verify version */
    uint8_t version = trailer_data[12];
    if (version != IUPD_INC_VERSION) {
        return false;
    }

    /* Read algorithm ID */
    uint8_t algorithm_id = trailer_data[13];
    if (!iupd_algorithm_is_known(algorithm_id)) {
        return false;
    }

    /* Read base hash length and hash */
    uint8_t base_hash_len = trailer_data[14];
    if (base_hash_len == 0) {
        return false;
    }

    if (15 + base_hash_len > trailer_len) {
        return false;
    }

    /* Read target hash length and optional hash */
    uint32_t target_hash_offset = 15 + base_hash_len;
    if (target_hash_offset >= trailer_len) {
        return false;
    }

    uint8_t target_hash_len = trailer_data[target_hash_offset];
    uint32_t crc32_offset = target_hash_offset + 1 + target_hash_len;

    if (crc32_offset + 4 > trailer_len) {
        return false;
    }

    /* Verify CRC32 (computed over everything except the CRC32 field itself) */
    uint32_t stored_crc32 = read_u32_le(&trailer_data[crc32_offset]);
    uint32_t computed_crc32 = iron_crc32(trailer_data, crc32_offset);

    if (stored_crc32 != computed_crc32) {
        return false;
    }

    /* All checks passed, populate output */
    out_metadata->algorithm_id = algorithm_id;
    out_metadata->base_hash = (uint8_t*)&trailer_data[15];
    out_metadata->base_hash_len = base_hash_len;

    if (target_hash_len > 0) {
        out_metadata->target_hash = (uint8_t*)&trailer_data[target_hash_offset + 1];
        out_metadata->target_hash_len = target_hash_len;
    } else {
        out_metadata->target_hash = NULL;
        out_metadata->target_hash_len = 0;
    }

    return true;
}

/*
 * Locate INCREMENTAL metadata trailer by magic.
 * Scans backwards from end of data looking for "IUPDINC1" magic.
 * Returns offset of magic start, or -1 if not found.
 */
int64_t iupd_incremental_metadata_find(
    const uint8_t* data,
    uint64_t data_len
) {
    if (!data || data_len < IUPD_INC_TRAILER_MIN_SIZE) {
        return -1;
    }

    /* Scan backwards from end */
    for (int64_t i = (int64_t)data_len - IUPD_INC_TRAILER_MIN_SIZE; i >= 0; i--) {
        if (memcmp(&data[i], IUPD_INC_MAGIC_STR, 8) == 0) {
            return i;
        }
    }

    return -1;
}
