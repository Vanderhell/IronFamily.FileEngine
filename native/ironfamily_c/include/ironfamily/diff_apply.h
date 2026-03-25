/*
 * DiffPack v1 Apply (Streaming)
 *
 * Device-side streaming application of binary patches.
 * No buffering: reads from base via reader callback, writes via writer callback.
 */

#ifndef DIFF_APPLY_H
#define DIFF_APPLY_H

#include <stdint.h>
#include "ironfamily/iupd_reader.h"
#include "ironfamily/io.h"

#ifdef __cplusplus
extern "C" {
#endif

/* === APPLY API === */
/**
 * Apply a DiffPack v1 patch to a base file, streaming output.
 *
 * @param base_r         Reader for base file (seek/read callback)
 * @param base_size      Size of base file (bytes)
 * @param patch_r        Reader for patch file (seek/read callback)
 * @param patch_size     Size of patch file (bytes)
 * @param out_w          Writer for output (seek/write callback)
 * @param out_expected   Expected output size (must match final size)
 * @param out_written    [OUT] Number of bytes written (optional, can be NULL)
 *
 * @return IRON_OK on success, error code otherwise (fail-closed)
 *
 * Error codes:
 * - IRON_OK (0)
 * - IRON_E_IO (I/O failure from reader/writer)
 * - IRON_E_FORMAT (patch format invalid: magic, version, header)
 * - IRON_E_DIFF_BASE_HASH (base hash mismatch)
 * - IRON_E_DIFF_TARGET_HASH (target hash mismatch)
 * - IRON_E_DIFF_OUT_OF_RANGE (COPY offset out of bounds)
 * - IRON_E_DIFF_MALFORMED (truncated opcode, invalid format)
 * - IRON_E_DIFF_LIMITS (DoS: op count, insert bytes, patch size, output size)
 */
int iron_diff_v1_apply(
    const iron_reader_t* base_r,
    uint64_t base_size,
    const iron_reader_t* patch_r,
    uint64_t patch_size,
    const iron_writer_t* out_w,
    uint64_t out_expected,
    uint64_t* out_written
);

#ifdef __cplusplus
}
#endif

#endif /* DIFF_APPLY_H */
