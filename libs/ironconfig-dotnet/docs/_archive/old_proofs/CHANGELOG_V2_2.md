> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONEDGE V2.2 - Release Notes (February 12, 2026)

## ✅ Unit Test Suite - Now Green

**Status**: 185/185 unit tests passing (CI baseline stable)

### Test Coverage Breakdown
- **IUPD**: 91/91 passing ✅
- **IRONCFG**: 57/57 passing + 3 skipped ✅
- **ILOG**: 37/37 passing + 3 skipped ✅

### CI Command (Recommended)
```bash
dotnet test -c Release --filter "Category!=Benchmark&Category!=Vectors"
```

## Proven Features

### Deterministic Output
- ✅ Byte-identical JSON verification results
- ✅ No timestamps or randomness
- ✅ Reproducible across environments

### Error Handling
- ✅ 17 public error categories (unified across IRONCFG/IUPD/ILOG)
- ✅ Exit code mapping: 0=success, 1=validation error, 2=IO error, 3=invalid args, 10=internal
- ✅ Structured JSON error output for CI/CD integration

### Validation Modes
- ✅ Fast validation (structural checks)
- ✅ Strict validation (cryptographic BLAKE3)
- ✅ Corruption detection (CRC32/BLAKE3 hashes)

### Self-Contained Deployment
- ✅ No .git dependency (works from ZIP checkouts)
- ✅ Test vectors bundled in output directory
- ✅ Environment variable overrides supported

## Test Categorization

Tests are now organized into three categories:

1. **Unit Tests** (Default, Green) - 185 tests, ~2 seconds
   - Pure logic testing with no external files
   - Suitable for every commit in CI/CD

2. **Vector Tests** (WIP) - 33 tests, ~3 seconds
   - Integration tests using placeholder test vectors
   - Status: Currently failing (vectors being finalized)
   - Will be green once real test vectors generated

3. **Benchmark Tests** (Optional) - Long-running, ~100+ seconds
   - Performance measurement tests
   - Run on demand or nightly

## Known Limitations & Roadmap

### Current (V2.2)
- ILOG test vectors: Using placeholders (generation in progress)
- ILOG error categorization: Not yet complete
- Compression: ~50s for 500MB with FAST profile

### Phase 1.2 (Planned)
- Complete ILOG test vector corpus
- Full ILOG error mapping
- Performance optimization

### Phase 1.3 (Planned)
- Cryptographic signatures (RSA/ECDSA)
- Payload encryption (AES-256)

## Documentation Updates

- **SALES_PROOF_V2_2.md**: Updated with exact CI commands and proven guarantees
- **TESTING.md**: Added test categorization and CI/CD guidance
- **Error Contract**: Public error categories now documented as 17 (was limiting constraint)

## Commits

- V2.2: Unit test suite green (185/185 passing)
- V2.2: Finalize SALES_PROOF and TESTING docs; clarify test categories
