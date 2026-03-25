> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# IRONCFG Benchmark Results

**Generated:** 2026-01-16T17:02:24.5299821Z

## Methodology

- **warmup_iterations:** 5
- **measure_iterations:** 10
- **datasets:** small, medium, large, mega (nocrc)
- **environment:** .NET 8.0

## Results by Dataset

### small

**Size:** 155 bytes

| Metric | Value |
|--------|-------|
| decode_mb_s | 335,95 |
| encode_mb_s | 119,21 |
| open_latency_ms | 0,00 |
| validate_fast_mb_s | 153,98 |
| validate_strict_mb_s | 48,95 |

### medium

**Size:** 630 bytes

| Metric | Value |
|--------|-------|
| decode_mb_s | 1430,51 |
| encode_mb_s | 125,48 |
| open_latency_ms | 0,00 |
| validate_fast_mb_s | 1465,40 |
| validate_strict_mb_s | 556,31 |

### large

**Size:** 6883 bytes

| Metric | Value |
|--------|-------|
| decode_mb_s | 15628,91 |
| encode_mb_s | 113,53 |
| open_latency_ms | 0,00 |
| validate_fast_mb_s | 15628,91 |
| validate_strict_mb_s | 2723,71 |

### mega

**Size:** 81501 bytes

| Metric | Value |
|--------|-------|
| decode_mb_s | 185060,50 |
| encode_mb_s | 78,17 |
| open_latency_ms | 0,00 |
| validate_fast_mb_s | 194313,53 |
| validate_strict_mb_s | 74024,20 |

## Aggregates (P50, P95)

| Metric | Value |
|--------|-------|
| decode_mb_s_p50 | 15628,91 |
| decode_mb_s_p95 | 185060,50 |
| encode_mb_s_p50 | 119,21 |
| encode_mb_s_p95 | 125,48 |
| open_latency_ms_p50 | 0,00 |
| open_latency_ms_p95 | 0,00 |
| validate_fast_mb_s_p50 | 15628,91 |
| validate_fast_mb_s_p95 | 194313,53 |
| validate_strict_mb_s_p50 | 2723,71 |
| validate_strict_mb_s_p95 | 74024,20 |

