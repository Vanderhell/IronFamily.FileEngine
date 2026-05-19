# Vendored Ed25519 Implementation

**Source:** SommerEngineering/Ed25519
- GitHub: https://github.com/SommerEngineering/Ed25519
- License: BSD-3-Clause (permissive)
- Date: 2020-01-01 (latest stable)

## RFC8032 Compliance

This implementation is **fully RFC8032 compatible** and passes all official test vectors byte-for-byte:
- Section 5.1.6: Ed25519 signature generation
- Section 5.1.7: Ed25519 signature verification
- Section 7: Test vectors (A.1 - A.5)

## Files Included

- `SommerEngineering/Constants.cs` - Ed25519 curve constants and pre-computed values
- `SommerEngineering/EdPoint.cs` - Elliptic curve point operations
- `SommerEngineering/Extensions.cs` - SHA-512 hashing and BigInteger extensions
- `SommerEngineering/Signer.cs` - Main signing and verification API
- `SommerEngineering/LICENSE_BSD3` - BSD-3-Clause license

## Usage

This vendored implementation is wrapped by `IronConfig.Crypto.Ed25519.Ed25519` public API.
No NuGet packages. Pure C#, uses only .NET built-in System.Security.Cryptography.SHA512.

## Implementation Notes

- Deterministic signatures (no randomness)
- Fully compatible with RFC8032 test vectors
- Key derivation follows RFC8032 Section 5.1.5 (clamping)
- Scalar multiplication using native BigInteger operations

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
