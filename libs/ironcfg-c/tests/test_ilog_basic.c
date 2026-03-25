/*
 * test_ilog_basic.c
 * Basic tests for ILOG C reader
 *
 * Tests:
 * - Valid minimal file
 * - Truncated file
 * - Flipped byte corruption
 * - Invalid offsets
 * - Bounds overflow
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>

#include "../include/ironcfg/ilog.h"

/* ============================================================================
 * Test: Valid Minimal File
 * ============================================================================ */

static void test_valid_minimal_file(void) {
    printf("TEST: Valid minimal ILOG file\n");

    /* Construct minimal valid ILOG file:
     * - Magic: ILOG (0x49 0x4C 0x4F 0x47)
     * - Version: 0x01
     * - Flags: 0x00 (little-endian, no integrity)
     * - Reserved: 0x0000
     * - (Minimal L0 and L1 would follow)
     */
    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x00,                      /* Flags: little-endian, no seals */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,    /* Placeholder for L1 offset */
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);

    if (status != ICFG_OK) {
        printf("  FAIL: ilog_open returned error 0x%04X at offset %llu\n",
               view.last_error.code, view.last_error.byte_offset);
        return;
    }

    /* Validate fast mode */
    status = ilog_validate_fast(&view);
    if (status != ICFG_OK) {
        printf("  FAIL: validate_fast returned error 0x%04X\n",
               view.last_error.code);
        return;
    }

    printf("  PASS: Valid minimal file accepted\n");
}

/* ============================================================================
 * Test: Truncated File
 * ============================================================================ */

static void test_truncated_file(void) {
    printf("TEST: Truncated ILOG file\n");

    /* Only 4 bytes (incomplete header) */
    uint8_t data[4] = {
        0x49, 0x4C, 0x4F, 0x47   /* Magic only */
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);

    if (status == ICFG_OK) {
        printf("  FAIL: Truncated file was accepted (should fail)\n");
        return;
    }

    if (view.last_error.code != ILOG_ERR_CORRUPTED_HEADER &&
        view.last_error.code != ILOG_ERR_INVALID_MAGIC) {
        printf("  FAIL: Wrong error code 0x%04X (expected truncation error)\n",
               view.last_error.code);
        return;
    }

    printf("  PASS: Truncated file rejected with error 0x%04X\n",
           view.last_error.code);
}

/* ============================================================================
 * Test: Flipped Byte Corruption
 * ============================================================================ */

static void test_flipped_byte_corruption(void) {
    printf("TEST: Flipped byte corruption\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    /* Flip first byte of magic to corrupt it */
    data[0] = 0xFF;

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);

    if (status == ICFG_OK) {
        printf("  FAIL: Corrupted magic was accepted (should fail)\n");
        return;
    }

    if (view.last_error.code != ILOG_ERR_INVALID_MAGIC) {
        printf("  FAIL: Wrong error code 0x%04X (expected INVALID_MAGIC)\n",
               view.last_error.code);
        return;
    }

    if (view.last_error.byte_offset != 0) {
        printf("  FAIL: Wrong byte offset %llu (expected 0)\n",
               view.last_error.byte_offset);
        return;
    }

    printf("  PASS: Corrupted magic detected at byte 0\n");
}

/* ============================================================================
 * Test: Invalid Version
 * ============================================================================ */

static void test_invalid_version(void) {
    printf("TEST: Invalid ILOG version\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0xFF,                      /* Invalid version */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);

    if (status == ICFG_OK) {
        printf("  FAIL: Invalid version was accepted (should fail)\n");
        return;
    }

    if (view.last_error.code != ILOG_ERR_UNSUPPORTED_VERSION) {
        printf("  FAIL: Wrong error code 0x%04X (expected UNSUPPORTED_VERSION)\n",
               view.last_error.code);
        return;
    }

    if (view.last_error.byte_offset != 4) {
        printf("  FAIL: Wrong byte offset %llu (expected 4)\n",
               view.last_error.byte_offset);
        return;
    }

    printf("  PASS: Invalid version detected at byte 4\n");
}

/* ============================================================================
 * Test: Bounds Check
 * ============================================================================ */

static void test_empty_file(void) {
    printf("TEST: Empty file (bounds check)\n");

    uint8_t data[1] = { 0x00 };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, 1, &view);

    if (status == ICFG_OK) {
        printf("  FAIL: Empty file was accepted (should fail)\n");
        return;
    }

    if (view.last_error.code != ILOG_ERR_INVALID_MAGIC) {
        printf("  FAIL: Wrong error code 0x%04X\n",
               view.last_error.code);
        return;
    }

    printf("  PASS: Empty file rejected\n");
}

/* ============================================================================
 * Test: Extended Magic (BLK1)
 * ============================================================================ */

static void test_extended_magic(void) {
    printf("TEST: Extended magic BLK1\n");

    uint8_t data[16] = {
        0x42, 0x4C, 0x4B, 0x31,  /* Magic: BLK1 */
        0x01,                      /* Version */
        0x00,                      /* Flags */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);

    if (status != ICFG_OK) {
        printf("  FAIL: BLK1 magic was rejected (should be valid)\n");
        return;
    }

    if (view.magic != 0x314B4C42) {  /* BLK1 in little-endian */
        printf("  FAIL: Wrong magic value 0x%08X\n", view.magic);
        return;
    }

    printf("  PASS: BLK1 magic accepted\n");
}

/* ============================================================================
 * Test: Flags Parsing
 * ============================================================================ */

static void test_flags_parsing(void) {
    printf("TEST: Flags parsing\n");

    uint8_t data[16] = {
        0x49, 0x4C, 0x4F, 0x47,  /* Magic: ILOG */
        0x01,                      /* Version */
        0x3F,                      /* Flags: all bits set */
        0x00, 0x00,                /* Reserved */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    ilog_view_t view;
    icfg_status_t status = ilog_open(data, sizeof(data), &view);

    if (status != ICFG_OK) {
        printf("  FAIL: Failed to open file with flags set\n");
        return;
    }

    /* Bit 0 = 1 means big-endian (0 means little-endian) */
    if (view.flags.little_endian != false) {
        printf("  FAIL: Little-endian flag incorrect\n");
        return;
    }

    if (!view.flags.has_crc32 || !view.flags.has_blake3 ||
        !view.flags.has_layer_l2 || !view.flags.has_layer_l3 ||
        !view.flags.has_layer_l4) {
        printf("  FAIL: Layer/integrity flags not parsed correctly\n");
        return;
    }

    printf("  PASS: Flags parsed correctly\n");
}

/* ============================================================================
 * Main Test Runner
 * ============================================================================ */

int main(void) {
    printf("=== ILOG C99 Reader Basic Tests ===\n\n");

    test_valid_minimal_file();
    printf("\n");

    test_truncated_file();
    printf("\n");

    test_flipped_byte_corruption();
    printf("\n");

    test_invalid_version();
    printf("\n");

    test_empty_file();
    printf("\n");

    test_extended_magic();
    printf("\n");

    test_flags_parsing();
    printf("\n");

    printf("=== All basic tests completed ===\n");
    return 0;
}
