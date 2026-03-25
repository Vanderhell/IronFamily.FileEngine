# MegaBench Methodology v6

**Document Version**: 1.0
**Date**: 2026-02-26
**Purpose**: Reproducible, hardware-agnostic benchmark protocol for IronFamily codecs

---

## Executive Summary

MegaBench V6 implements industry-standard statistical hardening for codec benchmarking:
- **Multi-process isolation** (eliminates JIT/cache effects)
- **MAD-based outlier removal** (3σ threshold from median)
- **Bootstrap confidence intervals** (1000 resamples, 95% CI)
- **Codec profile matrix** (FAST/BALANCED/SMALL)
- **Dataset credibility gates** (entropy, structure, semantic quality)

This document defines the reproducible methodology for all benchmark runs.

---

## Hardware & Environment Capture

Every benchmark run MUST capture and report:

### System Information
- **CPU**: Model, core count, GHz nominal
- **RAM**: Installed, available at benchmark time
- **OS**: Version, kernel
- **Disk**: Type (SSD/HDD), speed
- **.NET Runtime**: Version, build
- **Time of Day**: UTC timestamp

### Runtime Isolation Flags (CRITICAL)
```
DOTNET_TieredPGO=0              # Disable tiered JIT
COMPlus_ReadyToRun=0            # Disable AOT
COMPlus_TC_QuickJitForLoops=0   # Disable quick JIT
IRONFAMILY_DETERMINISTIC=1      # Reproducible datasets (seed=42)
```

These flags MUST be set identically across all child processes.

### Benchmark Configuration
- **Child Processes Per Job**: 7 runs (immutable)
- **Bootstrap Iterations**: 1000 resamples
- **Outlier Threshold**: 3 × MAD from median
- **Minimum Retention**: 70% of original samples (FAIL if not met)

---

## Sample Collection Protocol

### Warmup Phase
- **Count**: 5 iterations
- **Purpose**: JIT compilation, cache warming
- **Measurement**: Disabled (not recorded)

### Measurement Phase
- **Count**: 7 iterations
- **Purpose**: Statistical sampling
- **Measurement**: Time (nanoseconds), allocation (bytes)
- **Recording**: All 7 values collected

---

## Statistical Methods

### Percentile Calculation
```
Deterministic method: index = ceil(p × (n - 1))
Example: 95th percentile of 100 samples = sample[95]
```

### Median Absolute Deviation (MAD)
```
MAD = median(|x_i - median(x)|)

Purpose: Robust measure of spread (resistant to outliers)
Scale: Used for 3σ equivalent threshold (3 × MAD)
```

### Outlier Removal (MAD-Based)
```
1. Compute median: m = median(samples)
2. Compute MAD: dev = MAD(samples)
3. Threshold: t = 3 × dev
4. Keep: x where |x - m| ≤ t
5. Check: len(kept) >= 0.70 × len(original)
   - If YES: use kept samples
   - If NO: FAIL gate (too much data discarded)
```

**Rationale**: MAD is distribution-free, doesn't assume normality.

### Bootstrap 95% Confidence Interval
```
1. Resample 1000 times with replacement (seed=42, deterministic)
2. Compute median for each resample
3. Sort bootstrap medians
4. CI lower = Percentile(bootstrap_medians, 0.025)
5. CI upper = Percentile(bootstrap_medians, 0.975)
6. CI width = upper - lower

Gate: CI width < 25% of original median (indicates stability)
```

---

## Codec Profile Matrix

All codecs support three profiles. **Profiles are mutually exclusive**.

### Profile 1: FAST (Speed-Optimized)

| Codec | Setting | Notes |
|-------|---------|-------|
| JSON | Default serializer | WriteIndented=false |
| MessagePack | LZ4Block=disabled | Streaming, no compression |
| CBOR | canonical=false | Minimal encoding |
| Protobuf | Default | No special flags |
| FlatBuffers | Default | Streaming buffer |
| Zstd | compression_level=1 | Fastest compression |

**Use Case**: Network transmission, time-critical scenarios

### Profile 2: BALANCED (Default)

| Codec | Setting | Notes |
|-------|---------|-------|
| JSON | WriteIndented=false | Standard JSON without whitespace |
| MessagePack | Default | Standard settings |
| CBOR | canonical=true | Deterministic encoding |
| Protobuf | Default | Standard |
| FlatBuffers | Default | Standard |
| Zstd | compression_level=3 | Moderate compression |

**Use Case**: General-purpose, balanced trade-offs

### Profile 3: SMALL (Size-Optimized)

| Codec | Setting | Notes |
|-------|---------|-------|
| JSON | Aggressive minification | Remove all whitespace, comments |
| MessagePack | LZ4Block=enabled | Post-compression with LZ4 |
| CBOR | canonical=true + minimal | Minimal encoding + canonical |
| Protobuf | packed=true | Use packed encoding for numerics |
| FlatBuffers | Optimized vectors | Reuse buffers where possible |
| Zstd | compression_level=9 | Maximum compression (slowest) |

**Use Case**: Storage, archival, bandwidth-constrained

---

## Dataset Credibility Rules

Every real-world dataset MUST pass credibility gates before benchmarking.

### Gate 1: Semantic Diversity
```
Requirement: distinctKeyCount > 5
Validation: Count unique field/key names in structure
Failure: Dataset is too simple, not representative
```

### Gate 2: Structural Complexity
```
Requirement: averageDepth >= 2.0
Validation: Measure nesting depth of structures
Failure: Dataset is too flat, not realistic
```

### Gate 3: Data Count (ILOG-specific)
```
Requirement: eventCount > 50
Validation: Count distinct events/records in log
Failure: ILOG dataset has insufficient samples
Applicability: ILOG format only
```

### Gate 4: Byte Entropy
```
Requirement: distinctByteRatio >= 0.10
Validation: Count distinct bytes / 256
Failure: Data is repetitive or homogeneous (risk of compress artifacts)
```

### Rule: All Gates Must Pass
```
If any single gate fails → FAIL (cannot benchmark with this dataset)
```

---

## Output Files & Formats

### Raw Samples (NDJSON)
**File**: `raw_samples.ndjson` (one JSON per line)

```json
{
  "codecName": "protobuf",
  "profile": "BALANCED",
  "engine": "icfg",
  "encodeMedianUs": 45.2,
  "decodeMedianUs": 38.1,
  "p95Us": 52.3,
  "ci95Lower": 44.1,
  "ci95Upper": 46.3,
  "madUs": 2.1,
  "trimmedSamples": 7,
  "originalSamples": 7
}
```

### Summary JSON
**File**: `summary.json`

Aggregated metrics by codec, including:
- Count of valid samples
- Median, p95, CI bounds
- CV (coefficient of variation)
- Normalized metrics (us/KB, alloc/KB)

### Summary CSV
**File**: `summary_table.csv`

Human-readable table with rows per codec:
```csv
Codec,Profile,Engine,Samples,Median(us),P95(us),CV,CI_Width(%)
protobuf,BALANCED,icfg,7,45.2,52.3,0.08,5.2
```

### Methodology Snapshot
**File**: `methodology_snapshot.json`

Captures exact methodology used:
```json
{
  "version": "v6",
  "runsPerJob": 7,
  "bootstrapIterations": 1000,
  "madThreshold": 3.0,
  "minRetention": 0.70,
  "profiles": {
    "FAST": { "json": "default", "messagepack": "lz4=disabled", ... },
    "BALANCED": { ... },
    "SMALL": { ... }
  },
  "timestamp": "2026-02-26T..."
}
```

---

## Reproducibility Checklist

- [ ] Environment variables logged (DOTNET_TieredPGO, etc.)
- [ ] Seed=42 used for all RNG (dataset generation, bootstrap)
- [ ] Hardware info captured at start
- [ ] Dataset credibility gates all PASS
- [ ] Outlier trimming: >= 70% retention
- [ ] Bootstrap CI computation: deterministic (seed=42)
- [ ] Profile settings explicit (not default-only)
- [ ] Exit code 0 on success, 3 on gate failure
- [ ] All output files generated
- [ ] Timestamp recorded for each run

---

## Limitations & Caveats

### Platform-Specific Variance
- .NET JIT compiler varies by CPU architecture
- L3 cache effects vary with hardware generation
- Thermal throttling may affect repeated runs
- Background processes on same machine will skew results

**Mitigation**: Run in isolated environment (cloud VM recommended)

### Statistical Caveats
- Bootstrap assumes data is IID (independent, identically distributed)
- MAD-based outlier removal may fail for multi-modal distributions
- Small sample size (n=7) limits statistical power
- Confidence interval width can exceed 25% for volatile codecs (is not failure, but note)

**Mitigation**: Use deterministic seed, consider replicating runs

### Codec Implementation Variance
- Profile settings may not be fully configurable for all codecs
- Some codecs don't support specific compression levels
- EXCLUDED status documented if codec cannot meet profile spec

### Real-World Dataset Limitations
- Generated datasets are synthetic (deterministic generation mimics real patterns)
- Entropy >= 0.10 does not guarantee semantic diversity
- No validation of actual business logic (e.g., updateManifest structure)

---

## Exit Codes

| Code | Meaning | Action |
|------|---------|--------|
| 0 | SUCCESS | All gates PASS, results valid |
| 2 | ERROR | System error (file I/O, crash, timeout) |
| 3 | GATE FAIL | Correctness/stability/fairness gate failed |

---

## References

- MAD method: Tukey, J. W. (1977). "Exploratory Data Analysis"
- Bootstrap CI: Efron & Tibshirani (1993). "An Introduction to the Bootstrap"
- Outlier detection: Huber, P. J. (1964). "Robust Statistics"

---

**Document Status**: Locked for V6
**Changes Require**: Review + explicit documentation
**No marketing language.** Facts only.
