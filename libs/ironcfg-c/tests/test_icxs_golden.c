/*
 * test_icxs_golden.c
 * Golden vector tests for ICXS reader
 *
 * Tests reading and validating golden ICXS files with known content.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ironcfg/icxs.h>

typedef struct {
    int64_t id;
    const char* name;
    int64_t damage;
    int64_t speed;
    int64_t rarity;
} expected_item_t;

static const expected_item_t expected_items[] = {
    {1, "Iron Sword", 25, 15, 2},
    {2, "Golden Shield", 10, 20, 3},
    {3, "Diamond Pickaxe", 30, 10, 4}
};

static const int EXPECTED_RECORD_COUNT = 3;
static const int64_t EXPECTED_TOTAL_DAMAGE = 65;

int test_icxs_file(const char* filepath, const char* test_name) {
    printf("\n=== Testing %s ===\n", test_name);

    /* Load file */
    FILE* f = fopen(filepath, "rb");
    if (!f) {
        printf("SKIP: Missing vector file: %s\n", filepath);
        return 0;
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

    /* Open ICXS view */
    icxs_view_t view;
    icfg_status_t status = icxs_open(data, size, &view);
    if (status != ICFG_OK) {
        printf("FAIL: icxs_open failed: %d\n", status);
        free(data);
        return 1;
    }
    printf("PASS: File opened and header validated\n");

    /* Validate (if file has CRC) */
    status = icxs_validate(&view);
    if (status != ICFG_OK) {
        printf("FAIL: icxs_validate failed: %d\n", status);
        free(data);
        return 1;
    }
    printf("PASS: File validated (CRC check if present)\n");

    /* Check record count */
    if (view.record_count != EXPECTED_RECORD_COUNT) {
        printf("FAIL: Expected %d records, got %u\n", EXPECTED_RECORD_COUNT, view.record_count);
        free(data);
        return 1;
    }
    printf("PASS: Record count correct (%u)\n", view.record_count);

    /* Read records and verify fields */
    int64_t total_damage = 0;

    for (uint32_t i = 0; i < view.record_count; i++) {
        icxs_record_t record;
        status = icxs_get_record(&view, i, &record);
        if (status != ICFG_OK) {
            printf("FAIL: Cannot get record %u: %d\n", i, status);
            free(data);
            return 1;
        }

        /* Verify ID (field 1) */
        int64_t id;
        status = icxs_get_i64(&record, 1, &id);
        if (status != ICFG_OK) {
            printf("FAIL: Cannot read id field for record %u: %d\n", i, status);
            free(data);
            return 1;
        }
        if (id != expected_items[i].id) {
            printf("FAIL: Record %u id: expected %ld, got %ld\n", i, expected_items[i].id, id);
            free(data);
            return 1;
        }

        /* Verify name (field 2) */
        const uint8_t* name_ptr;
        uint32_t name_len;
        status = icxs_get_str(&record, 2, &name_ptr, &name_len);
        if (status != ICFG_OK) {
            printf("FAIL: Cannot read name field for record %u: %d\n", i, status);
            free(data);
            return 1;
        }
        const char* expected_name = expected_items[i].name;
        size_t expected_name_len = strlen(expected_name);
        if (name_len != expected_name_len || strncmp((const char*)name_ptr, expected_name, name_len) != 0) {
            printf("FAIL: Record %u name mismatch\n", i);
            free(data);
            return 1;
        }

        /* Verify damage (field 3) */
        int64_t damage;
        status = icxs_get_i64(&record, 3, &damage);
        if (status != ICFG_OK) {
            printf("FAIL: Cannot read damage field for record %u: %d\n", i, status);
            free(data);
            return 1;
        }
        if (damage != expected_items[i].damage) {
            printf("FAIL: Record %u damage: expected %ld, got %ld\n", i, expected_items[i].damage, damage);
            free(data);
            return 1;
        }
        total_damage += damage;

        /* Verify speed (field 4) */
        int64_t speed;
        status = icxs_get_i64(&record, 4, &speed);
        if (status != ICFG_OK) {
            printf("FAIL: Cannot read speed field for record %u: %d\n", i, status);
            free(data);
            return 1;
        }
        if (speed != expected_items[i].speed) {
            printf("FAIL: Record %u speed: expected %ld, got %ld\n", i, expected_items[i].speed, speed);
            free(data);
            return 1;
        }

        /* Verify rarity (field 5) */
        int64_t rarity;
        status = icxs_get_i64(&record, 5, &rarity);
        if (status != ICFG_OK) {
            printf("FAIL: Cannot read rarity field for record %u: %d\n", i, status);
            free(data);
            return 1;
        }
        if (rarity != expected_items[i].rarity) {
            printf("FAIL: Record %u rarity: expected %ld, got %ld\n", i, expected_items[i].rarity, rarity);
            free(data);
            return 1;
        }

        printf("  Record %u: OK (id=%ld, name=%.*s, damage=%ld, speed=%ld, rarity=%ld)\n",
            i, id, (int)name_len, (const char*)name_ptr, damage, speed, rarity);
    }

    /* Verify total damage */
    if (total_damage != EXPECTED_TOTAL_DAMAGE) {
        printf("FAIL: Total damage: expected %ld, got %ld\n", EXPECTED_TOTAL_DAMAGE, total_damage);
        free(data);
        return 1;
    }
    printf("PASS: Total damage sum correct: %ld\n", total_damage);

    free(data);
    printf("PASS: All tests passed for %s\n", test_name);
    return 0;
}

int main(int argc, char* argv[]) {
    int failed = 0;

    printf("===============================================\n");
    printf("ICXS Golden Vector Tests\n");
    printf("===============================================\n");

    /* Test with CRC */
    failed += test_icxs_file("vectors/small/icxs/golden_items_crc.icxs", "golden_items_crc.icxs");

    /* Test without CRC */
    failed += test_icxs_file("vectors/small/icxs/golden_items_nocrc.icxs", "golden_items_nocrc.icxs");

    printf("\n===============================================\n");
    if (failed == 0) {
        printf("ALL TESTS PASSED\n");
    } else {
        printf("TESTS FAILED: %d test(s) failed\n", failed);
    }
    printf("===============================================\n");

    return failed ? 1 : 0;
}
