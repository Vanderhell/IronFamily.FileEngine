# Security Model

This document describes repository-level security constraints for the active engines.

## Scope

- `ICFG`: parse and validate untrusted configuration payloads
- `ILOG`: parse and validate untrusted log containers
- `IUPD`: verify and apply update packages under explicit validation rules

## Current security posture

- Fail-closed validation is part of the public intent for native and managed validation paths.
- Public documentation only claims security properties that are visible in code and tests.
- The repository does not claim that all native and managed paths have identical security coverage.

## Current limitations

- The default native build does not depend on `OpenSSL`.
- Optional legacy codec builds may still require `OpenSSL`.
- Embedded portability should not be assumed from the full native tree while optional legacy dependencies remain.
- Historical codecs present in source are outside the active security model.

## Reporting

For vulnerability reporting workflow, see [SECURITY.md](../SECURITY.md).
