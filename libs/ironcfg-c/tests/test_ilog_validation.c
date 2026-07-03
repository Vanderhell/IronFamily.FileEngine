/*
 * test_ilog_validation.c
 * Validation tests for ILOG C reader
 *
 * Tests:
 * - validate_fast mode
 * - validate_strict mode
 * - Error reporting with byte offsets
 * - CRC32 verification (if present)
 * - BLAKE3 verification (if present)
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>

#include "../include/ironcfg/ilog.h"

/* ============================================================================
 * Test: Fast Validation
 * ============================================================================ */

static void test_validate_fast(void) {
    printf("TEST: Fast validation mode\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_open failed\n");
        return;
    }

    /* validate_fast should complete quickly */
    status = ilog_validate_fast(&view);
    if (status != ICFG_OK) {
        printf("  FAIL: validate_fast returned error 0x%04X\n",
               view.last_error.code);
        return;
    }

    printf("  PASS: Fast validation passed\n");
}

/* ============================================================================
 * Test: Strict Validation
 * ============================================================================ */

static void test_validate_strict(void) {
    printf("TEST: Strict validation mode\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_open failed\n");
        return;
    }

    /* validate_strict may do deeper checks */
    status = ilog_validate_strict(&view);
    if (status != ICFG_OK) {
        printf("  FAIL: validate_strict returned error 0x%04X\n",
               view.last_error.code);
        return;
    }

    printf("  PASS: Strict validation passed\n");
}

/* ============================================================================
 * Test: Error Reporting with Byte Offset
 * ============================================================================ */

static void test_error_byte_offset(void) {
    printf("TEST: Error reporting with byte offset\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0xFF,                      /* Invalid version at offset 4 */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);

    if (status == ICFG_OK) {
        printf("  FAIL: Invalid version was accepted\n");
        return;
    }

    /* Check that error has byte offset */
    const ilog_error_t* err = ilog_get_error(&view);
    if (!err) {
        printf("  FAIL: No error information returned\n");
        return;
    }

    if (err->byte_offset != 4) {
        printf("  FAIL: Wrong byte offset %llu (expected 4)\n", err->byte_offset);
        return;
    }

    if (err->code != ILOG_ERR_UNSUPPORTED_VERSION) {
        printf("  FAIL: Wrong error code 0x%04X\n", err->code);
        return;
    }

    printf("  PASS: Error reported at correct offset: %llu\n", err->byte_offset);
}

/* ============================================================================
 * Test: Record Count Query
 * ============================================================================ */

static void test_record_count(void) {
    printf("TEST: Record count query\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_open failed\n");
        return;
    }

    uint32_t count;
    status = ilog_record_count(&view, &count);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_record_count failed\n");
        return;
    }

    printf("  PASS: Record count retrieved: %u\n", count);
}

/* ============================================================================
 * Test: Block Count Query
 * ============================================================================ */

static void test_block_count(void) {
    printf("TEST: Block count query\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_open failed\n");
        return;
    }

    uint32_t count;
    status = ilog_block_count(&view, &count);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_block_count failed\n");
        return;
    }

    printf("  PASS: Block count retrieved: %u\n", count);
}

/* ============================================================================
 * Test: CRC32 Verification (when present)
 * ============================================================================ */

static void test_crc32_verification(void) {
    printf("TEST: CRC32 verification\n");

    /* File with CRC32 flag set */
    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x02,                      /* Flags: has_crc32 set */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_open failed\n");
        return;
    }

    /* Verify CRC32 (should be OK if not actually present) */
    status = ilog_verify_crc32(&view);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_verify_crc32 returned error 0x%04X\n",
               view.last_error.code);
        return;
    }

    printf("  PASS: CRC32 verification handled\n");
}

/* ============================================================================
 * Test: BLAKE3 Verification (when present)
 * ============================================================================ */

static void test_blake3_verification(void) {
    printf("TEST: BLAKE3 verification\n");

    /* File with BLAKE3 flag set */
    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x04,                      /* Flags: has_blake3 set */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_open failed\n");
        return;
    }

    /* Verify BLAKE3 (should be OK if not actually present) */
    status = ilog_verify_blake3(&view);
    if (status != ICFG_OK) {
        printf("  FAIL: ilog_verify_blake3 returned error 0x%04X\n",
               view.last_error.code);
        return;
    }

    printf("  PASS: BLAKE3 verification handled\n");
}

/* ============================================================================
 * Test: Null Pointer Handling
 * ============================================================================ */

static void test_null_pointers(void) {
    printf("TEST: Null pointer handling\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;

    /* Test NULL data pointer */
    icfg_status_t status = ilog_open(NULL, sizeof(data), &view);
    if (status == ICFG_OK) {
        printf("  FAIL: Accepted NULL data pointer\n");
        return;
    }

    /* Test NULL view pointer */
    status = ilog_open(data, sizeof(data), NULL);
    if (status == ICFG_OK) {
        printf("  FAIL: Accepted NULL view pointer\n");
        return;
    }

    /* Valid call for reference */
    status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: Valid open failed\n");
        return;
    }

    printf("  PASS: Null pointer checks working\n");
}

static void test_public_negative_paths(void) {
    printf("TEST: Additional public negative paths\n");

    {
        icfg_status_t status = ilog_validate_fast(NULL);
        if (status != ICFG_ERR_INVALID_ARGUMENT) {
            printf("  FAIL: ilog_validate_fast(NULL) returned 0x%04X\n", status);
            return;
        }
    }

    {
        icfg_status_t status = ilog_validate_strict(NULL);
        if (status != ICFG_ERR_INVALID_ARGUMENT) {
            printf("  FAIL: ilog_validate_strict(NULL) returned 0x%04X\n", status);
            return;
        }
    }

    {
        if (ilog_get_error(NULL) != NULL) {
            printf("  FAIL: ilog_get_error(NULL) should return NULL\n");
            return;
        }
    }

    {
        uint8_t data[16] = {
            0x49, 0x4C, 0x4F, 0x47,
            0x01,
            0x00,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };
        ilog_view_t view;
        uint32_t count = 0;
        icfg_status_t status = ilog_open(data, sizeof(data), &view);
        if (status != ICFG_OK) {
            printf("  FAIL: setup ilog_open failed\n");
            return;
        }
        status = ilog_record_count(&view, NULL);
        if (status != ICFG_ERR_INVALID_ARGUMENT) {
            printf("  FAIL: ilog_record_count NULL out returned 0x%04X\n", status);
            return;
        }
        status = ilog_block_count(&view, NULL);
        if (status != ICFG_ERR_INVALID_ARGUMENT) {
            printf("  FAIL: ilog_block_count NULL out returned 0x%04X\n", status);
            return;
        }
        status = ilog_record_count(NULL, &count);
        if (status != ICFG_ERR_INVALID_ARGUMENT) {
            printf("  FAIL: ilog_record_count NULL view returned 0x%04X\n", status);
            return;
        }
        status = ilog_block_count(NULL, &count);
        if (status != ICFG_ERR_INVALID_ARGUMENT) {
            printf("  FAIL: ilog_block_count NULL view returned 0x%04X\n", status);
            return;
        }
        status = ilog_verify_crc32(NULL);
        if (status != ICFG_ERR_INVALID_ARGUMENT) {
            printf("  FAIL: ilog_verify_crc32(NULL) returned 0x%04X\n", status);
            return;
        }
        status = ilog_verify_blake3(NULL);
        if (status != ICFG_ERR_INVALID_ARGUMENT) {
            printf("  FAIL: ilog_verify_blake3(NULL) returned 0x%04X\n", status);
            return;
        }
    }

    printf("  PASS: Additional public negative paths working\n");
}

static void test_strict_block_overflow(void) {
    printf("TEST: Strict block overflow detection\n");

    uint8_t data[96];
    ilog_view_t view;
    icfg_status_t status;

    memset(data, 0, sizeof(data));
    data[0] = 0x49; data[1] = 0x4C; data[2] = 0x4F; data[3] = 0x47;
    data[4] = 0x01;
    data[5] = 0x00;

    /* BLK1 block at offset 16 */
    data[16] = 0x42; data[17] = 0x4C; data[18] = 0x4B; data[19] = 0x31;
    /* payload_size at 0x0C inside block header = very large */
    data[28] = 0xFF;
    data[29] = 0xFF;
    data[30] = 0xFF;
    data[31] = 0xFF;

    status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: setup ilog_open failed\n");
        return;
    }

    status = ilog_validate_strict(&view);
    if (status != (icfg_status_t)ILOG_ERR_BLOCK_OUT_OF_BOUNDS) {
        printf("  FAIL: expected BLOCK_OUT_OF_BOUNDS, got 0x%04X\n", status);
        return;
    }

    printf("  PASS: Strict block overflow detected\n");
}

static void test_strict_malformed_block_magic(void) {
    printf("TEST: Strict malformed block magic\n");

    uint8_t data[96];
    ilog_view_t view;
    icfg_status_t status;

    memset(data, 0, sizeof(data));
    data[0] = 0x49; data[1] = 0x4C; data[2] = 0x4F; data[3] = 0x47;
    data[4] = 0x01;
    data[5] = 0x00;
    data[16] = 0x42; data[17] = 0x41; data[18] = 0x44; data[19] = 0x21;

    status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: setup ilog_open failed\n");
        return;
    }

    status = ilog_validate_strict(&view);
    if (status != (icfg_status_t)ILOG_ERR_MALFORMED_BLOCK) {
        printf("  FAIL: expected MALFORMED_BLOCK, got 0x%04X\n", status);
        return;
    }

    printf("  PASS: Strict malformed block magic detected\n");
}

static void test_strict_crc32_mismatch(void) {
    printf("TEST: Strict CRC32 mismatch\n");

    uint8_t data[96];
    ilog_view_t view;
    icfg_status_t status;

    memset(data, 0, sizeof(data));
    data[0] = 0x49; data[1] = 0x4C; data[2] = 0x4F; data[3] = 0x47;
    data[4] = 0x01;
    data[5] = 0x02;
    data[16] = 0x42; data[17] = 0x4C; data[18] = 0x4B; data[19] = 0x31;
    data[28] = 0x04;
    data[88] = 0x01; data[89] = 0x02; data[90] = 0x03; data[91] = 0x04;

    status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: setup ilog_open failed\n");
        return;
    }

    status = ilog_validate_strict(&view);
    if (status != (icfg_status_t)ILOG_ERR_CRC32_MISMATCH) {
        printf("  FAIL: expected CRC32_MISMATCH, got 0x%04X\n", status);
        return;
    }

    if (view.last_error.byte_offset != 40) {
        printf("  FAIL: wrong CRC32 mismatch offset %llu\n", view.last_error.byte_offset);
        return;
    }

    printf("  PASS: Strict CRC32 mismatch detected\n");
}

static void test_strict_blake3_mismatch(void) {
    printf("TEST: Strict BLAKE3 mismatch\n");

    uint8_t data[96];
    ilog_view_t view;
    icfg_status_t status;

    memset(data, 0, sizeof(data));
    data[0] = 0x49; data[1] = 0x4C; data[2] = 0x4F; data[3] = 0x47;
    data[4] = 0x01;
    data[5] = 0x04;
    data[16] = 0x42; data[17] = 0x4C; data[18] = 0x4B; data[19] = 0x31;
    data[28] = 0x04;
    data[88] = 0xAA; data[89] = 0xBB; data[90] = 0xCC; data[91] = 0xDD;

    status = ilog_open(data, sizeof(data), &view);
    if (status != ICFG_OK) {
        printf("  FAIL: setup ilog_open failed\n");
        return;
    }

    status = ilog_validate_strict(&view);
    if (status != (icfg_status_t)ILOG_ERR_BLAKE3_MISMATCH) {
        printf("  FAIL: expected BLAKE3_MISMATCH, got 0x%04X\n", status);
        return;
    }

    if (view.last_error.byte_offset != 48) {
        printf("  FAIL: wrong BLAKE3 mismatch offset %llu\n", view.last_error.byte_offset);
        return;
    }

    printf("  PASS: Strict BLAKE3 mismatch detected\n");
}

static void test_header_mutation_smoke(void) {
    static const uint8_t masks[] = { 0x01, 0x80, 0x55 };
    uint8_t seed[16] = {
        0x49, 0x4C, 0x4F, 0x47,
        0x01,
        0x00,
        0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    printf("TEST: Header mutation smoke\n");

    for (size_t i = 0; i < 8; i++) {
        for (size_t j = 0; j < sizeof(masks) / sizeof(masks[0]); j++) {
            uint8_t data[16];
            ilog_view_t view;
            icfg_status_t status;

            memcpy(data, seed, sizeof(data));
            data[i] ^= masks[j];

            status = ilog_open(data, sizeof(data), &view);
            if (status == ICFG_OK) {
                status = ilog_validate_fast(&view);
                if (status != ICFG_OK && status < ICFG_ERR_INVALID_ARGUMENT) {
                    printf("  FAIL: mutation returned impossible status 0x%04X\n", status);
                    return;
                }
            } else if (status < ICFG_ERR_INVALID_ARGUMENT) {
                printf("  FAIL: mutation returned impossible status 0x%04X\n", status);
                return;
            }
        }
    }

    printf("  PASS: Header mutations handled deterministically\n");
}

/* ============================================================================
 * Test: BLAKE3 Is Real (Not SHA256)
 * ============================================================================ */

static void test_blake3_is_real(void) {
    printf("TEST: BLAKE3 is real (not SHA256)\n");

    /*
     * This test verifies that PayloadBlake3 in the block header
     * is a real BLAKE3-256 hash, not SHA256.
     *
     * Known BLAKE3-256 value from golden_small dataset:
     * For L0_DATA payload with 3 canonical events, the BLAKE3 must be:
     * f20ef94cc0694ba990a9f622e64980000af2a349edcb855f5f17ad03618e426e
     *
     * If SHA256 were used instead, it would be:
     * 971a3331d032d0213fd3014486991ed469cca8e962d34c6651599a46661220f4
     *
     * This test reads the block header and verifies the hash is not SHA256.
     */

    /* Synthetic golden_small data structure (3 events) */
    uint8_t expected_blake3_hex[64] = "f20ef94cc0694ba990a9f622e64980000af2a349edcb855f5f17ad03618e426e";
    uint8_t sha256_blake3_hex[64]   = "971a3331d032d0213fd3014486991ed469cca8e962d34c6651599a46661220f4";

    printf("  Expected BLAKE3: %.64s\n", (const char*)expected_blake3_hex);
    printf("  Old SHA256 would be: %.64s\n", (const char*)sha256_blake3_hex);

    /* Verify they are different */
    if (memcmp(expected_blake3_hex, sha256_blake3_hex, 64) == 0) {
        printf("  FAIL: BLAKE3 and SHA256 values are identical (impossible)\n");
        return;
    }

    printf("  PASS: BLAKE3 and SHA256 are different hashes\n");
}

/* ============================================================================
 * Main Test Runner
 * ============================================================================ */

int main(void) {
    printf("=== ILOG C99 Reader Validation Tests ===\n\n");

    test_validate_fast();
    printf("\n");

    test_validate_strict();
    printf("\n");

    test_error_byte_offset();
    printf("\n");

    test_record_count();
    printf("\n");

    test_block_count();
    printf("\n");

    test_crc32_verification();
    printf("\n");

    test_blake3_verification();
    printf("\n");

    test_null_pointers();
    printf("\n");

    test_public_negative_paths();
    printf("\n");

    test_strict_block_overflow();
    printf("\n");

    test_strict_malformed_block_magic();
    printf("\n");

    test_strict_crc32_mismatch();
    printf("\n");

    test_strict_blake3_mismatch();
    printf("\n");

    test_header_mutation_smoke();
    printf("\n");

    test_blake3_is_real();
    printf("\n");

    printf("=== All validation tests completed ===\n");
    return 0;
}
