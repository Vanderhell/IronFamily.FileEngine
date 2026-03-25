> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# GATE Single-File Benchmark Report

## Test Configuration

**File:** `tools/datasets/datasets-10x/B_02_medium.json`
**Size:** 13,606 bytes (13.6 KB)
**Input Directory:** `gate_test/` (single file)
**Output Directory:** `gate_results/`

## Command Executed

```bash
ironcfg bench2 gate_test --outdir gate_results
```

**Status:** ✅ SUCCESS - All 12 configurations completed with 0 errors

---

## Benchmark Results - 12 Configuration Matrix

| KeyId | VSP   | CRC | JSON (B) | BJV (B) | Ratio % | Encode (μs) | Open+Val (μs) | Decode (μs) | Status |
|-------|-------|-----|----------|---------|---------|-------------|---------------|------------|--------|
| 32    | off   | off | 13606    | 5206    | 38.3    | 380         | 729           | 1378       | ✅     |
| 32    | off   | on  | 13606    | 5206    | 38.3    | 253         | 469           | 992        | ✅     |
| 32    | auto  | off | 13606    | 4985    | 36.6    | 394         | 202           | 393        | ✅     |
| 32    | auto  | on  | 13606    | 4985    | 36.6    | 665         | 180           | 393        | ✅     |
| 32    | force | off | 13606    | 4985    | 36.6    | 378         | 485           | 922        | ✅     |
| 32    | force | on  | 13606    | 4985    | 36.6    | 799         | 228           | 790        | ✅     |
| 16    | off   | off | 13606    | 4572    | 33.6    | 278         | 175           | 961        | ✅     |
| 16    | off   | on  | 13606    | 4572    | 33.6    | 258         | 469           | 1012       | ✅     |
| 16    | auto  | off | 13606    | 4351    | 32.0    | 387         | 178           | 514        | ✅     |
| 16    | auto  | on  | 13606    | 4351    | 32.0    | 386         | 178           | 390        | ✅     |
| 16    | force | off | 13606    | 4351    | 32.0    | 394         | 178           | 516        | ✅     |
| 16    | force | on  | 13606    | 4351    | 32.0    | 1141        | 187           | 653        | ✅     |

---

## Summary

- **Total Configurations:** 12
- **Passed:** 12/12 ✅
- **Failed:** 0
- **Compression Range:** 32.0% - 38.3%
- **Fastest Encode:** 253 μs (KeyId=32, VSP=off, CRC=on)
- **Fastest Decode:** 390 μs (KeyId=16, VSP=auto, CRC=on)
- **Best Compression:** 32.0% (KeyId=16, all VSP/CRC combinations)

---

## Output Artifacts

Generated in `gate_results/`:
- `MEGA_BENCH.csv` - Raw data
- `MEGA_BENCH.md` - Formatted table
- `MEGA_BENCH_summary.md` - Summary stats
- 12 configuration directories with BJV/BJX files

**Result:** PASS ✅
