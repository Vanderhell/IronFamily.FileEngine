using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;
using IronConfig.Iupd;
using IronConfig.Iupd.Delta;

namespace IronFamily.OtaCli.Tests;

/// <summary>
/// E2E integration tests for OTA CLI (create → verify → apply)
/// Tests the complete pipeline: base + target → package + delta → apply → output
/// </summary>
public class OtaCliE2ETests : IDisposable
{
    private readonly string _testDir;
    private readonly string _cliPath;

    public OtaCliE2ETests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ota-e2e-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        // Find CLI binary in standard Release build location
        // Tests run from: bin/Release/net8.0/
        // CLI is at:     ../../../../../tools/IronFamily.OtaCli/bin/Release/net8.0/ironfamily-ota.dll
        _cliPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "tools", "IronFamily.OtaCli", "bin", "Release", "net8.0", "ironfamily-ota.dll"
            )
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    /// <summary>
    /// Test: base + target → create → verify → apply → output == target
    /// </summary>
    [Fact]
    public void E2E_CreateVerifyApply_OutputEqualsTarget()
    {
        // Create test files
        byte[] baseData = GenerateDeterministicData(512 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        // Make small modification to target
        targetData[1000] ^= 0xFF;
        targetData[50000] ^= 0xFF;

        string basePath = Path.Combine(_testDir, "base.bin");
        string targetPath = Path.Combine(_testDir, "target.bin");
        File.WriteAllBytes(basePath, baseData);
        File.WriteAllBytes(targetPath, targetData);

        // CREATE
        string packagePath = Path.Combine(_testDir, "pkg.iupd");
        int createExitCode = RunCli($"create --base {basePath} --target {targetPath} --out {packagePath} --sequence 1 --force");
        Assert.Equal(0, createExitCode);
        Assert.True(File.Exists(packagePath), "Package should be created");
        Assert.True(File.Exists(packagePath + ".delta"), "Delta should be created");

        // VERIFY
        int verifyExitCode = RunCli($"verify --package {packagePath}");
        Assert.Equal(0, verifyExitCode);

        // APPLY
        string outputPath = Path.Combine(_testDir, "output.bin");
        int applyExitCode = RunCli($"apply --base {basePath} --package {packagePath} --out {outputPath} --force");
        Assert.Equal(0, applyExitCode);
        Assert.True(File.Exists(outputPath), "Output should be created");

        // VERIFY OUTPUT
        byte[] outputData = File.ReadAllBytes(outputPath);
        Assert.Equal(targetData.Length, outputData.Length);
        Assert.True(BytesEqual(outputData, targetData), "Output must equal target (byte-identical)");
    }

    /// <summary>
    /// Test: Determinism - create twice with same inputs, packages must be byte-identical
    /// </summary>
    [Fact]
    public void Determinism_SameInputsProduceSamePackage()
    {
        // Create test files
        byte[] baseData = GenerateDeterministicData(512 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        targetData[1000] ^= 0xFF;

        string basePath = Path.Combine(_testDir, "base.bin");
        string targetPath = Path.Combine(_testDir, "target.bin");
        File.WriteAllBytes(basePath, baseData);
        File.WriteAllBytes(targetPath, targetData);

        // Create first package
        string pkg1Path = Path.Combine(_testDir, "pkg1.iupd");
        int code1 = RunCli($"create --base {basePath} --target {targetPath} --out {pkg1Path} --sequence 1 --force");
        Assert.Equal(0, code1);

        // Create second package with same inputs
        string pkg2Path = Path.Combine(_testDir, "pkg2.iupd");
        int code2 = RunCli($"create --base {basePath} --target {targetPath} --out {pkg2Path} --sequence 1 --force");
        Assert.Equal(0, code2);

        // Compare packages
        byte[] pkg1Data = File.ReadAllBytes(pkg1Path);
        byte[] pkg2Data = File.ReadAllBytes(pkg2Path);
        Assert.True(BytesEqual(pkg1Data, pkg2Data), "Packages must be byte-identical (deterministic)");

        // Compare deltas
        byte[] delta1Data = File.ReadAllBytes(pkg1Path + ".delta");
        byte[] delta2Data = File.ReadAllBytes(pkg2Path + ".delta");
        Assert.True(BytesEqual(delta1Data, delta2Data), "Deltas must be byte-identical (deterministic)");
    }

    /// <summary>
    /// Test: Verify fails on corrupted package
    /// </summary>
    [Fact]
    public void Verify_FailsOnCorruptedPackage()
    {
        // Create a valid package first
        byte[] baseData = GenerateDeterministicData(100 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        targetData[1000] ^= 0xFF;

        string basePath = Path.Combine(_testDir, "base.bin");
        string targetPath = Path.Combine(_testDir, "target.bin");
        File.WriteAllBytes(basePath, baseData);
        File.WriteAllBytes(targetPath, targetData);

        string packagePath = Path.Combine(_testDir, "pkg.iupd");
        RunCli($"create --base {basePath} --target {targetPath} --out {packagePath} --sequence 1 --force");

        // Corrupt the package
        byte[] corruptedData = File.ReadAllBytes(packagePath);
        corruptedData[50] ^= 0xFF;  // Flip one byte
        File.WriteAllBytes(packagePath, corruptedData);

        // Verify should fail
        int verifyExitCode = RunCli($"verify --package {packagePath}");
        Assert.NotEqual(0, verifyExitCode);
    }

    /// <summary>
    /// Test: Apply fails if verify fails
    /// </summary>
    [Fact]
    public void Apply_FailsIfVerifyFails()
    {
        // Create test files and package
        byte[] baseData = GenerateDeterministicData(100 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        targetData[1000] ^= 0xFF;

        string basePath = Path.Combine(_testDir, "base.bin");
        string targetPath = Path.Combine(_testDir, "target.bin");
        string packagePath = Path.Combine(_testDir, "pkg.iupd");

        File.WriteAllBytes(basePath, baseData);
        File.WriteAllBytes(targetPath, targetData);
        RunCli($"create --base {basePath} --target {targetPath} --out {packagePath} --sequence 1 --force");

        // Corrupt the package
        byte[] corruptedData = File.ReadAllBytes(packagePath);
        corruptedData[50] ^= 0xFF;
        File.WriteAllBytes(packagePath, corruptedData);

        // Apply should fail (fail-closed gate)
        string outputPath = Path.Combine(_testDir, "output.bin");
        int applyExitCode = RunCli($"apply --base {basePath} --package {packagePath} --out {outputPath} --force");
        Assert.NotEqual(0, applyExitCode);
        Assert.False(File.Exists(outputPath), "Output should not be created if verification fails");
    }

    /// <summary>
    /// Test: Different sequence numbers produce different signatures
    /// </summary>
    [Fact]
    public void DifferentSequence_ProducesDifferentPackages()
    {
        byte[] baseData = GenerateDeterministicData(100 * 1024);
        byte[] targetData = (byte[])baseData.Clone();
        targetData[1000] ^= 0xFF;

        string basePath = Path.Combine(_testDir, "base.bin");
        string targetPath = Path.Combine(_testDir, "target.bin");
        File.WriteAllBytes(basePath, baseData);
        File.WriteAllBytes(targetPath, targetData);

        // Create package with sequence 1
        string pkg1Path = Path.Combine(_testDir, "pkg_seq1.iupd");
        RunCli($"create --base {basePath} --target {targetPath} --out {pkg1Path} --sequence 1 --force");

        // Create package with sequence 2
        string pkg2Path = Path.Combine(_testDir, "pkg_seq2.iupd");
        RunCli($"create --base {basePath} --target {targetPath} --out {pkg2Path} --sequence 2 --force");

        // Packages should differ (due to different UpdateSequence in signature)
        byte[] pkg1Data = File.ReadAllBytes(pkg1Path);
        byte[] pkg2Data = File.ReadAllBytes(pkg2Path);
        Assert.False(BytesEqual(pkg1Data, pkg2Data), "Different sequences should produce different packages");
    }

    // Helpers

    private int RunCli(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliPath}\" {args}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI");
        process.WaitForExit(30000);
        return process.ExitCode;
    }

    private static byte[] GenerateDeterministicData(int length)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = (byte)((i * 67 + 17) % 256);
        return data;
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
