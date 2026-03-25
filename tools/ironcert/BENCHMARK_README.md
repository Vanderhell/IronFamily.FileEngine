# IRONFAMILY Benchmark Harness

Unified benchmarking framework for IRONCFG, ILOG (5 profiles), and IUPD (5 profiles).

## Architecture

### Profile-Based Benchmarking

Each engine has multiple profiles optimized for different scenarios:

**ILOG Profiles:**
1. **MINIMAL** - L0+L1 only (fastest, no integrity)
2. **INTEGRITY** - L0+L1+CRC32 (balanced)
3. **SEARCHABLE** - L0+L1+L2 indices (query acceleration)
4. **ARCHIVED** - L0+L1+compression (storage optimization)
5. **AUDITED** - L0+L1+BLAKE3 (cryptographic integrity)

**IUPD Profiles:**
1. **MINIMAL** - Flat chunks, no integrity
2. **SIMPLE** - Chunks + CRC32 verification
3. **SEQUENTIAL** - + Dependency graph ordering
4. **STREAMING** - + Streaming apply capability
5. **VERIFIED** - + BLAKE3 hashing + signatures

**IRONCFG:**
- Single engine (non-profiled)
- Benchmarks: encode, decode, fast-validate, strict-validate

## Files

### Implementation

```
tools/ironcert/
├── BenchmarkCommand.cs      # CLI dispatcher
├── IlogBenchmarks.cs        # ILOG profile runner
├── IupdBenchmarks.cs        # IUPD profile runner
├── BenchmarkHarness.cs      # Base framework (metrics, utilities)
└── BENCHMARK_README.md      # This file
```

### Data

```
benchmarks/
├── datasets/
│   ├── config/
│   │   ├── config_1kb.json
│   │   ├── config_100kb.json
│   │   ├── config_1mb.json
│   │   ├── config_10mb.json
│   │   └── config_50mb.json
│   ├── logs/
│   │   ├── logs_1kb.jsonl
│   │   ├── logs_100kb.jsonl
│   │   ├── logs_1mb.jsonl
│   │   ├── logs_10mb.jsonl
│   │   └── logs_50mb.jsonl
│   └── patches/
│       ├── patches_1kb.json
│       ├── patches_100kb.json
│       ├── patches_1mb.json
│       ├── patches_10mb.json
│       └── patches_50mb.json
└── results/
    ├── ILOG_MINIMAL_1mb.md
    ├── ILOG_INTEGRITY_1mb.md
    ├── IUPD_SIMPLE_1mb.md
    └── ...
```

## Usage

### Step 1: Generate Datasets

```bash
dotnet script tools/dataset_generator.csx --output benchmarks/datasets
```

Output: 15 files (5 sizes × 3 types)

### Step 2: Run Benchmarks

#### IRONCFG (single engine)

```bash
# Run on 1 MB config
dotnet run --project tools/ironcert/IronCert.csproj -- \
  benchmark ironcfg --dataset benchmarks/datasets/config/config_1mb.json --iterations 10

# Run on all sizes
for SIZE in 1kb 100kb 1mb 10mb 50mb; do
  dotnet run --project tools/ironcert/IronCert.csproj -- \
    benchmark ironcfg \
    --dataset benchmarks/datasets/config/config_${SIZE}.json \
    --iterations 10
done
```

#### ILOG (all 5 profiles)

```bash
# Run all profiles on 1 MB log
dotnet run --project tools/ironcert/IronCert.csproj -- \
  benchmark ilog --dataset benchmarks/datasets/logs/logs_1mb.jsonl --iterations 10

# Runs automatically:
#  - MINIMAL (L0+L1)
#  - INTEGRITY (L0+L1+CRC32)
#  - SEARCHABLE (L0+L1+L2)
#  - ARCHIVED (L0+L1+compression)
#  - AUDITED (L0+L1+BLAKE3)
```

#### IUPD (all 5 profiles)

```bash
# Run all profiles on 10 MB patches
dotnet run --project tools/ironcert/IronCert.csproj -- \
  benchmark iupd --dataset benchmarks/datasets/patches/patches_10mb.json --iterations 5

# Runs automatically:
#  - MINIMAL
#  - SIMPLE (CRC32)
#  - SEQUENTIAL (+ dependencies)
#  - STREAMING (+ streaming apply)
#  - VERIFIED (+ BLAKE3)
```

## Output Format

### Console Output

Example ILOG run:

```
📝 ILOG Profile Benchmarking
======================================================================
📁 Dataset: logs_10mb.jsonl
🔄 Iterations per profile: 10

🧪 MINIMAL          ... ✓
🧪 INTEGRITY        ... ✓
🧪 SEARCHABLE       ... ✓
🧪 ARCHIVED         ... ✓
🧪 AUDITED          ... ✓

📈 ILOG Profile Results

▶ MINIMAL
────────────────────────────────────────────────────────────────────
  Encode
    Throughput:   450.3 MB/s
    Avg:           22.15 ms
    P50:           22.10 ms
    P95:           22.45 ms
  Decode
    Throughput:   680.5 MB/s
    Avg:           14.71 ms
    P50:           14.68 ms
    P95:           14.92 ms

▶ INTEGRITY
────────────────────────────────────────────────────────────────────
  ...

📊 Profile Comparison

| Profile     | Encode (MB/s) | Decode (MB/s) | Size Ratio | Best For |
|-------------|---------------|---------------|-----------|----------|
| MINIMAL     |           450.3 |           680.5 |     100.0% | Speed    |
| INTEGRITY   |           390.2 |           620.1 |     100.2% | Balance  |
| SEARCHABLE  |           320.5 |           480.3 |     108.5% | Queries  |
| ARCHIVED    |           110.2 |           200.4 |      45.3% | Storage  |
| AUDITED     |           200.3 |           350.2 |     100.8% | Security |
```

### Metrics Collected

Per profile/size:

- **Encode Throughput** (MB/s)
- **Decode Throughput** (MB/s)
- **Verification Time** (ms) — if applicable
- **Memory Delta** (KB) — peak memory used
- **Size Ratio** (%) — binary vs source
- **Latency Percentiles** (p50, p95, p99)

## Profile Selection Guide

### ILOG Profiles

| Use Case | Profile | Rationale |
|----------|---------|-----------|
| Real-time logging (fire-and-forget) | MINIMAL | Maximum speed, no overhead |
| Standard application logs | INTEGRITY | CRC32 detects corruption, minimal overhead |
| Log analysis tools | SEARCHABLE | O(log N) seeks for debugging |
| Long-term archival | ARCHIVED | 40-60% compression, slower but storage-efficient |
| Compliance/audit trails | AUDITED | Cryptographic integrity, tamper detection |

### IUPD Profiles

| Use Case | Profile | Rationale |
|----------|---------|-----------|
| Hotfixes (< 1 MB) | MINIMAL | Smallest manifest, fastest apply |
| Security patches | SIMPLE | CRC32 validates delivery, minimal overhead |
| Feature updates (ordered) | SEQUENTIAL | Dependencies ensure correct apply order |
| Large OS patches (> 100 MB) | STREAMING | Memory-efficient, can apply progressively |
| Secure distribution | VERIFIED | BLAKE3 + future signatures for supply chain |

## Implementation Notes

### Current Status

- **IlogBenchmarks.cs** - Framework implemented, TODO: Real encoder/decoder integration
- **IupdBenchmarks.cs** - Framework implemented, TODO: Real IUPD encoder/applier integration
- **BenchmarkCommand.cs** - CLI dispatcher, ready for use
- **dataset_generator.csx** - Dataset generation script, ready to run

### Integration Points

When implementing real encoding/decoding:

1. **IlogBenchmarks.cs** - Implement:
   - `EncodeWithProfile(byte[], IlogProfile)` → binary ILOG file
   - `DecodeWithProfile(byte[], IlogProfile)` → parsed events
   - `VerifyWithProfile(byte[], IlogProfile)` → integrity check

2. **IupdBenchmarks.cs** - Implement:
   - `EncodeWithProfile(byte[], IupdProfile)` → (manifest, payload)
   - `ApplyWithProfile(byte[], byte[], IupdProfile)` → applied result
   - `VerifyWithProfile(byte[], byte[], IupdProfile)` → validation

3. **BenchmarkCommand.cs** - Already dispatches to engines

## Batch Testing Scripts

### All ILOG sizes (all profiles)

```bash
#!/bin/bash
for SIZE in 1kb 100kb 1mb 10mb 50mb; do
  echo "Testing ILOG $SIZE..."
  dotnet run --project tools/ironcert/IronCert.csproj -- \
    benchmark ilog \
    --dataset "benchmarks/datasets/logs/logs_${SIZE}.jsonl" \
    --iterations 5
done
```

### All IUPD sizes (all profiles)

```bash
#!/bin/bash
for SIZE in 1kb 100kb 1mb 10mb 50mb; do
  echo "Testing IUPD $SIZE..."
  dotnet run --project tools/ironcert/IronCert.csproj -- \
    benchmark iupd \
    --dataset "benchmarks/datasets/patches/patches_${SIZE}.json" \
    --iterations 5
done
```

### Complete test suite

```bash
#!/bin/bash
echo "Generating datasets..."
dotnet script tools/dataset_generator.csx --output benchmarks/datasets

echo "Testing IRONCFG..."
for SIZE in 1kb 100kb 1mb 10mb 50mb; do
  dotnet run --project tools/ironcert/IronCert.csproj -- \
    benchmark ironcfg \
    --dataset "benchmarks/datasets/config/config_${SIZE}.json" \
    --iterations 10
done

echo "Testing ILOG..."
for SIZE in 1kb 100kb 1mb 10mb 50mb; do
  dotnet run --project tools/ironcert/IronCert.csproj -- \
    benchmark ilog \
    --dataset "benchmarks/datasets/logs/logs_${SIZE}.jsonl" \
    --iterations 5
done

echo "Testing IUPD..."
for SIZE in 1kb 100kb 1mb 10mb 50mb; do
  dotnet run --project tools/ironcert/IronCert.csproj -- \
    benchmark iupd \
    --dataset "benchmarks/datasets/patches/patches_${SIZE}.json" \
    --iterations 5
done

echo "All benchmarks complete!"
```

## Next Steps

1. **Integrate real encoders/decoders** into benchmark methods
2. **Run baseline benchmarks** on all datasets
3. **Generate profile comparison reports** (Markdown/CSV)
4. **Identify performance optimization** opportunities
5. **Run competitive analysis** vs Unity/industry engines
6. **Publish results** in `benchmarks/SUMMARY_REPORT.md`

---

**Framework Status:** ✅ Ready for integration with real engine implementations

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
