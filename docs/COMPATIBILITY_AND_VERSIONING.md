# Compatibility and Versioning

This repository keeps active scope limited to `ICFG`, `ILOG`, and `IUPD`.

## Active format/version facts

- `ICFG`: the current `.NET` writer constant is version `2`; the `.NET` reader accepts versions `1` and `2`.
- `ILOG`: the current format documents a single active versioned surface in the repository.
- `IUPD`: the `.NET` reader supports `v1` and `v2`; the `.NET` writer emits `v2`; the strict native verifier is a `v2` surface.

## Surface compatibility rules

- `native/ironfamily_c` and `libs/ironcfg-c` are different native surfaces and must not be described as one equivalent implementation.
- `.NET` code remains in scope as reference implementation, tooling, vector generation, and bindings support.
- Historical codecs in the repository are not part of the active compatibility promise.

## Versioning policy

- Public breaking changes should be documented in [CHANGELOG.md](../CHANGELOG.md).
- Release tags must be based on inspected repository state, not on documentation examples.
- Compatibility claims in documentation must match public headers, constants, and shipped commands in this repository.
