/* IRONCFG C99 - Unit Tests */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "../include/ironcfg/ironcfg.h"

/* Test framework */
#define ASSERT(cond) \
    do { \
        if (!(cond)) { \
            printf("FAIL: %s:%d: assertion failed\n", __FILE__, __LINE__); \
            return false; \
        } \
    } while (0)

#define ASSERT_EQ(a, b) \
    do { \
        if ((a) != (b)) { \
            printf("FAIL: %s:%d: expected %d, got %d\n", __FILE__, __LINE__, (int)(b), (int)(a)); \
            return false; \
        } \
    } while (0)

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

/* Test fixtures */

/* Minimal valid IRONCFG file (64-byte header + 0 schema) */
static uint8_t *make_header(uint32_t file_size, uint32_t schema_size, uint32_t data_size) {
    static uint8_t buf[1024];
    memset(buf, 0, 1024);

    /* Magic: ICFG (little-endian) */
    buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;
    /* Version */
    buf[4] = 1;
    /* Flags: CRC32 enabled */
    buf[5] = 0x01;
    /* Reserved0 */
    buf[6] = 0; buf[7] = 0;
    /* FileSize */
    buf[8] = (file_size >> 0) & 0xFF;
    buf[9] = (file_size >> 8) & 0xFF;
    buf[10] = (file_size >> 16) & 0xFF;
    buf[11] = (file_size >> 24) & 0xFF;
    /* SchemaOffset */
    buf[12] = 64; buf[13] = 0; buf[14] = 0; buf[15] = 0;
    /* SchemaSize */
    buf[16] = schema_size; buf[17] = 0; buf[18] = 0; buf[19] = 0;
    /* StringPoolOffset */
    buf[20] = 0; buf[21] = 0; buf[22] = 0; buf[23] = 0;
    /* StringPoolSize */
    buf[24] = 0; buf[25] = 0; buf[26] = 0; buf[27] = 0;
    /* DataOffset */
    uint32_t data_offset = 64 + schema_size;
    buf[28] = (data_offset >> 0) & 0xFF;
    buf[29] = (data_offset >> 8) & 0xFF;
    buf[30] = (data_offset >> 16) & 0xFF;
    buf[31] = (data_offset >> 24) & 0xFF;
    /* DataSize */
    buf[32] = data_size; buf[33] = 0; buf[34] = 0; buf[35] = 0;
    /* CrcOffset */
    uint32_t crc_offset = 64 + schema_size + data_size;
    buf[36] = (crc_offset >> 0) & 0xFF;
    buf[37] = (crc_offset >> 8) & 0xFF;
    buf[38] = (crc_offset >> 16) & 0xFF;
    buf[39] = (crc_offset >> 24) & 0xFF;
    /* Blake3Offset */
    buf[40] = 0; buf[41] = 0; buf[42] = 0; buf[43] = 0;
    /* Reserved1 */
    buf[44] = 0; buf[45] = 0; buf[46] = 0; buf[47] = 0;
    /* Reserved2 */
    for (int i = 0; i < 16; i++) buf[48 + i] = 0;

    return buf;
}

/* Test: Open with truncated file */
static bool test_truncated_file(void) {
    uint8_t buf[32];
    memset(buf, 0, 32);
    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buf, 32, &view);
    ASSERT_EQ(err.code, IRONCFG_TRUNCATED_FILE);
    ASSERT_EQ(err.offset, 0);
    return true;
}

/* Test: Open with invalid magic */
static bool test_invalid_magic(void) {
    uint8_t buf[64];
    memset(buf, 0, 64);
    buf[0] = 0xFF; buf[1] = 0xFF; buf[2] = 0xFF; buf[3] = 0xFF;
    buf[4] = 1;  /* valid version */
    buf[5] = 0;  /* valid flags */
    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buf, 64, &view);
    ASSERT_EQ(err.code, IRONCFG_INVALID_MAGIC);
    ASSERT_EQ(err.offset, 0);
    return true;
}

/* Test: Open with invalid version */
static bool test_invalid_version(void) {
    uint8_t buf[64];
    memset(buf, 0, 64);
    buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;  /* ICFG */
    buf[4] = 2;  /* invalid version */
    buf[5] = 0;
    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buf, 64, &view);
    ASSERT_EQ(err.code, IRONCFG_INVALID_VERSION);
    ASSERT_EQ(err.offset, 4);
    return true;
}

/* Test: Open with invalid flags (reserved bits set) */
static bool test_invalid_flags(void) {
    uint8_t buf[64];
    memset(buf, 0, 64);
    buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;  /* ICFG */
    buf[4] = 1;
    buf[5] = 0x80;  /* reserved bit 7 set */
    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buf, 64, &view);
    ASSERT_EQ(err.code, IRONCFG_INVALID_FLAGS);
    ASSERT_EQ(err.offset, 5);
    return true;
}

/* Test: Open with non-zero reserved0 */
static bool test_reserved0_nonzero(void) {
    uint8_t buf[64];
    memset(buf, 0, 64);
    buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;  /* ICFG */
    buf[4] = 1;
    buf[5] = 0;
    buf[6] = 0xFF;  /* reserved0 non-zero */
    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buf, 64, &view);
    ASSERT_EQ(err.code, IRONCFG_RESERVED_FIELD_NONZERO);
    ASSERT_EQ(err.offset, 6);
    return true;
}

/* Test: Open with flag mismatch (CRC flag set but no CRC) */
static bool test_flag_mismatch_crc(void) {
    uint8_t buf[64];
    memset(buf, 0, 64);
    buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;  /* ICFG */
    buf[4] = 1;
    buf[5] = 0x01;  /* CRC flag set */
    buf[6] = 0; buf[7] = 0;
    /* Set file size to 64 + 4 */
    buf[8] = 68; buf[9] = 0; buf[10] = 0; buf[11] = 0;
    /* SchemaOffset = 64 */
    buf[12] = 64; buf[13] = 0; buf[14] = 0; buf[15] = 0;
    /* SchemaSize = 0 */
    buf[16] = 1; buf[17] = 0; buf[18] = 0; buf[19] = 0;
    /* CrcOffset = 0 (mismatch!) */
    buf[36] = 0; buf[37] = 0; buf[38] = 0; buf[39] = 0;

    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buf, 64, &view);
    ASSERT_EQ(err.code, IRONCFG_FLAG_MISMATCH);
    ASSERT_EQ(err.offset, 5);
    return true;
}

/* Test: Open with bounds violation (offset beyond file) */
static bool test_bounds_violation(void) {
    uint8_t buf[64];
    memset(buf, 0, 64);
    buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;  /* ICFG */
    buf[4] = 1;
    buf[5] = 0;
    /* Set file size to 64 */
    buf[8] = 64; buf[9] = 0; buf[10] = 0; buf[11] = 0;
    /* SchemaOffset = 64 */
    buf[12] = 64; buf[13] = 0; buf[14] = 0; buf[15] = 0;
    /* SchemaSize = 100 (extends beyond file) */
    buf[16] = 100; buf[17] = 0; buf[18] = 0; buf[19] = 0;
    /* DataOffset = 64 */
    buf[28] = 64; buf[29] = 0; buf[30] = 0; buf[31] = 0;
    /* DataSize = 1 */
    buf[32] = 1; buf[33] = 0; buf[34] = 0; buf[35] = 0;

    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buf, 64, &view);
    ASSERT_EQ(err.code, IRONCFG_BOUNDS_VIOLATION);
    return true;
}

/* Test: Validate fast on valid header */
static bool test_validate_fast_ok(void) {
    uint8_t buf[64];
    memset(buf, 0, 64);
    buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;  /* ICFG */
    buf[4] = 1;
    buf[5] = 0;
    /* Set file size to 64 */
    buf[8] = 64; buf[9] = 0; buf[10] = 0; buf[11] = 0;
    /* SchemaOffset = 64 */
    buf[12] = 64; buf[13] = 0; buf[14] = 0; buf[15] = 0;
    /* SchemaSize = 0 */
    buf[16] = 0; buf[17] = 0; buf[18] = 0; buf[19] = 0;
    /* DataOffset = 64 */
    buf[28] = 64; buf[29] = 0; buf[30] = 0; buf[31] = 0;
    /* DataSize = 0 */
    buf[32] = 0; buf[33] = 0; buf[34] = 0; buf[35] = 0;

    ironcfg_error_t err = ironcfg_validate_fast(buf, 64);
    ASSERT_EQ(err.code, IRONCFG_BOUNDS_VIOLATION);  /* Schema and data both zero is invalid */
    return true;
}

/* Test: File size mismatch */
static bool test_file_size_mismatch(void) {
    uint8_t buf[64];
    memset(buf, 0, 64);
    buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47;  /* ICFG */
    buf[4] = 1;
    buf[5] = 0;
    /* Set file size to 100 but actually provide 64 */
    buf[8] = 100; buf[9] = 0; buf[10] = 0; buf[11] = 0;
    buf[12] = 64; buf[13] = 0; buf[14] = 0; buf[15] = 0;
    buf[16] = 1; buf[17] = 0; buf[18] = 0; buf[19] = 0;
    buf[28] = 65; buf[29] = 0; buf[30] = 0; buf[31] = 0;
    buf[32] = 1; buf[33] = 0; buf[34] = 0; buf[35] = 0;

    ironcfg_view_t view;
    ironcfg_error_t err = ironcfg_open(buf, 64, &view);
    ASSERT_EQ(err.code, IRONCFG_BOUNDS_VIOLATION);
    return true;
}

/* Main test runner */
int main(void) {
    int passed = 0;
    int failed = 0;

    test_fn tests[] = {
        test_truncated_file,
        test_invalid_magic,
        test_invalid_version,
        test_invalid_flags,
        test_reserved0_nonzero,
        test_flag_mismatch_crc,
        test_bounds_violation,
        test_file_size_mismatch,
    };

    const char *test_names[] = {
        "test_truncated_file",
        "test_invalid_magic",
        "test_invalid_version",
        "test_invalid_flags",
        "test_reserved0_nonzero",
        "test_flag_mismatch_crc",
        "test_bounds_violation",
        "test_file_size_mismatch",
    };

    printf("=== IRONCFG C99 Unit Tests ===\n\n");

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
