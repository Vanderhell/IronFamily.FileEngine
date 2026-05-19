# Trust Store Schema v1 (.ironupd_trust.json)

## Contract

File: `.ironupd_trust.json` (per-target local storage)

**Canonical JSON (deterministic):**
```json
{
  "version": 1,
  "keys": [
    {
      "key_id": "6c31041268f471609c79f5f2dbcc38e4",
      "pub": "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a",
      "comment": "optional string"
    }
  ],
  "revoked": ["abc123..."]
}
```

**Determinism Rules:**
- Field order: version, keys, revoked (stable)
- key_id: lowercase hex, 32 chars (first16(BLAKE3(pub32)))
- pub: lowercase hex, 64 chars (full 32-byte pubkey)
- comment: optional string (no newlines)
- keys: sorted by key_id ascending
- revoked: sorted by key_id ascending (also 32 chars hex)
- No timestamps, no randomness
- Atomic writes (temp file + rename)

**Validation:**
- version == 1 required
- All hex fields validated (length, chars)
- No duplicate key_id
- No duplicate in revoked
- No key_id in both keys and revoked simultaneously (revoked is shadow list)

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
