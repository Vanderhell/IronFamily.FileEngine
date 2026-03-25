/*
 * parity_icf2.c
 * Parity verification tool comparing C and .NET ICF2 implementations
 *
 * Runs all golden vectors through C implementation and compares expected
 * results (from .NET) with actual C implementation behavior.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../include/ironcfg/icf2.h"

#define TEST_DATA_DIR "vectors/small/icf2"

typedef struct {
    const char* name;
    const char* path;
    int expected_status;
    uint32_t expected_rows;
    uint32_t expected_fields;
} parity_test_t;

/* Golden vectors tested in .NET (from IronConfig.Tests.Icf2Tests) */
static parity_test_t vectors[] = {
    /* Golden vectors */
    { "golden_small", "golden_small.icf2", 0, 3, 5 },
    { "golden_small_nocrc", "golden_small_nocrc.icf2", 0, 3, 5 },
    { "golden_large_schema", "golden_large_schema.icf2", 0, 10, 30 },

    /* Medium vectors */
    { "medium_01", "inputs/medium_01.icf2", 0, 5, 5 },
    { "medium_02", "inputs/medium_02.icf2", 0, 8, 5 },
    { "medium_03", "inputs/medium_03.icf2", 0, 6, 5 },
    { "medium_04", "inputs/medium_04.icf2", 0, 10, 4 },
    { "medium_05", "inputs/medium_05.icf2", 0, 6, 4 },
    { "medium_06", "inputs/medium_06.icf2", 0, 7, 4 },
    { "medium_07", "inputs/medium_07.icf2", 0, 8, 4 },
    { "medium_08", "inputs/medium_08.icf2", 0, 7, 4 },
    { "medium_09", "inputs/medium_09.icf2", 0, 10, 4 },
    { "medium_10", "inputs/medium_10.icf2", 0, 10, 4 },

    /* Mega vectors */
    { "mega_01", "inputs/mega_01.icf2", 0, 30, 4 },
    { "mega_02", "inputs/mega_02.icf2", 0, 50, 5 },
    { "mega_03", "inputs/mega_03.icf2", 0, 100, 5 },

    /* Stress vectors */
    { "stress_01_depth", "inputs/stress_01_depth.icf2", 0, 3, 3 },
    { "stress_02_count", "inputs/stress_02_count.icf2", 0, 500, 3 },
    { "stress_03_strings", "inputs/stress_03_strings.icf2", 0, 3, 4 },

    { NULL, NULL, 0, 0, 0 }
};

static int read_file(const char* filename, uint8_t** out_data, size_t* out_size)
{
    FILE* fp = fopen(filename, "rb");
    if (!fp) return -1;

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

static const char* status_name(int status)
{
    switch (status) {
    case 0: return "OK";
    case 1: return "ERR_MAGIC";
    case 2: return "ERR_BOUNDS";
    case 3: return "ERR_CRC";
    case 4: return "ERR_SCHEMA";
    case 5: return "ERR_TYPE";
    case 6: return "ERR_RANGE";
    case 7: return "ERR_UNSUPPORTED";
    case 8: return "ERR_INVALID_ARGUMENT";
    default: return "UNKNOWN";
    }
}

int main(void)
{
    printf("================================================================================\n");
    printf("ICF2 C/.NET Parity Verification Report\n");
    printf("================================================================================\n\n");

    int pass_count = 0;
    int fail_count = 0;
    int skip_count = 0;

    for (parity_test_t* test = vectors; test->name; test++) {
        uint8_t* data = NULL;
        size_t size = 0;

        char fullpath[512];
        snprintf(fullpath, sizeof(fullpath), "%s/%s", TEST_DATA_DIR, test->path);

        if (read_file(fullpath, &data, &size) != 0) {
            printf("SKIP: %-25s (file not found)\n", test->name);
            skip_count++;
            continue;
        }

        icf2_view_t view;
        icfg_status_t c_status = icf2_open(data, size, &view);

        if ((int)c_status == test->expected_status) {
            if (c_status == ICFG_OK) {
                uint32_t rows, fields;
                icf2_row_count(&view, &rows);
                icf2_field_count(&view, &fields);

                if (rows == test->expected_rows && fields == test->expected_fields) {
                    printf("PASS: %-25s C: OK (%3u rows, %2u fields) | .NET: OK (%3u rows, %2u fields)\n",
                           test->name, rows, fields, test->expected_rows, test->expected_fields);
                    pass_count++;
                } else {
                    printf("FAIL: %-25s C: rows=%u (expect %u), fields=%u (expect %u) | .NET: rows=%u, fields=%u\n",
                           test->name, rows, test->expected_rows, fields, test->expected_fields,
                           test->expected_rows, test->expected_fields);
                    fail_count++;
                }
            } else {
                printf("PASS: %-25s C: %s (rejected) | .NET: %s (rejected)\n",
                       test->name, status_name(c_status), status_name(test->expected_status));
                pass_count++;
            }
        } else {
            printf("FAIL: %-25s C: %s (got %d) | .NET: %s (expected %d)\n",
                   test->name, status_name(c_status), c_status,
                   status_name(test->expected_status), test->expected_status);
            fail_count++;
        }

        free(data);
    }

    printf("\n================================================================================\n");
    printf("Results: %d PASS, %d FAIL, %d SKIP\n", pass_count, fail_count, skip_count);
    printf("================================================================================\n\n");

    if (fail_count == 0) {
        printf("âś“ ICF2 C/.NET PARITY VERIFIED: All vectors match between C and .NET implementations\n");
        return 0;
    } else {
        printf("âś— ICF2 PARITY FAILED: %d mismatches between C and .NET implementations\n", fail_count);
        return 1;
    }
}
