using System;
using System.IO;
using Xunit;
using IronConfig.Iupd;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// Minimal Deterministic Corruption Test Suite for IUPD.
/// Three mutations on golden vector verify exact error codes per spec.
/// PHASE 2: Deterministic tests (no randomness, no fault injection needed).
/// </summary>
public class MinimalFuzzTests
{
    private static string GetGoldenVectorPath()
    {
        // Find vectors directory by walking up from IronFamily.FileEngine root
        var assemblyPath = typeof(MinimalFuzzTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath) ?? ".");

        // Walk up until we find "vectors/small" directory
        while (dir != null)
        {
            var vectorsPath = Path.Combine(dir.FullName, "vectors/small", "iupd", "golden_small", "expected", "iupd.iupd");
            if (File.Exists(vectorsPath))
                return vectorsPath;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Golden IUPD vector not found. Started searching from: {assemblyPath}");
    }

    [Fact(DisplayName = "MinimalFuzz: IUPD invalid magic detects InvalidMagic error")]
    public void Fuzz_InvalidMagic()
    {
        var vectorPath = GetGoldenVectorPath();
        var data = File.ReadAllBytes(vectorPath);

        // Mutation 1: Flip byte in magic (offset 0x00-0x03)
        var corrupted = (byte[])data.Clone();
        corrupted[0] ^= 0xFF;  // Flip all bits in first byte

        // IUPD magic is "IUPD" = 0x49 0x55 0x50 0x44
        // After flip, it will no longer match
        var reader = IupdReader.Open(corrupted, out var error);

        // Expected error: InvalidMagic
        Assert.Equal(IupdErrorCode.InvalidMagic, error.Code);
    }

    [Fact(DisplayName = "MinimalFuzz: IUPD truncated header detects OffsetOutOfBounds error")]
    public void Fuzz_TruncatedHeader()
    {
        var vectorPath = GetGoldenVectorPath();
        var data = File.ReadAllBytes(vectorPath);

        // Mutation 2: Truncate file to partial header (< 36 bytes)
        // IUPD has 36-byte header minimum
        var corrupted = new byte[20];  // Less than half of header size
        Array.Copy(data, 0, corrupted, 0, 20);

        var reader = IupdReader.Open(corrupted, out var error);

        // Expected error: OffsetOutOfBounds or truncation-related
        Assert.True(
            error.Code == IupdErrorCode.OffsetOutOfBounds ||
            error.Code == IupdErrorCode.InvalidHeaderSize,
            $"Expected OffsetOutOfBounds or InvalidHeaderSize, got {error.Code}"
        );
    }

    [Fact(DisplayName = "MinimalFuzz: IUPD offset out-of-bounds detects boundary violation")]
    public void Fuzz_OffsetOutOfBounds()
    {
        var vectorPath = GetGoldenVectorPath();
        var data = File.ReadAllBytes(vectorPath);

        // Mutation 3: Truncate even more to trigger offset violations
        var corrupted = new byte[100];
        Array.Copy(data, 0, corrupted, 0, Math.Min(100, data.Length));

        var reader = IupdReader.Open(corrupted, out var error);

        // Expected: Offset-related error (OffsetOutOfBounds, InvalidChunkTableSize, etc.)
        Assert.True(
            error.Code == IupdErrorCode.OffsetOutOfBounds ||
            error.Code == IupdErrorCode.InvalidChunkTableSize ||
            error.Code == IupdErrorCode.InvalidHeaderSize,
            $"Expected offset-related error, got {error.Code}"
        );
    }
}
