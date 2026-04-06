# Engine Truth Summary

This document is a strict code-backed summary of the engines currently present in the repository.

It uses three evidence levels only:

- `EXECUTED_NOW`: confirmed by running tests or binaries in this session
- `CODE_CONFIRMED`: confirmed directly from source code and public headers
- `NOT_CONFIRMED_NOW`: code or tests exist, but were not freshly executed in this session

## Scope

Primary engines covered here:

- `IUPD`
- `ILOG`
- `ICFG`

Historical predecessor codecs are outside the supported scope and are not part of the active truth summary.

## At A Glance

| Engine | .NET status now | Fresh .NET result | Native status now | Fresh native result | Overall truth level |
|---|---|---|---|---|---|
| `IUPD` | Implemented and test-verified | `253/253 passed` | Native verifier/apply surface present and partially executed | `6/6`, `10/10`, `2/2`, `1/1` passed across executed native tests | `EXECUTED_NOW` for primary paths |
| `ILOG` | Implemented and test-verified | `144/144 passed` | Public C API exists in `libs/ironcfg-c`, but not freshly executed in this session | none run fresh for `libs/ironcfg-c` ILOG tests | mixed: `.NET EXECUTED_NOW`, native `CODE_CONFIRMED` |
| `ICFG` | Implemented and test-verified | `128/128 passed` | Native C validation and determinism paths executed successfully | `8/8`, `5/5` passed | `EXECUTED_NOW` for primary paths |

## Fresh Verification Performed In This Session

### .NET test projects

- `IronConfig.Iupd.Tests`: `253 passed, 0 failed, 0 skipped`
- `IronConfig.ILog.Tests`: `144 passed, 0 failed, 0 skipped`
- `IronConfig.IronCfgTests`: `128 passed, 0 failed, 0 skipped`

### Native C executables run successfully

- `native/build/tests/Release/test_ironcfg.exe`: `8 passed, 0 failed`
- `native/build/tests/Release/test_ironcfg_determinism.exe`: `5 passed, 0 failed`
- `native/build/tests/Release/test_iupd_vectors.exe`: `6 passed, 0 failed`
- `native/build/tests/Release/test_incremental_metadata.exe`: `10 passed, 0 failed`
- `native/build/tests/Release/test_delta2_vectors.exe`: `2 passed, 0 failed`
- `native/build/tests/Release/test_diff_vectors.exe`: `1 passed, 0 failed`
- `native/build_crcfix/tests/Release/test_crc32_kat.exe`: `PASS`

### Native C execution problems observed

- `ctest --test-dir native/build -C Release -N` is misconfigured in this build tree and tries to write under a wrong path.
- the old binary under `native/build/tests/Release/test_crc32_kat.exe` was stale and failed on output-file write; the fixed fresh binary in `native/build_crcfix/tests/Release/` now passes

## Code Evidence Anchors

These are the primary line-level anchors used for the claims above.

### IUPD

- `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs:7` - profile enum
- `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs:69` - BLAKE3 requirement
- `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs:74` - compression support
- `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs:79` - dependency support
- `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs:85` - strict signature requirement
- `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs:96` - incremental profile check
- `native/ironfamily_c/include/ironfamily/iupd_reader.h:49` - strict native verifier entry point
- `native/ironfamily_c/include/ironfamily/iupd_incremental_metadata.h:49` - incremental metadata parse entry point
- `native/ironfamily_c/include/ironfamily/delta2_apply.h:59` - delta2 apply entry point
- `libs/ironcfg-c/include/ironcfg/iupd.h:121` - `iupd_open`
- `libs/ironcfg-c/include/ironcfg/iupd.h:134` - `iupd_validate_fast`
- `libs/ironcfg-c/include/ironcfg/iupd.h:147` - `iupd_validate_strict`

### ILOG

- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogProfile.cs:7` - profile enum
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogProfile.cs:57` - BLAKE3 requirement
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogProfile.cs:63` - CRC32 support
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogProfile.cs:69` - compression support
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogProfile.cs:75` - search support
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogProfile.cs:81` - sealing support
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogProfile.cs:108` - flag mapping
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogDecoder.cs:22` - `L0`
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogDecoder.cs:23` - `L1`
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogDecoder.cs:24` - `L2`
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogDecoder.cs:25` - `L3`
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogDecoder.cs:26` - `L4`
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogDecoder.cs:189` - witness-chain verification branch
- `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogDecoder.cs:219` - single-block witness assumption
- `libs/ironcfg-c/include/ironcfg/ilog.h:128` - `ilog_open`
- `libs/ironcfg-c/include/ironcfg/ilog.h:148` - `ilog_validate_fast`
- `libs/ironcfg-c/include/ironcfg/ilog.h:169` - `ilog_validate_strict`

### ICFG

- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgHeader.cs:12` - current writer version constant
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgHeader.cs:64` - CRC32 header flag
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgHeader.cs:65` - BLAKE3 header flag
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgHeader.cs:66` - embedded schema flag
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgHeader.cs:86` - reader accepts versions `1` and `2`
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgEncoder.cs:40` - schema encoding
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgEncoder.cs:130` - schema encoder implementation
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgValidator.cs:65` - strict validation entry
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgValidator.cs:138` - CRC32 validation
- `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgValidator.cs:154` - BLAKE3 validation
- `libs/ironcfg-c/include/ironcfg/ironcfg.h:84` - `ironcfg_open`
- `libs/ironcfg-c/include/ironcfg/ironcfg.h:88` - `ironcfg_validate_fast`
- `libs/ironcfg-c/include/ironcfg/ironcfg.h:91` - `ironcfg_validate_strict`

## Feature Matrix

This table is intentionally limited to the currently relevant engines only.

| Engine | Feature | Primary code file | Primary test / execution evidence | Status |
|---|---|---|---|---|
| `IUPD` | Profile model | `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdProfile.cs` | `IupdProfileTests.cs` | `EXECUTED_NOW` |
| `IUPD` | Reader validation | `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdReader.cs` | `IupdReaderTests.cs` | `EXECUTED_NOW` |
| `IUPD` | Incremental apply | `libs/ironconfig-dotnet/src/IronConfig/Iupd/IupdApplyEngine.cs` | `IupdIncrementalApplyTests.cs` | `EXECUTED_NOW` |
| `IUPD` | Signing / trust | `libs/ironconfig-dotnet/src/IronConfig/Iupd/Signing/IupdSigner.cs` | `IupdSigningTests.cs`, trust-store tests in `IronConfig.Iupd.Tests` | `EXECUTED_NOW` |
| `IUPD` | Native strict verify | `native/ironfamily_c/include/ironfamily/iupd_reader.h` | `native/build/tests/Release/test_iupd_vectors.exe` | `EXECUTED_NOW` |
| `IUPD` | Native incremental metadata | `native/ironfamily_c/include/ironfamily/iupd_incremental_metadata.h` | `native/build/tests/Release/test_incremental_metadata.exe` | `EXECUTED_NOW` |
| `IUPD` | Native delta2 apply | `native/ironfamily_c/include/ironfamily/delta2_apply.h` | `native/build/tests/Release/test_delta2_vectors.exe` | `EXECUTED_NOW` |
| `IUPD` | Native diff apply | `native/ironfamily_c/include/ironfamily/diff_apply.h` | `native/build/tests/Release/test_diff_vectors.exe` | `EXECUTED_NOW` |
| `ILOG` | Profile model | `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogProfile.cs` | `IlogEncoderTests.cs`, profile coverage inside `IronConfig.ILog.Tests` | `EXECUTED_NOW` |
| `ILOG` | Encode / decode | `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogEncoder.cs`, `IlogDecoder.cs` | `IlogEncoderTests.cs` | `EXECUTED_NOW` |
| `ILOG` | Streaming path | `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogReader.cs` | `IlogStreamingTests.cs` | `EXECUTED_NOW` |
| `ILOG` | Witness path | `libs/ironconfig-dotnet/src/IronConfig.ILog/IlogDecoder.cs` | `IlogWitnessChainTests.cs` | `EXECUTED_NOW` |
| `ILOG` | Native open / validate API | `libs/ironcfg-c/include/ironcfg/ilog.h` | `test_ilog_basic.c`, `test_ilog_validation.c` exist but not run fresh | `CODE_CONFIRMED` |
| `ICFG` | Header / version model | `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgHeader.cs` | `RuntimeVerifyTests.cs`, `SpecLockTests.cs` | `EXECUTED_NOW` |
| `ICFG` | Encoder | `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgEncoder.cs` | `IronCfgEncoderTests.cs` | `EXECUTED_NOW` |
| `ICFG` | Corruption handling | `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgValidator.cs` | `IronCfgCorruptionTests.cs` | `EXECUTED_NOW` |
| `ICFG` | Invalid input handling | `libs/ironconfig-dotnet/src/IronConfig/IronCfg/IronCfgValidator.cs` | `IronCfgInvalidInputGauntletTests.cs` | `EXECUTED_NOW` |
| `ICFG` | Native validation | `libs/ironcfg-c/include/ironcfg/ironcfg.h` | `native/build/tests/Release/test_ironcfg.exe` | `EXECUTED_NOW` |
| `ICFG` | Native determinism | `libs/ironcfg-c/src/ironcfg_encode.c` | `native/build/tests/Release/test_ironcfg_determinism.exe` | `EXECUTED_NOW` |
| `CRC32 KAT` | Native known-answer check | `native/tests/test_crc32_kat.c` | `native/build_crcfix/tests/Release/test_crc32_kat.exe` | `EXECUTED_NOW` |

## IUPD

### .NET implementation

Evidence: `CODE_CONFIRMED`

Core implementation files under `libs/ironconfig-dotnet/src/IronConfig/Iupd/`:

- `IupdReader.cs`
- `IupdWriter.cs`
- `IupdApplyEngine.cs`
- `IupdApplyRecovery.cs`
- `IupdIncrementalMetadata.cs`
- `IupdPayloadCompression.cs`
- `IupdParallel.cs`
- `IupdSigner.cs`
- `IupdTrustStoreV1.cs`
- `IupdDeltaV1.cs`
- `IupdDeltaV2Cdc.cs`

### Profiles

Evidence: `CODE_CONFIRMED`

Defined in `IupdProfile.cs`:

| Profile | Value | Compression | BLAKE3 | Dependencies | Signature strict | Incremental |
|---|---:|---|---|---|---|---|
| `MINIMAL` | `0x00` | No | No | No | No | No |
| `FAST` | `0x01` | Yes | No | No | No | No |
| `SECURE` | `0x02` | No | Yes | Yes | Yes | No |
| `OPTIMIZED` | `0x03` | Yes | Yes | Yes | Yes | No |
| `INCREMENTAL` | `0x04` | Yes | Yes | Yes | Yes | Yes |

### Functional surface

Evidence: `CODE_CONFIRMED`

Confirmed in code:

- package reading
- package writing
- apply engine
- apply recovery
- Ed25519 signing
- trust-store based verification
- manifest and witness enforcement
- compression support
- delta v1 support
- delta v2 / `IRONDEL2` support
- incremental metadata parsing

### Test state

Evidence: `EXECUTED_NOW`

Fresh .NET result:

- `IronConfig.Iupd.Tests`: `253/253 passed`

Test coverage categories visible from test files:

- roundtrip
- reader validation
- profile behavior
- apply engine
- corruption handling
- delta
- delta v2 apply
- incremental apply
- signatures
- trust store
- update sequence
- back-compat

### Native C state

There are two C code paths in this repository.

#### `native/ironfamily_c`

Evidence: `CODE_CONFIRMED` plus partial `EXECUTED_NOW`

Public headers confirm:

- strict verifier in `iupd_reader.h`
- incremental metadata parsing in `iupd_incremental_metadata.h`
- delta v2 apply in `delta2_apply.h`
- diff apply in `diff_apply.h`
- OTA apply in `ota_apply.h`

Fresh native execution:

- `test_iupd_vectors.exe`: `6/6 passed`
- `test_incremental_metadata.exe`: `10/10 passed`
- `test_delta2_vectors.exe`: `2/2 passed`
- `test_diff_vectors.exe`: `1/1 passed`

What is freshly confirmed now:

- strict verification path
- incremental metadata parsing
- delta v2 apply
- diff apply vector path

What is only code-confirmed here:

- OTA apply integration

#### `libs/ironcfg-c`

Evidence: `CODE_CONFIRMED`

Public API in `include/ironcfg/iupd.h` confirms:

- open
- fast validation
- strict validation
- apply iteration

Tests exist in `libs/ironcfg-c/tests/`:

- `test_iupd_basic.c`
- `test_iupd_apply.c`
- `test_iupd_validation.c`

These specific `libs/ironcfg-c` IUPD tests were not freshly executed in this session.

## ILOG

### .NET implementation

Evidence: `CODE_CONFIRMED`

Core implementation files under `libs/ironconfig-dotnet/src/IronConfig.ILog/`:

- `IlogEncoder.cs`
- `IlogDecoder.cs`
- `IlogReader.cs`
- `IlogCompressor.cs`
- `IlogEncodeOptions.cs`
- `IlogProfile.cs`

### Profiles and layers

Evidence: `CODE_CONFIRMED`

Defined in `IlogProfile.cs` and reinforced by block constants in `IlogDecoder.cs`.

Layer / block model:

- `L0`: data
- `L1`: TOC
- `L2`: index
- `L3`: archive/compressed payload
- `L4`: seal

Profiles:

| Profile | Flags | Layers / blocks | CRC32 | BLAKE3 | Search | Compression | Witness / signing path |
|---|---:|---|---|---|---|---|---|
| `MINIMAL` | `0x01` | `L0 + L1` | No | No | No | No | No |
| `INTEGRITY` | `0x03` | `L0 + L1 + L4` | Yes | No | No | No | Seal only |
| `SEARCHABLE` | `0x09` | `L0 + L1 + L2` | No | No | Yes | No | No |
| `ARCHIVED` | `0x11` | `L1 + L3` | No | No | No | Yes | No |
| `AUDITED` | `0x27` | `L0 + L1 + L4` | Yes | Yes | No | No | Witness-enabled audited path |

### Functional surface

Evidence: `CODE_CONFIRMED`

Confirmed in code:

- encoding
- decoding
- fast and strict reading paths
- archive decompression path
- L2 index decoding
- L4 seal handling
- witness-chain validation logic in the decoder

### Test state

Evidence: `EXECUTED_NOW`

Fresh .NET result:

- `IronConfig.ILog.Tests`: `144/144 passed`

Coverage categories visible from test files:

- encoder
- compressor
- parity
- streaming
- strict regression
- profile back-compat
- witness chain
- corruption gauntlet
- guard/runtime checks
- spec-lock checks

### Native C state

#### `libs/ironcfg-c`

Evidence: `CODE_CONFIRMED`

Public API in `include/ironcfg/ilog.h` confirms:

- `ilog_open`
- `ilog_validate_fast`
- `ilog_validate_strict`
- `ilog_verify_crc32`
- record count and block count inspection

The header explicitly models:

- `L0` through `L4` offsets
- CRC32 and BLAKE3 integrity flags
- strict vs fast validation

Tests exist in `libs/ironcfg-c/tests/`:

- `test_ilog_basic.c`
- `test_ilog_validation.c`

These were not freshly executed in this session.

#### `native/ironfamily_c`

Evidence: `CODE_CONFIRMED`

No dedicated native `ILOG` implementation was confirmed under `native/ironfamily_c`.

## ICFG

### .NET implementation

Evidence: `CODE_CONFIRMED`

Core implementation files under `libs/ironconfig-dotnet/src/IronConfig/IronCfg/`:

- `IronCfgHeader.cs`
- `IronCfgEncoder.cs`
- `IronCfgValidator.cs`
- `IronCfgValueReader.cs`
- `IronCfgView.cs`
- `IronCfgTypeSystem.cs`

### Format surface

Evidence: `CODE_CONFIRMED`

Confirmed in code:

- parser accepts version `1` and `2`
- current writer/header constant is version `2`
- fixed header
- schema block
- optional string pool
- data block
- optional CRC32
- optional BLAKE3
- strict validation path
- fast validation path
- array element schema support for schema version `>= 2`

### Type-system / validation facts

Evidence: `CODE_CONFIRMED`

Confirmed in code:

- type-system validation exists
- schema parsing exists
- field-order validation exists
- CRC32 mismatch and BLAKE3 mismatch are explicit error codes
- strict validator verifies schema, data, CRC32, and BLAKE3

### Test state

Evidence: `EXECUTED_NOW`

Fresh .NET result:

- `IronConfig.IronCfgTests`: `128/128 passed`

Coverage categories visible from test files:

- value reader
- encoder
- corruption handling
- invalid input gauntlet
- runtime verify
- determinism / non-regression perf smoke
- guard tests
- spec-lock tests

### Native C state

#### `libs/ironcfg-c`

Evidence: `CODE_CONFIRMED`

Public API in `include/ironcfg/ironcfg.h` confirms:

- open
- fast validation
- strict validation
- root access
- schema access
- string-pool access
- header access
- CRC32 / BLAKE3 / embedded-schema flags

Tests exist in `libs/ironcfg-c/tests/`:

- `test_ironcfg.c`
- `test_ironcfg_determinism.c`

#### `native/ironfamily_c`

Evidence: `EXECUTED_NOW`

Fresh native execution:

- `test_ironcfg.exe`: `8/8 passed`
- `test_ironcfg_determinism.exe`: `5/5 passed`

What is freshly confirmed now:

- basic validation failures
- invalid magic/version/flags handling
- bounds and file-size validation
- deterministic encoding
- CRC32 validity check path
- float normalization and NaN rejection
- deterministic field ordering

## Historical Predecessors

The repository still contains historical predecessor codecs under `libs/ironcfg-c/src/`, but they are outside the supported scope and are not part of the active engine truth summary:

- `bjx`
- `icf2`
- `icfx`
- `icxs`

## Hard Truth Summary

### What is fully confirmed now

- `.NET IUPD` is implemented and its primary test project passes fresh: `253/253`
- `.NET ILOG` is implemented and its primary test project passes fresh: `144/144`
- `.NET ICFG` is implemented and its primary test project passes fresh: `128/128`
- native `IRONCFG` executable tests run and pass fresh: `8/8` and `5/5`
- native `IUPD` verifier / incremental / delta executables run and pass fresh: `6/6`, `10/10`, `2/2`, `1/1`

### What is code-confirmed but not freshly executed now

- `libs/ironcfg-c` public C APIs for `ILOG`, `IUPD`, and `IRONCFG`
- `libs/ironcfg-c` unit tests for active `ILOG`, `IUPD`, and `ICFG` APIs
- some native integration paths behind the broken `ctest` setup

### What should not be claimed as fresh truth from this session

- full `ctest` pass state for `native/build`
- any current result for historical predecessor codecs not explicitly executed above

## Safe Public Claims

- `IUPD`, `ILOG`, and `ICFG` are the active engines in this repository.
- The primary `.NET` test projects for all three active engines were executed in this session and passed.
- `IUPD` currently exposes five named `.NET` profiles: `MINIMAL`, `FAST`, `SECURE`, `OPTIMIZED`, `INCREMENTAL`.
- `ILOG` currently exposes five named `.NET` profiles: `MINIMAL`, `INTEGRITY`, `SEARCHABLE`, `ARCHIVED`, `AUDITED`.
- `ILOG` uses a layered block model with `L0` through `L4`.
- `ICFG` currently has reader support for format versions `1` and `2`, while the current writer/header constant is `2`.
- Native `ICFG` validation and deterministic encoding paths were executed successfully in this session.
- Native `IUPD` verification, incremental metadata parsing, delta v2 apply, diff apply, and CRC32 known-answer checks were executed successfully in this session.

## Claims To Avoid

- Do not claim full fresh native test coverage for the entire `native/build` tree.
- Do not claim fresh execution coverage for `libs/ironcfg-c` `ILOG` and `IUPD` unit tests unless they are run in a later session.
- Do not claim current support or validation status for historical predecessor codecs as part of the active engine surface.
