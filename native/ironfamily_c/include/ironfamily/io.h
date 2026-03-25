/*
 * Streaming I/O Interface for IUPD Verifier
 *
 * Abstraction to support testing without full-file reads.
 * No dynamic allocation in verifier itself; allocation (if needed)
 * is caller's responsibility via context.
 */

#ifndef IRON_IO_H
#define IRON_IO_H

#include <stdint.h>
#include "iupd_errors.h"

typedef struct {
    void* ctx;
    /* Read 'len' bytes at offset 'off' into 'dst'
     * Returns IRON_OK (0) on success, IRON_E_IO on failure
     */
    iron_error_t (*read)(void* ctx, uint64_t off, uint8_t* dst, uint32_t len);
} iron_reader_t;

typedef struct {
    void* ctx;
    /* Write 'len' bytes from 'src' at offset 'off'
     * Returns 0 on success, non-zero on failure
     */
    int (*write)(void* ctx, uint64_t off, const uint8_t* src, uint32_t len);
} iron_writer_t;

#endif /* IRON_IO_H */
