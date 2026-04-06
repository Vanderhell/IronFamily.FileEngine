/*
 * CRC32 Known-Answer Test (KAT)
 * Compute CRC32 over a deterministic header fixture and compare it
 * against a fixed expected value.
 */

#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <errno.h>

#ifdef _WIN32
#include <direct.h>
#define MKDIR(path) _mkdir(path)
#else
#include <sys/stat.h>
#define MKDIR(path) mkdir((path), 0777)
#endif

#include "../ironfamily_c/include/ironfamily/crc32.h"

static int ensure_output_dir(void) {
    const char* parts[] = {
        "artifacts",
        "artifacts/_dev",
        "artifacts/_dev/exec_crc32_01"
    };
    size_t i;

    for (i = 0; i < sizeof(parts) / sizeof(parts[0]); ++i) {
        if (MKDIR(parts[i]) != 0 && errno != EEXIST) {
            return 0;
        }
    }

    return 1;
}

int main(void) {
    const uint32_t expected_crc32 = 0x091490CBu;
    uint8_t header[100] = {0};

    printf("=== CRC32 Known-Answer Test (KAT) ===\n\n");

    memcpy(header, "IRONDEL2\x01\x00\x00\x00", 12);
    memcpy(&header[12], "\x00\x00\x08\x00\x00\x00\x00\x00", 8);
    memcpy(&header[20], "\x00\x00\x08\x00\x00\x00\x00\x00", 8);

    {
        const uint8_t base_hash[32] = {
            0x18, 0x18, 0xDA, 0x79, 0xF1, 0xCC, 0x8E, 0x6B,
            0x66, 0x5D, 0x7E, 0x98, 0x08, 0xFD, 0xE4, 0x54,
            0x7B, 0x6B, 0xA7, 0x47, 0xE6, 0xD3, 0xF8, 0xD7,
            0x3B, 0x6F, 0x7A, 0x48, 0x08, 0xF7, 0x60, 0xA6
        };
        const uint8_t target_hash[32] = {
            0x4D, 0x2F, 0x21, 0x11, 0x8E, 0xBB, 0x0D, 0xC5,
            0x94, 0x01, 0x18, 0x5A, 0x13, 0x06, 0x72, 0xF9,
            0x19, 0xC8, 0x3F, 0x07, 0x9B, 0x3B, 0xFD, 0xC0,
            0x7E, 0xF3, 0xFD, 0xC5, 0x93, 0x99, 0x55, 0x1A
        };
        memcpy(&header[28], base_hash, 32);
        memcpy(&header[60], target_hash, 32);
    }

    memcpy(&header[92], "\x40\x00\x00\x00", 4);

    printf("Fixture created (100 bytes)\n");
    printf("  Magic: %.8s\n", (char*)&header[0]);
    printf("  BaseLen: 0x%016llX\n", (unsigned long long)*(uint64_t*)&header[12]);
    printf("  TargetLen: 0x%016llX\n", (unsigned long long)*(uint64_t*)&header[20]);
    printf("  OpCount: %u\n", *(uint32_t*)&header[92]);
    printf("  CrcField before compute: [%02X %02X %02X %02X]\n",
           header[96], header[97], header[98], header[99]);

    {
        uint32_t crc32_computed = iron_crc32(header, 96);
        FILE* fp;

        printf("\nCRC32 Computation\n");
        printf("  Input: First 96 bytes of header\n");
        printf("  CRC32 Result: 0x%08X\n", crc32_computed);
        printf("  Expected:     0x%08X\n", expected_crc32);

        if (crc32_computed != expected_crc32) {
            printf("\nFAIL: CRC32 mismatch\n");
            return 1;
        }

        if (!ensure_output_dir()) {
            printf("\nFAIL: Cannot create output directory\n");
            return 1;
        }

        fp = fopen("artifacts/_dev/exec_crc32_01/native_crc32_kat.txt", "w");
        if (!fp) {
            printf("\nFAIL: Cannot write result file\n");
            return 1;
        }
        fprintf(fp, "%08x\n", crc32_computed);
        fclose(fp);

        fp = fopen("artifacts/_dev/exec_crc32_01/header_fixture.bin", "wb");
        if (!fp) {
            printf("FAIL: Cannot write fixture file\n");
            return 1;
        }
        fwrite(header, 1, sizeof(header), fp);
        fclose(fp);
    }

    printf("\nPASS: CRC32 known-answer verified\n");
    return 0;
}
