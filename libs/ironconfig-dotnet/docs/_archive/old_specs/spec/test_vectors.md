> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# BJV/BJX Test Vectors (Golden)

This document describes the canonical golden test vectors for validating BJV/BJX implementations.

## Base Configuration (golden_config.json)

All golden vectors use this configuration as source (unless noted):

```json
{
  "enabled": true,
  "ip": "192.168.0.10",
  "name": "PLC-01",
  "rack": 0,
  "slot": 1,
  "tags": ["lineA", "press"],
  "timeout_ms": 1500
}
```

Dictionary keys (lexicographically sorted):
- 0: "enabled"
- 1: "ip"
- 2: "name"
- 3: "rack"
- 4: "slot"
- 5: "tags"
- 6: "timeout_ms"

## Test Vectors

### golden_bjv2_crc.bjv (157 bytes)

**Configuration:**
- Format: BJV v2 (16-bit keyIds)
- CRC32: Present (flags = 0x03)
- VSP: Absent
- Encoding: Canonical

**Expected Properties:**
- Header[0:4]: "BJV2"
- Header[4]: flags = 0x03 (canonical | CRC)
- Root type: Object (0x40)
- Root fields: 6 fields
- Dictionary: 7 keys, lexicographically sorted

### golden_bjv4_crc.bjv (171 bytes)

**Configuration:**
- Format: BJV v4 (32-bit keyIds)
- CRC32: Present
- VSP: Absent
- Encoding: Canonical

**Expected Properties:**
- Header[0:4]: "BJV4"
- Header[4]: flags = 0x03
- Same logical structure as BJV2 but with 32-bit keyIds
- File size 14 bytes larger due to 4-byte keyIds instead of 2-byte

### repeat_bjv2_crc_vsp.bjv (190 bytes)

**Configuration:**
- Format: BJV v2 (16-bit keyIds)
- CRC32: Present
- VSP: Present (variable string pool for repeated strings)
- Encoding: Canonical

**Expected Properties:**
- Header[4]: flags = 0x07 (canonical | CRC | VSP)
- Dictionary: 10 keys
- VSP: 3 strings ("server", "service", "prod")
- String deduplication through StringId (0x22) type

### golden_bjx1_password.bjx (233 bytes)

**Configuration:**
- Source: golden_bjv2_crc.bjv (encrypted)
- Password: "test123"
- KDF: PBKDF2-HMAC-SHA256
- Cipher: AES-256-GCM
- Format: BJX v1

**Expected Properties:**
- Header[0:4]: "BJX1"
- Header[4]: flags = 0x01 (PBKDF2-HMAC-SHA256 + AES-GCM)
- Decryption with password "test123" must yield golden_bjv2_crc.bjv bytes

## Validation Rules

### Must Pass
1. **Parse & Validate**: No errors, all values accessible
2. **CRC Check**: CRC32 correct when present
3. **Canonical Roundtrip**: Decode â†’ re-encode with same flags â†’ bytes identical
4. **JSON Equivalence**: tojson output matches source JSON (order-insensitive)

### Must NOT Pass (Negative Tests)
- Bad magic bytes
- Non-minimal VarUInt
- Unsorted dictionary
- Duplicate keyIds in objects
- NaN or -0.0 in floats
- Invalid type codes
- Truncated values

## Test Location

Golden vectors: `vectors/small/`
- `golden_config.json`
- `golden_bjv2_crc.bjv`
- `golden_bjv4_crc.bjv`
- `repeat_bjv2_crc_vsp.bjv`
- `golden_bjx1_password.bjx`

All golden vectors are immutable reference implementations committed to git.
