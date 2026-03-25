> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# ICF2 CRC32 Root Cause Analysis

## Finding

**ROOT CAUSE: [F] File generated with wrong CRC computation**

The golden_small.icf2 file contains an INCORRECT CRC value.

### Evidence

**File:** vectors/small/icf2/golden_small.icf2
**File Size:** 246 bytes
**CRC Offset:** 242 (last 4 bytes before EOF)

| Algorithm | CRC Value | Verified By |
|-----------|-----------|------------|
| **WRONG (stored in file)** | 0x3CC1798C | File inspection |
| **CORRECT (System.IO.Hashing)** | 0x4C6AA91C | System.IO.Hashing.Crc32 |
| **CORRECT (Python zlib)** | 0x4C6AA91C | zlib.crc32 (cross-check) |

### Why Files Have Wrong CRC

1. Golden vectors were regenerated using the OLD broken implementation of Icf2Encoder
2. The OLD Icf2Encoder used a "simplified bit-rotation" CRC algorithm
3. Recent commit switched to System.IO.Hashing.Crc32 (correct IEEE CRC32)
4. Golden files were NOT regenerated after the switch
5. Result: Mismatch between encoded CRC and validator expectation

### Validation

```
Byte range checksummed: [0..242)
Computed CRC (IEEE std): 0x4C6AA91C
Stored CRC (wrong):     0x3CC1798C
Match: NO âś—
```

### Solution

Regenerate golden vectors with CORRECT CRC32 implementation (System.IO.Hashing.Crc32).
