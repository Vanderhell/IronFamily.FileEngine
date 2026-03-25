/*
 * INCREMENTAL Metadata Parsing Test Suite
 *
 * Tests the IUPD INCREMENTAL metadata trailer parsing and validation.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "ironfamily/iupd_incremental_metadata.h"
#include "ironfamily/crc32.h"

/* Helper: Build little-endian u32 */
static void write_u32_le(uint8_t* dst, uint32_t val) {
    dst[0] = (uint8_t)(val & 0xFF);
    dst[1] = (uint8_t)((val >> 8) & 0xFF);
    dst[2] = (uint8_t)((val >> 16) & 0xFF);
    dst[3] = (uint8_t)((val >> 24) & 0xFF);
}

/* Helper: Build test metadata trailer (with valid CRC32) */
static uint8_t* build_test_trailer(
    uint8_t algorithm_id,
    const uint8_t* base_hash, uint32_t base_hash_len,
    const uint8_t* target_hash, uint32_t target_hash_len,
    uint32_t* out_trailer_len
) {
    /* Calculate total length: 8(magic) + 4(len) + 1(ver) + 1(alg) +
       1(base_len) + base_hash + 1(target_len) + target_hash + 4(crc32) */
    uint32_t trailer_len = 8 + 4 + 1 + 1 + 1 + base_hash_len + 1 + target_hash_len + 4;
    uint8_t* trailer = (uint8_t*)malloc(trailer_len);
    if (!trailer) return NULL;

    memset(trailer, 0, trailer_len);

    /* Magic */
    memcpy(trailer, IUPD_INC_MAGIC_STR, 8);

    /* Length */
    write_u32_le(&trailer[8], trailer_len);

    /* Version */
    trailer[12] = IUPD_INC_VERSION;

    /* Algorithm ID */
    trailer[13] = algorithm_id;

    /* Base hash length and data */
    trailer[14] = (uint8_t)base_hash_len;
    if (base_hash_len > 0) {
        memcpy(&trailer[15], base_hash, base_hash_len);
    }

    /* Target hash length and data */
    uint32_t target_offset = 15 + base_hash_len;
    trailer[target_offset] = (uint8_t)target_hash_len;
    if (target_hash_len > 0) {
        memcpy(&trailer[target_offset + 1], target_hash, target_hash_len);
    }

    /* CRC32 (over everything except CRC32 field) */
    uint32_t crc32_offset = target_offset + 1 + target_hash_len;
    uint32_t crc32_val = iron_crc32(trailer, crc32_offset);
    write_u32_le(&trailer[crc32_offset], crc32_val);

    *out_trailer_len = trailer_len;
    return trailer;
}

typedef struct {
    const char* name;
    int should_succeed;
    const char* description;
} test_case_t;

int main(void) {
    int passed = 0, failed = 0;

    printf("========================================\n");
    printf("INCREMENTAL Metadata Parsing Test Suite\n");
    printf("========================================\n\n");

    /* Test 1: Valid metadata with DELTA_V1 and base hash only */
    {
        printf("[1/10] Testing valid DELTA_V1 metadata (base hash only)\n");

        uint8_t base_hash[32];
        memset(base_hash, 0xAA, 32);

        uint32_t trailer_len;
        uint8_t* trailer = build_test_trailer(IUPD_ALGORITHM_DELTA_V1, base_hash, 32, NULL, 0, &trailer_len);

        if (trailer) {
            iupd_incremental_metadata_t metadata;
            if (iupd_incremental_metadata_parse(trailer, trailer_len, &metadata)) {
                if (metadata.algorithm_id == IUPD_ALGORITHM_DELTA_V1 &&
                    metadata.base_hash_len == 32 &&
                    metadata.target_hash_len == 0 &&
                    memcmp(metadata.base_hash, base_hash, 32) == 0) {
                    printf("      ✅ PASS\n\n");
                    passed++;
                } else {
                    printf("      ❌ FAIL: Metadata fields incorrect\n\n");
                    failed++;
                }
            } else {
                printf("      ❌ FAIL: Parsing failed\n\n");
                failed++;
            }
            free(trailer);
        } else {
            printf("      ❌ FAIL: Cannot allocate trailer\n\n");
            failed++;
        }
    }

    /* Test 2: Valid metadata with IRONDEL2 and both hashes */
    {
        printf("[2/10] Testing valid IRONDEL2 metadata (both hashes)\n");

        uint8_t base_hash[32];
        uint8_t target_hash[32];
        memset(base_hash, 0xBB, 32);
        memset(target_hash, 0xCC, 32);

        uint32_t trailer_len;
        uint8_t* trailer = build_test_trailer(IUPD_ALGORITHM_IRONDEL2, base_hash, 32, target_hash, 32, &trailer_len);

        if (trailer) {
            iupd_incremental_metadata_t metadata;
            if (iupd_incremental_metadata_parse(trailer, trailer_len, &metadata)) {
                if (metadata.algorithm_id == IUPD_ALGORITHM_IRONDEL2 &&
                    metadata.base_hash_len == 32 &&
                    metadata.target_hash_len == 32 &&
                    memcmp(metadata.base_hash, base_hash, 32) == 0 &&
                    memcmp(metadata.target_hash, target_hash, 32) == 0) {
                    printf("      ✅ PASS\n\n");
                    passed++;
                } else {
                    printf("      ❌ FAIL: Metadata fields incorrect\n\n");
                    failed++;
                }
            } else {
                printf("      ❌ FAIL: Parsing failed\n\n");
                failed++;
            }
            free(trailer);
        } else {
            printf("      ❌ FAIL: Cannot allocate trailer\n\n");
            failed++;
        }
    }

    /* Test 3: Invalid trailer (bad magic) */
    {
        printf("[3/10] Testing invalid magic\n");

        uint8_t bad_trailer[32];
        memset(bad_trailer, 0, 32);
        memcpy(bad_trailer, "BADMAGIC", 8);

        iupd_incremental_metadata_t metadata;
        if (!iupd_incremental_metadata_parse(bad_trailer, 32, &metadata)) {
            printf("      ✅ PASS (correctly rejected)\n\n");
            passed++;
        } else {
            printf("      ❌ FAIL: Should have rejected bad magic\n\n");
            failed++;
        }
    }

    /* Test 4: Invalid trailer (bad version) */
    {
        printf("[4/10] Testing invalid version\n");

        uint8_t base_hash[32];
        memset(base_hash, 0xDD, 32);

        uint32_t trailer_len;
        uint8_t* trailer = build_test_trailer(IUPD_ALGORITHM_DELTA_V1, base_hash, 32, NULL, 0, &trailer_len);

        if (trailer) {
            /* Corrupt version byte */
            trailer[12] = 99;
            /* Recompute CRC32 */
            uint32_t crc32_offset = 15 + 32 + 1;
            uint32_t crc32_val = iron_crc32(trailer, crc32_offset);
            write_u32_le(&trailer[crc32_offset], crc32_val);

            iupd_incremental_metadata_t metadata;
            if (!iupd_incremental_metadata_parse(trailer, trailer_len, &metadata)) {
                printf("      ✅ PASS (correctly rejected)\n\n");
                passed++;
            } else {
                printf("      ❌ FAIL: Should have rejected bad version\n\n");
                failed++;
            }
            free(trailer);
        }
    }

    /* Test 5: Invalid trailer (bad CRC32) */
    {
        printf("[5/10] Testing invalid CRC32\n");

        uint8_t base_hash[32];
        memset(base_hash, 0xEE, 32);

        uint32_t trailer_len;
        uint8_t* trailer = build_test_trailer(IUPD_ALGORITHM_DELTA_V1, base_hash, 32, NULL, 0, &trailer_len);

        if (trailer) {
            /* Corrupt CRC32 */
            uint32_t crc32_offset = 15 + 32 + 1;
            write_u32_le(&trailer[crc32_offset], 0xDEADBEEF);

            iupd_incremental_metadata_t metadata;
            if (!iupd_incremental_metadata_parse(trailer, trailer_len, &metadata)) {
                printf("      ✅ PASS (correctly rejected)\n\n");
                passed++;
            } else {
                printf("      ❌ FAIL: Should have rejected bad CRC32\n\n");
                failed++;
            }
            free(trailer);
        }
    }

    /* Test 6: Invalid trailer (unknown algorithm) */
    {
        printf("[6/10] Testing unknown algorithm\n");

        uint8_t base_hash[32];
        memset(base_hash, 0xFF, 32);

        uint32_t trailer_len;
        uint8_t* trailer = build_test_trailer(0x99, base_hash, 32, NULL, 0, &trailer_len);

        if (trailer) {
            iupd_incremental_metadata_t metadata;
            if (!iupd_incremental_metadata_parse(trailer, trailer_len, &metadata)) {
                printf("      ✅ PASS (correctly rejected)\n\n");
                passed++;
            } else {
                printf("      ❌ FAIL: Should have rejected unknown algorithm\n\n");
                failed++;
            }
            free(trailer);
        }
    }

    /* Test 7: Too-short trailer */
    {
        printf("[7/10] Testing truncated trailer\n");

        uint8_t short_trailer[10];
        memcpy(short_trailer, IUPD_INC_MAGIC_STR, 8);

        iupd_incremental_metadata_t metadata;
        if (!iupd_incremental_metadata_parse(short_trailer, sizeof(short_trailer), &metadata)) {
            printf("      ✅ PASS (correctly rejected)\n\n");
            passed++;
        } else {
            printf("      ❌ FAIL: Should have rejected short trailer\n\n");
            failed++;
        }
    }

    /* Test 8: Metadata location search (iupd_incremental_metadata_find) */
    {
        printf("[8/10] Testing metadata location search\n");

        uint8_t base_hash[32];
        memset(base_hash, 0x12, 32);

        uint32_t trailer_len;
        uint8_t* trailer = build_test_trailer(IUPD_ALGORITHM_DELTA_V1, base_hash, 32, NULL, 0, &trailer_len);

        if (trailer) {
            /* Create a larger buffer with trailer at end */
            uint32_t total_len = 100 + trailer_len;
            uint8_t* pkg_data = (uint8_t*)malloc(total_len);
            if (pkg_data) {
                memset(pkg_data, 0x55, total_len);
                memcpy(&pkg_data[100], trailer, trailer_len);

                int64_t offset = iupd_incremental_metadata_find(pkg_data, total_len);
                if (offset == 100) {
                    printf("      ✅ PASS (found at offset %lld)\n\n", (long long)offset);
                    passed++;
                } else {
                    printf("      ❌ FAIL: Expected offset 100, got %lld\n\n", (long long)offset);
                    failed++;
                }
                free(pkg_data);
            }
            free(trailer);
        }
    }

    /* Test 9: iupd_algorithm_is_known validation */
    {
        printf("[9/10] Testing algorithm ID validation\n");

        if (iupd_algorithm_is_known(IUPD_ALGORITHM_DELTA_V1) &&
            iupd_algorithm_is_known(IUPD_ALGORITHM_IRONDEL2) &&
            !iupd_algorithm_is_known(0x00) &&
            !iupd_algorithm_is_known(0x99)) {
            printf("      ✅ PASS\n\n");
            passed++;
        } else {
            printf("      ❌ FAIL: Algorithm validation incorrect\n\n");
            failed++;
        }
    }

    /* Test 10: Length field validation */
    {
        printf("[10/10] Testing length field validation\n");

        uint8_t base_hash[32];
        memset(base_hash, 0x34, 32);

        uint32_t trailer_len;
        uint8_t* trailer = build_test_trailer(IUPD_ALGORITHM_DELTA_V1, base_hash, 32, NULL, 0, &trailer_len);

        if (trailer) {
            /* Corrupt length field */
            write_u32_le(&trailer[8], trailer_len + 1);

            iupd_incremental_metadata_t metadata;
            if (!iupd_incremental_metadata_parse(trailer, trailer_len, &metadata)) {
                printf("      ✅ PASS (correctly rejected)\n\n");
                passed++;
            } else {
                printf("      ❌ FAIL: Should have rejected bad length\n\n");
                failed++;
            }
            free(trailer);
        }
    }

    printf("========================================\n");
    printf("Results: %d passed, %d failed out of 10\n",
           passed, failed);
    printf("========================================\n");

    return (failed == 0) ? 0 : 1;
}
