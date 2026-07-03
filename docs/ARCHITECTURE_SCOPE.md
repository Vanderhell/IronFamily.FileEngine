# Architecture Scope

This document defines the active repository scope for public documentation.

## Active Engines

- `ICFG`
- `ILOG`
- `IUPD`

## Scope Rules

- Historical codecs and predecessor formats are not active supported engines and are outside the active API surface.
- The canonical native implementation target is portable `C99`.
- `C#` remains in scope as host tooling, reference implementation, vector generator, parity oracle, and binding until native parity is proven.
- `C++` is not an independent implementation target for this repository.
- Documentation must not present historical codecs as active supported engines.
