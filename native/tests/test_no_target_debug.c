/*
 * Debug test harness for success_05_delta_v1_no_target failure analysis
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#include "ironfamily/iupd_errors.h"
#include "ironfamily/iupd_reader.h"
#include "ironfamily/iupd_incremental_metadata.h"
#include "ironfamily/ota_apply.h"
#include "file_reader.h"

static uint64_t get_file_size(FILE* fp) {
    if (fseek(fp, 0, SEEK_END) != 0) return 0;
    long size = ftell(fp);
    fseek(fp, 0, SEEK_SET);
    return (uint64_t)size;
}

int main(void) {
    printf("=== SUCCESS_05 DEBUG ANALYSIS ===\n\n");

    /* Vector paths */
    const char* success_01_pkg = "artifacts/_dev/exec_07_incremental_full_package_vector_parity/incremental_vectors/success_01_delta_v1_simple/package.iupd";
    const char* success_05_pkg = "artifacts/_dev/exec_07_incremental_full_package_vector_parity/incremental_vectors/success_05_delta_v1_no_target/package.iupd";

    /* Test both vectors */
    const char* vectors[] = {success_01_pkg, success_05_pkg};
    const char* vector_names[] = {"success_01_delta_v1_simple", "success_05_delta_v1_no_target"};

    for (int v = 0; v < 2; v++) {
        printf("\n============================================\n");
        printf("Testing: %s\n", vector_names[v]);
        printf("============================================\n");

        FILE* fp = fopen(vectors[v], "rb");
        if (!fp) {
            printf("ERROR: Cannot open file\n");
            continue;
        }

        uint64_t pkg_size = get_file_size(fp);
        printf("Package file size: %llu bytes\n", pkg_size);

        /* Read entire package */
        uint8_t* pkg_data = malloc(pkg_size);
        if (fread(pkg_data, 1, pkg_size, fp) != pkg_size) {
            printf("ERROR: Failed to read package\n");
            fclose(fp);
            free(pkg_data);
            continue;
        }
        fclose(fp);

        /* Parse metadata */
        printf("\n--- METADATA PARSING ---\n");

        int64_t metadata_offset = iupd_incremental_metadata_find(pkg_data, pkg_size);
        printf("Metadata offset: %lld\n", metadata_offset);

        if (metadata_offset < 0) {
            printf("ERROR: Metadata not found\n");
            free(pkg_data);
            continue;
        }

        uint32_t metadata_len = (uint32_t)(pkg_size - metadata_offset);
        printf("Metadata size: %u bytes\n", metadata_len);

        /* Print metadata bytes in hex */
        printf("Metadata bytes (first 64): ");
        for (int i = 0; i < (metadata_len > 64 ? 64 : metadata_len); i++) {
            printf("%02x ", pkg_data[metadata_offset + i]);
        }
        printf("\n");

        iupd_incremental_metadata_t metadata;
        bool parse_ok = iupd_incremental_metadata_parse(&pkg_data[metadata_offset], metadata_len, &metadata);
        printf("Parse result: %s\n", parse_ok ? "SUCCESS" : "FAILED");

        if (!parse_ok) {
            printf("ERROR: Metadata parsing failed!\n");
            free(pkg_data);
            continue;
        }

        printf("Algorithm ID: %u\n", metadata.algorithm_id);
        printf("Base hash length: %u\n", metadata.base_hash_len);
        printf("Target hash length: %u\n", metadata.target_hash_len);

        if (metadata.base_hash_len > 0) {
            printf("Base hash: ");
            for (int i = 0; i < metadata.base_hash_len; i++) {
                printf("%02x", metadata.base_hash[i]);
            }
            printf("\n");
        }

        if (metadata.target_hash_len > 0) {
            printf("Target hash: ");
            for (int i = 0; i < metadata.target_hash_len; i++) {
                printf("%02x", metadata.target_hash[i]);
            }
            printf("\n");
        } else {
            printf("Target hash: <absent>\n");
        }

        /* Check payload bounds */
        printf("\n--- PAYLOAD ANALYSIS ---\n");
        uint32_t payload_offset = 0x48;  /* From IUPD v2 header constant */
        uint32_t delta_size = metadata_offset - payload_offset;
        printf("Payload offset: %u\n", payload_offset);
        printf("Delta size: %u\n", delta_size);
        printf("Metadata ends package: %s\n",
               (metadata_offset + metadata_len == pkg_size) ? "YES" : "NO");

        free(pkg_data);
    }

    printf("\n=== END DEBUG ===\n");
    return 0;
}
