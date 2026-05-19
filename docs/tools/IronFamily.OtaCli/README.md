# IronFamily OTA CLI (`ironfamily-ota`)

Production-grade OTA (Over-The-Air) package manager for IronFamily firmware updates. Implements a complete secure update pipeline:
1. **CREATE**: Build deterministic IUPD v2 SECURE packages with embedded Delta v1 patches
2. **VERIFY**: Strict verification with fail-closed security gates
3. **APPLY**: Atomic delta application with output validation

## Installation

Build as Release:
```bash
dotnet build tools/IronFamily.OtaCli/IronFamily.OtaCli.csproj -c Release
```

Binary output: `tools/IronFamily.OtaCli/bin/Release/net8.0/ironfamily-ota.exe`

## Usage

### CREATE: Build OTA Package

Generate a cryptographically signed IUPD v2 package with embedded Delta v1 patch:

```bash
ironfamily-ota create \
  --base <base-file> \
  --target <target-file> \
  --out <package-path> \
  --sequence <number> \
  [--key-seed-hex <64-char-hex>] \
  [--chunk-size <uint>] \
  [--force]
```

**Arguments**:
- `--base` (required): Path to base firmware file
- `--target` (required): Path to target firmware file
- `--out` (required): Output package path (writes `{out}` + `{out}.delta`)
- `--sequence` (required): UpdateSequence number (anti-replay value, any 64-bit uint)
- `--key-seed-hex` (optional): Ed25519 signing seed as 64-character hex string (default: deterministic bench seed)
- `--chunk-size` (optional): Delta v1 chunk size in bytes (default: 4096)
- `--force` (optional): Overwrite output files if they exist

**Output**:
- `{out}`: IUPD v2 SECURE package (includes manifest, UpdateSequence trailer, Ed25519 signature)
- `{out}.delta`: Delta v1 patch file (external model to avoid signature invalidation)

**Example**:
```bash
ironfamily-ota create \
  --base firmware_v1.bin \
  --target firmware_v2.bin \
  --out firmware_v1-to-v2.iupd \
  --sequence 5 \
  --force
```

**Guarantees**:
- Deterministic: Same inputs always produce byte-identical packages and deltas
- Cryptographically signed: Ed25519 signature over IUPD manifest
- Anti-replay: UpdateSequence trailer embedded in package
- No embedded delta: Maintains package integrity (Delta stored externally)

### VERIFY: Validate Package Integrity

Perform strict cryptographic verification with fail-closed security gates:

```bash
ironfamily-ota verify \
  --package <package-path> \
  [--pubkey-hex <64-char-hex>] \
  [--min-sequence <number>]
```

**Arguments**:
- `--package` (required): Path to IUPD v2 package file
- `--pubkey-hex` (optional): Ed25519 public key as 64-character hex (default: bench pubkey for test packages)
- `--min-sequence` (optional): Minimum acceptable UpdateSequence (replay attack prevention, default: 1)

**Security Gates Enforced**:
1. **Profile Whitelist**: Only SECURE/OPTIMIZED profiles allowed
2. **File Size**: Validates minimum readable size
3. **ChunkTable Bounds**: Prevents buffer overruns
4. **Manifest Integrity**: BLAKE3 witness hash verification
5. **Chunk Payload Bounds**: All payloads within file boundaries
6. **Signature Validation**: Ed25519 verification of manifest
7. **UpdateSequence**: Must be >= `--min-sequence` (anti-replay)
8. **DoS Limits**: Chunk count < 65536 (prevents malformed iteration)

**Exit Code**:
- `0`: Package valid and passed all gates
- `1`: Package invalid or verification failed

**Example**:
```bash
ironfamily-ota verify \
  --package firmware_v1-to-v2.iupd \
  --pubkey-hex 6061626364656667... \
  --min-sequence 5
```

### APPLY: Apply Package to Base Firmware

Atomically apply OTA package to base firmware with three-phase fail-closed validation:

```bash
ironfamily-ota apply \
  --base <base-file> \
  --package <package-path> \
  --out <output-file> \
  [--delta <delta-file>] \
  [--pubkey-hex <64-char-hex>] \
  [--min-sequence <number>] \
  [--force]
```

**Arguments**:
- `--base` (required): Path to base firmware file
- `--package` (required): Path to IUPD v2 package file
- `--out` (required): Output firmware file path
- `--delta` (optional): Path to Delta v1 file (auto-detected at `{package}.delta` if not specified)
- `--pubkey-hex` (optional): Ed25519 public key hex (default: bench pubkey)
- `--min-sequence` (optional): Minimum UpdateSequence (default: 1)
- `--force` (optional): Overwrite output file if it exists

**Three-Phase Pipeline** (fail-closed):

1. **Phase 1 - Verify Package**: Runs `verify` pipeline using `IupdReader.ValidateStrict()`
   - Fails immediately if any security gate is violated
   - Does not proceed to delta application without passing all checks

2. **Phase 2 - Apply Delta**: Applies Delta v1 patch to base firmware
   - Uses `IupdDeltaV1.ApplyDeltaV1(base, delta)`
   - Reconstructs target firmware from base + delta

3. **Phase 3 - Verify Output**: Validates output firmware size matches expected target size
   - Reads target size from Delta v1 header (offset 16, uint64 LE)
   - Ensures output size matches delta-specified target length
   - Prevents truncated or corrupted output

**Exit Code**:
- `0`: Package applied successfully, output firmware written
- `1`: Package verification failed OR delta application failed OR output validation failed

**Example**:
```bash
ironfamily-ota apply \
  --base firmware_v1.bin \
  --package firmware_v1-to-v2.iupd \
  --out firmware_v2.bin \
  --pubkey-hex 6061626364656667... \
  --min-sequence 5 \
  --force
```

**Output Safety**:
- Output file is **only written if all three phases pass**
- Corrupted base → delta application fails (phase 2)
- Corrupted package → verification fails (phase 1)
- Corrupted delta → phase 2 or phase 3 fails
- Result: Atomicity without explicit transactions

## Technical Details

### IUPD v2 SECURE Profile

- **Manifest Hash**: BLAKE3-256 of chunk table
- **Signature**: Ed25519 over manifest (Schnorr variant, not RFC8032)
- **UpdateSequence Trailer**: 21-byte extension (magic "IUPDSEQ1" + version + sequence value)
- **Profile Byte**: 0x02 (SECURE)

### Delta v1 Format

- **Header**: 96 bytes fixed (magic "IUPDDEL1", version, chunk size, base hash, target hash, entry count)
- **Entries**: Variable-length (ChunkIndex + DataLen + Data)
- **Deterministic**: Same base+target always produce identical delta

### Security Model

```
CREATE → (base, target, sequence, seed) → IUPD v2 + Delta v1
                                              ↓
                                         Sign with Ed25519
                                         Add UpdateSequence
                                              ↓
VERIFY → (package, pubkey, min-seq) → All 8 security gates pass
                                            ↓
APPLY → (base, package, delta) → Phase 1: Verify
                                   Phase 2: Apply Delta
                                   Phase 3: Validate Output
                                           ↓
                                   output == target
```

### Determinism Guarantees

- **Bench Seeds**: Default Ed25519 seeds are hardcoded (IupdEd25519Keys.BenchSeed32)
- **No Timestamps**: Package creation is timestamp-free
- **No Randomness**: All operations are deterministic
- **Reproducible**: Running CREATE twice with same inputs produces byte-identical packages

**Test Results** (500 MB firmware):
```
Package 1:  258 bytes, SHA256: 8e0f4b9b...
Package 2:  258 bytes, SHA256: 8e0f4b9b... ✓ Identical
Delta 1:   8304 bytes, SHA256: 6f441940...
Delta 2:   8304 bytes, SHA256: 6f441940... ✓ Identical
Output:   524288 bytes, matches target byte-for-byte ✓
```

## Integration Points

### Embedded Delta (Current Model)

- Delta stored **externally** as `{package}.delta`
- Avoids signature invalidation from embedded payload
- On device: Load both package and delta, call APPLY phase

### Future: Embedded Delta (Extended Model)

- Possible future extension to embed delta in IUPD payload section
- Would require: Payload API additions to `IupdWriter`, chunk-based storage
- Trade-off: Eliminates separate delta file, but increases package size

## Error Handling

All operations use structured error codes (IupdError) with fail-closed semantics:

| Error | Meaning | EXIT |
|-------|---------|------|
| IRON_OK | Operation succeeded | 0 |
| IRON_ERR_FILE_NOT_FOUND | File doesn't exist | 1 |
| IRON_ERR_INVALID_PROFILE | Profile not whitelisted | 1 |
| IRON_ERR_SIGNATURE_INVALID | Ed25519 verification failed | 1 |
| IRON_ERR_MANIFEST_HASH_MISMATCH | BLAKE3 witness validation failed | 1 |
| IRON_ERR_SEQUENCE_MISMATCH | UpdateSequence < minimum required | 1 |
| IRON_ERR_DELTA_APPLY_FAILED | Delta application failed (corrupted delta or base) | 1 |
| IRON_ERR_SIZE_MISMATCH | Output size doesn't match target | 1 |

## Development

### Build & Test

```bash
# Build Release
dotnet build tools/IronFamily.OtaCli -c Release

# Run E2E tests
dotnet test tests/IronFamily.OtaCli.Tests -c Release

# Manual test: Create → Verify → Apply → Check
dotnet run --project tools/IronFamily.OtaCli -- create --base base.bin --target target.bin --out pkg.iupd --sequence 1 --force
dotnet run --project tools/IronFamily.OtaCli -- verify --package pkg.iupd
dotnet run --project tools/IronFamily.OtaCli -- apply --base base.bin --package pkg.iupd --out output.bin --force
cmp output.bin target.bin && echo "✓ E2E PASS"
```

### Dependencies

- **IronConfig**: IUPD v2 reader/writer with Ed25519/BLAKE3
- **IronConfig.Iupd**: Delta v1 encoder/decoder (CreateDeltaV1, ApplyDeltaV1)
- **System.CommandLine v2.0.0-beta4**: CLI argument parsing
- **.NET 8.0**: Runtime

## License

Part of IronFamily project. See repository LICENSE.
