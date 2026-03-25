/* IRONCFG C99 Deterministic Encoder */

#include "ironcfg/ironcfg_encode.h"
#include <string.h>
#include <math.h>

/* CRC32 lookup table (IEEE 802.3) */
static uint32_t crc32_table[256];
static int crc32_table_initialized = 0;

/* Initialize CRC32 table */
static void crc32_init_table(void) {
    if (crc32_table_initialized) return;

    for (int i = 0; i < 256; i++) {
        uint32_t crc = i;
        for (int j = 0; j < 8; j++) {
            crc = (crc >> 1) ^ (0xEDB88320 & (-(crc & 1)));
        }
        crc32_table[i] = crc;
    }
    crc32_table_initialized = 1;
}

/* Compute CRC32 */
static uint32_t crc32_compute(const uint8_t *data, size_t len) {
    crc32_init_table();
    uint32_t crc = 0xFFFFFFFF;
    for (size_t i = 0; i < len; i++) {
        crc = crc32_table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
    }
    return crc ^ 0xFFFFFFFF;
}

/* Write uint32 little-endian */
static void write_le32(uint8_t *buf, uint32_t val) {
    buf[0] = (val >> 0) & 0xFF;
    buf[1] = (val >> 8) & 0xFF;
    buf[2] = (val >> 16) & 0xFF;
    buf[3] = (val >> 24) & 0xFF;
}

/* Write uint16 little-endian */
static void write_le16(uint8_t *buf, uint16_t val) {
    buf[0] = (val >> 0) & 0xFF;
    buf[1] = (val >> 8) & 0xFF;
}

/* Write uint64 little-endian */
static void write_le64(uint8_t *buf, uint64_t val) {
    buf[0] = (val >> 0) & 0xFF;
    buf[1] = (val >> 8) & 0xFF;
    buf[2] = (val >> 16) & 0xFF;
    buf[3] = (val >> 24) & 0xFF;
    buf[4] = (val >> 32) & 0xFF;
    buf[5] = (val >> 40) & 0xFF;
    buf[6] = (val >> 48) & 0xFF;
    buf[7] = (val >> 56) & 0xFF;
}

/* Check if float is NaN */
bool ironcfg_is_nan(double value) {
    return value != value;
}

/* Normalize float (-0.0 -> +0.0) */
double ironcfg_normalize_float(double value) {
    if (value == 0.0 && signbit(value)) {
        return 0.0;
    }
    return value;
}

/* Calculate VarUInt size */
uint32_t ironcfg_varuint_size(uint64_t value) {
    if (value < 128) return 1;
    if (value < 16384) return 2;
    if (value < 2097152) return 3;
    if (value < 268435456) return 4;
    return 5;
}

/* Encode VarUInt */
void ironcfg_encode_varuint(uint8_t *buffer, size_t offset, uint64_t value, uint32_t *out_size) {
    size_t pos = offset;
    while (value >= 128) {
        buffer[pos++] = (value & 0x7F) | 0x80;
        value >>= 7;
    }
    buffer[pos++] = value & 0x7F;
    *out_size = pos - offset;
}

/* Encode boolean value */
static ironcfg_error_t encode_bool(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    if (ctx->offset + 1 > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }
    ctx->buffer[ctx->offset++] = val->data.bool_val.value ? 0x02 : 0x01;
    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Encode i64 value */
static ironcfg_error_t encode_i64(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    if (ctx->offset + 9 > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }
    ctx->buffer[ctx->offset++] = 0x10;
    write_le64(&ctx->buffer[ctx->offset], (uint64_t)val->data.i64_val.value);
    ctx->offset += 8;
    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Encode u64 value */
static ironcfg_error_t encode_u64(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    if (ctx->offset + 9 > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }
    ctx->buffer[ctx->offset++] = 0x11;
    write_le64(&ctx->buffer[ctx->offset], val->data.u64_val.value);
    ctx->offset += 8;
    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Encode f64 value */
static ironcfg_error_t encode_f64(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    if (ctx->offset + 9 > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }

    double value = val->data.f64_val.value;

    /* Check for NaN */
    if (ironcfg_is_nan(value)) {
        return (ironcfg_error_t){IRONCFG_INVALID_FLOAT, ctx->offset};
    }

    /* Normalize -0.0 to +0.0 */
    value = ironcfg_normalize_float(value);

    ctx->buffer[ctx->offset++] = 0x12;
    uint8_t *bytes = (uint8_t *)&value;
    memcpy(&ctx->buffer[ctx->offset], bytes, 8);
    ctx->offset += 8;
    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Forward declaration */
static ironcfg_error_t encode_value(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val);

/* Encode string value (inline) */
static ironcfg_error_t encode_string(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    const uint32_t len = val->data.string_val.len;

    if (ctx->offset + 1 + 5 + len > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }

    ctx->buffer[ctx->offset++] = 0x20;
    uint32_t varuint_size;
    ironcfg_encode_varuint(ctx->buffer, ctx->offset, len, &varuint_size);
    ctx->offset += varuint_size;

    memcpy(&ctx->buffer[ctx->offset], val->data.string_val.data, len);
    ctx->offset += len;
    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Encode bytes value */
static ironcfg_error_t encode_bytes(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    const uint32_t len = val->data.bytes_val.len;

    if (ctx->offset + 1 + 5 + len > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }

    ctx->buffer[ctx->offset++] = 0x22;
    uint32_t varuint_size;
    ironcfg_encode_varuint(ctx->buffer, ctx->offset, len, &varuint_size);
    ctx->offset += varuint_size;

    memcpy(&ctx->buffer[ctx->offset], val->data.bytes_val.data, len);
    ctx->offset += len;
    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Encode array value */
static ironcfg_error_t encode_array(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    const uint32_t count = val->data.array_val.element_count;

    if (ctx->offset + 1 + 5 > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }

    ctx->buffer[ctx->offset++] = 0x30;
    uint32_t varuint_size;
    ironcfg_encode_varuint(ctx->buffer, ctx->offset, count, &varuint_size);
    ctx->offset += varuint_size;

    for (uint32_t i = 0; i < count; i++) {
        ironcfg_error_t err = encode_value(ctx, &val->data.array_val.elements[i]);
        if (err.code != IRONCFG_OK) return err;
    }

    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Encode object value */
static ironcfg_error_t encode_object(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    const uint32_t field_count = val->data.object_val.field_count;

    if (ctx->offset + 1 + 5 > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }

    ctx->buffer[ctx->offset++] = 0x40;
    uint32_t varuint_size;
    ironcfg_encode_varuint(ctx->buffer, ctx->offset, field_count, &varuint_size);
    ctx->offset += varuint_size;

    /* Fields must be in ascending fieldId order */
    for (uint32_t i = 0; i < field_count; i++) {
        uint32_t field_id = val->data.object_val.schema->fields[i].field_id;

        if (ctx->offset + 5 > ctx->buffer_size) {
            return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
        }

        ironcfg_encode_varuint(ctx->buffer, ctx->offset, field_id, &varuint_size);
        ctx->offset += varuint_size;

        ironcfg_error_t err = encode_value(ctx, &val->data.object_val.field_values[i]);
        if (!err.code == IRONCFG_OK) return err;
    }

    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Encode generic value (definition) */
static ironcfg_error_t encode_value(ironcfg_encode_ctx_t *ctx, const ironcfg_value_t *val) {
    if (!val) {
        return (ironcfg_error_t){IRONCFG_INVALID_SCHEMA, ctx->offset};
    }

    switch (val->type) {
        case IRONCFG_VAL_NULL:
            if (ctx->offset >= ctx->buffer_size) {
                return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
            }
            ctx->buffer[ctx->offset++] = 0x00;
            return (ironcfg_error_t){IRONCFG_OK, 0};

        case IRONCFG_VAL_BOOL:
            return encode_bool(ctx, val);

        case IRONCFG_VAL_I64:
            return encode_i64(ctx, val);

        case IRONCFG_VAL_U64:
            return encode_u64(ctx, val);

        case IRONCFG_VAL_F64:
            return encode_f64(ctx, val);

        case IRONCFG_VAL_STRING:
            return encode_string(ctx, val);

        case IRONCFG_VAL_BYTES:
            return encode_bytes(ctx, val);

        case IRONCFG_VAL_ARRAY:
            return encode_array(ctx, val);

        case IRONCFG_VAL_OBJECT:
            return encode_object(ctx, val);

        default:
            return (ironcfg_error_t){IRONCFG_INVALID_SCHEMA, ctx->offset};
    }
}

/* Encode schema block */
static ironcfg_error_t encode_schema(ironcfg_encode_ctx_t *ctx, ironcfg_schema_t *schema) {
    uint32_t varuint_size;

    if (ctx->offset + 5 > ctx->buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
    }

    /* Write field count */
    ironcfg_encode_varuint(ctx->buffer, ctx->offset, schema->field_count, &varuint_size);
    ctx->offset += varuint_size;

    for (uint32_t i = 0; i < schema->field_count; i++) {
        const ironcfg_field_def_t *field = &schema->fields[i];

        /* Write fieldId */
        if (ctx->offset + 5 > ctx->buffer_size) {
            return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
        }
        ironcfg_encode_varuint(ctx->buffer, ctx->offset, field->field_id, &varuint_size);
        ctx->offset += varuint_size;

        /* Write fieldNameLen */
        if (ctx->offset + 5 > ctx->buffer_size) {
            return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
        }
        ironcfg_encode_varuint(ctx->buffer, ctx->offset, field->field_name_len, &varuint_size);
        ctx->offset += varuint_size;

        /* Write fieldName */
        if (ctx->offset + field->field_name_len > ctx->buffer_size) {
            return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
        }
        memcpy(&ctx->buffer[ctx->offset], field->field_name, field->field_name_len);
        ctx->offset += field->field_name_len;

        /* Write fieldType */
        if (ctx->offset + 1 > ctx->buffer_size) {
            return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
        }
        ctx->buffer[ctx->offset++] = field->field_type;

        /* Write isRequired */
        if (ctx->offset + 1 > ctx->buffer_size) {
            return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, ctx->offset};
        }
        ctx->buffer[ctx->offset++] = field->is_required;
    }

    return (ironcfg_error_t){IRONCFG_OK, 0};
}

/* Main encode function */
ironcfg_error_t ironcfg_encode(
    const ironcfg_value_t *root,
    ironcfg_schema_t *schema,
    bool compute_crc32,
    bool compute_blake3,
    uint8_t *out_buffer,
    size_t buffer_size,
    size_t *out_encoded_size)
{
    ironcfg_encode_ctx_t ctx = {0};
    ctx.buffer = out_buffer;
    ctx.buffer_size = buffer_size;
    ctx.offset = 64; /* Skip header */
    ctx.has_crc32 = compute_crc32;
    ctx.has_blake3 = compute_blake3;

    if (buffer_size < 64) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, 0};
    }

    /* Encode schema */
    uint32_t schema_offset = ctx.offset;
    ironcfg_error_t err = encode_schema(&ctx, schema);
    if (err.code != IRONCFG_OK) return err;
    uint32_t schema_size = ctx.offset - schema_offset;

    /* Encode data */
    uint32_t data_offset = ctx.offset;
    err = encode_value(&ctx, root);
    if (err.code != IRONCFG_OK) return err;
    uint32_t data_size = ctx.offset - data_offset;

    /* Calculate CRC and BLAKE3 */
    uint32_t crc_offset = 0;
    uint32_t blake3_offset = 0;
    uint8_t flags = 0;

    if (compute_crc32) {
        flags |= 0x01;
        crc_offset = ctx.offset;
        ctx.offset += 4;
    }

    if (compute_blake3) {
        flags |= 0x02;
        blake3_offset = ctx.offset;
        ctx.offset += 32;
    }

    uint32_t file_size = ctx.offset;

    if (file_size > buffer_size) {
        return (ironcfg_error_t){IRONCFG_BOUNDS_VIOLATION, 0};
    }

    /* Write header */
    memset(out_buffer, 0, 64);
    write_le32(&out_buffer[0], IRONCFG_MAGIC);
    out_buffer[4] = IRONCFG_VERSION;
    out_buffer[5] = flags;
    write_le32(&out_buffer[8], file_size);
    write_le32(&out_buffer[12], schema_offset);
    write_le32(&out_buffer[16], schema_size);
    write_le32(&out_buffer[20], 0); /* stringPoolOffset */
    write_le32(&out_buffer[24], 0); /* stringPoolSize */
    write_le32(&out_buffer[28], data_offset);
    write_le32(&out_buffer[32], data_size);
    write_le32(&out_buffer[36], crc_offset);
    write_le32(&out_buffer[40], blake3_offset);

    /* Compute and write CRC32 */
    if (compute_crc32) {
        uint32_t crc = crc32_compute(out_buffer, crc_offset);
        write_le32(&out_buffer[crc_offset], crc);
    }

    /* BLAKE3 would require additional library; skip for now */

    *out_encoded_size = file_size;
    return (ironcfg_error_t){IRONCFG_OK, 0};
}
