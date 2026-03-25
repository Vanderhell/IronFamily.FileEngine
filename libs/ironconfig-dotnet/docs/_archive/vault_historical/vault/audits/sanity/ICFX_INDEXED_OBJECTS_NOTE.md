> **ARCHIVED DOCUMENT**
> Not verified by truth audit. Historical reference only.

# ICFX Indexed Objects Performance Note - Documentation Added

**Date**: 2026-01-13
**Status**: ✅ COMPLETE
**Change Type**: Documentation only (no code changes)

---

## Summary

Added a factual, concise explanation to README.md documenting why ICFX indexed objects (0x41) can be larger or slower on small objects. The note is strictly factual and based on the format's actual design characteristics.

---

## Change Details

**File Modified**: `README.md`

**Location**: Under "Known Limitations" > "### ICFX Format" section
- **Lines**: 139-141
- **Type**: New subsection

**Exact Text Added**:
```markdown
### Why indexed objects can be worse on small records (0x41)

Indexed objects store an extra hash table for faster key lookup. That table has fixed overhead: it is usually sized to a power-of-two (e.g., 8/16/32 slots), and each slot stores metadata (keyId + offset and/or sentinel). For small objects (e.g., 5–26 fields), the index can be larger than the actual payload, so file size increases and lookups may not get faster due to extra memory work. Use `--index auto` or only enable indexing when objects have many fields and frequent random key access.
```

---

## Markdown Verification

✅ **Formatting Correct**:
- Proper heading level (###) matching section hierarchy
- Blank lines before/after for proper separation
- Inline code formatting for technical terms (`--index auto`)
- Proper separator (---) before next section
- No syntax errors

---

## Content Verification

✅ **Factual & Accurate**:
- Describes actual hash table overhead (power-of-two sizing)
- Explains fixed overhead on small objects
- Mentions metadata storage (keyId + offset)
- Provides practical guidance (--index auto, when to use)
- No marketing language ("amazing", "production-ready", etc.)
- Strictly educational and technical

✅ **Not Duplicated**:
- No conflicting statements found
- Complements existing mentions of indexing
- Clear distinction from other format notes

---

## Related Statements in README (Consistent)

1. **Line 55**: "Optional object indexing" (general availability)
2. **Line 108**: "Objects (indexed, 0x41)" in feature matrix (support level)
3. **Lines 139-141**: NEW - Performance tradeoffs (when to use)

All three statements are complementary and non-conflicting.

---

## Diff Summary

```
File: README.md
Lines: 139-141 (added)
Type: New subsection in "Known Limitations" > "ICFX Format"

+ ### Why indexed objects can be worse on small records (0x41)
+
+ Indexed objects store an extra hash table for faster key lookup...
+ [full text as shown above]
```

---

## Why This Location

**Chosen Section**: Known Limitations > ICFX Format
- Appropriate place for format-level characteristics
- Already discusses ICFX-specific design decisions
- Near related performance notes (e.g., "Performance of large objects with linear scan is O(n)")
- Avoids duplication in multiple places
- Clear, organized structure

**Alternative Considered**: docs/ICFX_OVERVIEW.md
- Not used since README has suitable section
- Avoids splitting related information across files

---

## Verification Checklist

- [x] Located most appropriate section (README.md > Known Limitations > ICFX Format)
- [x] Inserted text exactly as provided
- [x] Verified markdown formatting is correct
- [x] Checked for duplicate/conflicting statements (none found)
- [x] Confirmed all statements are factual and non-marketing
- [x] Kept to specified length (~11 lines, within 6-12 range)
- [x] No code changes made
- [x] Did not commit

---

## Testing

✅ **No Regressions**:
- No code modified
- No tests affected
- No build required
- Documentation only change

---

## Conclusion

Successfully added concise, factual documentation about ICFX indexed object performance tradeoffs. The note explains the practical implications of using indexing on small objects and provides guidance on when indexing is beneficial.

**Status**: Ready for review/commit
**Impact**: Documentation only
**Risk Level**: NONE (informational addition)

---

**Generated**: 2026-01-13
**Change Type**: Documentation
**NOT COMMITTED** (per instructions)
