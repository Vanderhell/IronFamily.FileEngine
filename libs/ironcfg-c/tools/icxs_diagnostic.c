/*
 * icxs_diagnostic.c
 * Diagnostic tool for ICXS parsing issues
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ironcfg/icxs.h>

int main(int argc, char* argv[]) {
    if (argc < 2) {
        printf("Usage: icxs_diagnostic <file>\n");
        return 1;
    }

    const char* filepath = argv[1];
    FILE* f = fopen(filepath, "rb");
    if (!f) {
        printf("FAIL: Cannot open file: %s\n", filepath);
        return 1;
    }

    fseek(f, 0, SEEK_END);
    size_t size = ftell(f);
    fseek(f, 0, SEEK_SET);

    uint8_t* data = (uint8_t*)malloc(size);
    if (!data) {
        printf("FAIL: Cannot allocate %zu bytes\n", size);
        fclose(f);
        return 1;
    }

    if (fread(data, 1, size, f) != size) {
        printf("FAIL: Cannot read file\n");
        fclose(f);
        free(data);
        return 1;
    }
    fclose(f);

    printf("========================================\n");
    printf("ICXS Diagnostic Report\n");
    printf("========================================\n");
    printf("File: %s\n", filepath);
    printf("Size: %zu bytes\n\n", size);

    /* Dump header */
    printf("Header (first 64 bytes):\n");
    for (int i = 0; i < 64 && i < size; i++) {
        printf("%02x ", data[i]);
        if ((i + 1) % 16 == 0) printf("\n");
    }
    printf("\n\n");

    /* Parse header manually */
    printf("Header Fields:\n");
    printf("  Magic: %.4s\n", (const char*)&data[0]);
    printf("  Version: %u\n", data[4]);
    printf("  Flags: 0x%02x\n", data[5]);
    printf("    has_crc: %d\n", (data[5] & 0x01) != 0);

    uint32_t schema_offset = ((uint32_t)data[24]) |
                            (((uint32_t)data[25]) << 8) |
                            (((uint32_t)data[26]) << 16) |
                            (((uint32_t)data[27]) << 24);
    uint32_t data_offset = ((uint32_t)data[28]) |
                          (((uint32_t)data[29]) << 8) |
                          (((uint32_t)data[30]) << 16) |
                          (((uint32_t)data[31]) << 24);
    uint32_t crc_offset = ((uint32_t)data[32]) |
                         (((uint32_t)data[33]) << 8) |
                         (((uint32_t)data[34]) << 16) |
                         (((uint32_t)data[35]) << 24);

    printf("  Schema Block Offset: %u (0x%x)\n", schema_offset, schema_offset);
    printf("  Data Block Offset: %u (0x%x)\n", data_offset, data_offset);
    printf("  CRC Offset: %u (0x%x)\n", crc_offset, crc_offset);

    /* Open with icxs_open */
    printf("\n========================================\n");
    printf("Parsing with icxs_open():\n");
    printf("========================================\n");

    icxs_view_t view;
    icfg_status_t status = icxs_open(data, size, &view);
    if (status != ICFG_OK) {
        printf("FAIL: icxs_open returned %d\n", status);
        free(data);
        return 1;
    }

    printf("Record count: %u\n", view.record_count);
    printf("Record stride: %u\n", view.record_stride);
    printf("Field count: %u\n", view.field_count);
    printf("Schema block offset: %u\n", view.schema_block_offset);
    printf("Data block offset: %u\n", view.data_block_offset);

    /* Dump schema block */
    printf("\n========================================\n");
    printf("Schema Block (raw bytes):\n");
    printf("========================================\n");
    size_t schema_size = data_offset - schema_offset;
    printf("Schema size: %zu bytes\n\n", schema_size);
    
    uint32_t offset = schema_offset;
    uint32_t field_count_raw = ((uint32_t)data[offset]) |
                               (((uint32_t)data[offset+1]) << 8) |
                               (((uint32_t)data[offset+2]) << 16) |
                               (((uint32_t)data[offset+3]) << 24);
    printf("Field count (from schema): %u\n\n", field_count_raw);
    offset += 4;

    for (uint32_t i = 0; i < field_count_raw && offset < size; i++) {
        printf("Field %u:\n", i);
        
        uint32_t field_id = ((uint32_t)data[offset]) |
                            (((uint32_t)data[offset+1]) << 8) |
                            (((uint32_t)data[offset+2]) << 16) |
                            (((uint32_t)data[offset+3]) << 24);
        printf("  ID: %u (0x%x) at offset %u\n", field_id, field_id, offset);
        offset += 4;

        uint8_t field_type = data[offset];
        printf("  Type: %u at offset %u", field_type, offset);
        if (field_type == 1) printf(" (i64)");
        else if (field_type == 2) printf(" (u64)");
        else if (field_type == 3) printf(" (f64)");
        else if (field_type == 4) printf(" (bool)");
        else if (field_type == 5) printf(" (str)");
        else printf(" (INVALID!)");
        printf("\n");
        offset += 1;
    }

    /* Try to get first record */
    printf("\n========================================\n");
    printf("Getting Record 0:\n");
    printf("========================================\n");

    icxs_record_t record;
    status = icxs_get_record(&view, 0, &record);
    if (status != ICFG_OK) {
        printf("FAIL: icxs_get_record failed: %d\n", status);
        free(data);
        return 1;
    }
    printf("Got record 0 OK\n");

    /* Try to get field 1 */
    printf("\nTrying icxs_get_i64(record, field_id=1):\n");
    int64_t id;
    status = icxs_get_i64(&record, 1, &id);
    printf("Status: %d\n", status);
    if (status == 0) printf("  (ICFG_OK - SUCCESS)\n");
    else if (status == 1) printf("  (ICFG_ERR_MAGIC)\n");
    else if (status == 2) printf("  (ICFG_ERR_BOUNDS)\n");
    else if (status == 3) printf("  (ICFG_ERR_CRC)\n");
    else if (status == 4) printf("  (ICFG_ERR_SCHEMA)\n");
    else if (status == 5) printf("  (ICFG_ERR_TYPE)\n");
    else if (status == 6) printf("  (ICFG_ERR_RANGE)\n");
    else if (status == 7) printf("  (ICFG_ERR_UNSUPPORTED)\n");
    else if (status == 8) printf("  (ICFG_ERR_INVALID_ARGUMENT)\n");
    else printf("  (UNKNOWN)\n");

    if (status == 0) {
        printf("  Value: %ld\n", id);
    }

    free(data);
    return 0;
}
