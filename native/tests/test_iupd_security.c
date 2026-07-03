#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>

#include "ironfamily/iupd_errors.h"
#include "ironfamily/iupd_reader.h"

typedef struct {
    const uint8_t* data;
    uint64_t size;
} memory_reader_ctx_t;

static iron_error_t memory_read(void* ctx, uint64_t off, uint8_t* dst, uint32_t len) {
    memory_reader_ctx_t* reader = (memory_reader_ctx_t*)ctx;
    if (off > reader->size || len > reader->size - off) {
        return IRON_E_IO;
    }
    if (len > 0 && dst == NULL) {
        return IRON_E_IO;
    }
    memcpy(dst, reader->data + off, len);
    return IRON_OK;
}

static uint32_t read_u32_le(const uint8_t* data) {
    return ((uint32_t)data[0]) |
           (((uint32_t)data[1]) << 8) |
           (((uint32_t)data[2]) << 16) |
           (((uint32_t)data[3]) << 24);
}

static uint64_t read_u64_le(const uint8_t* data) {
    return ((uint64_t)read_u32_le(data)) |
           (((uint64_t)read_u32_le(data + 4)) << 32);
}

static uint8_t* load_file(const char* path, uint64_t* out_size) {
    FILE* fp = fopen(path, "rb");
    uint8_t* data;
    long size;

    if (!fp) {
        return NULL;
    }
    if (fseek(fp, 0, SEEK_END) != 0) {
        fclose(fp);
        return NULL;
    }
    size = ftell(fp);
    if (size <= 0) {
        fclose(fp);
        return NULL;
    }
    if (fseek(fp, 0, SEEK_SET) != 0) {
        fclose(fp);
        return NULL;
    }

    data = (uint8_t*)malloc((size_t)size);
    if (!data) {
        fclose(fp);
        return NULL;
    }
    if (fread(data, 1, (size_t)size, fp) != (size_t)size) {
        free(data);
        fclose(fp);
        return NULL;
    }
    fclose(fp);
    *out_size = (uint64_t)size;
    return data;
}

static int hex_to_bytes(const char* hex_str, uint8_t* out, size_t out_len) {
    size_t i;

    if (out_len != 32 || strlen(hex_str) != 64) {
        return 0;
    }
    for (i = 0; i < out_len; i++) {
        unsigned int byte_val;
        if (sscanf(&hex_str[i * 2], "%2x", &byte_val) != 1) {
            return 0;
        }
        out[i] = (uint8_t)byte_val;
    }
    return 1;
}

static int load_public_key(uint8_t pubkey[32]) {
    const char* candidates[] = {
        "artifacts/vectors/v1/iupd/v2/test_pubkey_hex.txt",
        "native/tests/test_pubkey_hex.txt"
    };
    size_t i;

    for (i = 0; i < sizeof(candidates) / sizeof(candidates[0]); i++) {
        FILE* fp = fopen(candidates[i], "rb");
        char line[80];
        if (!fp) {
            continue;
        }
        if (fgets(line, sizeof(line), fp) != NULL) {
            size_t len = strlen(line);
            while (len > 0 && (line[len - 1] == '\n' || line[len - 1] == '\r' || line[len - 1] == ' ')) {
                line[--len] = '\0';
            }
            fclose(fp);
            if (hex_to_bytes(line, pubkey, 32)) {
                return 1;
            }
        } else {
            fclose(fp);
        }
    }
    return 0;
}

static int test_invalid_arguments(const uint8_t pubkey[32]) {
    uint64_t out_sequence = 123;
    memory_reader_ctx_t ctx = { 0 };
    iron_reader_t reader = { &ctx, memory_read };

    if (iron_iupd_verify_strict(NULL, 0, pubkey, 0, &out_sequence) != IRON_E_INVALID_ARGUMENT) {
        return 0;
    }
    if (iron_iupd_verify_strict(&reader, 0, NULL, 0, &out_sequence) != IRON_E_INVALID_ARGUMENT) {
        return 0;
    }
    if (iron_iupd_verify_strict(&reader, 0, pubkey, 0, NULL) != IRON_E_INVALID_ARGUMENT) {
        return 0;
    }
    reader.read = NULL;
    if (iron_iupd_verify_strict(&reader, 0, pubkey, 0, &out_sequence) != IRON_E_INVALID_ARGUMENT) {
        return 0;
    }
    return 1;
}

static int test_valid_without_replay(const uint8_t pubkey[32], const uint8_t* data, uint64_t size) {
    memory_reader_ctx_t ctx = { data, size };
    iron_reader_t reader = { &ctx, memory_read };
    uint64_t out_sequence = 77;
    iron_error_t err = iron_iupd_verify_strict(&reader, size, pubkey, 0, &out_sequence);
    return err == IRON_OK && out_sequence == 1;
}

static int test_replay_requires_authenticated_sequence(const uint8_t pubkey[32], const uint8_t* data, uint64_t size) {
    memory_reader_ctx_t ctx = { data, size };
    iron_reader_t reader = { &ctx, memory_read };
    uint64_t out_sequence = 77;
    iron_error_t err = iron_iupd_verify_strict(&reader, size, pubkey, 1, &out_sequence);
    return err == IRON_E_SEQ_INVALID && out_sequence == 0;
}

static int test_signature_mutation_resets_output(const uint8_t pubkey[32], uint8_t* data, uint64_t size) {
    uint64_t manifest_offset = read_u64_le(data + 21);
    uint64_t manifest_size = read_u64_le(data + manifest_offset + 16);
    uint64_t sig_offset = manifest_offset + manifest_size + 4;
    memory_reader_ctx_t ctx = { data, size };
    iron_reader_t reader = { &ctx, memory_read };
    uint64_t out_sequence = 88;
    iron_error_t err;

    data[sig_offset] ^= 0x01;
    err = iron_iupd_verify_strict(&reader, size, pubkey, 0, &out_sequence);
    data[sig_offset] ^= 0x01;

    return err == IRON_E_SIG_INVALID && out_sequence == 0;
}

static int test_manifest_mutation_fails_signature(const uint8_t pubkey[32], uint8_t* data, uint64_t size) {
    uint64_t manifest_offset = read_u64_le(data + 21);
    memory_reader_ctx_t ctx = { data, size };
    iron_reader_t reader = { &ctx, memory_read };
    uint64_t out_sequence = 0;
    iron_error_t err;

    data[manifest_offset] ^= 0x01;
    err = iron_iupd_verify_strict(&reader, size, pubkey, 0, &out_sequence);
    data[manifest_offset] ^= 0x01;

    return err == IRON_E_SIG_INVALID;
}

static int test_missing_trailer_fails_closed(const uint8_t pubkey[32], uint8_t* data, uint64_t size) {
    uint64_t payload_offset = read_u64_le(data + 29);
    memory_reader_ctx_t ctx = { data, size };
    iron_reader_t reader = { &ctx, memory_read };
    uint64_t out_sequence = 999;
    iron_error_t err;

    data[payload_offset - 21] ^= 0x01;
    err = iron_iupd_verify_strict(&reader, size, pubkey, 1, &out_sequence);
    data[payload_offset - 21] ^= 0x01;

    return err == IRON_E_SEQ_INVALID && out_sequence == 0;
}

int main(void) {
    const char* vector_path = "artifacts/vectors/v1/iupd/v2/secure_ok_01.iupd";
    uint8_t pubkey[32];
    uint64_t size = 0;
    uint8_t* data = NULL;
    int failed = 0;

    if (!load_public_key(pubkey)) {
        printf("FAIL: could not load public key\n");
        return 1;
    }
    data = load_file(vector_path, &size);
    if (!data) {
        printf("FAIL: could not load %s\n", vector_path);
        return 1;
    }

    if (!test_invalid_arguments(pubkey)) {
        printf("FAIL: test_invalid_arguments\n");
        failed = 1;
    }
    if (!test_valid_without_replay(pubkey, data, size)) {
        printf("FAIL: test_valid_without_replay\n");
        failed = 1;
    }
    if (!test_replay_requires_authenticated_sequence(pubkey, data, size)) {
        printf("FAIL: test_replay_requires_authenticated_sequence\n");
        failed = 1;
    }
    if (!test_signature_mutation_resets_output(pubkey, data, size)) {
        printf("FAIL: test_signature_mutation_resets_output\n");
        failed = 1;
    }
    if (!test_manifest_mutation_fails_signature(pubkey, data, size)) {
        printf("FAIL: test_manifest_mutation_fails_signature\n");
        failed = 1;
    }
    if (!test_missing_trailer_fails_closed(pubkey, data, size)) {
        printf("FAIL: test_missing_trailer_fails_closed\n");
        failed = 1;
    }

    free(data);
    if (failed) {
        return 1;
    }

    printf("PASS: test_iupd_security\n");
    return 0;
}
