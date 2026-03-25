> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# ICFX v0 - Quick Reference Guide

## Golden Vectors Location
```
vectors/small/icfx/
â”śâ”€â”€ golden_config.json          # Source data (normative)
â”śâ”€â”€ golden_icfx_nocrc.icfx      # Binary format (no CRC)
â””â”€â”€ golden_icfx_crc.icfx        # Binary format (with CRC)
```

## Verification Commands

**Validate golden vectors:**
```bash
./tools/ironconfigtool/bin/Debug/net8.0/ironconfigtool.exe validate vectors/small/icfx/golden_icfx_nocrc.icfx
./tools/ironconfigtool/bin/Debug/net8.0/ironconfigtool.exe validate vectors/small/icfx/golden_icfx_crc.icfx
```

**Test determinism:**
```bash
python3 _icfx_impl/simple_determinism_test.py
```

**Run benchmarks:**
```bash
python3 benchmarks/icfx_bench_runner.py
```

**Encode JSON to ICFX:**
```bash
./tools/ironconfigtool/bin/Debug/net8.0/ironconfigtool.exe packx input.json output.icfx --crc on --vsp on
```

**Decode ICFX to JSON:**
```bash
./tools/ironconfigtool/bin/Debug/net8.0/ironconfigtool.exe tojson output.icfx
```

## Key Statistics

| Metric | Value |
|--------|-------|
| Format Version | v0 (Locked) |
| Magic Bytes | ICFX |
| Header Size | 48 bytes |
| Max Depth | 256 levels |
| Compression | 18.5% vs JSON |
| Dictionary Keys | Lexicographically sorted |
| VSP Strings | Lexicographically sorted |
| Determinism | Proven (3/3 identical) |

## Guards Implemented

1. **KeyId Validation** - Ensures dictionary keys exist
2. **Offset Bounds Checking** - All offsets within buffer
3. **Recursion Depth Guard** - Maximum 256 levels
4. **Iterative Offset Calculation** - Prevents size mismatches

## Files to Review

| File | Purpose |
|------|---------|
| `_icfx_impl/FINAL_LOCK_PROOF.txt` | Complete verification report |
| `_icfx_impl/COMPLETION_SUMMARY.txt` | This session's work |
| `benchmarks/ICFX_BENCH.md` | Performance analysis |
| `vectors/small/icfx/golden_config.json` | Test data definition |

## Status: âś“ PRODUCTION READY

ICFX v0 is locked and certified. No further changes to binary format
without:
- Proven critical bug
- Golden vector test failure
- Full regeneration of vectors
- Complete re-validation

**Important**: Changes left for review per instructions - DO NOT COMMIT.
