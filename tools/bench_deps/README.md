# MegaBench Dependencies

Pinned versions for external tools and libraries used in competitor benchmarks.

## .NET Libraries

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| protobuf-net | TBD | Protobuf serialization | Specify in .csproj |
| FlatSharp | TBD | FlatBuffers bindings | TBD |
| Cap'n Proto | TBD | Cap'n Proto bindings | TBD |
| MessagePack | TBD | MessagePack serialization | TBD |
| Cbor | TBD | CBOR serialization | TBD |

## External Tools

| Tool | Version | Platform | Purpose |
|------|---------|----------|---------|
| xdelta3 | TBD | Linux/Windows | Binary delta patching |
| bsdiff | TBD | Linux/Windows | Binary diff |

## Installation

### Windows

```bash
# TODO: Document Windows installation of external tools
```

### Linux

```bash
# TODO: Document Linux installation of external tools
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

**Status**: Skeleton (versions TBD during PHASE 3)

