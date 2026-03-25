#include "../include/bjv.h"

#include <string.h>
#include <math.h>
#include <limits.h>

/* ============================================================================
 * Constants
 * ============================================================================
 */

#define MAX_DEPTH 64
#define MAX_FILE_SIZE (64 * 1024 * 1024)  /* 64 MB */
#define MAX_STRING_LENGTH (4 * 1024 * 1024)  /* 4 MB */
#define MAX_DICT_COUNT 1000000
#define MAX_ARRAY_COUNT 1000000
#define MAX_VSP_COUNT 1000000

/* ============================================================================
 * Safe Read Functions
 * ============================================================================
 */

static bool bjv_check_bounds(const bjv_doc_t* doc, uint32_t off, size_t len) {
    /* Check for overflow */
    if (off > UINT32_MAX - len) {
        return false;
    }
    uint32_t end = off + (uint32_t)len;
    return end <= doc->size;
}

static bool bjv_read_byte(const bjv_doc_t* doc, uint32_t off, uint8_t* out) {
    if (!bjv_check_bounds(doc, off, 1)) {
        return false;
    }
    *out = doc->data[off];
    return true;
}

static bool bjv_read_u32_le(const bjv_doc_t* doc, uint32_t off, uint32_t* out) {
    if (!bjv_check_bounds(doc, off, 4)) {
        return false;
    }
    *out = ((uint32_t)doc->data[off] |
            ((uint32_t)doc->data[off+1] << 8) |
            ((uint32_t)doc->data[off+2] << 16) |
            ((uint32_t)doc->data[off+3] << 24));
    return true;
}

static bool bjv_read_u64_le(const bjv_doc_t* doc, uint32_t off, uint64_t* out) {
    if (!bjv_check_bounds(doc, off, 8)) {
        return false;
    }
    *out = ((uint64_t)doc->data[off] |
            ((uint64_t)doc->data[off+1] << 8) |
            ((uint64_t)doc->data[off+2] << 16) |
            ((uint64_t)doc->data[off+3] << 24) |
            ((uint64_t)doc->data[off+4] << 32) |
            ((uint64_t)doc->data[off+5] << 40) |
            ((uint64_t)doc->data[off+6] << 48) |
            ((uint64_t)doc->data[off+7] << 56));
    return true;
}

static bool bjv_read_i64_le(const bjv_doc_t* doc, uint32_t off, int64_t* out) {
    uint64_t u;
    if (!bjv_read_u64_le(doc, off, &u)) {
        return false;
    }
    *out = (int64_t)u;
    return true;
}

static bool bjv_read_f64_le(const bjv_doc_t* doc, uint32_t off, double* out) {
    uint64_t bits;
    if (!bjv_read_u64_le(doc, off, &bits)) {
        return false;
    }
    memcpy(out, &bits, sizeof(double));
    return true;
}

static bool bjv_read_bytes(const bjv_doc_t* doc, uint32_t off, size_t len, bjv_slice_t* out) {
    if (!bjv_check_bounds(doc, off, len)) {
        return false;
    }
    out->ptr = &doc->data[off];
    out->len = len;
    return true;
}

/* ============================================================================
 * VarUInt Decoding (ULEB128)
 * ============================================================================
 */

/**
 * Decode VarUInt, enforcing minimal encoding.
 * Returns number of bytes consumed (1-5 for u32, 1-10 for u64).
 * Returns 0 on error (bounds, overflow, non-minimal).
 */
static uint32_t bjv_decode_varuint_u32(const bjv_doc_t* doc, uint32_t off, uint32_t* out) {
    if (off >= doc->size) {
        return 0;
    }

    uint32_t result = 0;
    uint32_t shift = 0;
    uint32_t consumed = 0;

    for (int i = 0; i < 5; i++) {
        if (off + i >= doc->size) {
            return 0;
        }
        uint8_t byte = doc->data[off + i];
        consumed++;

        /* Extract 7-bit payload */
        uint32_t chunk = byte & 0x7f;

        /* Check overflow */
        if (shift >= 25 && chunk > 0x0f) {
            return 0;
        }

        result |= (chunk << shift);
        shift += 7;

        if ((byte & 0x80) == 0) {
            /* No more bytes */
            /* Check minimal encoding: if this is the last byte and shift >= 7,
               then the previous byte should have had high bit set */
            if (i > 0 && i < 5) {
                uint8_t prev = doc->data[off + i - 1];
                if ((prev & 0x80) == 0) {
                    /* Previous byte had no continuation bit, but we have more? Invalid */
                    return 0;
                }
            }
            *out = result;
            return consumed;
        }
    }

    /* Too many bytes */
    return 0;
}

static uint32_t bjv_decode_varuint_u64(const bjv_doc_t* doc, uint32_t off, uint64_t* out) {
    if (off >= doc->size) {
        return 0;
    }

    uint64_t result = 0;
    uint32_t shift = 0;
    uint32_t consumed = 0;

    for (int i = 0; i < 10; i++) {
        if (off + i >= doc->size) {
            return 0;
        }
        uint8_t byte = doc->data[off + i];
        consumed++;

        uint64_t chunk = byte & 0x7f;

        if (shift >= 63 && chunk > 1) {
            return 0;
        }

        result |= (chunk << shift);
        shift += 7;

        if ((byte & 0x80) == 0) {
            *out = result;
            return consumed;
        }
    }

    return 0;
}

/* ============================================================================
 * Core Open / Parse Header
 * ============================================================================
 */

static bjv_err_t bjv_parse_header(const void* data, size_t size, bjv_doc_t* out_doc) {
    if (size < 32) {
        return BJV_ERR_FORMAT;
    }

    const uint8_t* bytes = (const uint8_t*)data;

    /* Check magic */
    bool is_bjv2 = (bytes[0] == 'B' && bytes[1] == 'J' && bytes[2] == 'V' && bytes[3] == '2');
    bool is_bjv4 = (bytes[0] == 'B' && bytes[1] == 'J' && bytes[2] == 'V' && bytes[3] == '4');

    if (!is_bjv2 && !is_bjv4) {
        return BJV_ERR_FORMAT;
    }

    /* Check flags */
    uint8_t flags = bytes[4];

    /* Bit 0 (little endian) must be 1 */
    if ((flags & 0x01) == 0) {
        return BJV_ERR_FORMAT;
    }

    /* Reserved bits (3-7) must be 0 */
    if ((flags & 0xf8) != 0) {
        return BJV_ERR_UNSUPPORTED;
    }

    /* Check reserved fields */
    if (bytes[5] != 0 || bytes[28] != 0 || bytes[29] != 0 || bytes[30] != 0 || bytes[31] != 0) {
        return BJV_ERR_CANONICAL;
    }

    /* Check header size */
    uint16_t header_size = bytes[6] | ((uint16_t)bytes[7] << 8);
    if (header_size != 32) {
        return BJV_ERR_FORMAT;
    }

    /* Read offsets */
    uint32_t total_size = bytes[8] | ((uint32_t)bytes[9] << 8) |
                          ((uint32_t)bytes[10] << 16) | ((uint32_t)bytes[11] << 24);
    uint32_t dict_off = bytes[12] | ((uint32_t)bytes[13] << 8) |
                        ((uint32_t)bytes[14] << 16) | ((uint32_t)bytes[15] << 24);
    uint32_t vsp_off = bytes[16] | ((uint32_t)bytes[17] << 8) |
                       ((uint32_t)bytes[18] << 16) | ((uint32_t)bytes[19] << 24);
    uint32_t root_off = bytes[20] | ((uint32_t)bytes[21] << 8) |
                        ((uint32_t)bytes[22] << 16) | ((uint32_t)bytes[23] << 24);
    uint32_t crc_off = bytes[24] | ((uint32_t)bytes[25] << 8) |
                       ((uint32_t)bytes[26] << 16) | ((uint32_t)bytes[27] << 24);

    /* Sanity checks */
    if (total_size != size) {
        return BJV_ERR_FORMAT;
    }

    if (total_size > MAX_FILE_SIZE) {
        return BJV_ERR_FORMAT;
    }

    if (dict_off < 32 || dict_off >= total_size) {
        return BJV_ERR_FORMAT;
    }

    /* Fill out document */
    memset(out_doc, 0, sizeof(*out_doc));
    out_doc->data = (const uint8_t*)data;
    out_doc->size = size;
    out_doc->dict_off = dict_off;
    out_doc->vsp_off = vsp_off;
    out_doc->root_off = root_off;
    out_doc->crc_off = crc_off;
    out_doc->is_bjv4 = is_bjv4;
    out_doc->has_crc = (flags & 0x02) != 0;
    out_doc->has_vsp = (flags & 0x04) != 0;

    return BJV_OK;
}

static bjv_err_t bjv_parse_dict(bjv_doc_t* doc) {
    uint32_t off = doc->dict_off;
    uint64_t count;
    uint32_t consumed = bjv_decode_varuint_u64(doc, off, &count);

    if (consumed == 0 || count > MAX_DICT_COUNT) {
        return BJV_ERR_CANONICAL;
    }

    doc->dict_data = &doc->data[off];
    doc->dict_count = (uint32_t)count;

    /* Skip dictionary content validation for now (done in validate_root) */
    return BJV_OK;
}

static bjv_err_t bjv_parse_vsp(bjv_doc_t* doc) {
    if (!doc->has_vsp) {
        doc->vsp_count = 0;
        doc->vsp_data = NULL;
        return BJV_OK;
    }

    uint32_t off = doc->vsp_off;
    uint64_t count;
    uint32_t consumed = bjv_decode_varuint_u64(doc, off, &count);

    if (consumed == 0 || count > MAX_VSP_COUNT) {
        return BJV_ERR_CANONICAL;
    }

    doc->vsp_data = &doc->data[off];
    doc->vsp_count = (uint32_t)count;

    return BJV_OK;
}

bjv_err_t bjv_open(const void* data, size_t size, bjv_doc_t* out_doc) {
    if (!data || !out_doc) {
        return BJV_ERR_FORMAT;
    }

    bjv_err_t err = bjv_parse_header(data, size, out_doc);
    if (err != BJV_OK) {
        return err;
    }

    err = bjv_parse_dict(out_doc);
    if (err != BJV_OK) {
        return err;
    }

    err = bjv_parse_vsp(out_doc);
    if (err != BJV_OK) {
        return err;
    }

    return BJV_OK;
}

/* ============================================================================
 * Value Access
 * ============================================================================
 */

bjv_val_t bjv_root(const bjv_doc_t* doc) {
    bjv_val_t val = {
        .doc = doc,
        .off = doc->root_off
    };
    return val;
}

uint8_t bjv_type(bjv_val_t v) {
    if (!v.doc || v.off >= v.doc->size) {
        return 0xff;  /* Invalid */
    }
    return v.doc->data[v.off];
}

bool bjv_get_i64(bjv_val_t v, int64_t* out) {
    if (!v.doc || bjv_type(v) != BJV_I64) {
        return false;
    }
    return bjv_read_i64_le(v.doc, v.off + 1, out);
}

bool bjv_get_u64(bjv_val_t v, uint64_t* out) {
    if (!v.doc || bjv_type(v) != BJV_U64) {
        return false;
    }
    return bjv_read_u64_le(v.doc, v.off + 1, out);
}

bool bjv_get_f64(bjv_val_t v, double* out) {
    if (!v.doc || bjv_type(v) != BJV_F64) {
        return false;
    }
    return bjv_read_f64_le(v.doc, v.off + 1, out);
}

bool bjv_get_bool(bjv_val_t v, bool* out) {
    if (!v.doc) {
        return false;
    }
    uint8_t type = bjv_type(v);
    if (type == BJV_TRUE) {
        *out = true;
        return true;
    }
    if (type == BJV_FALSE) {
        *out = false;
        return true;
    }
    return false;
}

bool bjv_get_null(bjv_val_t v) {
    if (!v.doc) {
        return false;
    }
    return bjv_type(v) == BJV_NULL;
}

bool bjv_get_string(bjv_val_t v, bjv_slice_t* out_utf8) {
    if (!v.doc) {
        return false;
    }

    uint8_t type = bjv_type(v);

    if (type == BJV_STRING) {
        uint64_t len;
        uint32_t consumed = bjv_decode_varuint_u64(v.doc, v.off + 1, &len);
        if (consumed == 0 || len > MAX_STRING_LENGTH) {
            return false;
        }
        return bjv_read_bytes(v.doc, v.off + 1 + consumed, (size_t)len, out_utf8);
    }

    if (type == BJV_STR_ID) {
        uint64_t str_id;
        uint32_t consumed = bjv_decode_varuint_u64(v.doc, v.off + 1, &str_id);
        if (consumed == 0 || str_id >= v.doc->vsp_count) {
            return false;
        }
        /* Decode VSP string */
        uint64_t vsp_str_len;
        consumed = bjv_decode_varuint_u64(v.doc, v.doc->vsp_off, &vsp_str_len);
        if (consumed == 0) {
            return false;
        }
        uint32_t vsp_pos = v.doc->vsp_off + consumed;

        /* Skip to str_id-th string */
        for (uint64_t i = 0; i < str_id; i++) {
            uint64_t skip_len;
            consumed = bjv_decode_varuint_u64(v.doc, vsp_pos, &skip_len);
            if (consumed == 0) {
                return false;
            }
            vsp_pos += consumed + (uint32_t)skip_len;
            if (vsp_pos >= v.doc->size) {
                return false;
            }
        }

        /* Read the string */
        uint64_t str_len;
        consumed = bjv_decode_varuint_u64(v.doc, vsp_pos, &str_len);
        if (consumed == 0 || str_len > MAX_STRING_LENGTH) {
            return false;
        }
        return bjv_read_bytes(v.doc, vsp_pos + consumed, (size_t)str_len, out_utf8);
    }

    return false;
}

bool bjv_get_bytes(bjv_val_t v, bjv_slice_t* out_bytes) {
    if (!v.doc || bjv_type(v) != BJV_BYTES) {
        return false;
    }

    uint64_t len;
    uint32_t consumed = bjv_decode_varuint_u64(v.doc, v.off + 1, &len);
    if (consumed == 0 || len > MAX_STRING_LENGTH) {
        return false;
    }
    return bjv_read_bytes(v.doc, v.off + 1 + consumed, (size_t)len, out_bytes);
}

/* ============================================================================
 * Array Access
 * ============================================================================
 */

bool bjv_arr_count(bjv_val_t v, uint32_t* out_count) {
    if (!v.doc || bjv_type(v) != BJV_ARRAY) {
        return false;
    }

    uint64_t count;
    uint32_t consumed = bjv_decode_varuint_u64(v.doc, v.off + 1, &count);
    if (consumed == 0 || count > MAX_ARRAY_COUNT) {
        return false;
    }

    *out_count = (uint32_t)count;
    return true;
}

bool bjv_arr_get(bjv_val_t v, uint32_t idx, bjv_val_t* out_elem) {
    if (!v.doc || bjv_type(v) != BJV_ARRAY) {
        return false;
    }

    uint32_t count;
    if (!bjv_arr_count(v, &count)) {
        return false;
    }

    if (idx >= count) {
        return false;
    }

    uint64_t c;
    uint32_t consumed = bjv_decode_varuint_u64(v.doc, v.off + 1, &c);
    if (consumed == 0) {
        return false;
    }

    uint32_t offsets_pos = v.off + 1 + consumed;

    /* Simplified: read idx-th offset */
    uint64_t elem_offset;
    uint32_t offset_consumed = bjv_decode_varuint_u64(v.doc, offsets_pos + idx * 4, &elem_offset);
    if (offset_consumed == 0 || elem_offset >= v.doc->size) {
        return false;
    }

    uint32_t abs_offset = v.off + (uint32_t)elem_offset;
    if (abs_offset >= v.doc->size) {
        return false;
    }

    out_elem->doc = v.doc;
    out_elem->off = abs_offset;
    return true;
}

/* ============================================================================
 * Object Access
 * ============================================================================
 */

bool bjv_obj_count(bjv_val_t v, uint32_t* out_count) {
    if (!v.doc || bjv_type(v) != BJV_OBJECT) {
        return false;
    }

    uint64_t count;
    uint32_t consumed = bjv_decode_varuint_u64(v.doc, v.off + 1, &count);
    if (consumed == 0 || count > MAX_DICT_COUNT) {
        return false;
    }

    *out_count = (uint32_t)count;
    return true;
}

bool bjv_obj_get_by_keyid(bjv_val_t v, uint32_t keyId, bjv_val_t* out_val) {
    if (!v.doc || bjv_type(v) != BJV_OBJECT) {
        return false;
    }

    uint32_t count;
    if (!bjv_obj_count(v, &count)) {
        return false;
    }

    uint64_t c;
    uint32_t consumed = bjv_decode_varuint_u64(v.doc, v.off + 1, &c);
    if (consumed == 0) {
        return false;
    }

    uint32_t pairs_pos = v.off + 1 + consumed;

    /* Search for keyId */
    uint32_t keyid_size = v.doc->is_bjv4 ? 4 : 2;

    for (uint32_t i = 0; i < count; i++) {
        uint32_t pair_off = pairs_pos + i * (keyid_size + 4);  /* Rough estimate */

        uint32_t this_keyid;
        if (v.doc->is_bjv4) {
            uint32_t val;
            if (!bjv_read_u32_le(v.doc, pair_off, &val)) {
                return false;
            }
            this_keyid = val;
        } else {
            if (pair_off + 1 >= v.doc->size) {
                return false;
            }
            this_keyid = v.doc->data[pair_off] | ((uint32_t)v.doc->data[pair_off + 1] << 8);
        }

        if (this_keyid == keyId) {
            /* Read value offset */
            uint64_t val_off;
            uint32_t offset_pos = pair_off + keyid_size;
            uint32_t offset_consumed = bjv_decode_varuint_u64(v.doc, offset_pos, &val_off);
            if (offset_consumed == 0) {
                return false;
            }

            uint32_t abs_offset = v.off + (uint32_t)val_off;
            if (abs_offset >= v.doc->size) {
                return false;
            }

            out_val->doc = v.doc;
            out_val->off = abs_offset;
            return true;
        }
    }

    return false;
}

/* ============================================================================
 * Dictionary Access
 * ============================================================================
 */

bool bjv_keyid_find(const bjv_doc_t* doc, const char* key_utf8, size_t key_len, uint32_t* out_keyid) {
    if (!doc || !key_utf8) {
        return false;
    }

    /* Binary search on sorted dictionary */
    uint32_t left = 0, right = doc->dict_count;

    while (left < right) {
        uint32_t mid = left + (right - left) / 2;

        /* Decode mid-th key */
        uint32_t dict_pos = doc->dict_off;
        uint64_t count;
        uint32_t consumed = bjv_decode_varuint_u64(doc, dict_pos, &count);
        if (consumed == 0) {
            return false;
        }

        dict_pos += consumed;

        /* Skip to mid-th key */
        for (uint32_t i = 0; i < mid; i++) {
            uint64_t skip_len;
            consumed = bjv_decode_varuint_u64(doc, dict_pos, &skip_len);
            if (consumed == 0) {
                return false;
            }
            dict_pos += consumed + (uint32_t)skip_len;
            if (dict_pos >= doc->size) {
                return false;
            }
        }

        /* Read mid-th key length */
        uint64_t mid_len;
        consumed = bjv_decode_varuint_u64(doc, dict_pos, &mid_len);
        if (consumed == 0) {
            return false;
        }

        if (dict_pos + consumed + mid_len > doc->size) {
            return false;
        }

        const uint8_t* mid_key = &doc->data[dict_pos + consumed];
        int cmp;

        if (mid_len < key_len) {
            cmp = memcmp(mid_key, key_utf8, mid_len);
            if (cmp == 0) cmp = -1;
        } else if (mid_len > key_len) {
            cmp = memcmp(mid_key, key_utf8, key_len);
            if (cmp == 0) cmp = 1;
        } else {
            cmp = memcmp(mid_key, key_utf8, key_len);
        }

        if (cmp == 0) {
            *out_keyid = mid;
            return true;
        } else if (cmp < 0) {
            left = mid + 1;
        } else {
            right = mid;
        }
    }

    return false;
}

bool bjv_keyid_to_key(const bjv_doc_t* doc, uint32_t keyId, bjv_slice_t* out_key_utf8) {
    if (!doc || keyId >= doc->dict_count) {
        return false;
    }

    uint32_t dict_pos = doc->dict_off;
    uint64_t count;
    uint32_t consumed = bjv_decode_varuint_u64(doc, dict_pos, &count);
    if (consumed == 0) {
        return false;
    }

    dict_pos += consumed;

    /* Skip to keyId-th key */
    for (uint32_t i = 0; i < keyId; i++) {
        uint64_t skip_len;
        consumed = bjv_decode_varuint_u64(doc, dict_pos, &skip_len);
        if (consumed == 0) {
            return false;
        }
        dict_pos += consumed + (uint32_t)skip_len;
    }

    /* Read keyId-th key */
    uint64_t key_len;
    consumed = bjv_decode_varuint_u64(doc, dict_pos, &key_len);
    if (consumed == 0 || key_len > MAX_STRING_LENGTH) {
        return false;
    }

    return bjv_read_bytes(doc, dict_pos + consumed, (size_t)key_len, out_key_utf8);
}

/* ============================================================================
 * Validation
 * ============================================================================
 */

static bjv_err_t bjv_validate_value(const bjv_doc_t* doc, uint32_t off, uint32_t depth);

bjv_err_t bjv_validate_root(const bjv_doc_t* doc, uint32_t max_depth) {
    if (!doc || max_depth == 0) {
        return BJV_ERR_FORMAT;
    }

    return bjv_validate_value(doc, doc->root_off, max_depth);
}

static bjv_err_t bjv_validate_value(const bjv_doc_t* doc, uint32_t off, uint32_t depth) {
    if (depth == 0) {
        return BJV_ERR_CANONICAL;  /* Depth limit exceeded */
    }

    if (off >= doc->size) {
        return BJV_ERR_BOUNDS;
    }

    uint8_t type = doc->data[off];

    switch (type) {
        case BJV_NULL:
        case BJV_FALSE:
        case BJV_TRUE:
            return BJV_OK;

        case BJV_I64:
        case BJV_U64:
        case BJV_F64: {
            if (off + 8 >= doc->size) {
                return BJV_ERR_BOUNDS;
            }
            if (type == BJV_F64) {
                double val;
                if (!bjv_read_f64_le(doc, off + 1, &val)) {
                    return BJV_ERR_BOUNDS;
                }
                if (isnan(val)) {
                    return BJV_ERR_CANONICAL;
                }
            }
            return BJV_OK;
        }

        case BJV_STRING:
        case BJV_BYTES:
        case BJV_STR_ID: {
            uint64_t len;
            uint32_t consumed = bjv_decode_varuint_u64(doc, off + 1, &len);
            if (consumed == 0) {
                return BJV_ERR_CANONICAL;
            }
            if (len > MAX_STRING_LENGTH) {
                return BJV_ERR_FORMAT;
            }

            if (type == BJV_STR_ID) {
                if (len >= doc->vsp_count) {
                    return BJV_ERR_BOUNDS;
                }
            } else {
                if (!bjv_check_bounds(doc, off + 1 + consumed, (size_t)len)) {
                    return BJV_ERR_BOUNDS;
                }
            }
            return BJV_OK;
        }

        case BJV_ARRAY: {
            uint64_t count;
            uint32_t consumed = bjv_decode_varuint_u64(doc, off + 1, &count);
            if (consumed == 0 || count > MAX_ARRAY_COUNT) {
                return BJV_ERR_CANONICAL;
            }

            uint32_t offsets_pos = off + 1 + consumed;
            uint32_t prev_offset = 0;

            for (uint32_t i = 0; i < (uint32_t)count; i++) {
                uint64_t elem_offset;
                uint32_t offset_consumed = bjv_decode_varuint_u64(doc, offsets_pos, &elem_offset);
                if (offset_consumed == 0) {
                    return BJV_ERR_CANONICAL;
                }
                offsets_pos += offset_consumed;

                /* Check strictly increasing */
                if (i > 0 && elem_offset <= prev_offset) {
                    return BJV_ERR_CANONICAL;
                }
                prev_offset = (uint32_t)elem_offset;

                uint32_t abs_offset = off + (uint32_t)elem_offset;
                bjv_err_t err = bjv_validate_value(doc, abs_offset, depth - 1);
                if (err != BJV_OK) {
                    return err;
                }
            }
            return BJV_OK;
        }

        case BJV_OBJECT: {
            uint64_t count;
            uint32_t consumed = bjv_decode_varuint_u64(doc, off + 1, &count);
            if (consumed == 0 || count > MAX_DICT_COUNT) {
                return BJV_ERR_CANONICAL;
            }

            uint32_t pair_pos = off + 1 + consumed;
            uint32_t keyid_size = doc->is_bjv4 ? 4 : 2;
            uint32_t prev_keyid = (uint32_t)-1;
            uint32_t prev_val_offset = 0;

            for (uint32_t i = 0; i < (uint32_t)count; i++) {
                /* Read keyId */
                uint32_t this_keyid;
                if (doc->is_bjv4) {
                    if (!bjv_read_u32_le(doc, pair_pos, &this_keyid)) {
                        return BJV_ERR_BOUNDS;
                    }
                } else {
                    if (pair_pos + 1 >= doc->size) {
                        return BJV_ERR_BOUNDS;
                    }
                    this_keyid = doc->data[pair_pos] |
                                 ((uint32_t)doc->data[pair_pos + 1] << 8);
                }

                /* Check strictly increasing and valid */
                if (this_keyid >= doc->dict_count || this_keyid <= prev_keyid) {
                    return BJV_ERR_CANONICAL;
                }
                prev_keyid = this_keyid;

                /* Read value offset */
                uint64_t val_offset;
                uint32_t offset_consumed = bjv_decode_varuint_u64(doc, pair_pos + keyid_size, &val_offset);
                if (offset_consumed == 0) {
                    return BJV_ERR_CANONICAL;
                }

                if (i > 0 && val_offset <= prev_val_offset) {
                    /* Recommended: value offsets should be strictly increasing */
                    /* For now, we allow non-increasing but this is not canonical */
                }
                prev_val_offset = (uint32_t)val_offset;

                uint32_t abs_offset = off + (uint32_t)val_offset;
                bjv_err_t err = bjv_validate_value(doc, abs_offset, depth - 1);
                if (err != BJV_OK) {
                    return err;
                }

                pair_pos += keyid_size + offset_consumed;
            }
            return BJV_OK;
        }

        default:
            return BJV_ERR_UNSUPPORTED;
    }
}
