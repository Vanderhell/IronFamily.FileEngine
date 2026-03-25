// Phase 1.3 Test Utilities
// Common deterministic helpers for corruption testing across all engines

using IronConfig.Crypto;

namespace IronConfig.Tests;

/// <summary>
/// Common test helpers for deterministic corruption testing.
/// Ensures all tests are reproducible with no randomness except fixed seeds.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Creates a unique temporary directory for test isolation.
    /// Path: {TempPath}/IronEdgeTests/{guid}/
    /// </summary>
    /// <returns>Full path to unique directory (created if not exists)</returns>
    public static string CreateUniqueTempDir()
    {
        var testBaseDir = Path.Combine(Path.GetTempPath(), "IronEdgeTests");
        var uniqueDir = Path.Combine(testBaseDir, Guid.NewGuid().ToString());

        if (!Directory.Exists(uniqueDir))
        {
            Directory.CreateDirectory(uniqueDir);
        }

        return uniqueDir;
    }

    /// <summary>
    /// Deterministically flips a single bit in a byte array.
    /// Used for corruption testing with predictable mutations.
    /// </summary>
    /// <param name="data">Byte array to mutate (modified in-place)</param>
    /// <param name="offset">Byte offset in the array</param>
    /// <param name="bitIndex">Bit index within the byte (0-7, where 0 is LSB)</param>
    /// <exception cref="ArgumentOutOfRangeException">If offset or bitIndex are out of range</exception>
    public static void FlipBit(byte[] data, int offset, int bitIndex)
    {
        if (offset < 0 || offset >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), $"Offset {offset} out of range [0, {data.Length})");

        if (bitIndex < 0 || bitIndex > 7)
            throw new ArgumentOutOfRangeException(nameof(bitIndex), "Bit index must be 0-7");

        // Flip the bit: XOR with 2^bitIndex
        data[offset] ^= (byte)(1 << bitIndex);
    }

    /// <summary>
    /// Atomically writes bytes to file with minimal intermediate state.
    /// Used to ensure test file corruption is completed before validation.
    /// </summary>
    /// <param name="path">File path to write to</param>
    /// <param name="bytes">Byte content to write</param>
    public static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Write atomically: write to temp file, then move
        var tempFile = path + ".tmp";
        try
        {
            File.WriteAllBytes(tempFile, bytes);

            // Replace atomically (atomic on NTFS and modern filesystems)
            if (File.Exists(path))
                File.Delete(path);

            File.Move(tempFile, path, overwrite: true);
        }
        finally
        {
            // Cleanup temp file if it still exists
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }

    /// <summary>
    /// Reads an entire file into memory (convenience wrapper).
    /// </summary>
    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    /// <summary>
    /// Writes a complete binary file and returns its path.
    /// Ensures directory exists before writing.
    /// </summary>
    public static string WriteTestFile(string directory, string filename, byte[] content)
    {
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, filename);
        WriteAllBytesAtomic(path, content);
        return path;
    }

    /// <summary>
    /// Cleans up test directory recursively.
    /// </summary>
    public static void CleanupTempDir(string directory)
    {
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors (files may be locked)
            }
        }
    }

    /// <summary>
    /// Derives Ed25519 public key from a seed (private key).
    /// </summary>
    public static byte[] DerivePublicKeyFromSeed(byte[] seed)
    {
        if (seed.Length != 32)
            throw new ArgumentException("Seed must be 32 bytes", nameof(seed));

        Span<byte> pubKey = stackalloc byte[32];
        Ed25519.CreatePublicKey(seed, pubKey);
        return pubKey.ToArray();
    }
}
