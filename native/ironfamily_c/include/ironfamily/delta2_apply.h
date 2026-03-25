/*
 * IRONDEL2 (Delta v2) Apply API
 *
 * Streaming application of CDC-based delta patches.
 * Fail-closed: validates header, base hash, output length, and output hash.
 *
 * No heap allocations: all buffers are stack-based (64KB chunks).
 * Streaming I/O: supports reader/writer callbacks for large files.
 */

#ifndef DELTA2_APPLY_H
#define DELTA2_APPLY_H

#include "io.h"
#include <stdint.h>

/*
 * Apply IRONDEL2 patch to base file, writing reconstructed output.
 *
 * STRICT VALIDATION (fail-closed):
 * 1. Magic and version validation
 * 2. Header CRC32 verification
 * 3. Base length and target length matching
 * 4. Base hash verification (BLAKE3-256)
 * 5. Op stream parsing and validation
 * 6. Output length must equal target_len exactly
 * 7. Output hash verification (BLAKE3-256)
 *
 * @param base_r         Reader for base file
 * @param base_size      Size of base file in bytes
 * @param patch_r        Reader for IRONDEL2 patch file
 * @param patch_size     Size of patch file in bytes
 * @param out_w          Writer for output file
 * @param out_size_expected Expected output size (must match header target_len)
 * @param flags          Reserved for future use (must be 0)
 *
 * @return IRON_OK on success
 * @return IRON_E_FORMAT on format violation
 * @return IRON_E_LIMITS on DoS limit exceeded
 * @return IRON_E_HASH_MISMATCH on hash verification failure
 * @return IRON_E_IO on I/O error
 *
 * USAGE EXAMPLE:
 *   iron_reader_t base_r = ...; // opened base file
 *   iron_reader_t patch_r = ...;  // opened patch file
 *   iron_writer_t out_w = ...;  // opened output file
 *
 *   int err = iron_delta2_apply(
 *       &base_r, base_size,
 *       &patch_r, patch_size,
 *       &out_w, out_size_expected,
 *       0
 *   );
 *
 *   if (err != IRON_OK) {
 *       // Handle error
 *   }
 */
int iron_delta2_apply(
    const iron_reader_t* base_r,
    uint64_t base_size,
    const iron_reader_t* patch_r,
    uint64_t patch_size,
    const iron_writer_t* out_w,
    uint64_t out_size_expected,
    uint32_t flags
);

#endif /* DELTA2_APPLY_H */
