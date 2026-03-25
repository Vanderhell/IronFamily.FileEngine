/* IRONCFG C99 - Determinism Tests */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <math.h>
#include "../include/ironcfg/ironcfg_encode.h"

typedef bool (*test_fn)(void);

bool run_test(const char *name, test_fn fn) {
    printf("Running: %s ... ", name);
    if (fn()) {
        printf("OK\n");
        return true;
    } else {
        printf("FAILED\n");
        return false;
    }
}

/* Test: Same input encoded 3x produces identical bytes */
static bool test_encode_determinism_3x(void) {
    uint8_t buf1[1024], buf2[1024], buf3[1024];
    size_t size1, size2, size3;

    /* Create simple schema with one field */
    ironcfg_field_def_t fields[] = {
        {0, "count", 5, 0x11, 0x01}
    };
    ironcfg_schema_t schema = {fields, 1};

    /* Create simple value: object with one u64 field */
    ironcfg_value_t field_val = {
        IRONCFG_VAL_U64,
        {.u64_val = {42}}
    };
    ironcfg_value_t root = {
        IRONCFG_VAL_OBJECT,
        {.object_val = {
            .field_values = &field_val,
            .field_count = 1,
            .schema = &schema
        }}
    };

    /* Encode three times */
    ironcfg_error_t err1 = ironcfg_encode(&root, &schema, true, false, buf1, sizeof(buf1), &size1);
    ironcfg_error_t err2 = ironcfg_encode(&root, &schema, true, false, buf2, sizeof(buf2), &size2);
    ironcfg_error_t err3 = ironcfg_encode(&root, &schema, true, false, buf3, sizeof(buf3), &size3);

    if (err1.code != IRONCFG_OK || err2.code != IRONCFG_OK || err3.code != IRONCFG_OK) {
        printf("ENCODE ERROR\n");
        return false;
    }

    if (size1 != size2 || size2 != size3) {
        printf("SIZE MISMATCH: %zu %zu %zu\n", size1, size2, size3);
        return false;
    }

    if (memcmp(buf1, buf2, size1) != 0) {
        printf("BYTES 1-2 MISMATCH\n");
        return false;
    }

    if (memcmp(buf2, buf3, size2) != 0) {
        printf("BYTES 2-3 MISMATCH\n");
        return false;
    }

    return true;
}

/* Test: Encode with CRC32 produces valid checksum */
static bool test_encode_crc32_valid(void) {
    uint8_t buf[1024];
    size_t encoded_size;

    ironcfg_field_def_t fields[] = {
        {0, "value", 5, 0x10, 0x01}
    };
    ironcfg_schema_t schema = {fields, 1};

    ironcfg_value_t field_val = {
        IRONCFG_VAL_I64,
        {.i64_val = {-12345}}
    };
    ironcfg_value_t root = {
        IRONCFG_VAL_OBJECT,
        {.object_val = {
            .field_values = &field_val,
            .field_count = 1,
            .schema = &schema
        }}
    };

    ironcfg_error_t err = ironcfg_encode(&root, &schema, true, false, buf, sizeof(buf), &encoded_size);
    if (err.code != IRONCFG_OK) {
        return false;
    }

    /* Validate the file */
    ironcfg_view_t view;
    ironcfg_error_t validate_err = ironcfg_open(buf, encoded_size, &view);
    if (validate_err.code != IRONCFG_OK) {
        return false;
    }

    /* Verify CRC32 flag */
    if (!ironcfg_has_crc32(&view)) {
        return false;
    }

    return true;
}

/* Test: Float normalization (-0 -> +0) */
static bool test_float_normalization(void) {
    uint8_t buf1[1024], buf2[1024];
    size_t size1, size2;

    ironcfg_field_def_t fields[] = {
        {0, "temp", 4, 0x12, 0x01}
    };
    ironcfg_schema_t schema = {fields, 1};

    /* Encode with +0.0 */
    ironcfg_value_t field_val1 = {
        IRONCFG_VAL_F64,
        {.f64_val = {0.0}}
    };
    ironcfg_value_t root1 = {
        IRONCFG_VAL_OBJECT,
        {.object_val = {
            .field_values = &field_val1,
            .field_count = 1,
            .schema = &schema
        }}
    };

    /* Encode with -0.0 (create via negation) */
    double neg_zero = -0.0;
    ironcfg_value_t field_val2 = {
        IRONCFG_VAL_F64,
        {.f64_val = {neg_zero}}
    };
    ironcfg_value_t root2 = {
        IRONCFG_VAL_OBJECT,
        {.object_val = {
            .field_values = &field_val2,
            .field_count = 1,
            .schema = &schema
        }}
    };

    ironcfg_encode(&root1, &schema, true, false, buf1, sizeof(buf1), &size1);
    ironcfg_encode(&root2, &schema, true, false, buf2, sizeof(buf2), &size2);

    if (size1 != size2) {
        return false;
    }

    return memcmp(buf1, buf2, size1) == 0;
}

/* Test: NaN rejection */
static bool test_nan_rejection(void) {
    uint8_t buf[1024];
    size_t size;

    ironcfg_field_def_t fields[] = {
        {0, "data", 4, 0x12, 0x01}
    };
    ironcfg_schema_t schema = {fields, 1};

    /* Create NaN */
    double nan_val = sqrt(-1.0);
    ironcfg_value_t field_val = {
        IRONCFG_VAL_F64,
        {.f64_val = {nan_val}}
    };
    ironcfg_value_t root = {
        IRONCFG_VAL_OBJECT,
        {.object_val = {
            .field_values = &field_val,
            .field_count = 1,
            .schema = &schema
        }}
    };

    ironcfg_error_t err = ironcfg_encode(&root, &schema, true, false, buf, sizeof(buf), &size);
    return err.code == IRONCFG_INVALID_FLOAT;
}

/* Test: Deterministic field ordering */
static bool test_field_ordering_determinism(void) {
    uint8_t buf1[2048], buf2[2048];
    size_t size1, size2;

    /* Schema with 3 fields, IDs in ascending order */
    ironcfg_field_def_t fields[] = {
        {0, "alpha", 5, 0x11, 0x01},
        {1, "beta", 4, 0x11, 0x01},
        {2, "gamma", 5, 0x11, 0x01}
    };
    ironcfg_schema_t schema = {fields, 3};

    /* Create field values in same order as schema */
    ironcfg_value_t field_vals1[] = {
        {IRONCFG_VAL_U64, {.u64_val = {1}}},
        {IRONCFG_VAL_U64, {.u64_val = {2}}},
        {IRONCFG_VAL_U64, {.u64_val = {3}}}
    };

    ironcfg_value_t root1 = {
        IRONCFG_VAL_OBJECT,
        {.object_val = {
            .field_values = field_vals1,
            .field_count = 3,
            .schema = &schema
        }}
    };

    /* Re-encode to verify determinism */
    ironcfg_value_t field_vals2[] = {
        {IRONCFG_VAL_U64, {.u64_val = {1}}},
        {IRONCFG_VAL_U64, {.u64_val = {2}}},
        {IRONCFG_VAL_U64, {.u64_val = {3}}}
    };

    ironcfg_value_t root2 = {
        IRONCFG_VAL_OBJECT,
        {.object_val = {
            .field_values = field_vals2,
            .field_count = 3,
            .schema = &schema
        }}
    };

    ironcfg_encode(&root1, &schema, true, false, buf1, sizeof(buf1), &size1);
    ironcfg_encode(&root2, &schema, true, false, buf2, sizeof(buf2), &size2);

    if (size1 != size2) {
        return false;
    }

    return memcmp(buf1, buf2, size1) == 0;
}

int main(void) {
    int passed = 0;
    int failed = 0;

    test_fn tests[] = {
        test_encode_determinism_3x,
        test_encode_crc32_valid,
        test_float_normalization,
        test_nan_rejection,
        test_field_ordering_determinism,
    };

    const char *test_names[] = {
        "test_encode_determinism_3x",
        "test_encode_crc32_valid",
        "test_float_normalization",
        "test_nan_rejection",
        "test_field_ordering_determinism",
    };

    printf("=== IRONCFG C99 Determinism Tests ===\n\n");

    for (size_t i = 0; i < sizeof(tests) / sizeof(tests[0]); i++) {
        if (run_test(test_names[i], tests[i])) {
            passed++;
        } else {
            failed++;
        }
    }

    printf("\n=== Results ===\n");
    printf("Passed: %d\n", passed);
    printf("Failed: %d\n", failed);

    return failed > 0 ? 1 : 0;
}
