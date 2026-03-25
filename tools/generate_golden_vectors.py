#!/usr/bin/env python3
"""Generate correct ICFG golden vectors for testing"""

import struct
import os

def crc32_ieee(data):
    """Compute CRC32 IEEE 802.3"""
    crc = 0xFFFFFFFF
    for byte in data:
        crc = crc ^ byte
        for _ in range(8):
            if crc & 1:
                crc = (crc >> 1) ^ 0xEDB88320
            else:
                crc = crc >> 1
    return crc ^ 0xFFFFFFFF

def encode_varuint32(value):
    """Encode a uint32 as VarUInt"""
    result = bytearray()
    while value >= 0x80:
        result.append((value & 0x7F) | 0x80)
        value >>= 7
    result.append(value & 0x7F)
    return bytes(result)

def build_icfg_file(data_block, schema_block=None, compute_crc32=False):
    """Build a complete ICFG file"""
    magic = 0x47464349  # "ICFG"
    version = 0x0101    # Version 1.1

    if schema_block is None:
        # Schema block must have at least field count (minimal: 0 fields = 0x00)
        schema_block = b'\x00'  # Field count = 0

    # Block order: Header (64) -> Schema -> String pool (optional) -> Data -> CRC32 (optional)
    schema_offset = 64
    schema_size = len(schema_block)

    string_pool_offset = 0
    string_pool_size = 0

    # Data block comes after schema/pool
    data_offset = schema_offset + schema_size
    data_size = len(data_block)

    # File size includes CRC32 if present
    file_size = data_offset + data_size
    crc_offset = 0
    if compute_crc32:
        crc_offset = file_size
        file_size += 4

    # Build header (64 bytes)
    flags = 0x01 if compute_crc32 else 0x00
    header = bytearray(64)

    # Offsets 0-3: Magic
    struct.pack_into('<I', header, 0, magic)
    # Offset 4: Version
    header[4] = version & 0xFF
    # Offset 5: Flags
    header[5] = flags
    # Offsets 6-7: Reserved0
    struct.pack_into('<H', header, 6, 0)
    # Offsets 8-11: File size
    struct.pack_into('<I', header, 8, file_size)
    # Offsets 12-15: Schema offset
    struct.pack_into('<I', header, 12, schema_offset)
    # Offsets 16-19: Schema size
    struct.pack_into('<I', header, 16, schema_size)
    # Offsets 20-23: String pool offset
    struct.pack_into('<I', header, 20, string_pool_offset)
    # Offsets 24-27: String pool size
    struct.pack_into('<I', header, 24, string_pool_size)
    # Offsets 28-31: Data offset
    struct.pack_into('<I', header, 28, data_offset)
    # Offsets 32-35: Data size
    struct.pack_into('<I', header, 32, data_size)
    # Offsets 36-39: CRC32 offset (0 if no CRC32)
    struct.pack_into('<I', header, 36, crc_offset)
    # Offsets 40-43: BLAKE3 offset (0, not used)
    struct.pack_into('<I', header, 40, 0)
    # Offsets 44-47: Reserved1
    struct.pack_into('<I', header, 44, 0)
    # Offsets 48-63: Reserved2 (already zeroed)

    # Build file in block order: Header -> Schema -> String pool (empty) -> Data -> CRC32 (optional)
    file_data = bytes(header) + schema_block + data_block

    # Add CRC32 if requested (CRC32 is computed over header + data + schema, not over itself)
    if compute_crc32:
        crc = crc32_ieee(file_data)
        file_data = file_data + struct.pack('<I', crc)

    return file_data

# Generate vectors
output_dir = "artifacts/vectors/v1/icfg"
os.makedirs(output_dir, exist_ok=True)

# Vector 1: Minimal (empty object)
data = b'\x40\x00'  # Type 0x40 (object), field count 0
vector1 = build_icfg_file(data, compute_crc32=False)
with open(f"{output_dir}/01_minimal.bin", "wb") as f:
    f.write(vector1)
print("OK 01_minimal.bin ({0} bytes)".format(len(vector1)))

# Vector 2: Single int64 field
data = b'\x40\x01'  # Object with 1 field
data += encode_varuint32(0)  # Field ID = 0
data += b'\x10'  # Type code: int64
data += struct.pack('<q', 42)  # Value: 42
vector2 = build_icfg_file(data, compute_crc32=False)
with open(f"{output_dir}/02_single_int.bin", "wb") as f:
    f.write(vector2)
print("OK 02_single_int.bin ({0} bytes)".format(len(vector2)))

# Vector 3: Multiple fields
data = b'\x40\x02'  # Object with 2 fields
data += encode_varuint32(0)  # Field ID = 0
data += b'\x10'  # Type code: int64
data += struct.pack('<q', -100)  # Value: -100
data += encode_varuint32(1)  # Field ID = 1
data += b'\x11'  # Type code: uint64
data += struct.pack('<Q', 999)  # Value: 999
vector3 = build_icfg_file(data, compute_crc32=False)
with open(f"{output_dir}/03_multi_field.bin", "wb") as f:
    f.write(vector3)
print("OK 03_multi_field.bin ({0} bytes)".format(len(vector3)))

print("\nGolden vectors generated successfully")
