/*
 * ironcfg_common.c
 * Common utilities implementation
 */

#include "ironcfg/ironcfg_common.h"

/* ============================================================================
 * CRC32 IEEE/ZIP (0xEDB88320)
 * Bitwise reference implementation
 * ============================================================================ */

uint32_t icfg_crc32(const uint8_t* data, size_t size) {
    uint32_t crc = 0xFFFFFFFFU;

    for (size_t i = 0; i < size; i++) {
        crc ^= data[i];

        /* Process 8 bits */
        for (int j = 0; j < 8; j++) {
            if (crc & 1) {
                crc = (crc >> 1) ^ 0xEDB88320U;
            } else {
                crc = crc >> 1;
            }
        }
    }

    return crc ^ 0xFFFFFFFFU;
}
