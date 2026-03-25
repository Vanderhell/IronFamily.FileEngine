/*
 * test_icf2_golden.c
 * Tests for ICF2 C implementation against golden vectors
 *
 * Verifies parity with .NET implementation:
 * - Opens all golden ICF2 files successfully
 * - Validates CRC when present
 * - Correctly parses row/field counts
 * - Rejects malformed files gracefully
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>

#include "../include/ironcfg/icf2.h"

/* Test data directory */
#define TEST_DATA_DIR "vectors/small/icf2"

typedef struct {
    const char* filename;
    int expected_status;
    uint32_t expected_rows;
    uint32_t expected_fields;
} test_case_t;

/* Golden vectors to test */
static test_case_t golden_vectors[] = {
    /* Golden vectors */
    { "golden_small.icf2", ICFG_OK, 3, 5 },
    { "golden_small_nocrc.icf2", ICFG_OK, 3, 5 },
    { "golden_large_schema.icf2", ICFG_OK, 10, 30 },

    /* Medium vectors */
    { "inputs/medium_01.icf2", ICFG_OK, 5, 5 },
    { "inputs/medium_02.icf2", ICFG_OK, 8, 5 },
    { "inputs/medium_03.icf2", ICFG_OK, 6, 5 },
    { "inputs/medium_04.icf2", ICFG_OK, 10, 4 },
    { "inputs/medium_05.icf2", ICFG_OK, 6, 4 },
    { "inputs/medium_06.icf2", ICFG_OK, 7, 4 },
    { "inputs/medium_07.icf2", ICFG_OK, 8, 4 },
    { "inputs/medium_08.icf2", ICFG_OK, 7, 4 },
    { "inputs/medium_09.icf2", ICFG_OK, 10, 4 },
    { "inputs/medium_10.icf2", ICFG_OK, 10, 4 },

    /* Mega vectors */
    { "inputs/mega_01.icf2", ICFG_OK, 30, 4 },
    { "inputs/mega_02.icf2", ICFG_OK, 50, 5 },
    { "inputs/mega_03.icf2", ICFG_OK, 100, 5 },

    /* Stress vectors */
    { "inputs/stress_01_depth.icf2", ICFG_OK, 3, 3 },
    { "inputs/stress_02_count.icf2", ICFG_OK, 500, 3 },
    { "inputs/stress_03_strings.icf2", ICFG_OK, 3, 4 },

    /* Terminator */
    { NULL, 0, 0, 0 }
};

/* Malformed test cases - Note: checks bounds before magic, so small files get ICFG_ERR_BOUNDS */
static test_case_t malformed_vectors[] = {
    { "fuzz/fuzz-icf2/corpus/bad_magic.bin", ICFG_ERR_BOUNDS, 0, 0 },      /* 36 bytes < 64-byte header */
    { "fuzz/fuzz-icf2/corpus/truncated_header.bin", ICFG_ERR_BOUNDS, 0, 0 },
    { "fuzz/fuzz-icf2/corpus/single_byte.bin", ICFG_ERR_BOUNDS, 0, 0 },
    { "fuzz/fuzz-icf2/corpus/bad_flags.bin", ICFG_ERR_BOUNDS, 0, 0 },      /* 36 bytes < 64-byte header */
    { NULL, 0, 0, 0 }
};

static int read_file(const char* filename, uint8_t** out_data, size_t* out_size)
{
    FILE* fp = fopen(filename, "rb");
    if (!fp) {
        printf("  ERROR: Could not open %s\n", filename);
        return -1;
    }

    fseek(fp, 0, SEEK_END);
    long size = ftell(fp);
    fseek(fp, 0, SEEK_SET);

    if (size <= 0) {
        fclose(fp);
        return -1;
    }

    *out_data = malloc((size_t)size);
    if (!*out_data) {
        fclose(fp);
        return -1;
    }

    size_t nread = fread(*out_data, 1, (size_t)size, fp);
    fclose(fp);

    if (nread != (size_t)size) {
        free(*out_data);
        return -1;
    }

    *out_size = (size_t)size;
    return 0;
}

static const char* status_name(icfg_status_t status)
{
    switch (status) {
    case ICFG_OK: return "ICFG_OK";
    case ICFG_ERR_MAGIC: return "ICFG_ERR_MAGIC";
    case ICFG_ERR_BOUNDS: return "ICFG_ERR_BOUNDS";
    case ICFG_ERR_CRC: return "ICFG_ERR_CRC";
    case ICFG_ERR_SCHEMA: return "ICFG_ERR_SCHEMA";
    case ICFG_ERR_TYPE: return "ICFG_ERR_TYPE";
    case ICFG_ERR_RANGE: return "ICFG_ERR_RANGE";
    case ICFG_ERR_UNSUPPORTED: return "ICFG_ERR_UNSUPPORTED";
    case ICFG_ERR_INVALID_ARGUMENT: return "ICFG_ERR_INVALID_ARGUMENT";
    default: return "UNKNOWN";
    }
}

static int test_vectors(test_case_t* vectors, const char* label)
{
    printf("\n=== %s ===\n", label);

    int pass_count = 0;
    int fail_count = 0;

    for (test_case_t* tc = vectors; tc->filename; tc++) {
        uint8_t* data = NULL;
        size_t size = 0;

        char fullpath[512];
        /* Don't prepend TEST_DATA_DIR if path starts with fuzz/ */
        if (strncmp(tc->filename, "fuzz/", 5) == 0) {
            snprintf(fullpath, sizeof(fullpath), "%s", tc->filename);
        } else {
            snprintf(fullpath, sizeof(fullpath), "%s/%s", TEST_DATA_DIR, tc->filename);
        }

        if (read_file(fullpath, &data, &size) != 0) {
            printf("SKIP: %s (file not found)\n", tc->filename);
            continue;
        }

        icf2_view_t view;
        icfg_status_t status = icf2_open(data, size, &view);

        if (status == tc->expected_status) {
            if (status == ICFG_OK) {
                uint32_t rows, fields;
                icf2_row_count(&view, &rows);
                icf2_field_count(&view, &fields);

                if (rows == tc->expected_rows && fields == tc->expected_fields) {
                    printf("PASS: %s (rows=%u, fields=%u)\n",
                           tc->filename, rows, fields);
                    pass_count++;
                } else {
                    printf("FAIL: %s (rows: got %u, expected %u; fields: got %u, expected %u)\n",
                           tc->filename, rows, tc->expected_rows, fields, tc->expected_fields);
                    fail_count++;
                }

                /* Also test validate */
                icfg_status_t validate_status = icf2_validate(&view);
                if (validate_status != ICFG_OK) {
                    printf("  WARNING: validate failed with %s\n", status_name(validate_status));
                    fail_count++;
                }
            } else {
                printf("PASS: %s (rejected as expected: %s)\n",
                       tc->filename, status_name(status));
                pass_count++;
            }
        } else {
            printf("FAIL: %s (got %s, expected %s)\n",
                   tc->filename, status_name(status), status_name(tc->expected_status));
            fail_count++;
        }

        free(data);
    }

    printf("\nResult: %d passed, %d failed\n", pass_count, fail_count);
    return fail_count;
}

int main(void)
{
    printf("ICF2 C Implementation - Golden Vector Tests\n");
    printf("=============================================\n");

    int total_fails = 0;

    total_fails += test_vectors(golden_vectors, "Valid Golden Vectors");
    total_fails += test_vectors(malformed_vectors, "Malformed Vectors (Expected Failures)");

    printf("\n========================================\n");
    if (total_fails == 0) {
        printf("ALL TESTS PASSED\n");
        return 0;
    } else {
        printf("TESTS FAILED: %d errors\n", total_fails);
        return 1;
    }
}
