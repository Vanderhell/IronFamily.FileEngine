/*
 * IUPD v2 Strict Verifier (C99)
 *
 * Minimal device-side verification of IUPD v2 files with fail-closed semantics:
 * - Signature verification (Ed25519)
 * - UpdateSequence validation (anti-replay)
 * - DoS limit enforcement
 * - Profile whitelist
 *
 * No dynamic allocation by default (zero-copy, streaming I/O).
 * Verification only; no writer/builder included.
 */

#ifndef IUPD_READER_H
#define IUPD_READER_H

#include <stdint.h>
#include "io.h"
#include "iupd_errors.h"

/* Debug trace flag (test-only, disabled by default) */
#ifdef IRONFAMILY_TRACE
#define TRACE_ENABLED 1
#else
#define TRACE_ENABLED 0
#endif

/*
 * Strict IUPD v2 Verification
 *
 * Verifies:
 * 1. IUPD v2 magic and version
 * 2. Profile is SECURE or OPTIMIZED (fail-closed)
 * 3. UpdateSequence trailer (if present) >= expected_min_update_sequence
 * 4. Ed25519 signature over manifest
 * 5. DoS limits (manifest size, chunk count, chunk size)
 *
 * Args:
 *   r:                      Reader interface (file access)
 *   file_size:              Total file size in bytes
 *   ed25519_pubkey:         Ed25519 public key (32 bytes)
 *   expected_min_update_sequence:  Minimum acceptable sequence (anti-replay)
 *   out_update_sequence:    Output: extracted sequence from trailer (0 if no trailer)
 *
 * Returns:
 *   IRON_OK on successful verification
 *   IRON_E_* on any verification failure
 */
iron_error_t iron_iupd_verify_strict(
    const iron_reader_t* r,
    uint64_t file_size,
    const uint8_t ed25519_pubkey[32],
    uint64_t expected_min_update_sequence,
    uint64_t* out_update_sequence
);

#endif /* IUPD_READER_H */
