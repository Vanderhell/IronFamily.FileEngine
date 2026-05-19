# IUPD Profiles Guide

**Status**: Informative guide (non-normative)  
**Last Updated**: 2026-05-19  
**Scope**: Profile selection guidance for the repository's `IUPD` implementation

This document complements:

- `SPEC.md` (format definition)
- `COMPATIBILITY.md` (version support policy)
- `STRUCTURE_AND_VALIDATION.md` (writer/reader flow)

## Profile Summary

IUPD profiles are a single byte in the file header and apply to the whole file.

| Profile | Byte | Compression | BLAKE3 | Dependencies | Typical use |
|---|---:|---|---|---|---|
| `MINIMAL` | `0x00` | no | no | no | simplest / lowest CPU cost |
| `FAST` | `0x01` | yes | no | no | smaller files without crypto |
| `SECURE` | `0x02` | no | yes | yes | security-critical distribution |
| `OPTIMIZED` | `0x03` | yes | yes | yes | default general-purpose choice |
| `INCREMENTAL` | `0x04` | yes | yes | yes | patch-bound update flow |

Note: the strict verifier may intentionally reject some profiles by default. Use `SPEC.md` and `STRUCTURE_AND_VALIDATION.md` as the source of truth for what “strict” means and what is accepted.

## Selection Cheatsheet

- Need cryptographic integrity (untrusted transport / hostile storage): use `SECURE` or `OPTIMIZED`.
- Need smaller files and crypto: use `OPTIMIZED`.
- Need smallest CPU / simplest format handling: use `MINIMAL`.
- Need compression but no crypto: use `FAST`.
- Need incremental/patch workflow: use `INCREMENTAL`.

## API Pointers (informative)

.NET implementation surfaces:

- Writer/reader: `libs/ironconfig-dotnet/src/IronConfig/Iupd/`
- Profile model: `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs`

