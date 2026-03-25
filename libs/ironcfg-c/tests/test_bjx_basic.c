/*
 * test_bjx_basic.c
 * Basic BJX reader tests: magic, version, truncation, offset validation.
 *
 * License: MIT
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#include "ironcfg_common.h"
#include "ironcfg/bjx.h"

/* Test helper: create a valid BJX file header */
static void create_valid_header(uint8_t *buf, size_t buf_size) {
    if (buf_size < 32) return;

    /* Magic "BJX1" */
    buf[0] = 'B';
    buf[1] = 'J';
    buf[2] = 'X';
    buf[3] = '1';

    /* Flags: PBKDF2 + AES-GCM */
    buf[4] = (1U << 0) | (1U << 3);
    buf[5] = 0;  /* Reserved */

    /* Header size = 32 (little-endian) */
    buf[6] = 32;
    buf[7] = 0;

    /* Encrypted payload size = 16 (little-endian) */
    buf[8] = 16;
    buf[9] = 0;
    buf[10] = 0;
    buf[11] = 0;

    /* Plain BJV size = 16 (little-endian) */
    buf[12] = 16;
    buf[13] = 0;
    buf[14] = 0;
    buf[15] = 0;

    /* Salt offset = 32 (little-endian) */
    buf[16] = 32;
    buf[17] = 0;
    buf[18] = 0;
    buf[19] = 0;

    /* Nonce offset = 48 (little-endian) */
    buf[20] = 48;
    buf[21] = 0;
    buf[22] = 0;
    buf[23] = 0;

    /* Ciphertext offset = 60 (little-endian) */
    buf[24] = 60;
    buf[25] = 0;
    buf[26] = 0;
    buf[27] = 0;

    /* Reserved = 0 (little-endian) */
    buf[28] = 0;
    buf[29] = 0;
    buf[30] = 0;
    buf[31] = 0;
}

/* Test 1: Valid minimal BJX structure (header only, no decryption) */
static int test_bjx_valid_open(void) {
    printf("TEST: bjx_valid_open\n");

    uint8_t buf[100];
    memset(buf, 0, sizeof(buf));
    create_valid_header(buf, sizeof(buf));

    /* Add salt, nonce, ciphertext placeholder, tag */
    memset(buf + 32, 0xAA, 16);  /* Salt */
    memset(buf + 48, 0xBB, 12);  /* Nonce */
    memset(buf + 60, 0xCC, 16);  /* Ciphertext (matches payload size) */
    memset(buf + 76, 0xDD, 16);  /* Tag */

    bjx_ctx_t ctx;
    icfg_status_t status = bjx_open(buf, 92, &ctx);

    if (status != ICFG_OK) {
        printf("  FAIL: bjx_open returned %d (expected ICFG_OK)\n", status);
        return 0;
    }

    if (ctx.magic != 0x31584A42) {  /* "BJX1" in little-endian */
        printf("  FAIL: magic mismatch\n");
        return 0;
    }

    if (ctx.version != 0) {
        printf("  FAIL: version mismatch\n");
        return 0;
    }

    if (ctx.encrypted_payload_size != 16) {
        printf("  FAIL: encrypted payload size mismatch\n");
        return 0;
    }

    if (ctx.plain_bjv_size != 16) {
        printf("  FAIL: plain size mismatch\n");
        return 0;
    }

    bjx_close(&ctx);
    printf("  PASS\n");
    return 1;
}

/* Test 2: Invalid magic */
static int test_bjx_invalid_magic(void) {
    printf("TEST: bjx_invalid_magic\n");

    uint8_t buf[92];
    memset(buf, 0, sizeof(buf));
    create_valid_header(buf, sizeof(buf));

    /* Corrupt magic */
    buf[0] = 'X';

    bjx_ctx_t ctx;
    icfg_status_t status = bjx_open(buf, sizeof(buf), &ctx);

    if (status == ICFG_OK) {
        printf("  FAIL: should reject invalid magic\n");
        bjx_close(&ctx);
        return 0;
    }

    if (ctx.last_error.code != BJX_ERR_INVALID_MAGIC) {
        printf("  FAIL: wrong error code: %d\n", ctx.last_error.code);
        return 0;
    }

    bjx_close(&ctx);
    printf("  PASS\n");
    return 1;
}

/* Test 3: Invalid version */
static int test_bjx_invalid_version(void) {
    printf("TEST: bjx_invalid_version\n");

    uint8_t buf[92];
    memset(buf, 0, sizeof(buf));
    create_valid_header(buf, sizeof(buf));

    /* Set invalid version */
    buf[4] = 0xFF;

    bjx_ctx_t ctx;
    icfg_status_t status = bjx_open(buf, sizeof(buf), &ctx);

    if (status == ICFG_OK) {
        printf("  FAIL: should reject invalid version\n");
        bjx_close(&ctx);
        return 0;
    }

    if (ctx.last_error.code != BJX_ERR_UNSUPPORTED_VERSION) {
        printf("  FAIL: wrong error code: %d\n", ctx.last_error.code);
        return 0;
    }

    bjx_close(&ctx);
    printf("  PASS\n");
    return 1;
}

/* Test 4: Truncated file (too small for header) */
static int test_bjx_truncated_file(void) {
    printf("TEST: bjx_truncated_file\n");

    uint8_t buf[20];
    memset(buf, 0, sizeof(buf));

    bjx_ctx_t ctx;
    icfg_status_t status = bjx_open(buf, sizeof(buf), &ctx);

    if (status == ICFG_OK) {
        printf("  FAIL: should reject truncated file\n");
        bjx_close(&ctx);
        return 0;
    }

    if (ctx.last_error.code != BJX_ERR_TRUNCATED_FILE) {
        printf("  FAIL: wrong error code: %d\n", ctx.last_error.code);
        return 0;
    }

    bjx_close(&ctx);
    printf("  PASS\n");
    return 1;
}

/* Test 5: Invalid header size */
static int test_bjx_invalid_header_size(void) {
    printf("TEST: bjx_invalid_header_size\n");

    uint8_t buf[92];
    memset(buf, 0, sizeof(buf));
    create_valid_header(buf, sizeof(buf));

    /* Set header size to 64 instead of 32 */
    buf[6] = 64;
    buf[7] = 0;

    bjx_ctx_t ctx;
    icfg_status_t status = bjx_open(buf, sizeof(buf), &ctx);

    if (status == ICFG_OK) {
        printf("  FAIL: should reject invalid header size\n");
        bjx_close(&ctx);
        return 0;
    }

    if (ctx.last_error.code != BJX_ERR_INVALID_HEADER_SIZE) {
        printf("  FAIL: wrong error code: %d\n", ctx.last_error.code);
        return 0;
    }

    bjx_close(&ctx);
    printf("  PASS\n");
    return 1;
}

/* Test 6: Offset out of bounds */
static int test_bjx_offset_out_of_bounds(void) {
    printf("TEST: bjx_offset_out_of_bounds\n");

    uint8_t buf[92];
    memset(buf, 0, sizeof(buf));
    create_valid_header(buf, sizeof(buf));

    /* Set salt offset to wrong value */
    buf[16] = 64;
    buf[17] = 0;
    buf[18] = 0;
    buf[19] = 0;

    bjx_ctx_t ctx;
    icfg_status_t status = bjx_open(buf, sizeof(buf), &ctx);

    if (status == ICFG_OK) {
        printf("  FAIL: should reject invalid offset\n");
        bjx_close(&ctx);
        return 0;
    }

    if (ctx.last_error.code != BJX_ERR_OFFSET_OUT_OF_BOUNDS) {
        printf("  FAIL: wrong error code: %d\n", ctx.last_error.code);
        return 0;
    }

    bjx_close(&ctx);
    printf("  PASS\n");
    return 1;
}

/* Test 7: Reserved field non-zero */
static int test_bjx_reserved_non_zero(void) {
    printf("TEST: bjx_reserved_non_zero\n");

    uint8_t buf[92];
    memset(buf, 0, sizeof(buf));
    create_valid_header(buf, sizeof(buf));

    /* Set reserved field to non-zero */
    buf[28] = 0x01;

    bjx_ctx_t ctx;
    icfg_status_t status = bjx_open(buf, sizeof(buf), &ctx);

    if (status == ICFG_OK) {
        printf("  FAIL: should reject non-zero reserved field\n");
        bjx_close(&ctx);
        return 0;
    }

    if (ctx.last_error.code != BJX_ERR_RESERVED_NON_ZERO) {
        printf("  FAIL: wrong error code: %d\n", ctx.last_error.code);
        return 0;
    }

    bjx_close(&ctx);
    printf("  PASS\n");
    return 1;
}

/* Test 8: Null argument handling */
static int test_bjx_null_argument(void) {
    printf("TEST: bjx_null_argument\n");

    uint8_t buf[92];
    bjx_ctx_t ctx;

    icfg_status_t status = bjx_open(NULL, sizeof(buf), &ctx);
    if (status == ICFG_OK) {
        printf("  FAIL: should reject NULL data\n");
        return 0;
    }

    status = bjx_open(buf, sizeof(buf), NULL);
    if (status == ICFG_OK) {
        printf("  FAIL: should reject NULL context\n");
        return 0;
    }

    printf("  PASS\n");
    return 1;
}

int main(int argc, char *argv[]) {
    int passed = 0;
    int total = 0;

    printf("========================================\n");
    printf("BJX Basic Tests\n");
    printf("========================================\n\n");

    total++;
    if (test_bjx_valid_open()) passed++;

    total++;
    if (test_bjx_invalid_magic()) passed++;

    total++;
    if (test_bjx_invalid_version()) passed++;

    total++;
    if (test_bjx_truncated_file()) passed++;

    total++;
    if (test_bjx_invalid_header_size()) passed++;

    total++;
    if (test_bjx_offset_out_of_bounds()) passed++;

    total++;
    if (test_bjx_reserved_non_zero()) passed++;

    total++;
    if (test_bjx_null_argument()) passed++;

    printf("\n========================================\n");
    printf("Result: %d/%d tests passed\n", passed, total);
    printf("========================================\n");

    return (passed == total) ? 0 : 1;
}
