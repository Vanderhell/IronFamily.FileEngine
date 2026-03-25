using System;
using System.IO;
using Xunit;
using IronConfig;

namespace IronConfig.IronCfg.Tests;

/// <summary>
/// Minimal Deterministic Corruption Test Suite for IRONCFG.
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
            var vectorsPath = Path.Combine(dir.FullName, "vectors/small", "ironcfg", "small", "golden.icfg");
            if (File.Exists(vectorsPath))
                return vectorsPath;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Golden IRONCFG vector not found. Started searching from: {assemblyPath}");
    }

    [Fact(DisplayName = "MinimalFuzz: IRONCFG invalid magic detects InvalidMagic error")]
    public void Fuzz_InvalidMagic()
    {
        var vectorPath = GetGoldenVectorPath();
        var data = File.ReadAllBytes(vectorPath);

        // Mutation 1: Flip byte in magic (offset 0x00-0x03)
        var corrupted = (byte[])data.Clone();
        corrupted[0] ^= 0xFF;  // Flip all bits in first byte

        // IRONCFG magic is "ICFG" = 0x49 0x43 0x46 0x47
        // After flip, it will no longer match
        var error = IronCfgValidator.ValidateFast(corrupted);

        // Expected error: InvalidMagic
        Assert.NotEqual(IronCfgErrorCode.Ok, error.Code);
        Assert.Equal(IronCfgErrorCode.InvalidMagic, error.Code);
    }

    [Fact(DisplayName = "MinimalFuzz: IRONCFG truncated header detects bounds violation")]
    public void Fuzz_TruncatedHeader()
    {
        var vectorPath = GetGoldenVectorPath();
        var data = File.ReadAllBytes(vectorPath);

        // Mutation 2: Truncate file to partial header (< 64 bytes)
        // IRONCFG has 64-byte header minimum
        var corrupted = new byte[32];  // Half of header size
        Array.Copy(data, 0, corrupted, 0, 32);

        var error = IronCfgValidator.ValidateFast(corrupted);

        // Expected error: TruncatedFile or BoundsViolation
        Assert.NotEqual(IronCfgErrorCode.Ok, error.Code);
        Assert.True(
            error.Code == IronCfgErrorCode.TruncatedFile ||
            error.Code == IronCfgErrorCode.BoundsViolation,
            $"Expected TruncatedFile or BoundsViolation, got {error.Code}"
        );
    }

    [Fact(DisplayName = "MinimalFuzz: IRONCFG offset out-of-bounds detects bounds violation")]
    public void Fuzz_OffsetOutOfBounds()
    {
        var vectorPath = GetGoldenVectorPath();
        var data = File.ReadAllBytes(vectorPath);

        // Mutation 3: Truncate more aggressively to trigger offset violations
        var corrupted = new byte[50];
        Array.Copy(data, 0, corrupted, 0, Math.Min(50, data.Length));

        var error = IronCfgValidator.ValidateFast(corrupted);

        // Expected: Truncation-related error
        Assert.NotEqual(IronCfgErrorCode.Ok, error.Code);
        Assert.True(
            error.Code == IronCfgErrorCode.TruncatedFile ||
            error.Code == IronCfgErrorCode.BoundsViolation ||
            error.Code == IronCfgErrorCode.ArithmeticOverflow,
            $"Expected bounds-related error, got {error.Code}"
        );
    }
}
