using System;
using Blake3;

namespace IronConfig;

/// <summary>
/// BLAKE3-256 hash computation helper using Blake3 library
/// </summary>
public static class Blake3Ieee
{
    /// <summary>
    /// Compute BLAKE3-256 hash for the given data
    /// </summary>
    /// <param name="data">Data to hash</param>
    /// <returns>32-byte BLAKE3 hash</returns>
    public static byte[] Compute(ReadOnlySpan<byte> data)
    {
        using var hasher = Hasher.New();
        hasher.Update(data);
        return hasher.Finalize().AsSpan().ToArray();
    }

    /// <summary>
    /// Compute BLAKE3-256 hash and write to output span
    /// </summary>
    /// <param name="data">Data to hash</param>
    /// <param name="output">Output span (must be exactly 32 bytes)</param>
    /// <exception cref="ArgumentException">If output span is not exactly 32 bytes</exception>
    public static void Compute(ReadOnlySpan<byte> data, Span<byte> output)
    {
        if (output.Length != 32)
            throw new ArgumentException("Output span must be exactly 32 bytes for BLAKE3-256", nameof(output));

        using var hasher = Hasher.New();
        hasher.Update(data);
        var hash = hasher.Finalize();
        hash.AsSpan().CopyTo(output);
    }

    /// <summary>
    /// Verify that data matches the given BLAKE3-256 hash
    /// </summary>
    /// <param name="data">Data to verify</param>
    /// <param name="expectedHash">Expected 32-byte BLAKE3 hash</param>
    /// <returns>True if hash matches, false otherwise</returns>
    public static bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> expectedHash)
    {
        if (expectedHash.Length != 32)
            return false;

        using var hasher = Hasher.New();
        hasher.Update(data);
        var computed = hasher.Finalize();
        return computed.AsSpan().SequenceEqual(expectedHash);
    }
}
