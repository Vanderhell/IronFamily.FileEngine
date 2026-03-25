# Benchmark Results - 2026-03-25

Run label: `mega_all_20260325`

Datasets:
- `10KB`
- `100KB`
- `1MB`

Primary artifacts:
- `artifacts/bench/megabench_metrics/overview/overview_mega_all_20260325.json`
- `artifacts/bench/megabench_metrics/overview/overview_mega_all_20260325_ranking.csv`
- `artifacts/bench/megabench_metrics/overview/overview_mega_all_20260325_analysis.md`

## Overall winners by dataset

- `10KB`: `IUPD/FAST` (score `3.250`)
- `100KB`: `ILOG/MINIMAL` (score `2.600`)
- `1MB`: `ICFG/default` (score `2.333`)

## Key findings

- `ILOG/AUDITED` has very high encode cost (around 1s+), expected for signature-heavy profile.
- `ILOG/ARCHIVED` gives best encoded size, but significantly slower strict/decode/verify on larger data.
- `IUPD/MINIMAL` and `IUPD/FAST` are strongest low-latency IUPD profiles.
- `ICFG/default` is the strongest 1MB all-around profile in this run.

## Usage recommendation

- Throughput/latency first:
  - `ILOG/MINIMAL` (or `SEARCHABLE` when strict/verify path priority)
  - `IUPD/FAST` for small/medium, `IUPD/MINIMAL` for balanced behavior
  - `ICFG/default` for large payload baseline
- Size first:
  - `ILOG/ARCHIVED`
- Compliance/audit first:
  - `ILOG/AUDITED`

