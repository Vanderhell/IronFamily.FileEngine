> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# PHASE 1 CRC Alignment - Known Blocker

## Issue
CRC32 computation in C implementation does not match .NET reference implementation.

### Evidence
- Standard test vector: `"123456789"`
  - Expected (IEEE 802.3): `0xCBF43926`
  - C computed: `0x798853C0`
  - **MISMATCH CONFIRMED**

- Golden file test: `golden_icfx_crc.icfx`
  - Stored CRC: `0x8DFEF4BB`
  - .NET computed: `0x8DFEF4BB` âś“
  - C computed: `0xA4098949` âś—
  - **CRC MISMATCH**

## Root Cause Analysis
The C CRC32 implementation has a fundamental algorithm bug. Possibilities:
1. Lookup table is corrupted/incorrect (despite using zlib reference table)
2. Algorithm parameters wrong (init value, XOR output, reflection)
3. Byte processing order incorrect

## What Works
âś“ Flag bit interpretation fixed (bit 2 = CRC, not bit 1)
âś“ CRC offset parsing correct
âś“ Stored CRC reading correct (little-endian LE)
âś“ File format parsing works for non-CRC files

## Solution Path (Not Yet Implemented)
To fix this phase, we need ONE of:

### Option A: Port verified-correct CRC32
Use the System.IO.Hashing.Crc32 algorithm directly or port it to C:
- Create wrapper that calls .NET CRC via P/Invoke (Windows only)
- OR implement full CRC-32 algorithm from scratch with correct parameters

### Option B: Use pre-computed golden CRC values
Store expected CRC values in test files rather than computing:
- Less elegant but pragmatic
- Allows validation that file reads work correctly

### Option C: Disable CRC checking for Phase 1
Mark files without CRC as valid, defer CRC validation to Phase 2:
- Allows other functionality to proceed
- Requires deferring CRC parity testing

## Decision Required
Given mission constraints, recommend **Option A**: Port the correct CRC-32 algorithm.

The .NET code uses `System.IO.Hashing.Crc32`, which is part of the .NET standard library and uses the correct IEEE 802.3 parameters. We need to match this exactly.

## Files Created for Debugging
- `libs/ironcfg-c/tools/test_crc.c` - Standard test vector test
- `libs/ironcfg-c/tools/crc_diagnostic.c` - File CRC analyzer
- `vectors/small/icfx/expected_crc.txt` - .NET reference CRC values

## Status
**PHASE 1 BLOCKED** on CRC32 implementation bug.
ICFX reader works for all non-CRC files.
CRC flag handling fixed.
