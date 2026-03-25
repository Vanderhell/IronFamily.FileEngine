/*
 * ironcfg_common.h
 * Common types and utilities for IronConfig C99 readers
 *
 * Status: Production-ready
 * License: MIT
 */

#ifndef IRONCFG_COMMON_H
#define IRONCFG_COMMON_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <string.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================================
 * Status Codes
 * ============================================================================ */

typedef enum {
    ICFG_OK = 0,                   /* Success */
    ICFG_ERR_MAGIC = 1,            /* Invalid magic number */
    ICFG_ERR_BOUNDS = 2,           /* Data out of bounds */
    ICFG_ERR_CRC = 3,              /* CRC32 mismatch */
    ICFG_ERR_SCHEMA = 4,           /* Schema validation error */
    ICFG_ERR_TYPE = 5,             /* Type mismatch */
    ICFG_ERR_RANGE = 6,            /* Value out of range */
    ICFG_ERR_UNSUPPORTED = 7,      /* Unsupported feature */
    ICFG_ERR_INVALID_ARGUMENT = 8, /* Invalid argument */
} icfg_status_t;

/* ============================================================================
 * Utility Functions
 * ============================================================================ */

/* Read little-endian u32 from buffer at offset */
static inline uint32_t icfg_read_u32_le(const uint8_t* buf, size_t offset) {
    return ((uint32_t)buf[offset]) |
           (((uint32_t)buf[offset + 1]) << 8) |
           (((uint32_t)buf[offset + 2]) << 16) |
           (((uint32_t)buf[offset + 3]) << 24);
}

/* Read little-endian u64 from buffer at offset */
static inline uint64_t icfg_read_u64_le(const uint8_t* buf, size_t offset) {
    return ((uint64_t)icfg_read_u32_le(buf, offset)) |
           (((uint64_t)icfg_read_u32_le(buf, offset + 4)) << 32);
}

/* Read little-endian i64 from buffer at offset */
static inline int64_t icfg_read_i64_le(const uint8_t* buf, size_t offset) {
    return (int64_t)icfg_read_u64_le(buf, offset);
}

/* Read IEEE 754 double from buffer at offset (little-endian) */
static inline double icfg_read_f64_le(const uint8_t* buf, size_t offset) {
    uint64_t bits = icfg_read_u64_le(buf, offset);
    double result;
    memcpy(&result, &bits, sizeof(double));
    return result;
}

/* Compute CRC32 IEEE 802.3 (0x04C11DB7) over buffer */
uint32_t icfg_crc32(const uint8_t* data, size_t size);

#ifdef __cplusplus
}
#endif

#endif /* IRONCFG_COMMON_H */
