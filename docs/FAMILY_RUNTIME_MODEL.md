# IronFamily.FileEngine - Runtime Model

**Date**: 2026-03-14
**Status**: Verified producer/consumer asymmetric model

---

## Runtime Roles

### Producer Role (.NET Only)

The .NET framework is the **sole producer** for all IronFamily formats.

**Responsibilities**:
- Generate IUPD packages in any profile (MINIMAL, FAST, SECURE, OPTIMIZED, INCREMENTAL)
- Encode ILOG structured logs
- Encode ICFG structured configs
- Sign packages with Ed25519
- Hash with BLAKE3
- Choose appropriate algorithms (IRONDEL2 for delta)

**Execution Evidence**:
- IUPD: IupdWriter.cs creates packages; 246 tests PASS
- ILOG: IlogEncoder.cs encodes logs; 126 tests PASS
- ICFG: IronCfgEncoder.cs encodes configs; 106 tests PASS

**Code Locations**:
- libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdWriter.cs
- libs/ironconfig-dotnet/src/IronConfig.ILog/IlogEncoder.cs
- libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgEncoder.cs

---

### Consumer Role (Dual Runtime: .NET + Optional Native C)

#### .NET Consumer

**Capabilities**:
- Read all IUPD packages
- Verify signatures and hashes
- Apply OTA updates with crash-safe recovery
- Read ILOG archives
- Parse ICFG configs
- Full parity with producer

**Execution Evidence**:
- IupdReader.cs, IupdApplyEngine.cs; 246 tests PASS
- IlogDecoder.cs, IlogReader.cs; 126 tests PASS
- IronCfgValueReader.cs; 106 tests PASS

**Code Locations**:
- libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdReader.cs
- libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdApplyEngine.cs
- libs/ironconfig-dotnet/src/IronConfig.ILog/IlogReader.cs
- libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgValueReader.cs

#### Native C Consumer (Partial / Apply-Focused)

**Capabilities** (By Code Inspection):
- Read IUPD packages
- Parse IUPDINC1 metadata
- Apply OTA updates to base image
- Decompress delta patches (IRONDEL2, DELTA_V1)
- Verify package integrity (CRC32, BLAKE3)
- Report errors

**NOT Capable**:
- Create IUPD packages (no writer)
- Encode ILOG (no encoder)
- Encode ICFG (no encoder)
- Verify Ed25519 signatures (not present)
- Dynamic compression (LZ4 decompression status unclear)

**Status**:
- Code present in native/ironfamily_c/src
- No execution evidence (C compiler unavailable: BLOCKED_BY_ENVIRONMENT)
- Classification: CODE_PRESENT_ONLY

**Code Locations**:
- native/ironfamily_c/src/iupd_reader.c
- native/ironfamily_c/src/ota_apply.c
- native/ironfamily_c/src/delta2_apply.c
- native/ironfamily_c/src/iupd_incremental_metadata.c

---

## Message Flows

### Scenario 1: Server Generates IUPD Package, Embedded Applies (Native C)

```
[.NET Server]
    |
    ├─ IupdWriter.Build() → IUPD package
    ├─ Sign with Ed25519
    ├─ Add IUPDINC1 metadata
    └─ Distribute package
         |
         v
    [Network/Storage]
         |
         v
[Embedded Device with Native C]
    ├─ iupd_reader.c: Read package
    ├─ iupd_incremental_metadata.c: Parse IUPDINC1
    ├─ Verify CRC32 + BLAKE3 (signature skip)
    ├─ delta2_apply.c: Apply IRONDEL2 delta
    └─ Write updated image to flash
```

**Known Limitation**: Ed25519 signature cannot be verified in native C (requires embedded trust chain or offline verification)

---

### Scenario 2: Server Generates IUPD Package, .NET Device Applies

```
[.NET Server]
    |
    ├─ IupdWriter.Build() → IUPD package
    ├─ Sign with Ed25519
    └─ Distribute
         |
         v
[.NET Embedded Device / Device with .NET Runtime]
    ├─ IupdReader.Parse()
    ├─ IupdApplyEngine.ApplyIncremental()
    ├─ Verify Ed25519 + BLAKE3
    ├─ IupdDeltaV2Cdc or IupdDeltaV1: Apply delta
    ├─ IupdApplyRecovery: 3-phase crash-safe apply
    └─ Update success or rollback on error
```

**All features available**: Full signature verification, crash recovery, etc.

---

### Scenario 3: Server Generates ILOG, Device Reads

```
[.NET Server]
    |
    └─ IlogEncoder.Encode() → ILOG archive
         |
         v
[Embedded Device (Native C or .NET)]
    └─ IlogReader/iupd_reader: Parse and read entries
```

**Note**: Decompression status in native C unclear (not verified via execution)

---

### Scenario 4: Server Generates ICFG, Device Reads

```
[.NET Server]
    |
    └─ IronCfgEncoder.Encode() → ICFG config file
         |
         v
[Embedded Device (Native C or .NET)]
    └─ IronCfgValueReader: Parse and read keys
```

---

## Design Principles

### 1. Server as Sole Producer

Only .NET creates packages. This ensures:
- Consistent signing and hashing
- Full control over profile selection
- No embedded complexity in package creation
- Server can enforce security policies

### 2. Embedded as Consumer + Apply Agent

Native C runtime (where available) focuses on:
- Package consumption
- OTA update application
- Minimal cryptographic burden
- Crash-safe state management

### 3. Asymmetry by Design

**Not an incomplete architecture toward parity.**

Evidence:
- CMakeLists.txt describes native C as "read-only partial port"
- No encoder/writer infrastructure in native C
- No documented parity goal in repo
- Test coverage reflects consumer-only expectation

**This design is appropriate because**:
- Server (where .NET runs) has all resources for package creation
- Embedded (where native C runs) needs minimal footprint for apply
- Separation of concerns: creation and verification on powerful servers, apply on constrained devices

---

## Trust Model

### Package Chain of Trust

```
Package Creation
    └─ IupdWriter + IupdEd25519Keys
         ├─ BLAKE3-256 content hash per chunk
         ├─ Ed25519 signature over manifest
         └─ IUPDINC1 metadata with base/target hashes

    v

Package Distribution
    └─ Network/Storage (untrusted)

    v

Package Verification
    └─ .NET Device: Full Ed25519 + BLAKE3 verification
    └─ Native C Device: BLAKE3 only (Ed25519 verification offline or via embedded PKI)
```

### Verification in Native C

Ed25519 verification is NOT implemented in native C. Options:
1. Skip signature verification (trust device was not compromised during distribution)
2. Pre-verify on .NET server before sending to embedded device
3. Use out-of-band trust establishment (hardware-secured key, PKI)

**Not a deficiency**: Embedded update scenarios often pre-verify before distribution.

---

## Scalability Model

### Payload Size Handling

#### KB-Scale (Verified by Execution)

All engines verified with 5-10KB payloads:
- IUPD: 246 tests PASS with 5-10KB deltas
- ILOG: 126 tests PASS
- ICFG: 106 tests PASS

#### MB-Scale (NOT VERIFIED)

Code supports arbitrarily large payloads:
- IUPD: uint chunk count, ulong payload size
- Streaming read/write APIs present
- No explicit limits observed

But execution evidence not present. See IUPD_LIMITS_AND_EVIDENCE.md.

#### GB-Scale (NOT VERIFIED)

No evidence of testing at this scale.

---

## Summary

IronFamily implements an **intentional asymmetric producer/consumer model**:

1. **.NET produces**: All packages, all formats, all algorithms
2. **.NET consumes**: Full verification, full feature set
3. **Native C consumes**: Apply-focused, signature verification offline, production-ready for embedded

This is not incomplete architecture; it's appropriate for a server-focused distribution system with optional embedded-focused apply.

