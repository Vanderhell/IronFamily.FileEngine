/*
 * crc_diagnostic.c
 * Diagnostic tool to print CRC values from ICFX files
 */

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <ironcfg/ironcfg_common.h>

typedef struct {
    uint32_t magic;
    uint8_t flags;
    uint8_t reserved;
    uint16_t header_size;
    uint32_t total_size;
    uint32_t dict_offset;
    uint32_t vsp_offset;
    uint32_t index_offset;
    uint32_t payload_offset;
    uint32_t crc_offset;
    uint32_t payload_size;
    uint32_t dict_size;
    uint32_t vsp_size;
} icfx_header_t;

static uint32_t read_u32_le(const uint8_t* buf, size_t offset) {
    return ((uint32_t)buf[offset]) |
           (((uint32_t)buf[offset + 1]) << 8) |
           (((uint32_t)buf[offset + 2]) << 16) |
           (((uint32_t)buf[offset + 3]) << 24);
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        fprintf(stderr, "usage: crc_diagnostic <file.icfx>\n");
        return 1;
    }

    const char* filePath = argv[1];
    FILE* f = fopen(filePath, "rb");
    if (!f) {
        perror("fopen");
        return 1;
    }

    fseek(f, 0, SEEK_END);
    size_t size = ftell(f);
    fseek(f, 0, SEEK_SET);

    uint8_t* data = (uint8_t*)malloc(size);
    if (!data) {
        fprintf(stderr, "malloc failed\n");
        fclose(f);
        return 1;
    }

    if (fread(data, 1, size, f) != size) {
        fprintf(stderr, "fread failed\n");
        free(data);
        fclose(f);
        return 1;
    }
    fclose(f);

    printf("File: %s\n", filePath);
    printf("Size: %zu bytes\n\n", size);

    if (size < 48) {
        fprintf(stderr, "ERROR: File too small for ICFX header\n");
        free(data);
        return 1;
    }

    /* Parse header */
    printf("Magic: %.4s\n", (const char*)data);
    printf("Flags: 0x%02X\n", data[4]);
    printf("  Bit 0 (LE): %d\n", (data[4] & 0x01) ? 1 : 0);
    printf("  Bit 1 (VSP): %d\n", (data[4] & 0x02) ? 1 : 0);
    printf("  Bit 2 (CRC): %d\n", (data[4] & 0x04) ? 1 : 0);
    printf("  Bit 3 (Index): %d\n", (data[4] & 0x08) ? 1 : 0);

    uint32_t crc_offset = read_u32_le(data, 28);
    printf("\nCRC Offset: %u\n", crc_offset);

    if (crc_offset > 0 && crc_offset + 4 <= size) {
        uint32_t stored_crc = read_u32_le(data, crc_offset);

        /* Compute CRC over [0 .. crc_offset) */
        uint32_t computed_crc = icfg_crc32(data, crc_offset);

        printf("CRC Information:\n");
        printf("  Stored CRC: 0x%08X\n", stored_crc);
        printf("  Computed CRC: 0x%08X\n", computed_crc);
        printf("  Match: %s\n", (stored_crc == computed_crc) ? "YES ✓" : "NO ✗");
    } else {
        printf("No CRC present\n");
    }

    free(data);
    return 0;
}
