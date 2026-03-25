/*
 * test_crc.c
 * CRC32 IEEE/ZIP unit tests
 */

#include <stdio.h>
#include <string.h>
#include <ironcfg/ironcfg_common.h>

int main() {
    int tests_passed = 0;
    int tests_failed = 0;

    printf("CRC32 IEEE/ZIP Unit Tests\n");
    printf("================================================\n\n");

    /* Test 1: Standard test vector "123456789" */
    {
        const uint8_t test_data[] = "123456789";
        uint32_t result = icfg_crc32(test_data, strlen((const char*)test_data));
        uint32_t expected = 0xCBF43926U;

        printf("Test 1: Standard vector '123456789'\n");
        printf("  Expected: 0x%08X\n", expected);
        printf("  Computed: 0x%08X\n", result);
        printf("  Status: %s\n", result == expected ? "PASS ✓" : "FAIL ✗");

        if (result == expected) {
            tests_passed++;
        } else {
            tests_failed++;
        }
    }

    printf("\n");

    /* Test 2: Empty string */
    {
        const uint8_t test_data[] = "";
        uint32_t result = icfg_crc32(test_data, 0);
        uint32_t expected = 0x00000000U;

        printf("Test 2: Empty string\n");
        printf("  Expected: 0x%08X\n", expected);
        printf("  Computed: 0x%08X\n", result);
        printf("  Status: %s\n", result == expected ? "PASS ✓" : "FAIL ✗");

        if (result == expected) {
            tests_passed++;
        } else {
            tests_failed++;
        }
    }

    printf("\n");

    /* Test 3: Single byte */
    {
        const uint8_t test_data[] = { 0x00 };
        uint32_t result = icfg_crc32(test_data, 1);
        uint32_t expected = 0xD202EF8DU;

        printf("Test 3: Single byte 0x00\n");
        printf("  Expected: 0x%08X\n", expected);
        printf("  Computed: 0x%08X\n", result);
        printf("  Status: %s\n", result == expected ? "PASS ✓" : "FAIL ✗");

        if (result == expected) {
            tests_passed++;
        } else {
            tests_failed++;
        }
    }

    printf("\n");
    printf("================================================\n");
    printf("Results: %d passed, %d failed\n", tests_passed, tests_failed);
    printf("================================================\n\n");

    return (tests_failed == 0) ? 0 : 1;
}
