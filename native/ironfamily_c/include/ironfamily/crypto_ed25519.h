/*
 * Ed25519 Cryptographic Signature Verification
 * Wrapper for vendored ref10 implementation (RFC 8032)
 */

#ifndef IRON_CRYPTO_ED25519_H
#define IRON_CRYPTO_ED25519_H

#include <stdint.h>
#include <stddef.h>

/*
 * Verify an Ed25519 signature over a message using a public key.
 *
 * Args:
 *   sig:      64-byte Ed25519 signature
 *   msg:      Message bytes (arbitrary length)
 *   msg_len:  Length of message in bytes
 *   pub:      32-byte Ed25519 public key
 *
 * Returns:
 *   1 if signature is valid
 *   0 if signature is invalid
 *
 * NOTE: Message is expected to be the BLAKE3-256 hash (32 bytes) for IUPD v2.
 * Standard Ed25519.verify will internally hash this with SHA-512 per RFC 8032.
 */
int iron_crypto_ed25519_verify(
    const uint8_t sig[64],
    const uint8_t* msg,
    uint32_t msg_len,
    const uint8_t pub[32]
);

#endif /* IRON_CRYPTO_ED25519_H */
