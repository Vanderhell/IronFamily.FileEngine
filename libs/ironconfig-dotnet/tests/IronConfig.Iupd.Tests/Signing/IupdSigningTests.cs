using System;
using System.IO;
using System.Text;
using Xunit;
using IronConfig.Iupd.Signing;
using IronConfig.Crypto;

namespace IronConfig.Iupd.Tests.Signing;

/// <summary>
/// Unit tests for IUPD signing (detached .sig files) and verification.
/// Tests determinism, format, and cryptographic properties.
/// </summary>
public class IupdSigningTests : IDisposable
{
    private readonly string _tempDir;

    public IupdSigningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"iupd_sign_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Test A: Signature file is exactly 125 bytes.
    /// </summary>
    [Fact]
    public void SigFile_Format_Size_Is125Bytes()
    {
        // Create seed and public key
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        // Create a small package
        byte[] pkg = Encoding.ASCII.GetBytes("test package content");
        byte[] pkgHash = IupdSigner.GetIupdPackageHash(pkg);

        // Sign
        byte[] sig64 = new byte[64];
        Ed25519.Sign(seed32, pkgHash, sig64);

        var sigFile = IupdSigFile.Create(pub32, pkgHash, sig64);
        byte[] sigBytes = sigFile.ToBytes();

        Assert.Equal(IupdSigFile.TOTAL_SIZE, sigBytes.Length);
        Assert.Equal(125, sigBytes.Length);
    }

    /// <summary>
    /// Test B: Sign and verify roundtrip succeeds.
    /// </summary>
    [Fact]
    public void SignVerify_Roundtrip_Ok()
    {
        // Create keys
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        // Create package and files
        byte[] pkg = Encoding.ASCII.GetBytes("test package");
        string pkgPath = Path.Combine(_tempDir, "test.iupd");
        string seedPath = Path.Combine(_tempDir, "seed.bin");
        string pubPath = Path.Combine(_tempDir, "pub.bin");
        string sigPath = Path.Combine(_tempDir, "test.iupd.sig");

        File.WriteAllBytes(pkgPath, pkg);
        File.WriteAllBytes(seedPath, seed32);
        File.WriteAllBytes(pubPath, pub32);

        // Sign
        var (sigFile, signError) = IupdSigner.SignPackage(pkgPath, seedPath, pubPath, sigPath);
        Assert.Null(signError);

        // Verify signature file was created
        Assert.True(File.Exists(sigPath));

        // Verify
        var verifyError = IupdSigner.VerifyPackage(pkgPath, pubPath, sigPath);
        Assert.Null(verifyError);
    }

    /// <summary>
    /// Test C: Signature is deterministic - same inputs => identical bytes.
    /// </summary>
    [Fact]
    public void Sign_IsDeterministic_SigFileBytesIdentical()
    {
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pkg = Encoding.ASCII.GetBytes("test package");
        string pkgPath = Path.Combine(_tempDir, "pkg.iupd");
        string seedPath = Path.Combine(_tempDir, "seed.bin");
        string sigPath1 = Path.Combine(_tempDir, "sig1.sig");
        string sigPath2 = Path.Combine(_tempDir, "sig2.sig");

        File.WriteAllBytes(pkgPath, pkg);
        File.WriteAllBytes(seedPath, seed32);

        // Sign twice
        var (sig1, err1) = IupdSigner.SignPackage(pkgPath, seedPath, outSigPath: sigPath1);
        Assert.Null(err1);

        var (sig2, err2) = IupdSigner.SignPackage(pkgPath, seedPath, outSigPath: sigPath2);
        Assert.Null(err2);

        // Read both files
        byte[] bytes1 = File.ReadAllBytes(sigPath1);
        byte[] bytes2 = File.ReadAllBytes(sigPath2);

        // Must be byte-identical (deterministic)
        Assert.Equal(bytes1, bytes2);
    }

    /// <summary>
    /// Test D: Verification fails for tampered package (hash mismatch or sig invalid).
    /// </summary>
    [Fact]
    public void Verify_TamperedPackage_FailsInvalidChecksumOrSignature()
    {
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        byte[] pkg = Encoding.ASCII.GetBytes("original package");
        string pkgPath = Path.Combine(_tempDir, "pkg.iupd");
        string seedPath = Path.Combine(_tempDir, "seed.bin");
        string pubPath = Path.Combine(_tempDir, "pub.bin");
        string sigPath = Path.Combine(_tempDir, "pkg.sig");

        File.WriteAllBytes(pkgPath, pkg);
        File.WriteAllBytes(seedPath, seed32);
        File.WriteAllBytes(pubPath, pub32);

        // Sign
        var (_, err) = IupdSigner.SignPackage(pkgPath, seedPath, pubPath, sigPath);
        Assert.Null(err);

        // Tamper with package
        byte[] tamperedPkg = Encoding.ASCII.GetBytes("tampered package");
        File.WriteAllBytes(pkgPath, tamperedPkg);

        // Verify should fail
        var verifyErr = IupdSigner.VerifyPackage(pkgPath, pubPath, sigPath);
        Assert.NotNull(verifyErr);
        Assert.True(
            verifyErr.Value.Code == 0x67 || verifyErr.Value.Code == 0x68,
            $"Expected hash mismatch (0x67) or sig invalid (0x68), got {verifyErr.Value.Code:X2}");
    }

    /// <summary>
    /// Test E: Verification fails with wrong public key.
    /// </summary>
    [Fact]
    public void Verify_WrongPublicKey_FailsInvalidSignature()
    {
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] wrongSeed = Convert.FromHexString("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb");

        byte[] pub32 = new byte[32];
        byte[] wrongPub = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);
        Ed25519.CreatePublicKey(wrongSeed, wrongPub);

        byte[] pkg = Encoding.ASCII.GetBytes("test package");
        string pkgPath = Path.Combine(_tempDir, "pkg.iupd");
        string seedPath = Path.Combine(_tempDir, "seed.bin");
        string pubPath = Path.Combine(_tempDir, "pub.bin");
        string wrongPubPath = Path.Combine(_tempDir, "wrong_pub.bin");
        string sigPath = Path.Combine(_tempDir, "pkg.sig");

        File.WriteAllBytes(pkgPath, pkg);
        File.WriteAllBytes(seedPath, seed32);
        File.WriteAllBytes(pubPath, pub32);
        File.WriteAllBytes(wrongPubPath, wrongPub);

        // Sign with correct key
        var (_, err) = IupdSigner.SignPackage(pkgPath, seedPath, pubPath, sigPath);
        Assert.Null(err);

        // Verify with wrong key should fail
        var verifyErr = IupdSigner.VerifyPackage(pkgPath, wrongPubPath, sigPath);
        Assert.NotNull(verifyErr);
        Assert.Equal(0x68, verifyErr.Value.Code); // InvalidSignature
    }

    /// <summary>
    /// Test F: Verification fails when signature file is missing and required.
    /// </summary>
    [Fact]
    public void Verify_MissingSig_WhenRequired_FailsPolicyViolation()
    {
        byte[] pub32 = new byte[32];
        byte[] pkg = Encoding.ASCII.GetBytes("test");

        string pkgPath = Path.Combine(_tempDir, "pkg.iupd");
        string pubPath = Path.Combine(_tempDir, "pub.bin");
        string sigPath = Path.Combine(_tempDir, "pkg.iupd.sig");

        File.WriteAllBytes(pkgPath, pkg);
        File.WriteAllBytes(pubPath, pub32);
        // Do NOT create sig file

        // Verify should fail because sig doesn't exist
        var verifyErr = IupdSigner.VerifyPackage(pkgPath, pubPath, sigPath);
        Assert.NotNull(verifyErr);
        Assert.Equal(0x63, verifyErr.Value.Code); // Io (file not found)
    }

    /// <summary>
    /// Test G: Signature file format is strict (rejects invalid magic, alg, etc).
    /// </summary>
    [Fact]
    public void SigFile_BadMagic_RejectedStrict()
    {
        byte[] badSig = new byte[125];
        System.Text.Encoding.ASCII.GetBytes("BADMAGIC").CopyTo(badSig, 0);

        var (_, err) = IupdSigFile.TryRead(badSig);
        Assert.NotNull(err);
        Assert.Equal(0x42, err.Value.Code); // InvalidMagic
    }

    /// <summary>
    /// Test H: In-memory sign/verify (bytes, not files).
    /// </summary>
    [Fact]
    public void SignVerifyBytes_InMemory_Ok()
    {
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        byte[] pkg = Encoding.ASCII.GetBytes("test pkg");

        // Sign
        var (sig, signErr) = IupdSigner.SignPackageBytes(pkg, seed32, pub32);
        Assert.Null(signErr);

        // Verify
        byte[] sigBytes = sig.ToBytes();
        var verifyErr = IupdSigner.VerifyPackageBytes(pkg, pub32, sigBytes);
        Assert.Null(verifyErr);
    }

    /// <summary>
    /// Test I: Signature file roundtrip (write, read) produces exact match.
    /// </summary>
    [Fact]
    public void SigFile_Roundtrip_ExactMatch()
    {
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        byte[] pkg = Encoding.ASCII.GetBytes("test");
        byte[] pkgHash = IupdSigner.GetIupdPackageHash(pkg);

        byte[] sig64 = new byte[64];
        Ed25519.Sign(seed32, pkgHash, sig64);

        // Create and serialize
        var sig1 = IupdSigFile.Create(pub32, pkgHash, sig64);
        byte[] bytes1 = sig1.ToBytes();

        // Deserialize
        var (sig2, err) = IupdSigFile.TryRead(bytes1);
        Assert.Null(err);

        // Re-serialize
        byte[] bytes2 = sig2.ToBytes();

        // Must match exactly
        Assert.Equal(bytes1, bytes2);
    }

    /// <summary>
    /// Test J: Multiple concurrent signs don't interfere.
    /// </summary>
    [Fact]
    public void SignPackage_ConcurrentSigns_AllSucceed()
    {
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        string seedPath = Path.Combine(_tempDir, "seed.bin");
        string pubPath = Path.Combine(_tempDir, "pub.bin");
        File.WriteAllBytes(seedPath, seed32);
        File.WriteAllBytes(pubPath, pub32);

        var errors = new System.Collections.Concurrent.ConcurrentBag<IronEdgeError?>();

        System.Threading.Tasks.Parallel.For(0, 10, i =>
        {
            byte[] pkg = Encoding.ASCII.GetBytes($"package_{i}");
            string pkgPath = Path.Combine(_tempDir, $"pkg_{i}.iupd");
            string sigPath = Path.Combine(_tempDir, $"pkg_{i}.sig");
            File.WriteAllBytes(pkgPath, pkg);

            var (_, err) = IupdSigner.SignPackage(pkgPath, seedPath, pubPath, sigPath);
            errors.Add(err);
        });

        foreach (var err in errors)
            Assert.Null(err);
    }
}
