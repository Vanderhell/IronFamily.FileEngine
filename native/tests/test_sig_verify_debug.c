/*
 * Signature Verification Debug Test
 * Dumps verification inputs for debugging signature mismatches
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "ironfamily/iupd_reader.h"
#include "ironfamily/iupd_v2_spec_min.h"
#include "ironfamily/iupd_errors.h"
#include "blake3/blake3.h"
#include "ed25519/ed25519.h"
#include "file_reader.h"

static uint32_t read_u32_le(const uint8_t* data) {
    return ((uint32_t)data[0]) | (((uint32_t)data[1]) << 8) |
           (((uint32_t)data[2]) << 16) | (((uint32_t)data[3]) << 24);
}

static uint64_t read_u64_le(const uint8_t* data) {
    return ((uint64_t)data[0]) | (((uint64_t)data[1]) << 8) |
           (((uint64_t)data[2]) << 16) | (((uint64_t)data[3]) << 24) |
           (((uint64_t)data[4]) << 32) | (((uint64_t)data[5]) << 40) |
           (((uint64_t)data[6]) << 48) | (((uint64_t)data[7]) << 56);
}

static uint16_t read_u16_le(const uint8_t* data) {
    return ((uint16_t)data[0]) | (((uint16_t)data[1]) << 8);
}

static void dump_hex(const uint8_t* data, size_t len) {
    for (size_t i = 0; i < len; i++) {
        printf("%02X", data[i]);
    }
}

int main(int argc, char** argv) {
    if (argc < 2) {
        fprintf(stderr, "Usage: %s <iupd_file_path>\n", argv[0]);
        return 1;
    }

    const char* filepath = argv[1];
    FILE* fp = fopen(filepath, "rb");
    if (!fp) {
        fprintf(stderr, "[ERROR] Could not open file: %s\n", filepath);
        return 1;
    }

    // Get file size
    fseek(fp, 0, SEEK_END);
    long file_size_long = ftell(fp);
    fseek(fp, 0, SEEK_SET);
    uint64_t file_size = (uint64_t)file_size_long;

    printf("[INFO] Loaded IUPD package: %s (%llu bytes)\n", filepath, (unsigned long long)file_size);

    // Read header
    printf("\n[PHASE] Parsing IUPD header...\n");
    uint8_t header_buf[IUPD_V2_HEADER_SIZE];
    if (fread(header_buf, IUPD_V2_HEADER_SIZE, 1, fp) != 1) {
        fprintf(stderr, "[ERROR] Could not read header\n");
        fclose(fp);
        return 1;
    }

    uint32_t magic = read_u32_le(&header_buf[IUPD_V2_MAGIC_OFFSET]);
    uint8_t version = header_buf[IUPD_V2_VERSION_OFFSET];
    uint8_t profile = header_buf[IUPD_V2_PROFILE_OFFSET];
    uint32_t flags = read_u32_le(&header_buf[IUPD_V2_FLAGS_OFFSET]);
    uint16_t header_size = read_u16_le(&header_buf[IUPD_V2_HEADER_SIZE_OFFSET]);
    uint64_t chunk_table_offset = read_u64_le(&header_buf[IUPD_V2_CHUNK_TABLE_OFFSET_OFFSET]);
    uint64_t manifest_offset = read_u64_le(&header_buf[IUPD_V2_MANIFEST_OFFSET_OFFSET]);
    uint64_t payload_offset = read_u64_le(&header_buf[IUPD_V2_PAYLOAD_OFFSET_OFFSET]);

    printf("Magic:              0x%08X\n", magic);
    printf("Version:            0x%02X\n", version);
    printf("Profile:            0x%02X\n", profile);
    printf("Flags:              0x%08X\n", flags);
    printf("Header Size:        %u\n", header_size);
    printf("Chunk Table Off:    %llu\n", (unsigned long long)chunk_table_offset);
    printf("Manifest Off:       %llu\n", (unsigned long long)manifest_offset);
    printf("Payload Off:        %llu\n", (unsigned long long)payload_offset);

    // Read manifest header
    printf("\n[PHASE] Parsing manifest header...\n");
    uint8_t manifest_header_buf[IUPD_MANIFEST_HEADER_SIZE];
    if (fseek(fp, (long)manifest_offset, SEEK_SET) != 0 ||
        fread(manifest_header_buf, IUPD_MANIFEST_HEADER_SIZE, 1, fp) != 1) {
        fprintf(stderr, "[ERROR] Could not read manifest header\n");
        fclose(fp);
        return 1;
    }

    uint32_t dependency_count = read_u32_le(&manifest_header_buf[0]);
    uint32_t apply_order_count = read_u32_le(&manifest_header_buf[4]);
    uint32_t manifest_crc32 = read_u32_le(&manifest_header_buf[8]);
    uint64_t manifest_size = read_u64_le(&manifest_header_buf[16]);

    printf("Dependency Count:   %u\n", dependency_count);
    printf("Apply Order Count:  %u\n", apply_order_count);
    printf("Manifest CRC32:     0x%08X\n", manifest_crc32);
    printf("Manifest Size:      %llu bytes\n", (unsigned long long)manifest_size);

    // Calculate signed region
    printf("\n[PHASE] Computing signed region...\n");
    uint64_t signed_region_start = manifest_offset;
    uint64_t signed_region_length = manifest_size - IUPD_MANIFEST_CRCRESV_SIZE;
    uint64_t signed_region_end = signed_region_start + signed_region_length;

    printf("Signed Region Start: %llu\n", (unsigned long long)signed_region_start);
    printf("Signed Region End:   %llu\n", (unsigned long long)signed_region_end);
    printf("Signed Region Len:   %llu\n", (unsigned long long)signed_region_length);

    // Read signed bytes
    uint8_t* signed_bytes = (uint8_t*)malloc(signed_region_length);
    if (!signed_bytes) {
        fprintf(stderr, "[ERROR] Could not allocate memory\n");
        fclose(fp);
        return 1;
    }

    if (fseek(fp, (long)signed_region_start, SEEK_SET) != 0 ||
        fread(signed_bytes, signed_region_length, 1, fp) != 1) {
        fprintf(stderr, "[ERROR] Could not read signed region\n");
        fclose(fp);
        free(signed_bytes);
        return 1;
    }

    printf("\n[DATA] First 16 bytes of signed region (hex):\n0000: ");
    size_t first_len = signed_region_length < 16 ? signed_region_length : 16;
    dump_hex(signed_bytes, first_len);
    printf("\n");

    printf("\n[DATA] Last 16 bytes of signed region (hex):\n0000: ");
    size_t last_start = signed_region_length > 16 ? signed_region_length - 16 : 0;
    dump_hex(signed_bytes + last_start, signed_region_length - last_start);
    printf("\n");

    // Compute BLAKE3 hash
    printf("\n[PHASE] Computing BLAKE3-256 hash of signed region...\n");
    uint8_t hash[32];
    blake3_hasher hasher;
    blake3_hasher_init(&hasher);
    blake3_hasher_update(&hasher, signed_bytes, signed_region_length);
    blake3_hasher_finalize(&hasher, hash, 32);

    printf("Hash (hex): ");
    dump_hex(hash, 32);
    printf("\n");

    // Extract signature
    printf("\n[PHASE] Extracting signature...\n");
    uint64_t sig_footer_offset = manifest_offset + manifest_size;
    uint8_t sig_len_buf[4];
    if (fseek(fp, (long)sig_footer_offset, SEEK_SET) != 0 ||
        fread(sig_len_buf, 4, 1, fp) != 1) {
        fprintf(stderr, "[ERROR] Could not read signature length\n");
        fclose(fp);
        free(signed_bytes);
        return 1;
    }

    uint32_t sig_len = read_u32_le(sig_len_buf);
    uint8_t signature[64];
    if (fread(signature, sig_len, 1, fp) != 1) {
        fprintf(stderr, "[ERROR] Could not read signature\n");
        fclose(fp);
        free(signed_bytes);
        return 1;
    }

    printf("Signature Offset:   %llu\n", (unsigned long long)sig_footer_offset);
    printf("Signature Length:   %u\n", sig_len);
    printf("Signature (hex):    ");
    dump_hex(signature, sig_len);
    printf("\n");

    // Try to verify with bench public key
    printf("\n[PHASE] Attempting verification...\n");
    // This is the bench public key from IupdEd25519Keys
    uint8_t bench_pubkey[32] = {
        0x3F, 0x77, 0x08, 0xD5, 0xF5, 0xCC, 0x2B, 0xC6,
        0x33, 0xB5, 0x9D, 0x2B, 0x3A, 0x2E, 0xD9, 0x2E,
        0x74, 0x79, 0x22, 0x0C, 0x6F, 0x08, 0xAD, 0xE2,
        0x08, 0xBE, 0xBC, 0xD8, 0x58, 0x0A, 0xB9, 0x3B
    };

    printf("Public Key (hex):   ");
    dump_hex(bench_pubkey, 32);
    printf("\n");

    int verify_result = ed25519_verify(signature, hash, 32, bench_pubkey);
    printf("Verification Result: %s\n", verify_result ? "PASS" : "FAIL");

    fclose(fp);
    free(signed_bytes);
    return verify_result ? 0 : 1;
}
