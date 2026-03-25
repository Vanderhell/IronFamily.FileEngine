using System;
using System.IO;
using IronConfig.Iupd.Delta;
using IronConfig.Crypto;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// Helper to generate DELTA test vectors (golden files)
/// Run this once to generate deterministic test vectors
/// </summary>
public static class IupdDeltaVectorsGenerator
{
    public static void GenerateAllVectors()
    {
        GenerateCase01();
        GenerateCase02();
        Console.WriteLine("All test vectors generated successfully");
    }

    private static void GenerateCase01()
    {
        string caseDir = "vectors/small/iupd/delta/v1/case01";
        Directory.CreateDirectory(caseDir);

        // Create deterministic base
        byte[] baseBytes = new byte[4096];
        for (int i = 0; i < baseBytes.Length; i++)
            baseBytes[i] = (byte)((i * 0x47) & 0xFF);

        // Create target with changes
        byte[] targetBytes = (byte[])baseBytes.Clone();
        targetBytes[100] ^= 0xFF;
        targetBytes[500] ^= 0xFF;
        targetBytes[2000] ^= 0xFF;

        // Create delta
        byte[] deltaBytes = IupdDeltaV1.CreateDeltaV1(baseBytes, targetBytes);

        // Compute target hash
        byte[] targetHash = new byte[32];
        Blake3Ieee.Compute(targetBytes, targetHash);
        string hashHex = BitConverter.ToString(targetHash).Replace("-", "").ToLowerInvariant();

        // Write files
        File.WriteAllBytes(Path.Combine(caseDir, "base.bin"), baseBytes);
        File.WriteAllBytes(Path.Combine(caseDir, "target.bin"), targetBytes);
        File.WriteAllBytes(Path.Combine(caseDir, "delta.iupd.delta"), deltaBytes);
        File.WriteAllText(Path.Combine(caseDir, "expected_hash.txt"), hashHex);

        // Write readme
        string readme = $"""
        Case 01: Small File Delta
        Base:   4096 bytes, deterministic pattern (i * 0x47)
        Target: 3 bytes changed (flipped at offsets 100, 500, 2000)
        Delta:  {deltaBytes.Length} bytes
        Hash:   {hashHex}
        """;
        File.WriteAllText(Path.Combine(caseDir, "readme.txt"), readme);

        Console.WriteLine($"Case01: base={baseBytes.Length}, target={targetBytes.Length}, delta={deltaBytes.Length}");
    }

    private static void GenerateCase02()
    {
        string caseDir = "vectors/small/iupd/delta/v1/case02";
        Directory.CreateDirectory(caseDir);

        // Create deterministic base (larger)
        byte[] baseBytes = new byte[65536];
        for (int i = 0; i < baseBytes.Length; i++)
            baseBytes[i] = (byte)((i * 0x89) & 0xFF);

        // Create target with sparse changes
        byte[] targetBytes = (byte[])baseBytes.Clone();
        for (int chunkIdx = 0; chunkIdx < 5; chunkIdx++)
        {
            int offset = chunkIdx * 16384;
            if (offset < targetBytes.Length)
                targetBytes[offset] ^= 0xFF;
        }

        // Create delta
        byte[] deltaBytes = IupdDeltaV1.CreateDeltaV1(baseBytes, targetBytes);

        // Compute target hash
        byte[] targetHash = new byte[32];
        Blake3Ieee.Compute(targetBytes, targetHash);
        string hashHex = BitConverter.ToString(targetHash).Replace("-", "").ToLowerInvariant();

        // Write files
        File.WriteAllBytes(Path.Combine(caseDir, "base.bin"), baseBytes);
        File.WriteAllBytes(Path.Combine(caseDir, "target.bin"), targetBytes);
        File.WriteAllBytes(Path.Combine(caseDir, "delta.iupd.delta"), deltaBytes);
        File.WriteAllText(Path.Combine(caseDir, "expected_hash.txt"), hashHex);

        // Write readme
        string readme = $"""
        Case 02: Large File Delta
        Base:   65536 bytes, deterministic pattern (i * 0x89)
        Target: 5 chunks changed (at offsets 0, 16384, 32768, 49152, 65520)
        Delta:  {deltaBytes.Length} bytes
        Hash:   {hashHex}
        Ratio:  {((double)deltaBytes.Length / baseBytes.Length * 100):F2}%
        """;
        File.WriteAllText(Path.Combine(caseDir, "readme.txt"), readme);

        Console.WriteLine($"Case02: base={baseBytes.Length}, target={targetBytes.Length}, delta={deltaBytes.Length}");
    }
}
