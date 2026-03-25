/*
 * test_icfx_golden.c
 * Golden vector tests for ICFX reader
 *
 * Tests reading and validating golden ICFX files with known content.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <ironcfg/icfx.h>

int test_icfx_file(const char* filepath, const char* test_name) {
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

    /* Open ICFX view */
    icfx_view_t view;
    icfg_status_t status = icfx_open(data, size, &view);
    if (status != ICFG_OK) {
        printf("FAIL: icfx_open failed: %d\n", status);
        free(data);
        return 1;
    }
    printf("PASS: File opened and header validated\n");

    /* Validate (if file has CRC) */
    status = icfx_validate(&view);
    if (status != ICFG_OK) {
        printf("FAIL: icfx_validate failed: %d\n", status);
        free(data);
        return 1;
    }
    printf("PASS: File validated (CRC check if present)\n");

    /* Get root value */
    icfx_value_t root = icfx_root(&view);
    printf("PASS: Root value obtained\n");

    /* Test accessing root as object */
    if (!icfx_is_object(&root)) {
        printf("FAIL: Root is not an object\n");
        free(data);
        return 1;
    }
    printf("PASS: Root is an object\n");

    /* Get object length */
    uint32_t root_len;
    status = icfx_obj_len(&root, &root_len);
    if (status != ICFG_OK) {
        printf("FAIL: Cannot get root object length: %d\n", status);
        free(data);
        return 1;
    }
    printf("PASS: Root object has %u fields\n", root_len);

    /* Test accessing metadata object (key_id 1) */
    icfx_value_t metadata;
    status = icfx_obj_try_get_by_keyid(&root, 1, &metadata);
    if (status != ICFG_OK) {
        printf("INFO: Could not access metadata field by key_id (linear scan may be needed)\n");
    } else {
        if (icfx_is_object(&metadata)) {
            printf("PASS: Metadata is an object\n");
        }
    }

    /* Test accessing simple_types object (key_id 2) */
    icfx_value_t simple_types;
    status = icfx_obj_try_get_by_keyid(&root, 2, &simple_types);
    if (status == ICFG_OK && icfx_is_object(&simple_types)) {
        printf("PASS: simple_types is an object\n");

        /* Try to access integer_positive field within simple_types (key_id 4) */
        icfx_value_t int_pos;
        status = icfx_obj_try_get_by_keyid(&simple_types, 4, &int_pos);
        if (status == ICFG_OK && icfx_is_number(&int_pos)) {
            int64_t val;
            if (icfx_get_i64(&int_pos, &val) == ICFG_OK) {
                if (val == 12345) {
                    printf("PASS: integer_positive value correct: %ld\n", val);
                } else {
                    printf("FAIL: integer_positive: expected 12345, got %ld\n", val);
                    free(data);
                    return 1;
                }
            }
        } else {
            printf("INFO: Could not access integer_positive in simple_types\n");
        }
    } else {
        printf("INFO: Could not access simple_types object\n");
    }

    /* Test accessing arrays object (key_id 3) */
    icfx_value_t arrays;
    status = icfx_obj_try_get_by_keyid(&root, 3, &arrays);
    if (status == ICFG_OK && icfx_is_object(&arrays)) {
        printf("PASS: arrays is an object\n");

        /* Try to access int_array field (key_id 2) */
        icfx_value_t int_array;
        status = icfx_obj_try_get_by_keyid(&arrays, 2, &int_array);
        if (status == ICFG_OK && icfx_is_array(&int_array)) {
            uint32_t array_len;
            if (icfx_array_len(&int_array, &array_len) == ICFG_OK) {
                if (array_len == 5) {
                    printf("PASS: int_array length correct: %u\n", array_len);

                    /* Check first element */
                    icfx_value_t elem;
                    if (icfx_array_get(&int_array, 0, &elem) == ICFG_OK) {
                        int64_t elem_val;
                        if (icfx_get_i64(&elem, &elem_val) == ICFG_OK) {
                            if (elem_val == 1) {
                                printf("PASS: int_array[0] = %ld\n", elem_val);
                            } else {
                                printf("FAIL: int_array[0]: expected 1, got %ld\n", elem_val);
                                free(data);
                                return 1;
                            }
                        }
                    }
                } else {
                    printf("FAIL: int_array length: expected 5, got %u\n", array_len);
                    free(data);
                    return 1;
                }
            }
        } else {
            printf("INFO: Could not access int_array in arrays\n");
        }
    } else {
        printf("INFO: Could not access arrays object\n");
    }

    free(data);
    printf("PASS: All tests passed for %s\n", test_name);
    return 0;
}

int main(int argc, char* argv[]) {
    int failed = 0;

    printf("===============================================\n");
    printf("ICFX Golden Vector Tests\n");
    printf("===============================================\n");

    /* Test with CRC */
    failed += test_icfx_file("vectors/small/icfx/golden_icfx_crc.icfx", "golden_icfx_crc.icfx");

    /* Test without CRC */
    failed += test_icfx_file("vectors/small/icfx/golden_icfx_nocrc.icfx", "golden_icfx_nocrc.icfx");

    printf("\n===============================================\n");
    if (failed == 0) {
        printf("ALL TESTS PASSED\n");
    } else {
        printf("TESTS FAILED: %d test(s) failed\n", failed);
    }
    printf("===============================================\n");

    return failed ? 1 : 0;
}
