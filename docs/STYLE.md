# Documentation Style Guide

This repository keeps all technical documentation under `docs/`.

Goal: engine documentation should look and feel consistent across engines (`ICFG`, `ILOG`, `IUPD`, etc.).

## Engine doc layout (standard)

Each engine gets a folder:

`docs/engines/<engine>/`

Recommended files (when applicable):

- `README.md` — overview + links (entry point)
- `SPEC.md` — normative format specification
- `SCHEMA_AND_TYPES.md` — schemas, type system, encodings
- `COMPATIBILITY.md` — versions, migrations, readers/writers
- `BENCHMARKS.md` (or `benchmarks/*.md`) — performance methodology/results
- `EXAMPLES.md` — examples and sample encodings

## Standard sections (use in this order)

1. **Title** (`# <ENGINE> ...`)
2. **Status block** (top of file)
   - `**Status**`: LOCKED / ACTIVE / INCUBATING / DRAFT
   - `**Last Updated**`: `YYYY-MM-DD`
   - `**Scope**`: what code surfaces the doc claims to cover
3. **Scope**
4. **Glossary** (optional)
5. **Format** (header, blocks, fields)
6. **Validation** (fast/strict rules, error codes)
7. **Limits** (DoS limits, max sizes)
8. **Compatibility** (v1/v2, writer/reader behavior)
9. **References / Evidence**

## Evidence (truth gate friendly)

When using normative language (`MUST`, `SHOULD`, `SHALL`, `NEVER`, `REQUIRED`), keep evidence nearby:

- a test name (`SpecLockTests`, `RuntimeVerifyTests`, etc.), or
- a code file path, or
- a vectors directory path.

Example (single-line evidence):

> The header size MUST be 64 bytes (`libs/ironconfig-dotnet/src/.../IronCfgHeader.cs`, `SpecLockTests`).

## Naming and casing

- Engine names in headings: `ICFG`, `ILOG`, `IUPD`
- File names: `README.md`, `SPEC.md`, `COMPATIBILITY.md`, `SCHEMA_AND_TYPES.md`
- Prefer ASCII only in file names.

