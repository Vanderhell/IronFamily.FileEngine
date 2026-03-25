/*
 * IUPD Vector Verification Test Harness
 *
 * Tests the IUPD v2 strict verifier against golden test vectors
 * from artifacts/vectors/v1/vectors.json
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "ironfamily/iupd_reader.h"
#include "ironfamily/iupd_errors.h"
#include "file_reader.h"

/* File reader implementation for tests */
static iron_error_t file_read_impl(void* ctx, uint64_t off, uint8_t* dst, uint32_t len) {
    file_reader_ctx_t* fr = (file_reader_ctx_t*)ctx;

    if (fseek(fr->fp, (long)off, SEEK_SET) != 0) {
        return IRON_E_IO;
    }

    size_t read = fread(dst, 1, len, fr->fp);
    if (read != len) {
        return IRON_E_IO;
    }

    return IRON_OK;
}

/* Parse hex string to bytes (simple hex decoder)
 * Input: null-terminated hex string like "abcd..."
 * Output: 32 bytes
 * Returns: 1 on success, 0 on failure
 */
static int hex_to_bytes(const char* hex_str, uint8_t* out, size_t out_len) {
    if (out_len != 32) return 0;
    if (strlen(hex_str) != 64) return 0;

    for (size_t i = 0; i < 32; i++) {
        char byte_str[3] = {hex_str[2*i], hex_str[2*i+1], '\0'};
        unsigned int byte_val;
        if (sscanf(byte_str, "%2x", &byte_val) != 1) {
            return 0;
        }
        out[i] = (uint8_t)byte_val;
    }
    return 1;
}

/* Load public key from file */
static int load_public_key_from_file(const char* path, uint8_t pubkey[32]) {
    FILE* fp = fopen(path, "rb");
    if (!fp) return 0;

    char hex_buffer[70];
    if (fgets(hex_buffer, sizeof(hex_buffer), fp) == NULL) {
        fclose(fp);
        return 0;
    }
    fclose(fp);

    /* Trim trailing whitespace/newline */
    size_t len = strlen(hex_buffer);
    while (len > 0 && (hex_buffer[len-1] == '\n' || hex_buffer[len-1] == '\r' || hex_buffer[len-1] == ' ')) {
        hex_buffer[len-1] = '\0';
        len--;
    }

    return hex_to_bytes(hex_buffer, pubkey, 32);
}

typedef struct {
    const char* name;
    const char* path;
    iron_error_t expected_error;
    const char* description;
} test_case_t;

static const test_case_t TEST_CASES[] = {
    {
        "secure_ok_01",
        "artifacts/vectors/v1/iupd/v2/secure_ok_01.iupd",
        IRON_OK,
        "Valid IUPD v2 (SECURE profile, valid signature, valid UpdateSequence=1)"
    },
    {
        "secure_bad_sig_01",
        "artifacts/vectors/v1/iupd/v2/secure_bad_sig_01.iupd",
        IRON_E_SIG_INVALID,
        "Corrupted Ed25519 signature (1 byte flip)"
    },
    {
        "secure_bad_seq_01",
        "artifacts/vectors/v1/iupd/v2/secure_bad_seq_01.iupd",
        IRON_E_SEQ_INVALID,
        "Corrupted UpdateSequence trailer"
    },
    {
        "secure_dos_manifest_01",
        "artifacts/vectors/v1/iupd/v2/secure_dos_manifest_01.iupd",
        IRON_E_DOS_LIMIT,
        "DoS vector: manifest size declared > 100MB"
    },
    {
        "secure_dos_chunks_01",
        "artifacts/vectors/v1/iupd/v2/secure_dos_chunks_01.iupd",
        IRON_E_FORMAT,
        "DoS vector: chunk count exceeds limit, rejected as FORMAT (manifest_offset > file_size)"
    },
    {
        "secure_dos_chunk_size_01",
        "artifacts/vectors/v1/iupd/v2/secure_dos_chunk_size_01.iupd",
        IRON_E_DOS_LIMIT,
        "DoS vector: chunk size declared > 1GB"
    },
};

static const int TEST_CASE_COUNT = sizeof(TEST_CASES) / sizeof(TEST_CASES[0]);

static int load_bench_pubkey(uint8_t pubkey[32]) {
    static const char* candidates[] = {
        "artifacts/vectors/v1/iupd/v2/test_pubkey_hex.txt",
        "native/tests/test_pubkey_hex.txt"
    };

    for (size_t i = 0; i < sizeof(candidates) / sizeof(candidates[0]); i++) {
        if (load_public_key_from_file(candidates[i], pubkey)) {
            return 1;
        }
    }

    return 0;
}

int main(void) {
    int passed = 0, failed = 0;
    uint8_t bench_pubkey[32];

    printf("========================================\n");
    printf("IUPD v2 Vector Verification Test Suite\n");
    printf("========================================\n\n");

    /* Load public key from file */
    if (!load_bench_pubkey(bench_pubkey)) {
        printf("ERROR: Cannot load public key from test_pubkey_hex.txt\n");
        return 1;
    }

    for (int i = 0; i < TEST_CASE_COUNT; i++) {
        const test_case_t* tc = &TEST_CASES[i];

        printf("[%d/%d] Testing %s\n", i + 1, TEST_CASE_COUNT, tc->name);
        printf("      Path: %s\n", tc->path);
        printf("      Expected: %s\n", iron_error_str(tc->expected_error));

        /* Get file size */
        FILE* fp = fopen(tc->path, "rb");
        if (!fp) {
            printf("      ❌ FAIL: Cannot open file\n\n");
            failed++;
            continue;
        }

        fseek(fp, 0, SEEK_END);
        uint64_t file_size = ftell(fp);
        fseek(fp, 0, SEEK_SET);

        if (file_size == 0) {
            printf("      ❌ FAIL: File is empty\n\n");
            fclose(fp);
            failed++;
            continue;
        }

        /* Set up reader */
        file_reader_ctx_t reader_ctx;
        iron_reader_t reader;

        reader_ctx.fp = fp;
        reader.ctx = &reader_ctx;
        reader.read = file_read_impl;

        /* Run verification */
        uint64_t update_sequence = 0;
        iron_error_t result = iron_iupd_verify_strict(
            &reader,
            file_size,
            bench_pubkey,
            1,  /* expected_min_update_sequence */
            &update_sequence
        );

        fclose(fp);

        printf("      Got: %s\n", iron_error_str(result));

        if (result == tc->expected_error) {
            printf("      ✅ PASS\n\n");
            passed++;
        } else {
            printf("      ❌ FAIL: Expected %s but got %s\n\n",
                   iron_error_str(tc->expected_error), iron_error_str(result));
            failed++;
        }
    }

    printf("========================================\n");
    printf("Results: %d passed, %d failed out of %d\n",
           passed, failed, TEST_CASE_COUNT);
    printf("========================================\n");

    return (failed == 0) ? 0 : 1;
}
