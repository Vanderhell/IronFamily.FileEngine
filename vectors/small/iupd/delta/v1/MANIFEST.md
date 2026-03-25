# IUPD DELTA v1 Test Vectors

## Case 01
- base.bin: 4096 bytes (deterministic pattern: i * 0x47)
- target.bin: 4096 bytes (3 bytes changed at offsets 100, 500, 2000)
- delta.iupd.delta: Generated via CreateDeltaV1
- expected_hash.txt: BLAKE3-256 of target

## Case 02  
- base.bin: 65536 bytes (deterministic pattern: i * 0x89)
- target.bin: 65536 bytes (5 bytes changed at offsets 0, 16384, 32768, 49152, 65520)
- delta.iupd.delta: Generated via CreateDeltaV1
- expected_hash.txt: BLAKE3-256 of target

All vectors are deterministic (byte-identical across runs with same inputs).
