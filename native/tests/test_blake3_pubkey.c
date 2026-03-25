/*
 * Debug test for BLAKE3 and public key parsing
 */

#include <stdio.h>
#include <string.h>
#include <stdint.h>
#include "blake3/blake3.h"

int main(void) {
    /* Test BLAKE3 with simple known input */
    uint8_t test_msg[] = "hello world";
    uint8_t hash[32];

    printf("Testing BLAKE3 hash of 'hello world':\n");
    blake3_hash(test_msg, sizeof(test_msg) - 1, hash);

    printf("Hash (hex): ");
    for (int i = 0; i < 32; i++) {
        printf("%02x", hash[i]);
    }
    printf("\n\n");

    /* Test hex parsing */
    const char* test_hex = "d78e36181fa67e79caf1897e3d999ca5b70e89f90d8c91a89e1e6768ed3d8ae4";
    uint8_t pubkey[32];

    printf("Testing hex parsing of test pubkey:\n");
    printf("Input hex: %s\n", test_hex);
    printf("Parsed (hex): ");

    for (size_t i = 0; i < 32; i++) {
        char byte_str[3] = {test_hex[2*i], test_hex[2*i+1], '\0'};
        unsigned int byte_val;
        if (sscanf(byte_str, "%2x", &byte_val) != 1) {
            printf("\nERROR parsing hex at position %zu\n", i);
            return 1;
        }
        pubkey[i] = (uint8_t)byte_val;
        printf("%02x", pubkey[i]);
    }
    printf("\n");

    return 0;
}
