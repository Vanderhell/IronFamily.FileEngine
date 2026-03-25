using System;
using System.Buffers.Binary;
using IronConfig.Crypto;

namespace IronConfig.Iupd.Signing;

using Blake3Hasher = IronConfig.Blake3Ieee;

/// <summary>
/// Detached signature file format (IUPDSIG1).
/// Binary format: exactly 125 bytes
/// Magic[8] + AlgId[1] + HashId[1] + KeyIdLen[1] + KeyId[16] + PkgHash[32] + SigLen[2] + Sig[64]
/// </summary>
public readonly struct IupdSigFile
{
    public const string MAGIC = "IUPDSIG1";
    public const int MAGIC_BYTES = 8;
    public const byte ALG_ID = 1;      // Ed25519
    public const byte HASH_ID = 1;     // BLAKE3-256
    public const byte KEY_ID_LEN = 16;
    public const int KEY_ID_BYTES = 16;
    public const int PKG_HASH_BYTES = 32;
    public const ushort SIG_LEN = 64;
    public const int TOTAL_SIZE = 8 + 1 + 1 + 1 + 16 + 32 + 2 + 64; // = 125

    public readonly byte[] Magic;
    public readonly byte AlgId;
    public readonly byte HashId;
    public readonly byte KeyIdLen;
    public readonly byte[] KeyId;           // 16 bytes: first16(BLAKE3(pubKey32))
    public readonly byte[] PkgHash;         // 32 bytes: BLAKE3-256 of package
    public readonly ushort SigLen;
    public readonly byte[] Sig;             // 64 bytes: Ed25519 signature

    private IupdSigFile(
        byte[] magic,
        byte algId,
        byte hashId,
        byte keyIdLen,
        byte[] keyId,
        byte[] pkgHash,
        ushort sigLen,
        byte[] sig)
    {
        Magic = magic ?? Array.Empty<byte>();
        AlgId = algId;
        HashId = hashId;
        KeyIdLen = keyIdLen;
        KeyId = keyId ?? Array.Empty<byte>();
        PkgHash = pkgHash ?? Array.Empty<byte>();
        SigLen = sigLen;
        Sig = sig ?? Array.Empty<byte>();
    }

    /// <summary>
    /// Create IupdSigFile from components.
    /// </summary>
    public static IupdSigFile Create(byte[] pubKey32, byte[] pkgHash32, byte[] sig64)
    {
        if (pubKey32?.Length != 32)
            throw new ArgumentException("Public key must be 32 bytes", nameof(pubKey32));
        if (pkgHash32?.Length != 32)
            throw new ArgumentException("Package hash must be 32 bytes", nameof(pkgHash32));
        if (sig64?.Length != 64)
            throw new ArgumentException("Signature must be 64 bytes", nameof(sig64));

        // Compute KeyId = first16(BLAKE3(pubKey32))
        byte[] keyIdFull = Blake3Hasher.Compute(pubKey32);
        byte[] keyId = new byte[KEY_ID_BYTES];
        Array.Copy(keyIdFull, 0, keyId, 0, KEY_ID_BYTES);

        byte[] magic = System.Text.Encoding.ASCII.GetBytes(MAGIC);
        return new IupdSigFile(
            magic: magic,
            algId: ALG_ID,
            hashId: HASH_ID,
            keyIdLen: KEY_ID_LEN,
            keyId: keyId,
            pkgHash: pkgHash32,
            sigLen: SIG_LEN,
            sig: sig64);
    }

    /// <summary>
    /// Write to binary format (125 bytes exactly, no variability).
    /// </summary>
    public byte[] ToBytes()
    {
        byte[] result = new byte[TOTAL_SIZE];
        int offset = 0;

        // Magic[8]
        Array.Copy(Magic, 0, result, offset, MAGIC_BYTES);
        offset += MAGIC_BYTES;

        // AlgId[1]
        result[offset++] = AlgId;

        // HashId[1]
        result[offset++] = HashId;

        // KeyIdLen[1]
        result[offset++] = KeyIdLen;

        // KeyId[16]
        Array.Copy(KeyId, 0, result, offset, KEY_ID_BYTES);
        offset += KEY_ID_BYTES;

        // PkgHash[32]
        Array.Copy(PkgHash, 0, result, offset, PKG_HASH_BYTES);
        offset += PKG_HASH_BYTES;

        // SigLen[2] (little-endian)
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset), SigLen);
        offset += 2;

        // Sig[64]
        Array.Copy(Sig, 0, result, offset, SigLen);
        offset += SigLen;

        return result;
    }

    /// <summary>
    /// Read from binary format with strict validation.
    /// Rejects non-standard AlgId, HashId, KeyIdLen, trailing bytes, etc.
    /// </summary>
    public static (IupdSigFile sigFile, IronEdgeError? error) TryRead(byte[] data)
    {
        if (data == null)
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.InvalidArgument,
                0x40,
                IronEdgeEngine.Iupd,
                "Signature data is null"));

        if (data.Length != TOTAL_SIZE)
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.CorruptData,
                0x41,
                IronEdgeEngine.Iupd,
                $"Signature must be exactly {TOTAL_SIZE} bytes, got {data.Length}"));

        int offset = 0;

        // Read Magic[8]
        byte[] magic = new byte[MAGIC_BYTES];
        Array.Copy(data, offset, magic, 0, MAGIC_BYTES);
        offset += MAGIC_BYTES;

        string magicStr = System.Text.Encoding.ASCII.GetString(magic);
        if (magicStr != MAGIC)
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.InvalidMagic,
                0x42,
                IronEdgeEngine.Iupd,
                $"Invalid signature magic: expected '{MAGIC}', got '{magicStr}'"));

        // Read AlgId[1]
        byte algId = data[offset++];
        if (algId != ALG_ID)
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.UnsupportedVersion,
                0x43,
                IronEdgeEngine.Iupd,
                $"Unsupported algorithm ID: {algId}"));

        // Read HashId[1]
        byte hashId = data[offset++];
        if (hashId != HASH_ID)
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.UnsupportedVersion,
                0x44,
                IronEdgeEngine.Iupd,
                $"Unsupported hash ID: {hashId}"));

        // Read KeyIdLen[1]
        byte keyIdLen = data[offset++];
        if (keyIdLen != KEY_ID_LEN)
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.CorruptData,
                0x45,
                IronEdgeEngine.Iupd,
                $"Invalid key ID length: expected {KEY_ID_LEN}, got {keyIdLen}"));

        // Read KeyId[16]
        byte[] keyId = new byte[KEY_ID_BYTES];
        Array.Copy(data, offset, keyId, 0, KEY_ID_BYTES);
        offset += KEY_ID_BYTES;

        // Read PkgHash[32]
        byte[] pkgHash = new byte[PKG_HASH_BYTES];
        Array.Copy(data, offset, pkgHash, 0, PKG_HASH_BYTES);
        offset += PKG_HASH_BYTES;

        // Read SigLen[2] (little-endian)
        ushort sigLen = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        offset += 2;

        if (sigLen != SIG_LEN)
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.CorruptData,
                0x46,
                IronEdgeEngine.Iupd,
                $"Invalid signature length: expected {SIG_LEN}, got {sigLen}"));

        // Read Sig[64]
        byte[] sig = new byte[SIG_LEN];
        Array.Copy(data, offset, sig, 0, SIG_LEN);
        offset += SIG_LEN;

        // Reject trailing bytes
        if (offset != data.Length)
            return (default, new IronEdgeError(
                IronEdgeErrorCategory.CorruptData,
                0x47,
                IronEdgeEngine.Iupd,
                $"Trailing bytes detected: {data.Length - offset} extra bytes"));

        var sigFile = new IupdSigFile(
            magic: magic,
            algId: algId,
            hashId: hashId,
            keyIdLen: keyIdLen,
            keyId: keyId,
            pkgHash: pkgHash,
            sigLen: sigLen,
            sig: sig);

        return (sigFile, null);
    }
}
