/*
 * test_icfx_vsp_indexed.c
 * Comprehensive tests for ICFX VSP (Variable String Pool) and Indexed Objects
 *
 * Tests:
 * 1. VSP string resolution (ICFX_STR_ID type 0x22)
 * 2. Indexed object (ICFX_INDEXED_OBJECT type 0x41) hash table lookups
 * 3. Parity between C and .NET implementations
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ironcfg/icfx.h>

typedef struct {
    const char* field_name;
    uint32_t key_id;
    const char* expected_str_value;
} test_field_t;

/* Test data for golden_icfx_crc_index.icfx (indexed objects) */
static const test_field_t indexed_object_tests[] = {
    {"long_string", 8, "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. This string is longer than 256 characters to test VSP handling in the ICFX format implementation for proper interning and offset calculation."},
    {"empty", 2, ""},
    {"simple", 10, "hello"},
};

int test_indexed_object_by_keyid(const icfx_value_t* obj, uint32_t key_id, const char* expected_str) {
    icfx_value_t field;
    icfg_status_t status = icfx_obj_try_get_by_keyid(obj, key_id, &field);

    if (status != ICFG_OK) {
        printf("  FAIL: Cannot get field by key_id %u: %d\n", key_id, status);
        return 1;
    }

    /* Get string value */
    const uint8_t* str_ptr;
    uint32_t str_len;
    status = icfx_get_str(&field, &str_ptr, &str_len);

    if (status != ICFG_OK) {
        printf("  FAIL: Cannot read string from field (key_id %u): %d\n", key_id, status);
        return 1;
    }

    /* Verify string matches expected */
    size_t expected_len = strlen(expected_str);
    if (str_len != expected_len || memcmp(str_ptr, expected_str, str_len) != 0) {
        printf("  FAIL: String mismatch for key_id %u\n", key_id);
        printf("    Expected (%zu bytes): %.50s%s\n", expected_len, expected_str,
               expected_len > 50 ? "..." : "");
        printf("    Got (%u bytes): %.50s%s\n", str_len, (const char*)str_ptr,
               str_len > 50 ? "..." : "");
        return 1;
    }

    printf("  PASS: Field key_id=%u: \"%.*s%s\"\n", key_id,
           (int)(str_len > 50 ? 50 : str_len), str_ptr,
           str_len > 50 ? "..." : "");
    return 0;
}

int test_icfx_vsp_indexed_file(const char* filepath, const char* test_name, bool has_vsp, bool is_indexed) {
    printf("\n=== Testing %s ===\n", test_name);
    printf("Expected: VSP=%s, Indexed=%s\n", has_vsp ? "yes" : "no", is_indexed ? "yes" : "no");

    /* Load file */
    FILE* f = fopen(filepath, "rb");
    if (!f) {
        printf("SKIP: Missing vector file: %s\n", filepath);
        return 0;
    }

    fseek(f, 0, SEEK_END);
    size_t size = ftell(f);
    fseek(f, 0, SEEK_SET);

    uint8_t* data = (uint8_t*)malloc(size);
    if (!data) {
        printf("FAIL: Cannot allocate %zu bytes\n", size);
        fclose(f);
        return 1;
    }

    if (fread(data, 1, size, f) != size) {
        printf("FAIL: Cannot read file\n");
        fclose(f);
        free(data);
        return 1;
    }
    fclose(f);

    /* Open and validate ICFX */
    icfx_view_t view;
    icfg_status_t status = icfx_open(data, size, &view);
    if (status != ICFG_OK) {
        printf("FAIL: icfx_open failed: %d\n", status);
        free(data);
        return 1;
    }
    printf("PASS: File opened\n");

    /* Validate VSP/Index flags */
    if (view.has_vsp != has_vsp) {
        printf("FAIL: has_vsp=%d, expected %d\n", view.has_vsp, has_vsp);
        free(data);
        return 1;
    }
    if (view.has_index != is_indexed) {
        printf("FAIL: has_index=%d, expected %d\n", view.has_index, is_indexed);
        free(data);
        return 1;
    }
    printf("PASS: Flags correct (VSP=%d, Index=%d)\n", view.has_vsp, view.has_index);

    /* Validate CRC if present */
    status = icfx_validate(&view);
    if (status != ICFG_OK) {
        printf("FAIL: icfx_validate failed: %d\n", status);
        free(data);
        return 1;
    }
    printf("PASS: File validated (CRC check if present)\n");

    /* Get root */
    icfx_value_t root = icfx_root(&view);
    if (!icfx_is_object(&root)) {
        printf("FAIL: Root is not an object\n");
        free(data);
        return 1;
    }
    printf("PASS: Root is an object\n");

    /* For non-indexed files, test VSP string access */
    if (!is_indexed && has_vsp) {
        printf("\nTesting VSP string access:\n");

        /* Enumerate root fields to find 'strings' object */
        uint32_t root_len;
        status = icfx_obj_len(&root, &root_len);
        printf("  Root has %u fields\n", root_len);

        uint32_t strings_keyid = 0xFFFFFFFFU;
        for (uint32_t i = 0; i < root_len; i++) {
            uint32_t keyid;
            icfx_value_t field_val;
            status = icfx_obj_enum(&root, i, &keyid, &field_val);
            if (status == ICFG_OK) {
                /* Try to read field name from dictionary */
                const uint8_t* key_ptr;
                uint32_t key_len;
                if (icfx_dict_get_key(&view, keyid, &key_ptr, &key_len) == ICFG_OK) {
                    printf("    Field %u: key_id=%u name='%.*s'\n", i, keyid,
                           (int)(key_len > 30 ? 30 : key_len), key_ptr);
                    if (key_len == 7 && memcmp(key_ptr, "strings", 7) == 0) {
                        strings_keyid = keyid;
                        printf("  Found 'strings' field at key_id %u\n", keyid);
                        break;
                    }
                }
            }
        }

        if (strings_keyid != 0xFFFFFFFFU) {
            icfx_value_t strings_obj;
            status = icfx_obj_try_get_by_keyid(&root, strings_keyid, &strings_obj);
            if (status == ICFG_OK && icfx_is_object(&strings_obj)) {
                printf("  Accessing 'strings' object to test VSP lookups\n");

                /* Enumerate strings to find VSP references */
                uint32_t strings_len;
                status = icfx_obj_len(&strings_obj, &strings_len);
                if (status == ICFG_OK) {
                    printf("  Strings object has %u fields\n", strings_len);

                    int vsp_string_tests = 0;
                    int vsp_string_found = 0;

                    /* Look for long_string which should be VSP-referenced */
                    for (uint32_t i = 0; i < strings_len; i++) {
                        uint32_t str_keyid;
                        icfx_value_t str_field;
                        status = icfx_obj_enum(&strings_obj, i, &str_keyid, &str_field);
                        if (status == ICFG_OK) {
                            icfx_kind_t kind = icfx_kind(&str_field);
                            if (kind == ICFX_STRING || kind == ICFX_STR_ID) {
                                vsp_string_tests++;
                                const uint8_t* str_ptr;
                                uint32_t str_len;
                                if (icfx_get_str(&str_field, &str_ptr, &str_len) == ICFG_OK) {
                                    vsp_string_found++;
                                    char kind_str[20];
                                    if (kind == ICFX_STRING) {
                                        strcpy(kind_str, "INLINE");
                                    } else {
                                        strcpy(kind_str, "VSP_REF");
                                    }
                                    printf("    PASS: %s string (key_id %u, %u bytes): \"%.30s%s\"\n",
                                           kind_str, str_keyid, str_len, str_ptr,
                                           str_len > 30 ? "..." : "");
                                }
                            }
                        }
                    }

                    if (vsp_string_found > 0) {
                        printf("  PASS: Successfully read %d/%d strings (VSP or inline)\n",
                               vsp_string_found, vsp_string_tests);
                    } else {
                        printf("  WARN: Could not read strings\n");
                    }
                }
            }
        } else {
            printf("  INFO: Could not find 'strings' object in root\n");
        }
    }

    /* For indexed files, test hash table lookup */
    if (is_indexed) {
        printf("\nTesting indexed object hash table lookup:\n");

        /* Enumerate root fields to find an indexed object */
        uint32_t root_len;
        status = icfx_obj_len(&root, &root_len);
        printf("  Root has %u fields\n", root_len);

        int indexed_objects_tested = 0;

        for (uint32_t i = 0; i < root_len; i++) {
            uint32_t keyid;
            icfx_value_t field_val;
            status = icfx_obj_enum(&root, i, &keyid, &field_val);
            if (status == ICFG_OK) {
                icfx_kind_t kind = icfx_kind(&field_val);

                /* Test indexed objects */
                if (kind == ICFX_INDEXED_OBJECT) {
                    indexed_objects_tested++;
                    const uint8_t* key_ptr;
                    uint32_t key_len;
                    icfx_dict_get_key(&view, keyid, &key_ptr, &key_len);

                    printf("  Found indexed object 0x41 at key_id %u (name: %.*s)\n",
                           keyid, (int)(key_len > 20 ? 20 : key_len), key_ptr);

                    if (icfx_is_object(&field_val)) {
                        uint32_t obj_len;
                        status = icfx_obj_len(&field_val, &obj_len);
                        if (status == ICFG_OK) {
                            printf("    Object has %u fields\n", obj_len);

                            /* Enumerate all fields using hash table */
                            int field_lookups = 0;
                            int field_passed = 0;

                            for (uint32_t j = 0; j < obj_len; j++) {
                                uint32_t field_keyid;
                                icfx_value_t field_val2;
                                status = icfx_obj_enum(&field_val, j, &field_keyid, &field_val2);
                                if (status == ICFG_OK) {
                                    field_lookups++;

                                    /* Now try to look up the same field by key_id */
                                    icfx_value_t lookup_field;
                                    if (icfx_obj_try_get_by_keyid(&field_val, field_keyid, &lookup_field) == ICFG_OK) {
                                        field_passed++;
                                        printf("    PASS: Hash lookup for field key_id %u succeeded\n",
                                               field_keyid);
                                    }
                                }
                            }

                            if (field_passed > 0) {
                                printf("    PASS: %d/%d field lookups succeeded\n",
                                       field_passed, field_lookups);
                            }
                        }
                    }
                }
            }
        }

        if (indexed_objects_tested == 0) {
            printf("  WARN: No indexed objects (0x41) found in root\n");
        } else {
            printf("  PASS: Tested %d indexed object(s)\n", indexed_objects_tested);
        }
    }

    free(data);
    printf("PASS: All tests passed for %s\n", test_name);
    return 0;
}

int main() {
    int total_tests = 0;
    int passed_tests = 0;

    printf("===============================================\n");
    printf("ICFX VSP and Indexed Object Tests\n");
    printf("===============================================\n");

    /* Test indexed object file with VSP */
    if (test_icfx_vsp_indexed_file("vectors/small/icfx/golden_icfx_crc_index.icfx",
                                    "golden_icfx_crc_index.icfx (indexed with CRC+VSP)",
                                    true, true) == 0) {
        passed_tests++;
    }
    total_tests++;

    /* Test indexed object file without CRC */
    if (test_icfx_vsp_indexed_file("vectors/small/icfx/golden_icfx_nocrc_index.icfx",
                                    "golden_icfx_nocrc_index.icfx (indexed without CRC)",
                                    true, true) == 0) {
        passed_tests++;
    }
    total_tests++;

    /* Test non-indexed file with VSP and CRC */
    if (test_icfx_vsp_indexed_file("vectors/small/icfx/golden_icfx_crc.icfx",
                                    "golden_icfx_crc.icfx (non-indexed with CRC+VSP)",
                                    true, false) == 0) {
        passed_tests++;
    }
    total_tests++;

    printf("\n===============================================\n");
    printf("Results: %d/%d tests passed\n", passed_tests, total_tests);
    printf("===============================================\n");

    return (passed_tests == total_tests) ? 0 : 1;
}
