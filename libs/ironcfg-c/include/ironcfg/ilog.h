/*
 * ilog.h
 * ILOG (Log / Stream Container) zero-copy C99 reader
 *
 * Status: Reference implementation (DESIGN)
 * License: MIT
 *
 * Features:
 * - Zero-copy: No allocations required for header/TOC parsing
 * - Bounds-safe: All reads validated against buffer size
 * - Two-mode validation: validate_fast and validate_strict
 * - CRC32 IEEE and BLAKE3 integrity verification
 * - Deterministic error reporting with byte offsets
 */

#ifndef IRONCFG_ILOG_H
#define IRONCFG_ILOG_H

#include "ironcfg_common.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================================
 * Constants
 * ============================================================================ */

#define ILOG_MAGIC_PRIMARY   0x474F4C49U  /* "ILOG" in little-endian */
#define ILOG_MAGIC_EXTENDED  0x314B4C42U  /* "BLK1" in little-endian */
#define ILOG_VERSION         0x01
#define ILOG_MIN_HEADER_SIZE 8

/* ILOG-specific error codes */
typedef enum {
    ILOG_ERR_INVALID_MAGIC = 0x0001,
    ILOG_ERR_UNSUPPORTED_VERSION = 0x0002,
    ILOG_ERR_CORRUPTED_HEADER = 0x0003,
    ILOG_ERR_MISSING_LAYER = 0x0004,
    ILOG_ERR_MALFORMED_BLOCK = 0x0005,
    ILOG_ERR_BLOCK_OUT_OF_BOUNDS = 0x0006,
    ILOG_ERR_INVALID_BLOCK_TYPE = 0x0007,
    ILOG_ERR_SCHEMA_VALIDATION = 0x0008,
    ILOG_ERR_OUT_OF_BOUNDS_REF = 0x0009,
    ILOG_ERR_DICT_LOOKUP = 0x000A,
    ILOG_ERR_VARINT_DECODE = 0x000B,
    ILOG_ERR_CRC32_MISMATCH = 0x000C,
    ILOG_ERR_BLAKE3_MISMATCH = 0x000D,
    ILOG_ERR_COMPRESSION_FAILED = 0x000E,
    ILOG_ERR_RECORD_TRUNCATED = 0x000F,
    ILOG_ERR_DEPTH_LIMIT = 0x0010,
    ILOG_ERR_FILE_SIZE_LIMIT = 0x0011,
    ILOG_ERR_RECORD_COUNT_LIMIT = 0x0012,
    ILOG_ERR_STRING_LENGTH_LIMIT = 0x0013,
    ILOG_ERR_CRITICAL_FLAG = 0x0014,
} ilog_error_code_t;

/* ============================================================================
 * Types
 * ============================================================================ */

/* Error information with byte offset */
typedef struct {
    ilog_error_code_t code;
    uint64_t byte_offset;
    const char* message;
} ilog_error_t;

/* File flags (bit 0 of flags byte) */
typedef struct {
    bool little_endian;      /* Bit 0 */
    bool has_crc32;          /* Bit 1 */
    bool has_blake3;         /* Bit 2 */
    bool has_layer_l2;       /* Bit 3 */
    bool has_layer_l3;       /* Bit 4 */
    bool has_layer_l4;       /* Bit 5 */
    uint8_t reserved;        /* Bits 6-7 */
} ilog_flags_t;

/* ILOG file view (zero-copy) */
typedef struct {
    const uint8_t* data;
    size_t size;

    /* Header fields */
    uint32_t magic;
    uint8_t version;
    ilog_flags_t flags;

    /* Layer offsets (populated by header parsing) */
    uint64_t l0_offset;
    uint64_t l1_offset;
    uint64_t l2_offset;
    uint64_t l3_offset;
    uint64_t l4_offset;

    /* Parsed TOC metadata */
    uint32_t block_count;
    uint32_t record_count;

    /* Last error (if any) */
    ilog_error_t last_error;
} ilog_view_t;

/* ============================================================================
 * Core Functions
 * ============================================================================ */

/**
 * Open and parse ILOG file header (fast mode)
 *
 * Performs quick validation:
 * - Magic number check
 * - Version check
 * - Flags consistency
 * - Header structural integrity
 * - L1 presence check
 *
 * Args:
 *   data: Pointer to ILOG file bytes
 *   size: Size of data buffer
 *   out: Output view (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   ICFG_ERR_MAGIC or other error code on failure
 */
icfg_status_t ilog_open(const uint8_t* data, size_t size, ilog_view_t* out);

/**
 * Fast validation (gate check only)
 *
 * Checks:
 * - Magic and version
 * - Flags consistency
 * - Header structure
 * - L1 readability
 *
 * Completes in O(1) or O(header size) time.
 *
 * Args:
 *   v: View from ilog_open()
 *
 * Returns:
 *   ICFG_OK if file structure is valid for reading
 *   Error code with byte_offset if invalid
 */
icfg_status_t ilog_validate_fast(const ilog_view_t* v);

/**
 * Strict validation (full integrity check)
 *
 * Checks:
 * - All from validate_fast
 * - L0 record enumeration and schema validation
 * - Optional layer presence validation
 * - Integrity seal verification (CRC32, BLAKE3)
 * - Cross-layer consistency
 *
 * May require O(file size) time.
 *
 * Args:
 *   v: View from ilog_open()
 *
 * Returns:
 *   ICFG_OK if all validations pass
 *   Error code with byte_offset if any check fails
 */
icfg_status_t ilog_validate_strict(const ilog_view_t* v);

/**
 * Get the last error details
 *
 * Args:
 *   v: View from ilog_open()
 *
 * Returns:
 *   Pointer to error struct (includes code, byte_offset, message)
 */
const ilog_error_t* ilog_get_error(const ilog_view_t* v);

/**
 * Get record/event count from parsed L1 TOC
 *
 * Args:
 *   v: View from ilog_open()
 *   out: Output count (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 */
icfg_status_t ilog_record_count(const ilog_view_t* v, uint32_t* out);

/**
 * Get block count from parsed L1 TOC
 *
 * Args:
 *   v: View from ilog_open()
 *   out: Output count (must be non-NULL)
 *
 * Returns:
 *   ICFG_OK on success
 */
icfg_status_t ilog_block_count(const ilog_view_t* v, uint32_t* out);

/**
 * Verify CRC32 if present
 *
 * Args:
 *   v: View from ilog_open()
 *
 * Returns:
 *   ICFG_OK if CRC32 is valid or not present
 *   ILOG_ERR_CRC32_MISMATCH if CRC32 mismatch
 *   ILOG_ERR_BLOCK_OUT_OF_BOUNDS if CRC not found
 */
icfg_status_t ilog_verify_crc32(const ilog_view_t* v);

/**
 * Verify BLAKE3 if present (strict mode)
 *
 * Args:
 *   v: View from ilog_open()
 *
 * Returns:
 *   ICFG_OK if BLAKE3 is valid or not present
 *   ILOG_ERR_BLAKE3_MISMATCH if BLAKE3 mismatch
 *   ILOG_ERR_BLOCK_OUT_OF_BOUNDS if BLAKE3 not found
 */
icfg_status_t ilog_verify_blake3(const ilog_view_t* v);

#ifdef __cplusplus
}
#endif

#endif /* IRONCFG_ILOG_H */
