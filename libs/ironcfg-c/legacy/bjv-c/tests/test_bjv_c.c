#include "../include/bjv.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int total_tests = 0;
int passed_tests = 0;

void assert_eq_int(int actual, int expected, const char* test_name) {
    total_tests++;
    if (actual == expected) {
        printf("✓ %s\n", test_name);
        passed_tests++;
    } else {
        printf("✗ %s (expected %d, got %d)\n", test_name, expected, actual);
    }
}

void assert_true(bool cond, const char* test_name) {
    total_tests++;
    if (cond) {
        printf("✓ %s\n", test_name);
        passed_tests++;
    } else {
        printf("✗ %s\n", test_name);
    }
}

/* Test 1: Minimal valid BJV2 file with simple NULL value */
void test_bjv_open_minimal() {
    /* Header: BJV2, flags=0x01 (little-endian only), no CRC, no VSP */
    /* Total file size: 33 bytes (32 header + 1 root NULL) */
    uint8_t data[33] = {
        /* Header (32 bytes) */
        'B', 'J', 'V', '2',           /* Magic */
        0x01,                          /* Flags: 0x01 = little-endian */
        0x00,                          /* Reserved */
        0x20, 0x00,                    /* Header size: 32 */
        0x21, 0x00, 0x00, 0x00,       /* Total file size: 33 */
        0x20, 0x00, 0x00, 0x00,       /* Dict offset: 32 */
        0x00, 0x00, 0x00, 0x00,       /* VSP offset: 0 (not present) */
        0x20, 0x00, 0x00, 0x00,       /* Root offset: 32 */
        0x00, 0x00, 0x00, 0x00,       /* CRC offset: 0 */
        0x00, 0x00, 0x00, 0x00,       /* Reserved */

        /* Dictionary (1 byte): count=0 (empty dict) */
        0x00,

        /* Root value: NULL */
        0x00
    };

    bjv_doc_t doc;
    bjv_err_t err = bjv_open(data, sizeof(data), &doc);

    assert_eq_int((int)err, BJV_OK, "BJV open minimal file");
    assert_eq_int((int)doc.size, 33, "Document size");
    assert_eq_int((int)doc.is_bjv4, 0, "Is BJV2 (not BJV4)");
    assert_eq_int((int)doc.has_crc, 0, "No CRC");
    assert_eq_int((int)doc.has_vsp, 0, "No VSP");

    bjv_val_t root = bjv_root(&doc);
    assert_eq_int((int)bjv_type(root), BJV_NULL, "Root is NULL");
    assert_true(bjv_get_null(root), "Get NULL value");
}

/* Test 2: Bad magic */
void test_bjv_bad_magic() {
    uint8_t data[33] = {
        'B', 'A', 'D', 'X',           /* Bad magic */
        0x01, 0x00, 0x20, 0x00,
        0x21, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,

        0x00, 0x00
    };

    bjv_doc_t doc;
    bjv_err_t err = bjv_open(data, sizeof(data), &doc);
    assert_eq_int((int)err, BJV_ERR_FORMAT, "Reject bad magic");
}

/* Test 3: Missing little-endian flag */
void test_bjv_no_le_flag() {
    uint8_t data[33] = {
        'B', 'J', 'V', '2',
        0x00,  /* Flags: no LE flag (bit 0 = 0) */
        0x00, 0x20, 0x00,
        0x21, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,

        0x00, 0x00
    };

    bjv_doc_t doc;
    bjv_err_t err = bjv_open(data, sizeof(data), &doc);
    assert_eq_int((int)err, BJV_ERR_FORMAT, "Reject no LE flag");
}

/* Test 4: Reserved field must be 0 */
void test_bjv_reserved_nonzero() {
    uint8_t data[33] = {
        'B', 'J', 'V', '2',
        0x01,
        0x01,  /* Reserved: should be 0 */
        0x20, 0x00,
        0x21, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,

        0x00, 0x00
    };

    bjv_doc_t doc;
    bjv_err_t err = bjv_open(data, sizeof(data), &doc);
    assert_eq_int((int)err, BJV_ERR_CANONICAL, "Reject non-zero reserved");
}

/* Test 5: File size mismatch */
void test_bjv_size_mismatch() {
    uint8_t data[33] = {
        'B', 'J', 'V', '2',
        0x01, 0x00, 0x20, 0x00,
        0x22, 0x00, 0x00, 0x00,  /* Total size: 34 (but file is 33) */
        0x20, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,

        0x00, 0x00
    };

    bjv_doc_t doc;
    bjv_err_t err = bjv_open(data, sizeof(data), &doc);
    assert_eq_int((int)err, BJV_ERR_FORMAT, "Reject size mismatch");
}

/* Test 6: Simple integer values */
void test_bjv_integers() {
    /* Create a BJV file with U64 value 42 */
    uint8_t data[42] = {
        'B', 'J', 'V', '2',
        0x01, 0x00, 0x20, 0x00,
        0x2A, 0x00, 0x00, 0x00,  /* Size: 42 */
        0x20, 0x00, 0x00, 0x00,  /* Dict: 32 */
        0x00, 0x00, 0x00, 0x00,
        0x21, 0x00, 0x00, 0x00,  /* Root: 33 */
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,

        /* Dict: count=0 */
        0x00,

        /* U64: 42 */
        0x11,  /* Type: U64 */
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00  /* LE: 42 */
    };

    bjv_doc_t doc;
    bjv_err_t err = bjv_open(data, sizeof(data), &doc);
    assert_eq_int((int)err, BJV_OK, "Open U64 file");

    bjv_val_t root = bjv_root(&doc);
    assert_eq_int((int)bjv_type(root), BJV_U64, "Root is U64");

    uint64_t val = 0;
    bool ok = bjv_get_u64(root, &val);
    assert_true(ok, "Get U64 succeeds");
    assert_eq_int((int)(val & 0xFF), 42, "U64 value is 42");
}

int main() {
    printf("BJV C Library Tests\n");
    printf("===================\n\n");

    test_bjv_open_minimal();
    test_bjv_bad_magic();
    test_bjv_no_le_flag();
    test_bjv_reserved_nonzero();
    test_bjv_size_mismatch();
    test_bjv_integers();

    printf("\n===================\n");
    printf("Results: %d/%d passed\n", passed_tests, total_tests);

    return (passed_tests == total_tests) ? 0 : 1;
}
