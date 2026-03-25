/*
 * CRC32 Implementation (ISO/IEC polynomial, reflected)
 * Standard CRC32 used in ZIP, PNG, and zlib.
 *
 * Polynomial: 0x04C11DB7
 * Initial: 0xFFFFFFFF
 * Final XOR: 0xFFFFFFFF
 * Input reflected: Yes
 * Output reflected: Yes
 */

#include <stdint.h>
#include <string.h>

/* CRC32 lookup table (256 entries) */
static uint32_t crc32_table[256];
static int crc32_table_initialized = 0;

/* Initialize CRC32 lookup table */
static void crc32_init_table(void) {
    if (crc32_table_initialized) {
        return;
    }

    for (int i = 0; i < 256; i++) {
        uint32_t crc = (uint32_t)i;
        for (int j = 0; j < 8; j++) {
            if (crc & 1) {
                crc = (crc >> 1) ^ 0xEDB88320;  /* Reflected polynomial */
            } else {
                crc = crc >> 1;
            }
        }
        crc32_table[i] = crc;
    }

    crc32_table_initialized = 1;
}

/* Compute CRC32 of data buffer */
uint32_t iron_crc32(const uint8_t* data, uint32_t len) {
    crc32_init_table();

    uint32_t crc = 0xFFFFFFFFU;

    for (uint32_t i = 0; i < len; i++) {
        uint32_t index = (crc ^ data[i]) & 0xFF;
        crc = (crc >> 8) ^ crc32_table[index];
    }

    return crc ^ 0xFFFFFFFFU;
}
