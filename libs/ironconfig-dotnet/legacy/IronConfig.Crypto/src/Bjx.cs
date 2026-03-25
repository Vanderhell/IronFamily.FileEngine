using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace IronConfig.Crypto;

/// <summary>
/// BJX1 encryption/decryption for IRONCFG (.icfs) files.
/// Provides AES-GCM authenticated encryption with password-based key derivation (PBKDF2-HMAC-SHA256 in current implementation).
/// </summary>
public static class Bjx
{
    public const int HeaderSize = 32;
    public const int SaltSize = 16;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    private const byte FLAG_ARGON2ID = 1 << 0;
    private const byte FLAG_RAWKEY32 = 1 << 1;
    private const byte FLAG_CHACHA = 1 << 2;
    private const byte FLAG_AESGCM = 1 << 3;

    /// <summary>
    /// Encrypts a BJV binary buffer to BJX1 format.
    /// </summary>
    /// <param name="plainBJV">Raw BJV binary data to encrypt</param>
    /// <param name="password">Password for key derivation (or null if using rawKey32)</param>
    /// <param name="rawKey32">32-byte raw key (or null to use password)</param>
    /// <param name="argonTimeCost">Argon2id time cost (default: 3)</param>
    /// <param name="argonMemoryMiB">Argon2id memory cost in MiB (default: 64)</param>
    /// <param name="argonParallelism">Argon2id parallelism (default: 1)</param>
    /// <returns>Encrypted BJX1 binary data</returns>
    public static byte[] EncryptBjx1(byte[] plainBJV, string? password, byte[]? rawKey32,
        int argonTimeCost = 3, int argonMemoryMiB = 64, int argonParallelism = 1)
    {
        if (plainBJV is null || plainBJV.Length == 0)
            throw new ArgumentException("BJX: empty input");
        if ((password is null) == (rawKey32 is null))
            throw new ArgumentException("BJX: provide either password OR rawKey32");

        byte[] salt = new byte[SaltSize];
        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        byte[] key = rawKey32 ?? DeriveKeyArgon2id(password!, salt, argonTimeCost, argonMemoryMiB, argonParallelism);

        uint payloadSize = (uint)plainBJV.Length;
        byte[] outBuf = new byte[HeaderSize + SaltSize + NonceSize + plainBJV.Length + TagSize];

        // Write header
        outBuf[0] = (byte)'B';
        outBuf[1] = (byte)'J';
        outBuf[2] = (byte)'X';
        outBuf[3] = (byte)'1';
        outBuf[4] = 0;
        outBuf[5] = 0;

        BinaryPrimitives.WriteUInt16LittleEndian(outBuf.AsSpan(6, 2), HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(outBuf.AsSpan(8, 4), payloadSize);
        BinaryPrimitives.WriteUInt32LittleEndian(outBuf.AsSpan(12, 4), payloadSize);
        BinaryPrimitives.WriteUInt32LittleEndian(outBuf.AsSpan(16, 4), HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(outBuf.AsSpan(20, 4), HeaderSize + SaltSize);
        BinaryPrimitives.WriteUInt32LittleEndian(outBuf.AsSpan(24, 4), HeaderSize + SaltSize + NonceSize);
        BinaryPrimitives.WriteUInt32LittleEndian(outBuf.AsSpan(28, 4), 0);

        salt.CopyTo(outBuf, HeaderSize);
        nonce.CopyTo(outBuf, HeaderSize + SaltSize);

        byte[] ciphertext = new byte[plainBJV.Length];
        byte[] tag = new byte[TagSize];

        byte flags = 0;
        flags |= (password != null) ? FLAG_ARGON2ID : (byte)0;
        flags |= (rawKey32 != null) ? FLAG_RAWKEY32 : (byte)0;
        flags |= FLAG_AESGCM;

        // Set flags in buffer BEFORE creating headerAD for authentication
        outBuf[4] = flags;

        byte[] headerAD = new byte[HeaderSize];
        Array.Copy(outBuf, headerAD, HeaderSize);

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plainBJV, ciphertext, tag, headerAD);
        }
        catch
        {
            throw new InvalidOperationException("BJX: encryption failed");
        }
        ciphertext.CopyTo(outBuf, HeaderSize + SaltSize + NonceSize);
        tag.CopyTo(outBuf, HeaderSize + SaltSize + NonceSize + plainBJV.Length);

        return outBuf;
    }

    /// <summary>
    /// Decrypts a BJX1 encrypted buffer back to BJV binary.
    /// </summary>
    /// <param name="bjx">Encrypted BJX1 binary data</param>
    /// <param name="password">Password for key derivation (or null if using rawKey32)</param>
    /// <param name="rawKey32">32-byte raw key (or null to use password)</param>
    /// <param name="argonTimeCost">Argon2id time cost (must match encryption)</param>
    /// <param name="argonMemoryMiB">Argon2id memory cost in MiB (must match encryption)</param>
    /// <param name="argonParallelism">Argon2id parallelism (must match encryption)</param>
    /// <returns>Decrypted BJV binary data</returns>
    public static byte[] DecryptBjx1(byte[] bjx, string? password, byte[]? rawKey32,
        int argonTimeCost = 3, int argonMemoryMiB = 64, int argonParallelism = 1)
    {
        if (bjx is null || bjx.Length < HeaderSize + SaltSize + NonceSize + TagSize)
            throw new ArgumentException("BJX: file too small");
        if ((password is null) == (rawKey32 is null))
            throw new ArgumentException("BJX: provide either password OR rawKey32");

        if (!(bjx[0] == 'B' && bjx[1] == 'J' && bjx[2] == 'X' && bjx[3] == '1'))
            throw new InvalidOperationException("BJX: bad magic");

        byte flags = bjx[4];
        ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(bjx.AsSpan(6, 2));
        if (headerSize != HeaderSize)
            throw new InvalidOperationException("BJX: header_size != 32");

        uint encSize = BinaryPrimitives.ReadUInt32LittleEndian(bjx.AsSpan(8, 4));
        uint plainSize = BinaryPrimitives.ReadUInt32LittleEndian(bjx.AsSpan(12, 4));

        byte[] salt = new byte[SaltSize];
        Array.Copy(bjx, HeaderSize, salt, 0, SaltSize);

        byte[] nonce = new byte[NonceSize];
        Array.Copy(bjx, HeaderSize + SaltSize, nonce, 0, NonceSize);

        byte[] key = rawKey32 ?? DeriveKeyArgon2id(password!, salt, argonTimeCost, argonMemoryMiB, argonParallelism);

        byte[] headerAD = new byte[HeaderSize];
        Array.Copy(bjx, 0, headerAD, 0, HeaderSize);

        byte[] ciphertext = new byte[encSize];
        Array.Copy(bjx, HeaderSize + SaltSize + NonceSize, ciphertext, 0, (int)encSize);

        byte[] tag = new byte[TagSize];
        Array.Copy(bjx, HeaderSize + SaltSize + NonceSize + encSize, tag, 0, TagSize);

        byte[] plain = new byte[plainSize];

        if ((flags & FLAG_AESGCM) != 0)
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plain, headerAD);
            return plain;
        }

        throw new InvalidOperationException("BJX: unsupported cipher");
    }

    private static byte[] DeriveKeyArgon2id(string password, byte[] salt, int time, int memMiB, int par)
    {
        using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
        {
            return pbkdf2.GetBytes(32);
        }
    }
}
