using System;
using Xunit;
using IronConfig.Iupd.Delta;
using IronConfig.Iupd;

namespace IronConfig.DeltaV2.Tests;

/// <summary>
/// IRONDEL2 (Delta v2 CDC) comprehensive test suite.
/// Tests: E2E roundtrip, determinism, and performance ratio vs v1.
/// </summary>
public class IronDel2Tests
{
    /// <summary>
    /// Test 1: E2E roundtrip with small file (64KB) and minimal changes.
    /// base + small changes → patch → apply → output == target ✓
    /// </summary>
    [Fact]
    public void E2E_Roundtrip_Small()
    {
        // Generate deterministic base (64KB)
        byte[] base64k = GenerateDeterministicData(64 * 1024);
        byte[] target64k = (byte[])base64k.Clone();

        // Make small modifications
        target64k[100] ^= 0xFF;
        target64k[5000] ^= 0xFF;
        target64k[64000] ^= 0xFF;

        // Create patch
        byte[] patch = IronDel2.Create(base64k, target64k);
        Assert.NotEmpty(patch);
        Assert.True(patch.Length < base64k.Length, "Patch should be smaller than base");

        // Apply patch
        byte[] output = IronDel2.Apply(base64k, patch, out var error);
        Assert.True(error.IsOk, $"Apply failed: {error}");
        Assert.NotEmpty(output);

        // Verify output == target
        Assert.Equal(target64k.Length, output.Length);
        Assert.True(BytesEqual(output, target64k), "Output must equal target");
    }

    /// <summary>
    /// Test 2: E2E with insert-at-start pattern (common in firmware updates).
    /// base 512KB + 16KB prepend → patch → apply → output == target ✓
    /// This is the key metric: Delta v2 should be 5× smaller than v1.
    /// </summary>
    [Fact]
    public void E2E_InsertAtStart()
    {
        // Generate deterministic base (512KB)
        byte[] base512k = GenerateDeterministicData(512 * 1024);

        // Create target with 16KB inserted at start
        byte[] prepend = GenerateDeterministicData(16 * 1024);
        byte[] target = new byte[base512k.Length + prepend.Length];
        Array.Copy(prepend, target, prepend.Length);
        Array.Copy(base512k, 0, target, prepend.Length, base512k.Length);

        // Create Delta v2 patch
        byte[] patchV2 = IronDel2.Create(base512k, target);
        Assert.NotEmpty(patchV2);

        // Create Delta v1 patch for comparison
        byte[] patchV1 = IupdDeltaV1.CreateDeltaV1(base512k, target);
        Assert.NotEmpty(patchV1);

        // Verify Delta v2 is smaller
        Assert.True(patchV2.Length < patchV1.Length,
            $"Delta v2 ({patchV2.Length}B) should be smaller than Delta v1 ({patchV1.Length}B)");

        // Apply Delta v2 patch
        byte[] outputV2 = IronDel2.Apply(base512k, patchV2, out var errorV2);
        Assert.True(errorV2.IsOk, $"Apply v2 failed: {errorV2}");
        Assert.Equal(target.Length, outputV2.Length);
        Assert.True(BytesEqual(outputV2, target), "Output must equal target");

        // Verify v1 for consistency
        byte[] outputV1 = IupdDeltaV1.ApplyDeltaV1(base512k, patchV1, out var errorV1);
        Assert.True(errorV1.IsOk);
        Assert.True(BytesEqual(outputV1, target), "v1 Output must also equal target");
    }

    /// <summary>
    /// Test 3: Determinism - same inputs produce byte-identical patches.
    /// Create run1, Create run2 → sha256(patch1) == sha256(patch2) ✓
    /// </summary>
    [Fact]
    public void Determinism_SameInputsSamePatch()
    {
        // Generate deterministic base and target
        byte[] base512k = GenerateDeterministicData(512 * 1024);
        byte[] target512k = (byte[])base512k.Clone();
        target512k[1000] ^= 0xFF;
        target512k[100000] ^= 0xFF;

        // Create patches twice
        byte[] patch1 = IronDel2.Create(base512k, target512k);
        byte[] patch2 = IronDel2.Create(base512k, target512k);

        // Compute hashes
        string hash1 = ComputeSha256Hex(patch1);
        string hash2 = ComputeSha256Hex(patch2);

        // Verify identical
        Assert.Equal(hash1, hash2);
        Assert.True(BytesEqual(patch1, patch2), "Patches must be byte-identical");
    }

    /// <summary>
    /// Test 4: Size ratio - Delta v2 must be ≤ v1 size for shift patterns.
    /// On insert/shift: size(delta2) ≤ size(delta1) (target: 5× smaller or fail).
    /// This is the KEY performance metric for IRONDEL2 MVP.
    /// </summary>
    [Fact]
    public void Ratio_BetterThanDeltaV1_OnShift()
    {
        // Test case 1: Insert at start (16KB)
        {
            byte[] base512k = GenerateDeterministicData(512 * 1024);
            byte[] prepend = GenerateDeterministicData(16 * 1024);
            byte[] target = new byte[base512k.Length + prepend.Length];
            Array.Copy(prepend, target, prepend.Length);
            Array.Copy(base512k, 0, target, prepend.Length, base512k.Length);

            byte[] patchV1 = IupdDeltaV1.CreateDeltaV1(base512k, target);
            byte[] patchV2 = IronDel2.Create(base512k, target);

            double ratio = (double)patchV2.Length / patchV1.Length;
            Assert.True(ratio <= 0.2,  // Target: 5× smaller (ratio ≤ 0.2)
                $"Delta v2 ratio {ratio:P1} must be ≤ 20% (5× smaller). v1={patchV1.Length}B, v2={patchV2.Length}B");
        }

        // Test case 2: Middle insert (32KB)
        {
            byte[] base512k = GenerateDeterministicData(512 * 1024);
            byte[] insert = GenerateDeterministicData(32 * 1024);
            byte[] target = new byte[base512k.Length + insert.Length];
            int splitPoint = 256 * 1024;  // Insert at middle
            Array.Copy(base512k, 0, target, 0, splitPoint);
            Array.Copy(insert, 0, target, splitPoint, insert.Length);
            Array.Copy(base512k, splitPoint, target, splitPoint + insert.Length, base512k.Length - splitPoint);

            byte[] patchV1 = IupdDeltaV1.CreateDeltaV1(base512k, target);
            byte[] patchV2 = IronDel2.Create(base512k, target);

            double ratio = (double)patchV2.Length / patchV1.Length;
            Assert.True(ratio <= 0.2,
                $"Delta v2 ratio {ratio:P1} must be ≤ 20%. v1={patchV1.Length}B, v2={patchV2.Length}B");
        }
    }

    /// <summary>
    /// Test 5: Fail-closed validation - corrupted patch is rejected.
    /// </summary>
    [Fact]
    public void Validation_FailsClosed_OnCorruptedPatch()
    {
        byte[] base64k = GenerateDeterministicData(64 * 1024);
        byte[] target = (byte[])base64k.Clone();
        target[1000] ^= 0xFF;

        byte[] patch = IronDel2.Create(base64k, target);

        // Corrupt the patch (flip byte in header)
        patch[50] ^= 0xFF;

        // Apply should fail
        byte[] output = IronDel2.Apply(base64k, patch, out var error);
        Assert.False(error.IsOk, "Corrupted patch should fail validation");
        Assert.Empty(output);
    }

    /// <summary>
    /// Test 6: Backwards compatibility - IupdDeltaV2Cdc wrapper still works.
    /// </summary>
    [Fact]
    public void Compatibility_IupdDeltaV2Cdc_Wrapper()
    {
        byte[] base64k = GenerateDeterministicData(64 * 1024);
        byte[] target = (byte[])base64k.Clone();
        target[500] ^= 0xFF;

        // Call via old wrapper class
        byte[] patch = IupdDeltaV2Cdc.CreateDeltaV2(base64k, target);
        Assert.NotEmpty(patch);

        byte[] output = IupdDeltaV2Cdc.ApplyDeltaV2(base64k, patch, out var error);
        Assert.True(error.IsOk);
        Assert.True(BytesEqual(output, target));
    }

    // Helpers

    private byte[] GenerateDeterministicData(int length)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
            data[i] = (byte)((i * 67 + 17) % 256);
        return data;
    }

    private bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private string ComputeSha256Hex(byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash);
    }
}
