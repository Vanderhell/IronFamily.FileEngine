# MegaBench Implementation TODO

## PHASE 2 — Dataset Generators (Deterministic) ✅ COMPLETE

### IRONCFG Datasets
- [x] Create `Datasets/IronCfg/Generator.cs`
  - [x] 1KB minimal fixture
  - [x] 10KB typical fixture
  - [x] 100KB complex fixture
  - [x] 1MB large fixture
  - [x] 10MB stress fixture
  - [x] All use fixed seed when `IRONFAMILY_DETERMINISTIC=1`

### ILOG Datasets
- [x] Create `Datasets/ILog/Generator.cs`
  - [x] 10KB event stream (~100 events)
  - [x] 100KB event stream (~1000 events)
  - [x] 1MB event stream (~10000 events)
  - [x] 10MB event stream (~100000 events)
  - [x] 100MB event stream (~1000000 events, heavy mode)
  - [x] Deterministic event generation

### IUPD Datasets
- [x] Create `Datasets/IUpd/Generator.cs`
  - [x] Base v1 binary blobs (10KB, 100KB, 1MB, 10MB)
  - [x] Updated v2 variants
  - [x] Change rates: 1%, 10%, 50%, reorder
  - [x] All deterministic

---

## PHASE 3 — Bench Harness Implementation

### Core
- [ ] Argument parsing (--engine, --profile, --dataset, --format)
- [ ] Dataset loading/generation
- [ ] Metric collection framework
- [ ] Results JSON serialization
- [ ] Report markdown generation

### Competitor Implementations
- [ ] **IRONCFG**:
  - [ ] `Competitors/IronCfg/IcfgBench.cs` (baseline)
  - [ ] `Competitors/IronCfg/ProtobufBench.cs` (TODO: needs protobuf-net)
  - [ ] `Competitors/IronCfg/FlatBuffersBench.cs` (TODO: needs FlatSharp)
  - [ ] `Competitors/IronCfg/CapnProtoBench.cs` (TODO: needs Cap'n Proto)
  - [ ] `Competitors/IronCfg/MessagePackBench.cs` (TODO: needs MessagePack)
  - [ ] `Competitors/IronCfg/CborBench.cs` (TODO: needs CBOR)
  - [ ] `Competitors/IronCfg/JsonRoundtripValidator.cs`

- [ ] **ILOG**:
  - [ ] `Competitors/ILog/IlogBench.cs` (baseline for each profile)
  - [ ] `Competitors/ILog/ProtobufDelimitedBench.cs`
  - [ ] `Competitors/ILog/MessagePackBench.cs`
  - [ ] `Competitors/ILog/CborBench.cs`
  - [ ] `Competitors/ILog/SqliteBench.cs` (for SEARCHABLE)
  - [ ] `Competitors/ILog/TarBench.cs` (for ARCHIVED)

- [ ] **IUPD**:
  - [ ] `Competitors/IUpd/IupdBench.cs` (baseline for each profile)
  - [ ] `Competitors/IUpd/TarBench.cs` (uncompressed)
  - [ ] `Competitors/IUpd/TarLz4Bench.cs`
  - [ ] `Competitors/IUpd/TarZstdBench.cs`
  - [ ] `Competitors/IUpd/XdeltaBench.cs`
  - [ ] `Competitors/IUpd/BsdiffBench.cs`

### Metrics Collection
- [ ] Encode time (ms)
- [ ] Decode time (ms)
- [ ] Output size (bytes)
- [ ] Allocated memory (bytes)
- [ ] GC collection count
- [ ] Validate time (ms, where applicable)
- [ ] Query time (ms, ILOG SEARCHABLE)
- [ ] Delta size (bytes, IUPD DELTA)
- [ ] Apply time (ms, IUPD DELTA)

### Report Generation
- [ ] Generate `results.json` with all metrics
- [ ] Generate `REPORT.md` with:
  - [ ] Dataset info
  - [ ] Fairness rules applied
  - [ ] Metric table (median/min/max per format)
  - [ ] Graph placeholders (for manual generation)
  - [ ] Caveats about model differences

---

## PHASE 4 — JSON Roundtrip (IRONCFG)

- [ ] `Competitors/IronCfg/JsonRoundtripValidator.cs`
  - [ ] JSON → ICFG → JSON semantic comparison
  - [ ] Report mismatch count (must be 0)
  - [ ] Handle number precision, key ordering, null semantics

---

## PHASE 5 — CI Integration

- [ ] Update `.github/workflows/bench.yml` (or create) to:
  - [ ] Run small/medium datasets only in CI (max 1MB)
  - [ ] Respect `IRONFAMILY_BENCH_HEAVY=1` env var
  - [ ] Skip 100MB datasets unless explicitly enabled
  - [ ] Don't fail on benchmark results (informational only)

---

## PHASE 6 — Local Verification

- [ ] Test `megabench run --engine icfg --dataset 10KB --format icfg`
- [ ] Verify results.json is generated
- [ ] Verify REPORT.md is generated
- [ ] Check metrics are reasonable

---

## PHASE 7 — Commit

- [ ] Add all files to git
- [ ] Commit message: "MegaBench: competitor matrix, deterministic datasets, harness skeleton"
- [ ] NO Author/CoAuthor in commit

---

## External Dependencies to Resolve

- [ ] Find/install: protobuf-net (version?)
- [ ] Find/install: FlatSharp (version?)
- [ ] Find/install: Cap'n Proto .NET bindings (version?)
- [ ] Find/install: MessagePack (version?)
- [ ] Find/install: CBOR library (version?)
- [ ] Verify: xdelta3 available on Windows/Linux
- [ ] Verify: bsdiff available on Windows/Linux

See `tools/bench_deps/README.md` for exact versions and installation instructions.

---

## Size Semantics Definition (PHASE 3 HARDENING)

### IRONCFG
- **targetSize** = final encoded IRONCFG file size (including 64-byte header, schema, data, CRC32, BLAKE3)
- Generated via IronCfgEncoder.Encode() to exact specification
- Target tolerance: **±10%** (0.9x to 1.0x targetSize)
- If overshooting > 2%: iterate with smaller objects

### ILOG
- **targetSize** = final encoded ILOG file size (file header + L0 block header + event data + optional L2/L3/L4)
- Generated via IlogEncoder.Encode() for specified profile
- All profiles must respect same targetSize boundary
- Target tolerance: **±10%** (0.9x to 1.0x targetSize)
- Events are deterministically generated from seed

### IUPD
- **targetSize** = final IUPD package size (file header + chunk table + manifest + compressed payload)
- Generated via IupdWriter.Build() for specified profile
- Compression (FAST/SECURE/OPTIMIZED profiles) affects final size
- DELTA profile: base and updated files independently target their sizes
- Target tolerance: **±10%** (0.9x to 1.0x targetSize)

**Note**: All generators now fail-fast with explicit MegaBenchDatasetException if unable to reach target (within tolerance).

---

**Status**: PHASE 0-2 COMPLETE (skeleton + dataset generators). PHASE 1.2 HARDENING IN PROGRESS (exception handling, manifests, determinism validation)

