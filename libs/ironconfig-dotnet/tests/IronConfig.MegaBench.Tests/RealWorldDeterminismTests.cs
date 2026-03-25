using Xunit;
using IronFamily.MegaBench.Datasets.RealWorld;
using IronFamily.MegaBench.Competitors.Fairness;
using IronFamily.MegaBench.Semantics;
using System;
using System.Security.Cryptography;

namespace IronFamily.MegaBench.Tests;

public class RealWorldDeterminismTests
{
    [Fact]
    public void RealWorldIcfgDeviceTree10kbHashMatch()
    {
        // PHASE 6: Ensure deterministic generation
        // Set seed
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");

        // Generate dataset twice
        var payload1 = RealWorldDatasetGenerator.GenerateDataset(RealWorldDatasetId.RW_ICFG_DEVICE_TREE_10KB);
        var payload2 = RealWorldDatasetGenerator.GenerateDataset(RealWorldDatasetId.RW_ICFG_DEVICE_TREE_10KB);

        // Compute hashes
        var hash1 = RealWorldProof.ComputePayloadHash(payload1);
        var hash2 = RealWorldProof.ComputePayloadHash(payload2);

        // Hashes must match
        Assert.Equal(hash1, hash2);

        // Compute canonical JSON hashes (if available)
        var canonical1 = IcfgToCanonicalJson.ToCanonicalJson(payload1, minBytes: 1024);
        var canonical2 = IcfgToCanonicalJson.ToCanonicalJson(payload2, minBytes: 1024);

        var canonHash1 = ComputeHash(canonical1);
        var canonHash2 = ComputeHash(canonical2);

        Assert.Equal(canonHash1, canonHash2);
    }

    [Fact]
    public void RealWorldIlogPlcEvents1mbHashMatch()
    {
        // PHASE 6: Ensure deterministic generation
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");

        // Generate dataset twice
        var payload1 = RealWorldDatasetGenerator.GenerateDataset(RealWorldDatasetId.RW_ILOG_PLC_EVENTS_1MB);
        var payload2 = RealWorldDatasetGenerator.GenerateDataset(RealWorldDatasetId.RW_ILOG_PLC_EVENTS_1MB);

        // Compute hashes
        var hash1 = RealWorldProof.ComputePayloadHash(payload1);
        var hash2 = RealWorldProof.ComputePayloadHash(payload2);

        Assert.Equal(hash1, hash2);

        // Compute canonical JSON hashes
        var canonical1 = IlogToCanonicalJson.ToCanonicalJson(payload1, minBytes: 1024);
        var canonical2 = IlogToCanonicalJson.ToCanonicalJson(payload2, minBytes: 1024);

        var canonHash1 = ComputeHash(canonical1);
        var canonHash2 = ComputeHash(canonical2);

        Assert.Equal(canonHash1, canonHash2);
    }

    [Fact]
    public void RealWorldIupdManifest1mbHashMatch()
    {
        // PHASE 6: Ensure deterministic generation
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");

        // Generate dataset twice
        var payload1 = RealWorldDatasetGenerator.GenerateDataset(RealWorldDatasetId.RW_IUPD_MANIFEST_1MB);
        var payload2 = RealWorldDatasetGenerator.GenerateDataset(RealWorldDatasetId.RW_IUPD_MANIFEST_1MB);

        // Compute hashes
        var hash1 = RealWorldProof.ComputePayloadHash(payload1);
        var hash2 = RealWorldProof.ComputePayloadHash(payload2);

        Assert.Equal(hash1, hash2);

        // Compute canonical JSON hashes
        var canonical1 = IupdManifestToCanonicalJson.ToCanonicalJson(payload1, minBytes: 1024);
        var canonical2 = IupdManifestToCanonicalJson.ToCanonicalJson(payload2, minBytes: 1024);

        var canonHash1 = ComputeHash(canonical1);
        var canonHash2 = ComputeHash(canonical2);

        Assert.Equal(canonHash1, canonHash2);
    }

    /// <summary>
    /// Helper: compute SHA256 of bytes.
    /// </summary>
    private static string ComputeHash(byte[] data)
    {
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(data);
            return Convert.ToHexString(hash);
        }
    }
}
