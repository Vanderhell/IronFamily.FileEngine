/*
 * OTA Client Bundle: Integrated IUPD v2 Verify + IUPD Delta v1 Apply
 *
 * High-level API for device-side OTA application:
 * 1. Verify IUPD v2 package (signature, UpdateSequence, DoS limits)
 * 2. Extract and locate embedded delta payload
 * 3. Apply delta to base image
 * 4. Verify output hash matches target
 * 5. Return next UpdateSequence for persistence
 *
 * No buffering, streaming I/O, no heap allocation.
 */

#ifndef OTA_APPLY_H
#define OTA_APPLY_H

#include <stdint.h>
#include "io.h"
#include "iupd_errors.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct
{
    const iron_reader_t* base_r;
    uint64_t base_size;
    const iron_reader_t* iupd_pkg_r;
    uint64_t iupd_pkg_size;
    const iron_reader_t* delta_r;      /* Delta v1 reader (embedded or external) */
    uint64_t delta_size;
    const iron_writer_t* out_w;
    const iron_reader_t* out_r;
    uint64_t out_size_expected;
    const uint8_t* pubkey32;
    uint64_t expected_min_update_sequence;
    uint64_t* out_update_sequence;
} iron_ota_apply_ctx_t;

iron_error_t iron_ota_apply_iupd_v2_delta_v1(const iron_ota_apply_ctx_t* ctx);

/*
 * OTA Apply: Verify IUPD v2 INCREMENTAL + Apply dispatched algorithm
 *
 * Supports both DELTA_V1 and IRONDEL2 algorithms based on metadata.
 * Validates:
 * - IUPD v2 signature and UpdateSequence
 * - INCREMENTAL profile
 * - INCREMENTAL metadata trailer (magic, CRC32, algorithm)
 * - Base image BLAKE3-256 hash binding
 * - Output image BLAKE3-256 hash (if metadata target hash present)
 *
 * Returns IRON_E_PROFILE_NOT_ALLOWED if profile is not INCREMENTAL.
 * Returns IRON_E_SIG_INVALID if metadata trailer is missing/malformed.
 * Returns IRON_E_FORMAT if unknown algorithm ID detected.
 * Returns IRON_E_BLAKE3_MISMATCH if base/target hash validation fails.
 */
iron_error_t iron_ota_apply_iupd_v2_incremental(const iron_ota_apply_ctx_t* ctx);

#ifdef __cplusplus
}
#endif

#endif /* OTA_APPLY_H */
