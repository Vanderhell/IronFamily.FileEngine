#ifndef IRONCFG_BJX_H
#define IRONCFG_BJX_H

#include <stdint.h>
#include <stddef.h>
#include "ironcfg_common.h"

#ifdef __cplusplus
extern "C" {
#endif

/* BJX Constants */
#define BJX_MAGIC_PRIMARY 0x31584A42U   /* "BJX1" in little-endian */
#define BJX_VERSION 0x00
#define BJX_HEADER_SIZE 32
#define BJX_SALT_SIZE 16
#define BJX_NONCE_SIZE 12
#define BJX_TAG_SIZE 16

#define BJX_MAX_ENCRYPTED_PAYLOAD_SIZE (64UL << 20)  /* 64 MB */
#define BJX_MAX_PLAIN_SIZE (64UL << 20)  /* 64 MB */

/* BJX Flags */
#define BJX_FLAG_KDF_PBKDF2 (1U << 0)      /* Bit 0: PBKDF2-HMAC-SHA256 key derivation */
#define BJX_FLAG_KDF_RAWKEY (1U << 1)      /* Bit 1: Raw 32-byte key */
#define BJX_FLAG_CIPHER_AESGCM (1U << 3)   /* Bit 3: AES-256-GCM cipher */

/* BJX Error Codes */
typedef enum {
    BJX_ERR_OK = 0x0000,
    BJX_ERR_INVALID_MAGIC = 0x0001,
    BJX_ERR_UNSUPPORTED_VERSION = 0x0002,
    BJX_ERR_INVALID_HEADER_SIZE = 0x0003,
    BJX_ERR_INVALID_FLAGS = 0x0004,
    BJX_ERR_OFFSET_OUT_OF_BOUNDS = 0x0005,
    BJX_ERR_SIZE_OUT_OF_BOUNDS = 0x0006,
    BJX_ERR_LIMIT_EXCEEDED = 0x0007,
    BJX_ERR_UNSUPPORTED_CIPHER = 0x0008,
    BJX_ERR_UNSUPPORTED_KDF = 0x0009,
    BJX_ERR_INVALID_SALT = 0x000A,
    BJX_ERR_INVALID_NONCE = 0x000B,
    BJX_ERR_AUTH_TAG_MISMATCH = 0x000C,
    BJX_ERR_DECRYPT_FAILED = 0x000D,
    BJX_ERR_PLAIN_SIZE_MISMATCH = 0x000E,
    BJX_ERR_INNER_FORMAT_INVALID = 0x000F,
    BJX_ERR_ALLOCATION_FAILED = 0x0010,
    BJX_ERR_NULL_ARGUMENT = 0x0011,
    BJX_ERR_RESERVED_NON_ZERO = 0x0012,
    BJX_ERR_TRUNCATED_FILE = 0x0013,
    BJX_ERR_INTERNAL_ERROR = 0x0014
} bjx_error_code_t;

/* BJX Error Structure */
typedef struct {
    bjx_error_code_t code;
    uint64_t byte_offset;
    const char *message;
} bjx_error_t;

/* BJX Reader Context */
typedef struct {
    const uint8_t *data;
    size_t size;

    /* Parsed header fields */
    uint32_t magic;
    uint8_t version;
    uint8_t flags;
    uint16_t header_size;
    uint32_t encrypted_payload_size;
    uint32_t plain_bjv_size;
    uint32_t salt_offset;
    uint32_t nonce_offset;
    uint32_t ciphertext_offset;
    uint32_t reserved;

    /* Parsed metadata */
    const uint8_t *salt;
    const uint8_t *nonce;
    const uint8_t *ciphertext;
    const uint8_t *tag;

    /* Decrypted plaintext (allocated during validate_strict) */
    uint8_t *plaintext;
    size_t plaintext_size;

    /* Last error */
    bjx_error_t last_error;
} bjx_ctx_t;

/* BJX Reader Functions */

/**
 * bjx_open - Parse and open a BJX file for reading.
 * @data: Binary data buffer containing BJX file
 * @size: Size of data buffer in bytes
 * @out: Output reader context (must not be NULL)
 *
 * Returns:
 *   ICFG_OK on success
 *   Non-zero on error (error is stored in out->last_error)
 *
 * On success, *out is initialized and can be used for validation.
 * The plaintext field is NULL until validate_strict is called.
 */
icfg_status_t bjx_open(const uint8_t *data, size_t size, bjx_ctx_t *out);

/**
 * bjx_validate_fast - Perform fast structural validation.
 * @ctx: Reader context from bjx_open()
 *
 * Returns:
 *   ICFG_OK on success (valid), non-zero on error
 *
 * Checks file structure, header, offsets, and bounds.
 * Does NOT perform decryption or authentication tag verification.
 * Error details are stored in ctx->last_error.
 */
icfg_status_t bjx_validate_fast(bjx_ctx_t *ctx);

/**
 * bjx_validate_strict - Perform full integrity validation with decryption.
 * @ctx: Reader context from bjx_open()
 * @password: Password for key derivation (for PBKDF2 mode), or NULL
 * @raw_key: Raw 32-byte key (for raw key mode), or NULL
 *
 * Returns:
 *   ICFG_OK on success (fully valid), non-zero on error
 *
 * Performs all fast checks plus:
 * - Key derivation (PBKDF2 or raw key)
 * - AES-256-GCM decryption
 * - Authentication tag verification
 * - Plaintext size validation
 *
 * On success, ctx->plaintext is allocated and contains decrypted data.
 * Caller MUST call bjx_close() to free plaintext.
 * Error details are stored in ctx->last_error.
 */
icfg_status_t bjx_validate_strict(bjx_ctx_t *ctx, const char *password, const uint8_t *raw_key);

/**
 * bjx_close - Clean up BJX context and free allocated memory.
 * @ctx: Reader context from bjx_open()
 *
 * Frees the plaintext buffer if it was allocated.
 */
void bjx_close(bjx_ctx_t *ctx);

#ifdef __cplusplus
}
#endif

#endif /* IRONCFG_BJX_H */
