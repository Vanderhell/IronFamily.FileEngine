/*
 * test_icxs_basic.c
 * Basic test for ICXS reader
 *
 * Reads golden_items_crc.icxs and validates:
 * - File opens successfully
 * - Record count is correct
 * - Can read int64 fields (damage)
 * - Can read string fields (name)
 * - CRC validation works
 */

#include <ironcfg/icxs.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define TEST_FILE "vectors/small/icxs/golden_items_crc.icxs"

static bool test_open_and_read() {
    printf("Test: Open and read golden ICXS file...\n");

    /* Read file */
    FILE* f = fopen(TEST_FILE, "rb");
    if (!f) {
        printf("  SKIP: File not found: %s\n", TEST_FILE);
        return true; /* Skip, not fail */
    }

    fseek(f, 0, SEEK_END);
    long file_size = ftell(f);
    fseek(f, 0, SEEK_SET);

    uint8_t* data = (uint8_t*)malloc(file_size);
    if (!data) {
        fclose(f);
        printf("  FAIL: malloc failed\n");
        return false;
    }

    size_t read_size = fread(data, 1, file_size, f);
    fclose(f);

    if (read_size != (size_t)file_size) {
        printf("  FAIL: read size mismatch\n");
        free(data);
        return false;
    }

    /* Open ICXS */
    icxs_view_t view;
    icfg_status_t status = icxs_open(data, file_size, &view);
    if (status != ICFG_OK) {
        printf("  FAIL: icxs_open returned %d\n", status);
        free(data);
        return false;
    }

    /* Validate */
    status = icxs_validate(&view);
    if (status != ICFG_OK) {
        printf("  FAIL: icxs_validate returned %d\n", status);
        free(data);
        return false;
    }

    /* Check record count */
    uint32_t record_count;
    status = icxs_record_count(&view, &record_count);
    if (status != ICFG_OK || record_count != 3) {
        printf("  FAIL: Expected 3 records, got %u\n", record_count);
        free(data);
        return false;
    }

    /* Read first record, field 3 (damage = 25) */
    icxs_record_t record;
    status = icxs_get_record(&view, 0, &record);
    if (status != ICFG_OK) {
        printf("  FAIL: icxs_get_record returned %d\n", status);
        free(data);
        return false;
    }

    int64_t damage;
    status = icxs_get_i64(&record, 3, &damage);
    if (status != ICFG_OK || damage != 25) {
        printf("  FAIL: Expected damage=25, got %ld (status=%d)\n", damage, status);
        free(data);
        return false;
    }

    /* Read first record, field 2 (name = "Iron Sword") */
    const uint8_t* name_ptr;
    uint32_t name_len;
    status = icxs_get_str(&record, 2, &name_ptr, &name_len);
    if (status != ICFG_OK) {
        printf("  FAIL: icxs_get_str returned %d\n", status);
        free(data);
        return false;
    }

    if (name_len != 10 || memcmp(name_ptr, "Iron Sword", 10) != 0) {
        printf("  FAIL: Expected name='Iron Sword', got len=%u\n", name_len);
        free(data);
        return false;
    }

    /* Read second record, field 3 (damage = 10) */
    status = icxs_get_record(&view, 1, &record);
    if (status != ICFG_OK) {
        free(data);
        return false;
    }

    status = icxs_get_i64(&record, 3, &damage);
    if (status != ICFG_OK || damage != 10) {
        printf("  FAIL: Second record damage: expected 10, got %ld\n", damage);
        free(data);
        return false;
    }

    printf("  PASS\n");
    free(data);
    return true;
}

int main() {
    printf("=== ICXS C99 Reader Tests ===\n\n");

    int passed = 0;
    int failed = 0;

    if (test_open_and_read()) {
        passed++;
    } else {
        failed++;
    }

    printf("\nResults: %d passed, %d failed\n", passed, failed);
    return failed > 0 ? 1 : 0;
}
