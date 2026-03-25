// Test Vector Discovery Helper
// Deterministic repo root discovery WITHOUT .git dependency

using System;
using System.IO;

namespace IronConfig.ILog.Tests;

/// <summary>
/// Robust test-vector discovery that works in CI, ZIP checkout, and vendor scenarios.
/// Does NOT require .git directory.
/// </summary>
public static class TestVectorHelper
{
    /// <summary>
    /// Resolve test vectors root using output-first strategy (for self-contained tests).
    /// Tries in order:
    /// 1. IRONCONFIG_TESTVECTORS_ROOT environment variable (explicit override)
    /// 2. AppContext.BaseDirectory/vectors (output directory - preferred)
    /// 3. IRONCONFIG_REPO_ROOT environment variable
    /// 4. Walk up to find IronConfig.sln + /vectors
    /// 5. Walk up to find libs/ironconfig-dotnet + /vectors
    /// </summary>
    /// <returns>Test vectors root path</returns>
    public static string ResolveTestVectorsRoot()
    {
        // Priority 1: Explicit test vectors root override
        var envTestVectors = Environment.GetEnvironmentVariable("IRONCONFIG_TESTVECTORS_ROOT");
        if (!string.IsNullOrWhiteSpace(envTestVectors) && Directory.Exists(envTestVectors))
        {
            return envTestVectors;
        }

        // Priority 2: Output-local path (self-contained, for ZIP checkouts)
        var outputLocal = Path.Combine(AppContext.BaseDirectory, "vectors");
        if (Directory.Exists(outputLocal))
        {
            return outputLocal;
        }

        // Priority 3: Repo root via environment variable
        var envRepoRoot = Environment.GetEnvironmentVariable("IRONCONFIG_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(envRepoRoot))
        {
            var repoVectors = Path.Combine(envRepoRoot, "vectors");
            if (Directory.Exists(repoVectors))
            {
                return repoVectors;
            }
        }

        // Priority 4: Walk up looking for IronConfig.sln
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        int depth = 0;
        while (current != null && depth < 12)
        {
            var sln = Path.Combine(current.FullName, "IronConfig.sln");
            if (File.Exists(sln))
            {
                var repoVectors = Path.Combine(current.FullName, "vectors");
                if (Directory.Exists(repoVectors))
                {
                    return repoVectors;
                }
            }

            // Priority 5: Alternative - look for libs/ironconfig-dotnet structure
            var libsPath = Path.Combine(current.FullName, "libs", "ironconfig-dotnet");
            if (Directory.Exists(libsPath))
            {
                var libsVectors = Path.Combine(current.FullName, "vectors");
                if (Directory.Exists(libsVectors))
                {
                    return libsVectors;
                }
            }

            current = current.Parent;
            depth++;
        }

        // If not found, provide diagnostic
        throw new InvalidOperationException(
            $"Could not find test vectors.\n" +
            $"Searched from: {AppContext.BaseDirectory}\n" +
            $"Tried:\n" +
            $"  1. IRONCONFIG_TESTVECTORS_ROOT env var\n" +
            $"  2. {outputLocal}\n" +
            $"  3. IRONCONFIG_REPO_ROOT env var + /vectors\n" +
            $"  4. Walk up for IronConfig.sln + /vectors\n" +
            $"  5. Walk up for libs/ironconfig-dotnet + /vectors\n" +
            $"Set IRONCONFIG_TESTVECTORS_ROOT or IRONCONFIG_REPO_ROOT environment variable if needed.");
    }

    // Legacy compatibility
    public static string FindRepositoryRoot() => ResolveTestVectorsRoot();

    /// <summary>
    /// Get path to IronCfg test vector.
    /// </summary>
    public static string GetIronCfgTestVectorPath(string datasetName)
    {
        var vectorsRoot = ResolveTestVectorsRoot();
        var vectorPath = Path.Combine(vectorsRoot, "small", "ironcfg", datasetName, "golden.icfg");

        if (!File.Exists(vectorPath))
        {
            throw new FileNotFoundException(
                $"IronCfg test vector not found: {vectorPath}\n" +
                $"Dataset: '{datasetName}'\n" +
                $"Available datasets: small, medium, large, mega\n" +
                $"Test vectors directory: {Path.Combine(vectorsRoot, "small", "ironcfg")}");
        }

        return vectorPath;
    }

    /// <summary>
    /// Get path to IUPD test vectors root.
    /// </summary>
    public static string GetIupdTestVectorsRoot()
    {
        var vectorsRoot = ResolveTestVectorsRoot();
        var vectorsPath = Path.Combine(vectorsRoot, "small", "iupd");

        if (!Directory.Exists(vectorsPath))
        {
            throw new DirectoryNotFoundException(
                $"IUPD test vectors directory not found: {vectorsPath}\n" +
                $"Vectors root: {vectorsRoot}\n" +
                $"Expected structure: {Path.Combine(vectorsRoot, "small", "iupd")}");
        }

        return vectorsPath;
    }

    /// <summary>
    /// Get path to ILOG test vectors root.
    /// </summary>
    public static string GetIlogTestVectorsRoot()
    {
        var vectorsRoot = ResolveTestVectorsRoot();
        var vectorsPath = Path.Combine(vectorsRoot, "small", "ilog");

        if (!Directory.Exists(vectorsPath))
        {
            throw new DirectoryNotFoundException(
                $"ILOG test vectors directory not found: {vectorsPath}\n" +
                $"Vectors root: {vectorsRoot}\n" +
                $"Expected structure: {Path.Combine(vectorsRoot, "small", "ilog")}");
        }

        return vectorsPath;
    }
}
