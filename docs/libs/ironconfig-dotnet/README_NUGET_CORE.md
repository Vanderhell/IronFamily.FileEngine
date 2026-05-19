# IRONCFG Core

Core library for the IRONCFG deterministic binary configuration format.

**Format:** `.icfg` files (30-80% smaller than JSON)
**Language:** C#, .NET 8.0
**License:** MIT

## Installation

```bash
dotnet add package IronCfg.Core
```

## Quick Start

### Encode JSON to Binary

```csharp
using IronConfig;
using System.Text.Json;

// Load JSON
string json = File.ReadAllText("config.json");
var doc = BjvDocument.FromJson(json);

// Encode to binary
byte[] binary = BjvEncoder.Encode(doc);
File.WriteAllBytes("config.icfg", binary);
```

### Decode Binary to JSON

```csharp
using IronConfig;

// Read binary
byte[] binary = File.ReadAllBytes("config.icfg");
var doc = BjvDocument.FromBinary(binary);

// Convert to JSON
string json = doc.ToJson();
Console.WriteLine(json);
```

### Validate Binary Files

```csharp
using IronConfig;

try
{
    byte[] data = File.ReadAllBytes("config.icfg");
    var doc = BjvDocument.FromBinary(data);
    Console.WriteLine("File is valid");
}
catch (Exception ex)
{
    Console.WriteLine($"Validation failed: {ex.Message}");
}
```

## Features

- **Canonical Encoding**: Same logical data → identical bytes (deterministic)
- **Variable String Pool (VSP)**: Automatic string deduplication
- **CRC Corruption Detection**: Built-in checksum verification
- **Safe Parsing**: Bounds-checked, fuzz-tested
- **Compression**: 30-80% smaller than JSON (workload-dependent)

## Encryption

For encrypted `.icfs` files, see the **IronCfg.Crypto** package which provides:
- `Bjx.EncryptBjx1()` for .icfg → .icfs
- `Bjx.DecryptBjx1()` for .icfs → .icfg
- AES-256-GCM + PBKDF2-HMAC-SHA256 (100,000 iterations) key derivation (current implementation)

## Documentation

See the [full README](https://github.com/Vanderhell/ironcfg) for:
- CLI tool installation
- Format specifications
- API reference
- Examples

## Support

- **GitHub Issues**: https://github.com/Vanderhell/ironcfg/issues
- **Repository**: https://github.com/Vanderhell/ironcfg

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
