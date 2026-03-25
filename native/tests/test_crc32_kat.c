/*
 * CRC32 Known-Answer Test (KAT)
 * Compute CRC32 over deterministic header fixture
 * Compare with .NET implementation for cross-runtime parity
 */

#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include "../ironfamily_c/include/ironfamily/crc32.h"

/* Deterministic header fixture (100 bytes) */
static uint8_t create_header_fixture(void) {
    uint8_t header[100] = {0};

    /* Magic: "IRONDEL2" */
    memcpy(&header[0], "IRONDEL2", 8);

    /* Version (1 byte at offset 8) */
    header[8] = 0x01;

    /* Flags (1 byte at offset 9) */
    header[9] = 0x00;

    /* Reserved (2 bytes at offset 10-11, little-endian) */
    header[10] = 0x00;
    header[11] = 0x00;

    /* BaseLen (8 bytes at offset 12-19, little-endian = 524288 = 0x00080000) */
    header[12] = 0x00;
    header[13] = 0x00;
    header[14] = 0x08;
    header[15] = 0x00;
    header[16] = 0x00;
    header[17] = 0x00;
    header[18] = 0x00;
    header[19] = 0x00;

    /* TargetLen (8 bytes at offset 20-27, little-endian = 524288) */
    header[20] = 0x00;
    header[21] = 0x00;
    header[22] = 0x08;
    header[23] = 0x00;
    header[24] = 0x00;
    header[25] = 0x00;
    header[26] = 0x00;
    header[27] = 0x00;

    /* BaseHash (32 bytes at offset 28-59) - from golden vector case_01 */
    uint8_t base_hash[32] = {
        0x18, 0x18, 0xDA, 0x79, 0xF1, 0xCC, 0x8E, 0x6B,
        0x66, 0x5D, 0x7E, 0x98, 0x08, 0xFD, 0xE4, 0x54,
        0x7B, 0x6B, 0xA7, 0x47, 0xE6, 0xD3, 0xF8, 0xD7,
        0x3B, 0x6F, 0x7A, 0x48, 0x08, 0xF7, 0x60, 0xA6
    };
    memcpy(&header[28], base_hash, 32);

    /* TargetHash (32 bytes at offset 60-91) - from golden vector case_01 */
    uint8_t target_hash[32] = {
        0x4D, 0x2F, 0x21, 0x11, 0x8E, 0xBB, 0x0D, 0xC5,
        0x94, 0x01, 0x18, 0x5A, 0x13, 0x06, 0x72, 0xF9,
        0x19, 0xC8, 0x3F, 0x07, 0x9B, 0x3B, 0xFD, 0xC0,
        0x7E, 0xF3, 0xFD, 0xC5, 0x93, 0x99, 0x55, 0x1A
    };
    memcpy(&header[60], target_hash, 32);

    /* OpCount (4 bytes at offset 92-95, little-endian = 64) */
    header[92] = 0x40;
    header[93] = 0x00;
    header[94] = 0x00;
    header[95] = 0x00;

    /* CrcField (4 bytes at offset 96-99) - must be zero for computation */
    header[96] = 0x00;
    header[97] = 0x00;
    header[98] = 0x00;
    header[99] = 0x00;

    return header;
}

int main(void) {
    printf("=== CRC32 Known-Answer Test (KAT) ===\n\n");

    /* Create fixture */
    uint8_t header[100];
    // Note: We can't return structs from functions in plain C, so we'll recreate here
    memcpy(header, "IRONDEL2\x01\x00\x00\x00", 12);
    // BaseLen: 524288 LE
    memcpy(&header[12], "\x00\x00\x08\x00\x00\x00\x00\x00", 8);
    // TargetLen: 524288 LE
    memcpy(&header[20], "\x00\x00\x08\x00\x00\x00\x00\x00", 8);
    // BaseHash
    uint8_t base_hash[32] = {
        0x18, 0x18, 0xDA, 0x79, 0xF1, 0xCC, 0x8E, 0x6B,
        0x66, 0x5D, 0x7E, 0x98, 0x08, 0xFD, 0xE4, 0x54,
        0x7B, 0x6B, 0xA7, 0x47, 0xE6, 0xD3, 0xF8, 0xD7,
        0x3B, 0x6F, 0x7A, 0x48, 0x08, 0xF7, 0x60, 0xA6
    };
    memcpy(&header[28], base_hash, 32);
    // TargetHash
    uint8_t target_hash[32] = {
        0x4D, 0x2F, 0x21, 0x11, 0x8E, 0xBB, 0x0D, 0xC5,
        0x94, 0x01, 0x18, 0x5A, 0x13, 0x06, 0x72, 0xF9,
        0x19, 0xC8, 0x3F, 0x07, 0x9B, 0x3B, 0xFD, 0xC0,
        0x7E, 0xF3, 0xFD, 0xC5, 0x93, 0x99, 0x55, 0x1A
    };
    memcpy(&header[60], target_hash, 32);
    // OpCount: 64 LE
    memcpy(&header[92], "\x40\x00\x00\x00", 4);
    // CrcField: zeros (already initialized)
    header[96] = header[97] = header[98] = header[99] = 0;

    printf("Fixture created (100 bytes)\n");
    printf("  Magic: %s\n", (char*)&header[0]);
    printf("  BaseLen: 0x%016lx\n", *(uint64_t*)&header[12]);
    printf("  TargetLen: 0x%016lx\n", *(uint64_t*)&header[20]);
    printf("  OpCount: %u\n", *(uint32_t*)&header[92]);
    printf("  CrcField before compute: [%02X %02X %02X %02X]\n",
           header[96], header[97], header[98], header[99]);

    /* Compute CRC32 over header[0:96] */
    uint32_t crc32_computed = iron_crc32(header, 96);

    printf("\nCRC32 Computation\n");
    printf("  Input: First 96 bytes of header\n");
    printf("  CRC32 Result: 0x%08X\n", crc32_computed);
    printf("  Hex string: %08x\n", crc32_computed);

    /* Write to file for comparison with .NET */
    FILE* fp = fopen("artifacts/_dev/exec_crc32_01/native_crc32_kat.txt", "w");
    if (fp) {
        fprintf(fp, "%08x\n", crc32_computed);
        fclose(fp);
        printf("\n✅ Result written to: artifacts/_dev/exec_crc32_01/native_crc32_kat.txt\n");
    } else {
        printf("\n❌ Cannot write to output file\n");
        return 1;
    }

    /* Also write header fixture for reference */
    fp = fopen("artifacts/_dev/exec_crc32_01/header_fixture.bin", "wb");
    if (fp) {
        fwrite(header, 1, 100, fp);
        fclose(fp);
        printf("✅ Header fixture written to: artifacts/_dev/exec_crc32_01/header_fixture.bin\n");
    }

    return 0;
}
