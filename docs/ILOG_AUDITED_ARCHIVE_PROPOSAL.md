# ILOG Audited Archive Proposal

## Goal

Add a profile for logs that need both:

- storage-first size reduction
- cryptographic tamper evidence

Current `ARCHIVED` is now `L1 + L3` and intentionally omits `L0`.
Current `AUDITED` is `L0 + L1 + L4`.
There is no profile that combines archive-first storage with a seal over the archived payload.

## Recommended Profile

Name:

- `AUDITED_ARCHIVE`

Flags:

- keep existing bits
- add `L3` and `L4`
- expected effective flag shape: little-endian + BLAKE3 + L3 + witness-enabled

Blocks:

- `L1 + L3 + L4`

Semantics:

- `L3` is the primary payload carrier
- `L4` seals the canonical decompressed event payload, not the compressed byte stream
- reader opens without `L0`
- decode returns decompressed event payload from `L3`

## Why Seal Decompressed Payload

Recommended choice:

- seal the logical event payload after decompression

Reasons:

- stable meaning even if compressor internals evolve
- easier parity with current `AUDITED`, which conceptually seals content, not transport encoding
- avoids false signature churn when compression strategy changes without payload changes

Alternative:

- seal raw `L3` bytes

Tradeoff:

- stronger binding to exact archive representation
- but much worse migration flexibility

## Reader Rules

- `Open()` accepts `L1 + L3 + L4` without `L0`
- `ValidateStrict()` requires:
  - `L3` present
  - `L4` present
  - non-zero seal hash
  - BLAKE3 over decompressed payload matches `L4`
- `Decode()`:
  - decompress `L3`
  - return logical event payload

## Benchmark Expectations

Compared to `ARCHIVED`:

- slightly larger output due to `L4`
- slightly slower encode/decode due to hash/signature work
- still dramatically smaller than `MINIMAL` on log-like data

Compared to `AUDITED`:

- much smaller output on compressible logs
- slower decode than raw `AUDITED`

## Migration Notes

- keep current `AUDITED` unchanged
- add `AUDITED_ARCHIVE` as a new profile, not a silent semantic rewrite
- keep `ARCHIVED` as unsigned storage-first

## Suggested Priority

Implement only if one of these is true:

- compliance requires long-term archived logs with tamper evidence
- transport/storage cost is large enough that `AUDITED` raw payload is too expensive

Otherwise current profile set is already coherent:

- `SEARCHABLE` for access
- `ARCHIVED` for storage
- `AUDITED` for tamper evidence
