/*
 * Derive Ed25519 public key from bench seed and output as hex
 */

#include <stdio.h>
#include <stdint.h>

/* Forward declarations - will be linked from ed25519 library */
void ed25519_create_keypair(unsigned char *public_key, unsigned char *private_key, const unsigned char *seed);

int main(void) {
    /* Bench seed from IupdEd25519Keys.BenchSeed32 */
    unsigned char bench_seed[32] = {
        0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57,
        0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67,
        0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F
    };

    unsigned char public_key[32];
    unsigned char private_key[64];  /* Not used, but required by API */

    /* Derive public key */
    ed25519_create_keypair(public_key, private_key, bench_seed);

    /* Print as hex */
    printf("Derived public key (hex):\n");
    for (int i = 0; i < 32; i++) {
        printf("%02x", public_key[i]);
    }
    printf("\n");

    return 0;
}
