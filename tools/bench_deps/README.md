# MegaBench Dependencies

Working notes for optional external benchmark dependencies. This file is not a source of truth for pinned production versions.

## .NET Libraries

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| protobuf-net | not pinned here | Protobuf serialization | Check benchmark project files |
| FlatSharp | not pinned here | FlatBuffers bindings | Check benchmark project files |
| Cap'n Proto | not pinned here | Cap'n Proto bindings | Check benchmark project files |
| MessagePack | not pinned here | MessagePack serialization | Check benchmark project files |
| Cbor | not pinned here | CBOR serialization | Check benchmark project files |

## External Tools

| Tool | Version | Platform | Purpose |
|------|---------|----------|---------|
| xdelta3 | environment-specific | Linux/Windows | Binary delta patching |
| bsdiff | environment-specific | Linux/Windows | Binary diff |

## Installation

### Windows

```bash
# Install external benchmark tools separately and ensure they are on PATH.
```

### Linux

```bash
# Install external benchmark tools separately and ensure they are on PATH.
```

## Environment Setup

```bash
# Ensure tools are in PATH
export PATH="/path/to/tools/bench_deps/bin:$PATH"

# Verify installations
protoc --version
xdelta3 --version
bsdiff -v
```

---

**Status**: Informational only. Resolve exact versions from benchmark project files or the local benchmark environment before using this as a setup reference.

