# IUPD Use Cases

**Status**: Informative guide (non-normative)  
**Last Updated**: 2026-05-19  
**Scope**: When to use `IUPD` vs. other packaging formats

This document is guidance only. For format requirements, see `SPEC.md`.

## Good fits for IUPD

IUPD is designed for distributing and validating large binary payloads where you need deterministic structure and (optionally) cryptographic verification.

Typical good fits:

1. **Firmware / OTA updates**
2. **Large single-file binaries** (application bundles, monolithic assets)
3. **Model weight distribution** (large immutable blobs)
4. **Incremental update flows** where the consumer applies updates in a controlled sequence

## Not a good fit for IUPD

Prefer an archive/container format (zip/tar/7z/OCI) when the primary job is “bundle many files and directories”.

Typical poor fits:

1. **General file archiving** (folders + many files)
2. **Human-authored document bundles** (docs + images + spreadsheets)

## Quick profile mapping

If you are using `IUPD`, pick the profile based on your threat model and constraints:

- **Untrusted transport/storage** → `SECURE` or `OPTIMIZED`
- **Need smaller files + crypto** → `OPTIMIZED`
- **Trusted environment + smallest CPU** → `MINIMAL`
- **Need compression but no crypto** → `FAST`
- **Incremental/patch workflow** → `INCREMENTAL`

