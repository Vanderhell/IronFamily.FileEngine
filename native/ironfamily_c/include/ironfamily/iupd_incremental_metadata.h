/*
 * IUPD INCREMENTAL Metadata Trailer (C99)
 *
 * Format (outside signature range, variable-length):
 * [0-7]    Magic: "IUPDINC1" (8 bytes ASCII)
 * [8-11]   Length: total trailer length (4 bytes, little-endian)
 * [12]     Version: trailer format version (1 byte, =1)
 * [13]     AlgorithmId: patch algorithm (1 byte, 0x01=DELTA_V1, 0x02=IRONDEL2)
 * [14]     BaseHashLength: length of base hash (1 byte)
 * [15+]    BaseHash: base image hash (BaseHashLength bytes, typically 32 for BLAKE3)
 * [...]    TargetHashLength: length of target hash (1 byte, optional)
 * [...]    TargetHash: target image hash (TargetHashLength bytes, optional)
 * [...]    CRC32: integrity check (4 bytes, little-endian)
 *
 * Variable size: 21 bytes minimum (no hashes) to ~84 bytes (32-byte hashes)
 */

#ifndef IUPD_INCREMENTAL_METADATA_H
#define IUPD_INCREMENTAL_METADATA_H

#include <stdint.h>
#include <stdbool.h>

/* Magic and version */
#define IUPD_INC_MAGIC_STR "IUPDINC1"
#define IUPD_INC_VERSION 1
#define IUPD_INC_TRAILER_MIN_SIZE 21

/* Algorithm IDs */
#define IUPD_ALGORITHM_UNSPECIFIED 0x00
#define IUPD_ALGORITHM_DELTA_V1 0x01
#define IUPD_ALGORITHM_IRONDEL2 0x02

/* Metadata structure */
typedef struct {
    uint8_t algorithm_id;
    uint8_t* base_hash;
    uint32_t base_hash_len;
    uint8_t* target_hash;
    uint32_t target_hash_len;
} iupd_incremental_metadata_t;

/*
 * Parse INCREMENTAL metadata trailer from buffer.
 * Returns true on success, false on failure.
 * On success, metadata fields point into trailer_data (no copy).
 * Caller must not free trailer_data while metadata is in use.
 */
bool iupd_incremental_metadata_parse(
    const uint8_t* trailer_data,
    uint32_t trailer_len,
    iupd_incremental_metadata_t* out_metadata
);

/*
 * Locate INCREMENTAL metadata trailer by magic.
 * Scans backwards from end looking for magic "IUPDINC1".
 * Returns offset of magic start on success, or -1 if not found.
 *
 * Typical usage:
 *   int64_t offset = iupd_incremental_metadata_find(pkg_data, pkg_size);
 *   if (offset >= 0) {
 *       const uint8_t* trailer = &pkg_data[offset];
 *       uint32_t trailer_len = pkg_size - offset;
 *       iupd_incremental_metadata_parse(trailer, trailer_len, &metadata);
 *   }
 */
int64_t iupd_incremental_metadata_find(
    const uint8_t* data,
    uint64_t data_len
);

/*
 * Check if algorithm ID is known/supported.
 */
static inline bool iupd_algorithm_is_known(uint8_t algorithm_id) {
    return algorithm_id == IUPD_ALGORITHM_DELTA_V1 ||
           algorithm_id == IUPD_ALGORITHM_IRONDEL2;
}

#endif /* IUPD_INCREMENTAL_METADATA_H */
