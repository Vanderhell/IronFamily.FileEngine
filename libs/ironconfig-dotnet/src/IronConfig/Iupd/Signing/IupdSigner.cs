using System;
using System.IO;
using IronConfig.Crypto;

namespace IronConfig.Iupd.Signing;

/// <summary>
/// Sign and verify IUPD packages deterministically.
/// Uses internal Ed25519 scheme (SommerEngineering, NOT RFC8032) and BLAKE3-256 for hashing.
/// </summary>
public static class IupdSigner
{
    /// <summary>
    /// Compute BLAKE3-256 hash of package file deterministically.
    /// </summary>
    public static byte[] GetIupdPackageHash(string pkgPath)
    {
        if (string.IsNullOrEmpty(pkgPath))
            throw new ArgumentNullException(nameof(pkgPath));

        byte[] pkgBytes = File.ReadAllBytes(pkgPath);
        return Blake3Ieee.Compute(pkgBytes);
    }

    /// <summary>
    /// Compute BLAKE3-256 hash of raw package bytes.
    /// </summary>
    public static byte[] GetIupdPackageHash(byte[] pkgBytes)
    {
        if (pkgBytes == null)
            throw new ArgumentNullException(nameof(pkgBytes));

        return Blake3Ieee.Compute(pkgBytes);
    }

    /// <summary>
    /// Sign package file with seed, write detached .sig file.
    /// Deterministic: same seed + same pkg => same .sig bytes.
    /// </summary>
    public static (IupdSigFile sigFile, IronEdgeError? error) SignPackage(
        string pkgPath,
        string seedPath,
        string? pubPath = null,
        string? outSigPath = null)
    {
        try
        {
            // Read package hash
            byte[] pkgHash = GetIupdPackageHash(pkgPath);

            // Read seed
            if (!File.Exists(seedPath))
                return (default, new IronEdgeError(
                    IronEdgeErrorCategory.Io,
                    0x50,
                    IronEdgeEngine.Iupd,
                    $"Seed file not found: {seedPath}"));

            byte[] seed32 = File.ReadAllBytes(seedPath);
            if (seed32.Length != 32)
                return (default, new IronEdgeError(
                    IronEdgeErrorCategory.InvalidArgument,
                    0x51,
                    IronEdgeEngine.Iupd,
                    $"Seed must be 32 bytes, got {seed32.Length}"));

            // Derive public key from seed (or read provided)
            byte[] pub32;
            if (!string.IsNullOrEmpty(pubPath))
            {
                if (!File.Exists(pubPath))
                    return (default, new IronEdgeError(
                        IronEdgeErrorCategory.Io,
                        0x52,
                        IronEdgeEngine.Iupd,
                        $"Public key file not found: {pubPath}"));

                pub32 = File.ReadAllBytes(pubPath);
                if (pub32.Length != 32)
                    return (default, new IronEdgeError(
                        IronEdgeErrorCategory.InvalidArgument,
                        0x53,
                        IronEdgeEngine.Iupd,
                        $"Public key must be 32 bytes, got {pub32.Length}"));
            }
            else
            {
                // Derive from seed
                pub32 = new byte[32];
                Ed25519.CreatePublicKey(seed32, pub32);
            }

            // Sign package hash
            byte[] sig64 = new byte[64];
            Ed25519.Sign(seed32, pkgHash, sig64);

            // Create signature file
            var sigFile = IupdSigFile.Create(pub32, pkgHash, sig64);

            // Write to disk if requested
            if (!string.IsNullOrEmpty(outSigPath))
            {
                byte[] sigBytes = sigFile.ToBytes();
                File.WriteAllBytes(outSigPath, sigBytes);
            }

            return (sigFile, null);
        }
        catch (Exception ex)
        {
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.Io,
                0x54,
                IronEdgeEngine.Iupd,
                $"Failed to sign package: {ex.Message}",
                innerException: ex));
        }
    }

    /// <summary>
    /// Sign package bytes (in-memory), return signature file (not written to disk).
    /// </summary>
    public static (IupdSigFile sigFile, IronEdgeError? error) SignPackageBytes(
        byte[] pkgBytes,
        byte[] seed32,
        byte[]? pub32 = null)
    {
        try
        {
            if (pkgBytes == null)
                return (default, new IronEdgeError(
                    IronEdgeErrorCategory.InvalidArgument,
                    0x55,
                    IronEdgeEngine.Iupd,
                    "Package bytes cannot be null"));

            if (seed32?.Length != 32)
                return (default, new IronEdgeError(
                    IronEdgeErrorCategory.InvalidArgument,
                    0x56,
                    IronEdgeEngine.Iupd,
                    "Seed must be 32 bytes"));

            // Compute package hash
            byte[] pkgHash = GetIupdPackageHash(pkgBytes);

            // Derive or use provided public key
            byte[] derivedPub = new byte[32];
            if (pub32 == null)
            {
                Ed25519.CreatePublicKey(seed32, derivedPub);
                pub32 = derivedPub;
            }
            else if (pub32.Length != 32)
            {
                return (default, new IronEdgeError(
                    IronEdgeErrorCategory.InvalidArgument,
                    0x57,
                    IronEdgeEngine.Iupd,
                    "Public key must be 32 bytes"));
            }

            // Sign
            byte[] sig64 = new byte[64];
            Ed25519.Sign(seed32, pkgHash, sig64);

            var sigFile = IupdSigFile.Create(pub32, pkgHash, sig64);
            return (sigFile, null);
        }
        catch (Exception ex)
        {
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.InvariantBroken,
                0x58,
                IronEdgeEngine.Iupd,
                $"Unexpected error signing package: {ex.Message}",
                innerException: ex));
        }
    }

    /// <summary>
    /// Verify package against signature file deterministically.
    /// Checks:
    /// 1. Package hash matches signature.PkgHash
    /// 2. Signature is valid for (pubKey, pkgHash)
    /// </summary>
    public static IronEdgeError? VerifyPackage(
        string pkgPath,
        string pubPath,
        string? sigPath = null)
    {
        try
        {
            // Read package
            if (!File.Exists(pkgPath))
                return new IronEdgeError(
                    IronEdgeErrorCategory.Io,
                    0x60,
                    IronEdgeEngine.Iupd,
                    $"Package file not found: {pkgPath}");

            byte[] pkgBytes = File.ReadAllBytes(pkgPath);

            // Read public key
            if (!File.Exists(pubPath))
                return new IronEdgeError(
                    IronEdgeErrorCategory.Io,
                    0x61,
                    IronEdgeEngine.Iupd,
                    $"Public key file not found: {pubPath}");

            byte[] pub32 = File.ReadAllBytes(pubPath);
            if (pub32.Length != 32)
                return new IronEdgeError(
                    IronEdgeErrorCategory.InvalidArgument,
                    0x62,
                    IronEdgeEngine.Iupd,
                    $"Public key must be 32 bytes, got {pub32.Length}");

            // Determine signature path
            string resolvedSigPath = string.IsNullOrEmpty(sigPath)
                ? pkgPath + ".sig"
                : sigPath;

            if (!File.Exists(resolvedSigPath))
                return new IronEdgeError(
                    IronEdgeErrorCategory.Io,
                    0x63,
                    IronEdgeEngine.Iupd,
                    $"Signature file not found: {resolvedSigPath}");

            byte[] sigBytes = File.ReadAllBytes(resolvedSigPath);

            // Verify
            return VerifyPackageBytes(pkgBytes, pub32, sigBytes);
        }
        catch (Exception ex)
        {
            return new IronEdgeError(
                IronEdgeErrorCategory.Io,
                0x64,
                IronEdgeEngine.Iupd,
                $"Failed to verify package: {ex.Message}",
                innerException: ex);
        }
    }

    /// <summary>
    /// Verify package bytes against signature bytes.
    /// </summary>
    public static IronEdgeError? VerifyPackageBytes(
        byte[] pkgBytes,
        byte[] pub32,
        byte[] sigBytes)
    {
        try
        {
            if (pkgBytes == null)
                return new IronEdgeError(
                    IronEdgeErrorCategory.InvalidArgument,
                    0x65,
                    IronEdgeEngine.Iupd,
                    "Package bytes cannot be null");

            if (pub32?.Length != 32)
                return new IronEdgeError(
                    IronEdgeErrorCategory.InvalidArgument,
                    0x66,
                    IronEdgeEngine.Iupd,
                    "Public key must be 32 bytes");

            // Parse signature file
            var (sigFile, parseError) = IupdSigFile.TryRead(sigBytes);
            if (parseError != null)
                return parseError;

            // Compute package hash
            byte[] pkgHash = GetIupdPackageHash(pkgBytes);

            // Check package hash matches signature
            if (!Ed25519.FixedTimeEquals(pkgHash, sigFile.PkgHash))
                return new IronEdgeError(
                    IronEdgeErrorCategory.InvalidChecksum,
                    0x67,
                    IronEdgeEngine.Iupd,
                    "Package hash mismatch");

            // Verify signature
            bool isValid = Ed25519.Verify(pub32, pkgHash, sigFile.Sig);
            if (!isValid)
                return new IronEdgeError(
                    IronEdgeErrorCategory.InvalidSignature,
                    0x68,
                    IronEdgeEngine.Iupd,
                    "Signature verification failed");

            return null;
        }
        catch (Exception ex)
        {
            return new IronEdgeError(
                IronEdgeErrorCategory.InvariantBroken,
                0x69,
                IronEdgeEngine.Iupd,
                $"Unexpected error verifying package: {ex.Message}",
                innerException: ex);
        }
    }
}
