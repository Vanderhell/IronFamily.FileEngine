/*
 * Quick test to compute CRC32 of actual case_01.patch2 header
 */

#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include "../ironfamily_c/include/ironfamily/crc32.h"

int main(void) {
    FILE* fp = fopen("artifacts/vectors/v1/delta2/v1/case_01.patch2.bin", "rb");
    if (!fp) {
        printf("Cannot open patch file\n");
        return 1;
    }

    uint8_t header[100];
    if (fread(header, 1, 100, fp) != 100) {
        printf("Cannot read header\n");
        fclose(fp);
        return 1;
    }
    fclose(fp);

    printf("Actual patch2 header[96:100]: [%02X %02X %02X %02X]\n",
           header[96], header[97], header[98], header[99]);

    /* Extract stored CRC32 */
    uint32_t stored_crc = ((uint32_t)header[96]) |
                          (((uint32_t)header[97]) << 8) |
                          (((uint32_t)header[98]) << 16) |
                          (((uint32_t)header[99]) << 24);
    printf("Stored CRC32: 0x%08X\n", stored_crc);

    /* Compute CRC32 over [0:96] with CRC field zeroed */
    uint8_t header_for_crc[100];
    memcpy(header_for_crc, header, 100);
    memset(&header_for_crc[96], 0, 4);

    uint32_t computed_crc = iron_crc32(header_for_crc, 96);
    printf("Computed CRC32: 0x%08X\n", computed_crc);

    if (stored_crc == computed_crc) {
        printf("✅ CRC32 MATCH!\n");
        return 0;
    } else {
        printf("❌ CRC32 MISMATCH!\n");
        printf("   Stored:   0x%08X\n", stored_crc);
        printf("   Computed: 0x%08X\n", computed_crc);
        return 1;
    }
}
