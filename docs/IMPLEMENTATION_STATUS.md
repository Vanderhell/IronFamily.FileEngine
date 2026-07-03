# Implementation Status

This document separates implementation status from normative specifications.

## Repository-wide status

- Active engines: `ICFG`, `ILOG`, `IUPD`
- Canonical native implementation target: portable `C99`
- Current host/reference surface: `C#` and `.NET`
- Production readiness is not claimed by this repository documentation

## Surface status

| Surface | Role | Status |
|---|---|---|
| `libs/ironconfig-dotnet` | reference implementation, tooling, tests, vectors | present |
| `native/ironfamily_c` | strict native verification and adjacent native helpers | present |
| `libs/ironcfg-c` | native C API surface with active and historical code | present |

## Parity status

- Managed and native surfaces are not described as equivalent by default.
- Claims about parity must be backed by explicit tests or gates, not by documentation wording alone.
