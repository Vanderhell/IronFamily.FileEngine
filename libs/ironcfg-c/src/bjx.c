/*
 * bjx.c
 * BJX1 encrypted container reader for IRONCFG.
 * Provides OpenSSL-based decryption using PBKDF2-HMAC-SHA256 + AES-256-GCM.
 *
 * Status: C99 reference implementation
 * License: MIT
 */

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <string.h>
#include <stdlib.h>

#include <openssl/evp.h>
#include <openssl/crypto.h>

#include "ironcfg/ironcfg_common.h"
#include "ironcfg/bjx.h"

/* Forward declarations for internal functions */
static void bjx_set_error(bjx_ctx_t *ctx, bjx_error_code_t code, uint64_t offset, const char *msg);
static icfg_status_t bjx_validate_header(bjx_ctx_t *ctx);
static icfg_status_t bjx_validate_bounds(bjx_ctx_t *ctx);
static uint32_t bjx_derive_key_pbkdf2(const char *password, const uint8_t *salt,
                                      uint8_t *out_key, size_t key_len);
static icfg_status_t bjx_decrypt_aes_gcm(const uint8_t *ciphertext, size_t ciphertext_len,
                                         const uint8_t *tag, const uint8_t *nonce,
                                         const uint8_t *aad, size_t aad_len,
                                         const uint8_t *key, uint8_t *plaintext);

/* ============================================================================
 * Helper Functions
 * ============================================================================ */

static void bjx_set_error(bjx_ctx_t *ctx, bjx_error_code_t code, uint64_t offset, const char *msg) {
    if (ctx == NULL) return;
    ctx->last_error.code = code;
    ctx->last_error.byte_offset = offset;
    ctx->last_error.message = msg;
}

static uint32_t bjx_read_u32_le(const uint8_t *buf, size_t offset) {
    return ((uint32_t)buf[offset]) |
           (((uint32_t)buf[offset + 1]) << 8) |
           (((uint32_t)buf[offset + 2]) << 16) |
           (((uint32_t)buf[offset + 3]) << 24);
}

static uint16_t bjx_read_u16_le(const uint8_t *buf, size_t offset) {
    return ((uint16_t)buf[offset]) | (((uint16_t)buf[offset + 1]) << 8);
}

/* ============================================================================
 * Key Derivation
 * ============================================================================ */

/**
 * bjx_derive_key_pbkdf2 - Derive a 32-byte key using PBKDF2-HMAC-SHA256.
 * @password: Null-terminated password string
 * @salt: 16-byte salt
 * @out_key: Output buffer for 32-byte key
 * @key_len: Length of output key (should be 32)
 *
 * Returns: 1 on success, 0 on failure
 *
 * Uses parameters from spec: iter=100000, output=32 bytes
 */
static uint32_t bjx_derive_key_pbkdf2(const char *password, const uint8_t *salt,
                                      uint8_t *out_key, size_t key_len) {
    if (password == NULL || salt == NULL || out_key == NULL) {
        return 0;
    }

    int ret = PKCS5_PBKDF2_HMAC(password,
                                (int)strlen(password),
                                salt,
                                BJX_SALT_SIZE,
                                100000,  /* iterations per spec */
                                EVP_sha256(),
                                (int)key_len,
                                out_key);

    return (ret == 1) ? 1 : 0;
}

/* ============================================================================
 * Decryption
 * ============================================================================ */

/**
 * bjx_decrypt_aes_gcm - Decrypt ciphertext using AES-256-GCM.
 * @ciphertext: Encrypted data
 * @ciphertext_len: Length of ciphertext
 * @tag: 16-byte authentication tag
 * @nonce: 12-byte nonce
 * @aad: Additional authenticated data (BJX header)
 * @aad_len: Length of AAD
 * @key: 32-byte AES key
 * @plaintext: Output buffer (must be at least ciphertext_len bytes)
 *
 * Returns: ICFG_OK on success, non-zero on failure
 */
static icfg_status_t bjx_decrypt_aes_gcm(const uint8_t *ciphertext, size_t ciphertext_len,
                                         const uint8_t *tag, const uint8_t *nonce,
                                         const uint8_t *aad, size_t aad_len,
                                         const uint8_t *key, uint8_t *plaintext) {
    if (ciphertext == NULL || tag == NULL || nonce == NULL || key == NULL || plaintext == NULL) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    EVP_CIPHER_CTX *ctx = EVP_CIPHER_CTX_new();
    if (ctx == NULL) {
        return ICFG_ERR_UNSUPPORTED;
    }

    int len = 0;
    int ret = 1;

    /* Initialize decryption context */
    ret = EVP_DecryptInit_ex(ctx, EVP_aes_256_gcm(), NULL, key, nonce);
    if (ret != 1) {
        EVP_CIPHER_CTX_free(ctx);
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Set tag before decryption */
    ret = EVP_CIPHER_CTX_ctrl(ctx, EVP_CTRL_GCM_SET_TAG, BJX_TAG_SIZE, (uint8_t *)tag);
    if (ret != 1) {
        EVP_CIPHER_CTX_free(ctx);
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Add AAD (header) */
    if (aad != NULL && aad_len > 0) {
        ret = EVP_DecryptUpdate(ctx, NULL, &len, aad, (int)aad_len);
        if (ret != 1) {
            EVP_CIPHER_CTX_free(ctx);
            return ICFG_ERR_UNSUPPORTED;
        }
    }

    /* Decrypt ciphertext */
    ret = EVP_DecryptUpdate(ctx, plaintext, &len, ciphertext, (int)ciphertext_len);
    if (ret != 1) {
        EVP_CIPHER_CTX_free(ctx);
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Finalize decryption (verifies tag) */
    ret = EVP_DecryptFinal_ex(ctx, plaintext + len, &len);
    EVP_CIPHER_CTX_free(ctx);

    if (ret != 1) {
        /* Tag verification failed */
        return ICFG_ERR_CRC;
    }

    return ICFG_OK;
}

/* ============================================================================
 * Validation Functions
 * ============================================================================ */

static icfg_status_t bjx_validate_header(bjx_ctx_t *ctx) {
    if (ctx == NULL || ctx->data == NULL) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    if (ctx->size < BJX_HEADER_SIZE) {
        bjx_set_error(ctx, BJX_ERR_TRUNCATED_FILE, 0, "File too small for header");
        return ICFG_ERR_BOUNDS;
    }

    /* Check magic */
    uint32_t magic = bjx_read_u32_le(ctx->data, 0);
    ctx->magic = magic;
    if (magic != BJX_MAGIC_PRIMARY) {
        bjx_set_error(ctx, BJX_ERR_INVALID_MAGIC, 0, "Invalid BJX magic");
        return ICFG_ERR_MAGIC;
    }

    /* Parse header bytes: byte 4 = flags, byte 5 = version */
    uint8_t flags = ctx->data[4];
    uint8_t version = ctx->data[5];

    ctx->version = version;
    if (version != BJX_VERSION) {
        bjx_set_error(ctx, BJX_ERR_UNSUPPORTED_VERSION, 5, "Unsupported BJX version");
        return ICFG_ERR_UNSUPPORTED;
    }

    ctx->flags = flags;

    /* Reject unknown flag bits early */
    if ((flags & (BJX_FLAG_KDF_PBKDF2 | BJX_FLAG_KDF_RAWKEY | BJX_FLAG_CIPHER_AESGCM)) != flags) {
        bjx_set_error(ctx, BJX_ERR_UNSUPPORTED_VERSION, 4, "Unsupported BJX header format");
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Validate flags: exactly one KDF mode, exactly one cipher mode */
    uint8_t kdf_count = 0;
    if (flags & BJX_FLAG_KDF_PBKDF2) kdf_count++;
    if (flags & BJX_FLAG_KDF_RAWKEY) kdf_count++;
    if (kdf_count != 1) {
        bjx_set_error(ctx, BJX_ERR_INVALID_FLAGS, 5, "Invalid KDF flags (must be exactly one)");
        return ICFG_ERR_UNSUPPORTED;
    }

    uint8_t cipher_count = 0;
    if (flags & BJX_FLAG_CIPHER_AESGCM) cipher_count++;
    if (cipher_count != 1) {
        bjx_set_error(ctx, BJX_ERR_INVALID_FLAGS, 5, "Invalid cipher flags (must be AES-GCM)");
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Check header size */
    uint16_t header_size = bjx_read_u16_le(ctx->data, 6);
    ctx->header_size = header_size;
    if (header_size != BJX_HEADER_SIZE) {
        bjx_set_error(ctx, BJX_ERR_INVALID_HEADER_SIZE, 6, "Header size must be 32");
        return ICFG_ERR_BOUNDS;
    }

    /* Parse sizes */
    uint32_t encrypted_size = bjx_read_u32_le(ctx->data, 8);
    uint32_t plain_size = bjx_read_u32_le(ctx->data, 12);
    ctx->encrypted_payload_size = encrypted_size;
    ctx->plain_bjv_size = plain_size;

    /* Parse offsets */
    ctx->salt_offset = bjx_read_u32_le(ctx->data, 16);
    ctx->nonce_offset = bjx_read_u32_le(ctx->data, 20);
    ctx->ciphertext_offset = bjx_read_u32_le(ctx->data, 24);
    ctx->reserved = bjx_read_u32_le(ctx->data, 28);

    /* Check reserved field */
    if (ctx->reserved != 0) {
        bjx_set_error(ctx, BJX_ERR_RESERVED_NON_ZERO, 28, "Reserved field must be zero");
        return ICFG_ERR_UNSUPPORTED;
    }

    return ICFG_OK;
}

static icfg_status_t bjx_validate_bounds(bjx_ctx_t *ctx) {
    if (ctx == NULL) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    /* Check DoS limits */
    if (ctx->encrypted_payload_size > BJX_MAX_ENCRYPTED_PAYLOAD_SIZE) {
        bjx_set_error(ctx, BJX_ERR_LIMIT_EXCEEDED, 8, "Encrypted payload exceeds 64 MB limit");
        return ICFG_ERR_RANGE;
    }

    if (ctx->plain_bjv_size > BJX_MAX_PLAIN_SIZE) {
        bjx_set_error(ctx, BJX_ERR_LIMIT_EXCEEDED, 12, "Plain size exceeds 64 MB limit");
        return ICFG_ERR_RANGE;
    }

    /* Validate offset values */
    if (ctx->salt_offset != BJX_HEADER_SIZE) {
        bjx_set_error(ctx, BJX_ERR_OFFSET_OUT_OF_BOUNDS, 16, "Salt offset must be 32");
        return ICFG_ERR_BOUNDS;
    }

    if (ctx->nonce_offset != (BJX_HEADER_SIZE + BJX_SALT_SIZE)) {
        bjx_set_error(ctx, BJX_ERR_OFFSET_OUT_OF_BOUNDS, 20, "Nonce offset must be 48");
        return ICFG_ERR_BOUNDS;
    }

    if (ctx->ciphertext_offset != (BJX_HEADER_SIZE + BJX_SALT_SIZE + BJX_NONCE_SIZE)) {
        bjx_set_error(ctx, BJX_ERR_OFFSET_OUT_OF_BOUNDS, 24, "Ciphertext offset must be 60");
        return ICFG_ERR_BOUNDS;
    }

    /* Calculate expected file size */
    uint64_t expected_size = (uint64_t)BJX_HEADER_SIZE + BJX_SALT_SIZE + BJX_NONCE_SIZE +
                             (uint64_t)ctx->encrypted_payload_size + BJX_TAG_SIZE;

    if (ctx->size < expected_size) {
        bjx_set_error(ctx, BJX_ERR_TRUNCATED_FILE, ctx->size, "File too small for declared payload");
        return ICFG_ERR_BOUNDS;
    }

    /* Set up pointers to sections */
    ctx->salt = ctx->data + ctx->salt_offset;
    ctx->nonce = ctx->data + ctx->nonce_offset;
    ctx->ciphertext = ctx->data + ctx->ciphertext_offset;
    ctx->tag = ctx->data + ctx->ciphertext_offset + ctx->encrypted_payload_size;

    return ICFG_OK;
}

/* ============================================================================
 * Public API
 * ============================================================================ */

icfg_status_t bjx_open(const uint8_t *data, size_t size, bjx_ctx_t *out) {
    if (data == NULL || out == NULL) {
        if (out != NULL) {
            bjx_set_error(out, BJX_ERR_NULL_ARGUMENT, 0, "Null argument to bjx_open");
        }
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    /* Initialize context */
    memset(out, 0, sizeof(*out));
    out->data = data;
    out->size = size;

    /* Validate header */
    icfg_status_t ret = bjx_validate_header(out);
    if (ret != ICFG_OK) {
        return ret;
    }

    /* Validate bounds and offsets */
    ret = bjx_validate_bounds(out);
    if (ret != ICFG_OK) {
        return ret;
    }

    return ICFG_OK;
}

icfg_status_t bjx_validate_fast(bjx_ctx_t *ctx) {
    if (ctx == NULL) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    /* Header and bounds are already validated in bjx_open */
    /* Nothing additional needed for "fast" validation */
    return ICFG_OK;
}

icfg_status_t bjx_validate_strict(bjx_ctx_t *ctx, const char *password, const uint8_t *raw_key) {
    if (ctx == NULL) {
        return ICFG_ERR_INVALID_ARGUMENT;
    }

    uint8_t key[32];
    icfg_status_t ret = ICFG_OK;

    /* Derive or use key based on flags */
    if (ctx->flags & BJX_FLAG_KDF_PBKDF2) {
        /* PBKDF2 mode: password must be provided */
        if (password == NULL) {
            bjx_set_error(ctx, BJX_ERR_INVALID_SALT, ctx->salt_offset, "Password required for PBKDF2 mode");
            return ICFG_ERR_UNSUPPORTED;
        }

        if (!bjx_derive_key_pbkdf2(password, ctx->salt, key, sizeof(key))) {
            bjx_set_error(ctx, BJX_ERR_DECRYPT_FAILED, ctx->salt_offset, "PBKDF2 key derivation failed");
            return ICFG_ERR_UNSUPPORTED;
        }
    } else if (ctx->flags & BJX_FLAG_KDF_RAWKEY) {
        /* Raw key mode: 32-byte key must be provided */
        if (raw_key == NULL) {
            bjx_set_error(ctx, BJX_ERR_INVALID_SALT, ctx->salt_offset, "Raw key required for raw key mode");
            return ICFG_ERR_UNSUPPORTED;
        }
        memcpy(key, raw_key, 32);
    } else {
        bjx_set_error(ctx, BJX_ERR_UNSUPPORTED_KDF, 5, "Unsupported KDF mode");
        return ICFG_ERR_UNSUPPORTED;
    }

    /* Allocate plaintext buffer */
    ctx->plaintext = (uint8_t *)malloc(ctx->plain_bjv_size);
    if (ctx->plaintext == NULL) {
        bjx_set_error(ctx, BJX_ERR_ALLOCATION_FAILED, 0, "Failed to allocate plaintext buffer");
        return ICFG_ERR_UNSUPPORTED;
    }
    ctx->plaintext_size = ctx->plain_bjv_size;

    /* Decrypt using AES-256-GCM */
    ret = bjx_decrypt_aes_gcm(ctx->ciphertext,
                              ctx->encrypted_payload_size,
                              ctx->tag,
                              ctx->nonce,
                              ctx->data,  /* AAD is the header (first 32 bytes) */
                              BJX_HEADER_SIZE,
                              key,
                              ctx->plaintext);

    if (ret != ICFG_OK) {
        free(ctx->plaintext);
        ctx->plaintext = NULL;
        ctx->plaintext_size = 0;
        if (ret == ICFG_ERR_CRC) {
            bjx_set_error(ctx, BJX_ERR_AUTH_TAG_MISMATCH, ctx->ciphertext_offset, "Authentication tag mismatch");
        } else {
            bjx_set_error(ctx, BJX_ERR_DECRYPT_FAILED, ctx->ciphertext_offset, "Decryption failed");
        }
        return ret;
    }

    /* Verify plaintext size matches declared size */
    if (ctx->plaintext_size != ctx->plain_bjv_size) {
        free(ctx->plaintext);
        ctx->plaintext = NULL;
        ctx->plaintext_size = 0;
        bjx_set_error(ctx, BJX_ERR_PLAIN_SIZE_MISMATCH, 12, "Plaintext size mismatch");
        return ICFG_ERR_BOUNDS;
    }

    return ICFG_OK;
}

void bjx_close(bjx_ctx_t *ctx) {
    if (ctx == NULL) return;

    if (ctx->plaintext != NULL) {
        free(ctx->plaintext);
        ctx->plaintext = NULL;
    }
    ctx->plaintext_size = 0;
}
