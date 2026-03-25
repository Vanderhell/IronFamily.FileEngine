/* Debug version of golden vector test */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "../ironfamily_c/include/ironcfg/ironcfg.h"

/* Read binary file into buffer */
static bool read_vector_file(const char *path, uint8_t *buffer, size_t max_size, size_t *out_size) {
    FILE *f = fopen(path, "rb");
    if (!f) {
        printf("Cannot open %s\n", path);
        return false;
    }

    fseek(f, 0, SEEK_END);
    size_t size = ftell(f);
    fseek(f, 0, SEEK_SET);

    if (size > max_size) {
        fclose(f);
        printf("File too large: %zu\n", size);
        return false;
    }

    size_t read = fread(buffer, 1, size, f);
    fclose(f);

    if (read != size) {
        printf("Read error: %zu != %zu\n", read, size);
        return false;
    }

    *out_size = size;
    return true;
}

int main(void) {
    uint8_t buffer[256];
    size_t size;

    printf("=== Debug ICFG Golden Vector Analysis ===\n\n");

    if (!read_vector_file("artifacts/vectors/v1/icfg/01_minimal.bin", buffer, sizeof(buffer), &size)) {
        return 1;
    }

    printf("Minimal vector size: %zu bytes\n", size);

    /* Print header */
    printf("\nHeader contents (first 32 bytes):\n");
    for (int i = 0; i < 32 && i < size; i++) {
        printf("[%2d] = 0x%02X", i, buffer[i]);
        if ((i + 1) % 4 == 0) printf("\n");
        else printf(" ");
    }
    if (size > 0 && 32 % 4 != 0) printf("\n");

    /* Parse header */
    uint32_t *magic_ptr = (uint32_t*)(buffer + 0);
    uint8_t *version_ptr = buffer + 4;
    uint8_t *flags_ptr = buffer + 5;
    uint32_t *schema_offset_ptr = (uint32_t*)(buffer + 12);
    uint32_t *schema_size_ptr = (uint32_t*)(buffer + 16);
    uint32_t *data_offset_ptr = (uint32_t*)(buffer + 28);
    uint32_t *data_size_ptr = (uint32_t*)(buffer + 32);
    uint32_t *crc_offset_ptr = (uint32_t*)(buffer + 36);

    printf("\nHeader fields:\n");
    printf("  Magic: 0x%08X\n", *magic_ptr);
    printf("  Version: %u\n", (unsigned)*version_ptr);
    printf("  Flags: 0x%02X\n", (unsigned)*flags_ptr);
    printf("  CRC offset: %u\n", *crc_offset_ptr);
    printf("  Schema: offset=%u, size=%u\n", *schema_offset_ptr, *schema_size_ptr);
    printf("  Data: offset=%u, size=%u\n", *data_offset_ptr, *data_size_ptr);

    /* Test fast validation */
    ironcfg_error_t err = ironcfg_validate_fast(buffer, size);
    printf("\nFast validation: code=%u, offset=%u\n", err.code, err.offset);

    /* Test strict validation */
    err = ironcfg_validate_strict(buffer, size);
    printf("Strict validation: code=%u, offset=%u\n", err.code, err.offset);

    if (err.code != 0) {
        printf("\nError details:\n");
        if (*schema_size_ptr > 0) {
            printf("Schema block (%u bytes):\n", *schema_size_ptr);
            uint8_t *schema = buffer + *schema_offset_ptr;
            for (uint32_t i = 0; i < *schema_size_ptr && i < 20; i++) {
                printf("  [%u] = 0x%02X\n", i, schema[i]);
            }
        }
    }

    return err.code == 0 ? 0 : 1;
}
