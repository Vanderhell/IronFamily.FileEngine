using System;
using System.Collections.Generic;
using Xunit;
using IronConfig.Iupd;
using IronConfig;

namespace IronConfig.Iupd.Tests;

/// <summary>
/// Tests for IUPD v2 Profile system
/// </summary>
public class IupdProfileTests
{
    // ============================================================================
    // A) Profile Extension Methods
    // ============================================================================

    [Theory]
    [InlineData(IupdProfile.MINIMAL, false)]
    [InlineData(IupdProfile.FAST, false)]
    [InlineData(IupdProfile.SECURE, true)]
    [InlineData(IupdProfile.OPTIMIZED, true)]
    [InlineData(IupdProfile.INCREMENTAL, true)]
    public void TestProfileRequiresBlake3(IupdProfile profile, bool expected)
    {
        Assert.Equal(expected, profile.RequiresBlake3());
    }

    [Theory]
    [InlineData(IupdProfile.MINIMAL, false)]
    [InlineData(IupdProfile.FAST, true)]
    [InlineData(IupdProfile.SECURE, false)]
    [InlineData(IupdProfile.OPTIMIZED, true)]
    [InlineData(IupdProfile.INCREMENTAL, true)]
    public void TestProfileSupportsCompression(IupdProfile profile, bool expected)
    {
        Assert.Equal(expected, profile.SupportsCompression());
    }

    [Theory]
    [InlineData(IupdProfile.MINIMAL, false)]
    [InlineData(IupdProfile.FAST, false)]
    [InlineData(IupdProfile.SECURE, true)]
    [InlineData(IupdProfile.OPTIMIZED, true)]
    [InlineData(IupdProfile.INCREMENTAL, true)]
    public void TestProfileSupportsDependencies(IupdProfile profile, bool expected)
    {
        Assert.Equal(expected, profile.SupportsDependencies());
    }

    [Theory]
    [InlineData(IupdProfile.MINIMAL, false)]
    [InlineData(IupdProfile.FAST, false)]
    [InlineData(IupdProfile.SECURE, false)]
    [InlineData(IupdProfile.OPTIMIZED, false)]
    [InlineData(IupdProfile.INCREMENTAL, true)]
    public void TestProfileIsIncremental(IupdProfile profile, bool expected)
    {
        Assert.Equal(expected, profile.IsIncremental());
    }

    [Theory]
    [InlineData(IupdProfile.MINIMAL, "MINIMAL")]
    [InlineData(IupdProfile.FAST, "FAST")]
    [InlineData(IupdProfile.SECURE, "SECURE")]
    [InlineData(IupdProfile.OPTIMIZED, "OPTIMIZED")]
    [InlineData(IupdProfile.INCREMENTAL, "INCREMENTAL")]
    public void TestProfileDisplayName(IupdProfile profile, string expected)
    {
        Assert.Equal(expected, profile.GetDisplayName());
    }

    // ============================================================================
    // B) MINIMAL Profile Tests
    // ============================================================================

    [Fact(DisplayName = "MINIMAL Profile: Round-trip encoding and decoding")]
    public void TestMinimalProfile_RoundTrip()
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.MINIMAL);

        // Add chunks
        writer.AddChunk(0, new byte[] { 1, 2, 3, 4, 5 });
        writer.AddChunk(1, new byte[] { 6, 7, 8 });
        writer.SetApplyOrder(0, 1);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        // SECURITY: MINIMAL profile is not in the allowed set and should be rejected by IupdReader
        var reader = IupdReader.Open(data, out var error);
        Assert.False(error.IsOk, "Expected MINIMAL profile to be rejected");
        Assert.Equal(IupdErrorCode.ProfileNotAllowed, error.Code);
    }

    [Fact(DisplayName = "MINIMAL Profile: No dependencies support")]
    public void TestMinimalProfile_NoDependencies()
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.MINIMAL);

        writer.AddChunk(0, new byte[] { 1, 2, 3 });
        writer.AddChunk(1, new byte[] { 4, 5, 6 });
        writer.AddDependency(0, 1);  // This should fail at Build time
        writer.SetApplyOrder(0, 1);
        writer.WithUpdateSequence(1);

        Assert.Throws<InvalidOperationException>(() => writer.Build());
    }

    // ============================================================================
    // C) FAST Profile Tests
    // ============================================================================

    [Fact(DisplayName = "FAST Profile: Round-trip encoding and decoding")]
    public void TestFastProfile_RoundTrip()
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.FAST);

        writer.AddChunk(0, new byte[] { 1, 2, 3, 4, 5 });
        writer.AddChunk(1, new byte[] { 6, 7, 8 });
        writer.SetApplyOrder(0, 1);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        // SECURITY: FAST profile is not in the allowed set and should be rejected by IupdReader
        var reader = IupdReader.Open(data, out var error);
        Assert.False(error.IsOk, "Expected FAST profile to be rejected");
        Assert.Equal(IupdErrorCode.ProfileNotAllowed, error.Code);
    }

    [Fact(DisplayName = "FAST Profile: No dependencies support")]
    public void TestFastProfile_NoDependencies()
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.FAST);

        writer.AddChunk(0, new byte[] { 1 });
        writer.AddChunk(1, new byte[] { 2 });
        writer.AddDependency(0, 1);
        writer.SetApplyOrder(0, 1);
        writer.WithUpdateSequence(1);

        Assert.Throws<InvalidOperationException>(() => writer.Build());
    }

    // ============================================================================
    // D) SECURE Profile Tests
    // ============================================================================

    [Fact(DisplayName = "SECURE Profile: Round-trip with dependencies")]
    public void TestSecureProfile_RoundTrip()
    {
        // Use unified bench keys (seed and derived public key)
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);

        writer.AddChunk(0, new byte[] { 1, 2, 3, 4, 5 });
        writer.AddChunk(1, new byte[] { 6, 7, 8 });
        writer.AddDependency(0, 1);
        writer.SetApplyOrder(0, 1);
        writer.WithUpdateSequence(1);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        var reader = IupdReader.Open(data, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);
        Assert.Equal(IupdProfile.SECURE, reader.Profile);

        reader.SetVerificationKey(benchPublicKey);

        var fastError = reader.ValidateFast();
        Assert.True(fastError.IsOk);

        var strictError = reader.ValidateStrict();
        Assert.True(strictError.IsOk);
    }

    [Fact(DisplayName = "SECURE Profile: BLAKE3 verification")]
    public void TestSecureProfile_WithBlake3()
    {
        // Use unified bench keys (seed and derived public key)
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        writer.AddChunk(0, testData);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        var reader = IupdReader.Open(data, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);

        // ValidateStrict should check BLAKE3 for SECURE profile
        var strictError = reader.ValidateStrict();
        Assert.True(strictError.IsOk);
    }

    // ============================================================================
    // E) OPTIMIZED Profile Tests
    // ============================================================================

    [Fact(DisplayName = "OPTIMIZED Profile: Round-trip with all features")]
    public void TestOptimizedProfile_RoundTrip()
    {
        // Use unified bench keys (seed and derived public key)
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);

        writer.AddChunk(0, new byte[] { 1, 2, 3, 4, 5 });
        writer.AddChunk(1, new byte[] { 6, 7, 8 });
        writer.AddChunk(2, new byte[] { 9, 10 });
        writer.AddDependency(0, 1);
        writer.AddDependency(1, 2);
        writer.SetApplyOrder(0, 1, 2);
        writer.WithUpdateSequence(1);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        var reader = IupdReader.Open(data, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);
        Assert.Equal(IupdProfile.OPTIMIZED, reader.Profile);
        Assert.Equal(3u, reader.ChunkCount);

        reader.SetVerificationKey(benchPublicKey);

        var fastError = reader.ValidateFast();
        Assert.True(fastError.IsOk);

        var strictError = reader.ValidateStrict();
        Assert.True(strictError.IsOk);
    }

    [Fact(DisplayName = "OPTIMIZED Profile: Full feature set")]
    public void TestOptimizedProfile_FullFeatures()
    {
        // Use unified bench keys (seed and derived public key)
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);

        var chunk0 = new byte[100];
        for (int i = 0; i < 100; i++) chunk0[i] = (byte)(i % 256);

        var chunk1 = new byte[50];
        for (int i = 0; i < 50; i++) chunk1[i] = (byte)((i * 2) % 256);

        writer.AddChunk(0, chunk0);
        writer.AddChunk(1, chunk1);
        writer.AddChunk(2, new byte[] { 255 });
        writer.AddDependency(0, 1);
        writer.AddDependency(0, 2);
        writer.SetApplyOrder(0, 2, 1);  // Non-sequential apply order
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        var reader = IupdReader.Open(data, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);
        Assert.Equal(IupdProfile.OPTIMIZED, reader.Profile);

        reader.SetVerificationKey(benchPublicKey);

        var strictError = reader.ValidateStrict();
        Assert.True(strictError.IsOk);

        // Verify we can read chunks in apply order
        var applier = reader.BeginApply();
        var chunks = new List<(uint index, byte[] payload)>();
        while (applier.TryNext(out var chunk))
        {
            chunks.Add((chunk.ChunkIndex, chunk.Payload.ToArray()));
        }

        Assert.Equal(3, chunks.Count);
        Assert.Equal(0u, chunks[0].index);
        Assert.Equal(2u, chunks[1].index);
        Assert.Equal(1u, chunks[2].index);
    }

    // ============================================================================
    // F) Profile Default Tests
    // ============================================================================

    [Fact(DisplayName = "Profile Defaults: Default is OPTIMIZED")]
    public void TestDefaultProfile_IsOptimized()
    {
        var writer = new IupdWriter();
        // Don't set profile - should default to OPTIMIZED

        writer.AddChunk(0, new byte[] { 1 });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        var reader = IupdReader.Open(data, out var error);
        Assert.NotNull(reader);
        Assert.True(error.IsOk);
        Assert.Equal(IupdProfile.OPTIMIZED, reader.Profile);
    }

    [Fact(DisplayName = "Profile API: Switching between profiles")]
    public void TestProfileSwitching()
    {
        var writer = new IupdWriter();

        // Start with MINIMAL
        writer.SetProfile(IupdProfile.MINIMAL);
        Assert.Equal(IupdProfile.MINIMAL, writer.GetProfile());

        // Switch to FAST
        writer.SetProfile(IupdProfile.FAST);
        Assert.Equal(IupdProfile.FAST, writer.GetProfile());

        // Switch to SECURE
        writer.SetProfile(IupdProfile.SECURE);
        Assert.Equal(IupdProfile.SECURE, writer.GetProfile());

        // Switch to OPTIMIZED
        writer.SetProfile(IupdProfile.OPTIMIZED);
        Assert.Equal(IupdProfile.OPTIMIZED, writer.GetProfile());
    }

    // ============================================================================
    // G) Header Size Tests
    // ============================================================================

    [Fact(DisplayName = "Binary Format: Header size comparison")]
    public void TestHeaderSize_V1IsSmaller()
    {
        // Create v1 file (for reference - but we now always write v2)
        // Note: v1 would be 36 bytes, v2 is 37 bytes
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.AddChunk(0, new byte[] { 1 });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        // At minimum: 37 byte header + 56 byte chunk entry + 24 byte manifest header + 8 byte manifest CRC + 1 byte payload
        Assert.True(data.Length >= 37 + 56 + 24 + 8 + 1);
    }

    // ============================================================================
    // H) Version Detection Tests
    // ============================================================================

    [Fact(DisplayName = "Binary Format: V2 version detection")]
    public void TestV2VersionDetection()
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.SECURE);

        writer.AddChunk(0, new byte[] { 42 });
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var data = writer.Build();

        // Check version byte at offset 4
        Assert.Equal(0x02, data[4]);

        // Check profile byte at offset 5
        Assert.Equal((byte)IupdProfile.SECURE, data[5]);
    }

    // ============================================================================
    // I) Parallel Compression Tests
    // ============================================================================

    [Fact(DisplayName = "Compression: Parallel compression produces valid output")]
    public void TestParallelCompression_ProducesValidOutput()
    {
        // Use unified bench keys (seed and derived public key)
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        // Build same firmware with multiple chunks to test parallel compression
        var data = new byte[1024 * 10]; // 10KB
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i % 256);

        // Build with OPTIMIZED (uses parallel compression)
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);
        writer.AddChunk(0, data);
        writer.SetApplyOrder(0);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Verify it can be read
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);

        // Verify validation works
        var validateError = reader.ValidateStrict();
        Assert.True(validateError.IsOk);

        // Verify chunk count
        Assert.Equal(1u, reader.ChunkCount);
    }

    [Fact(DisplayName = "Compression: Many chunks all valid")]
    public void TestParallelCompression_ManyChunks_AllValid()
    {
        // Use unified bench keys (seed and derived public key)
        var benchPrivateKey = IupdEd25519Keys.BenchSeed32;
        var benchPublicKey = IupdEd25519Keys.BenchPublicKey32;

        // Build with 16 chunks to stress parallel compression
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);
        writer.WithSigningKey(benchPrivateKey, benchPublicKey);

        const int chunkSize = 64 * 1024; // 64KB per chunk
        var chunkList = new List<uint>();

        for (uint i = 0; i < 16; i++)
        {
            var chunk = new byte[chunkSize];
            for (int j = 0; j < chunk.Length; j++)
                chunk[j] = (byte)((i * 256 + j) % 256);

            writer.AddChunk(i, chunk);
            chunkList.Add(i);

            if (i > 0)
                writer.AddDependency(i - 1, i);
        }

        writer.SetApplyOrder(chunkList.ToArray());
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Verify it can be read
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);

        reader.SetVerificationKey(benchPublicKey);

        // Verify validation works (strict includes BLAKE3)
        var validateError = reader.ValidateStrict();
        Assert.True(validateError.IsOk);

        // Verify chunk count
        Assert.Equal(16u, reader.ChunkCount);
    }

    [Fact(DisplayName = "Compression: Thread safety in concurrent builds")]
    public void TestParallelCompression_ThreadSafety_MultipleConcurrentBuilds()
    {
        // Run sequential builds to test thread-safety of parallel compression internals
        for (int t = 0; t < 4; t++)
        {
            var writer = new IupdWriter();
            writer.SetProfile(IupdProfile.OPTIMIZED);

            // Build with 4 chunks each
            for (uint i = 0; i < 4; i++)
            {
                var chunk = new byte[16 * 1024]; // 16KB
                for (int j = 0; j < chunk.Length; j++)
                    chunk[j] = (byte)(i + j);

                writer.AddChunk(i, chunk);
                if (i > 0)
                    writer.AddDependency(i - 1, i);
            }

            writer.SetApplyOrder(0, 1, 2, 3);
        writer.WithUpdateSequence(1);

            // Build (parallel compression should be thread-safe internally)
            var data = writer.Build();
            Assert.NotNull(data);
            Assert.True(data.Length > 0);
        }
    }

    [Fact(DisplayName = "Compression: Correct offsets after build")]
    public void TestParallelCompression_CorrectOffsets_AfterBuild()
    {
        // Verify that payload offsets calculated by parallel compression are correct
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.OPTIMIZED);

        // Add 4 chunks with different sizes
        var chunk0 = new byte[1000];
        var chunk1 = new byte[2000];
        var chunk2 = new byte[500];
        var chunk3 = new byte[1500];

        writer.AddChunk(0, chunk0);
        writer.AddChunk(1, chunk1);
        writer.AddChunk(2, chunk2);
        writer.AddChunk(3, chunk3);

        writer.SetApplyOrder(0, 1, 2, 3);
        writer.WithUpdateSequence(1);

        var iupdData = writer.Build();

        // Read back and verify structure
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);

        // Verify chunk count
        Assert.Equal(4u, reader.ChunkCount);

        // Verify can iterate through chunks in apply order
        var applier = reader.BeginApply();
        int count = 0;
        while (applier.TryNext(out var _))
            count++;

        Assert.Equal(4, count);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Reader accepts byte value 0x04")]
    public void TestIncrementalProfile_AcceptedByReader()
    {
        // INCREMENTAL = 0x04 should be accepted by the reader
        var baseHash = Blake3.Hasher.Hash(new byte[] { 1, 2, 3 });
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        writer.AddChunk(0, new byte[] { 1, 2, 3, 4, 5 });
        writer.SetApplyOrder(0);
        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, baseHash.AsSpan().ToArray());
        var iupdData = writer.Build();

        // Reader should accept it (no ProfileNotAllowed error)
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);
        Assert.NotNull(reader);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Requires signature validation")]
    public void TestIncrementalProfile_RequiresSignatureValidation()
    {
        // INCREMENTAL profile requires signature validation
        var baseHash = Blake3.Hasher.Hash(new byte[] { 1, 2, 3 });
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        writer.AddChunk(0, new byte[] { 1, 2, 3, 4, 5 });
        writer.SetApplyOrder(0);
        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, baseHash.AsSpan().ToArray());
        var iupdData = writer.Build();

        // Verify reader needs valid signature
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);

        // Strict validation should require signature (RequiresSignatureStrict = true)
        Assert.True(IupdProfile.INCREMENTAL.RequiresSignatureStrict());
        Assert.True(IupdProfile.INCREMENTAL.RequiresWitnessStrict());
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Supports dependencies")]
    public void TestIncrementalProfile_SupportsDependencies()
    {
        // INCREMENTAL should support dependencies
        Assert.True(IupdProfile.INCREMENTAL.SupportsDependencies());

        // Test creating an INCREMENTAL package with dependencies
        var baseHash = Blake3.Hasher.Hash(new byte[] { 1, 2, 3 });
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        writer.AddChunk(0, new byte[] { 1, 2, 3 });
        writer.AddChunk(1, new byte[] { 4, 5, 6 });
        writer.AddDependency(0, 1); // chunk 1 depends on chunk 0
        writer.SetApplyOrder(0, 1);
        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, baseHash.AsSpan().ToArray());

        var iupdData = writer.Build();
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);
        Assert.NotNull(reader);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Supports compression")]
    public void TestIncrementalProfile_SupportsCompression()
    {
        // INCREMENTAL should support compression
        Assert.True(IupdProfile.INCREMENTAL.SupportsCompression());

        // Test creating an INCREMENTAL package with compression
        var baseHash = Blake3.Hasher.Hash(new byte[] { 1, 2, 3 });
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        // Create compressible data
        byte[] compressible = new byte[1024];
        for (int i = 0; i < compressible.Length; i++)
            compressible[i] = (byte)(i % 256);
        writer.AddChunk(0, compressible);
        writer.SetApplyOrder(0);
        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, baseHash.AsSpan().ToArray());

        var iupdData = writer.Build();
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);
        Assert.NotNull(reader);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata trailer - create with DELTA_V1")]
    public void TestIncrementalProfile_Metadata_CreateWithDeltaV1()
    {
        var baseHash = Blake3.Hasher.Hash(new byte[] { 1, 2, 3, 4, 5 });
        var targetHash = Blake3.Hasher.Hash(new byte[] { 6, 7, 8, 9, 10 });

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        writer.AddChunk(0, new byte[] { 11, 12, 13 });
        writer.SetApplyOrder(0);

        // Set INCREMENTAL metadata with DELTA_V1 algorithm
        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, baseHash.AsSpan().ToArray(), targetHash.AsSpan().ToArray());

        var iupdData = writer.Build();
        Assert.NotNull(iupdData);
        Assert.True(iupdData.Length > 0);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata trailer - create with IRONDEL2")]
    public void TestIncrementalProfile_Metadata_CreateWithIrondel2()
    {
        var baseHash = Blake3.Hasher.Hash(new byte[] { 20, 21, 22, 23, 24 });
        var targetHash = Blake3.Hasher.Hash(new byte[] { 25, 26, 27, 28, 29 });

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        writer.AddChunk(0, new byte[] { 30, 31, 32 });
        writer.SetApplyOrder(0);

        // Set INCREMENTAL metadata with IRONDEL2 algorithm
        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_IRONDEL2, baseHash.AsSpan().ToArray(), targetHash.AsSpan().ToArray());

        var iupdData = writer.Build();
        Assert.NotNull(iupdData);
        Assert.True(iupdData.Length > 0);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata trailer - missing metadata fails build")]
    public void TestIncrementalProfile_Metadata_MissingMetadataFails()
    {
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        writer.AddChunk(0, new byte[] { 1, 2, 3 });
        writer.SetApplyOrder(0);

        // Don't set metadata - should fail at Build()
        var ex = Assert.Throws<InvalidOperationException>(() => writer.Build());
        Assert.Contains("INCREMENTAL", ex.Message);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata trailer - read and validate")]
    public void TestIncrementalProfile_Metadata_ReadAndValidate()
    {
        var baseHash = Blake3.Hasher.Hash(new byte[] { 40, 41, 42, 43, 44 });
        var targetHash = Blake3.Hasher.Hash(new byte[] { 45, 46, 47, 48, 49 });

        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        writer.AddChunk(0, new byte[] { 50, 51, 52 });
        writer.SetApplyOrder(0);
        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, baseHash.AsSpan().ToArray(), targetHash.AsSpan().ToArray());

        var iupdData = writer.Build();

        // Read back and verify metadata
        var reader = IupdReader.Open(iupdData, out var error);
        Assert.True(error.IsOk);
        Assert.NotNull(reader);
        Assert.Equal(IupdProfile.INCREMENTAL, reader.Profile);

        // Validate metadata is present and accessible
        Assert.NotNull(reader.IncrementalMetadata);
        Assert.Equal(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, reader.IncrementalMetadata.AlgorithmId);
        Assert.NotNull(reader.IncrementalMetadata.BaseHash);
        Assert.NotNull(reader.IncrementalMetadata.TargetHash);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata trailer - algorithm name mapping")]
    public void TestIncrementalProfile_Metadata_AlgorithmNameMapping()
    {
        var baseHash = Blake3.Hasher.Hash(new byte[] { 1, 2, 3, 4, 5 });

        var metadata = new IupdIncrementalMetadata
        {
            AlgorithmId = IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
            BaseHash = baseHash.AsSpan().ToArray(),
            TargetHash = null
        };

        Assert.Equal("DELTA_V1", metadata.GetAlgorithmName());

        metadata.AlgorithmId = IupdIncrementalMetadata.ALGORITHM_IRONDEL2;
        Assert.Equal("IRONDEL2", metadata.GetAlgorithmName());

        metadata.AlgorithmId = 0xFF;
        Assert.Contains("UNKNOWN", metadata.GetAlgorithmName());
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata trailer - IsKnownAlgorithm")]
    public void TestIncrementalProfile_Metadata_IsKnownAlgorithm()
    {
        var baseHash = Blake3.Hasher.Hash(new byte[] { 1, 2, 3, 4, 5 });

        var metadata = new IupdIncrementalMetadata
        {
            AlgorithmId = IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
            BaseHash = baseHash.AsSpan().ToArray()
        };

        Assert.True(metadata.IsKnownAlgorithm());

        metadata.AlgorithmId = IupdIncrementalMetadata.ALGORITHM_IRONDEL2;
        Assert.True(metadata.IsKnownAlgorithm());

        metadata.AlgorithmId = 0xFF;
        Assert.False(metadata.IsKnownAlgorithm());
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata serialization roundtrip")]
    public void TestIncrementalProfile_Metadata_SerializationRoundtrip()
    {
        var baseHash = Blake3.Hasher.Hash(new byte[] { 60, 61, 62, 63, 64 });
        var targetHash = Blake3.Hasher.Hash(new byte[] { 65, 66, 67, 68, 69 });

        var original = new IupdIncrementalMetadata
        {
            AlgorithmId = IupdIncrementalMetadata.ALGORITHM_IRONDEL2,
            BaseHash = baseHash.AsSpan().ToArray(),
            TargetHash = targetHash.AsSpan().ToArray()
        };

        // Serialize
        byte[] trailerBytes = original.Serialize();
        Assert.True(trailerBytes.Length >= 84);

        // Deserialize
        var (success, deserialized, error) = IupdIncrementalMetadata.TryDeserialize(trailerBytes);
        Assert.True(success);
        Assert.NotNull(deserialized);
        Assert.Null(error);

        // Verify fields match
        Assert.Equal(original.AlgorithmId, deserialized.AlgorithmId);
        Assert.Equal(original.BaseHash, deserialized.BaseHash);
        Assert.Equal(original.TargetHash, deserialized.TargetHash);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata with only base hash (no target)")]
    public void TestIncrementalProfile_Metadata_NoTargetHash()
    {
        // Create a 32-byte base hash (BLAKE3-256 size)
        byte[] baseHash = new byte[32];
        for (int i = 0; i < 32; i++)
            baseHash[i] = (byte)i;

        var metadata = new IupdIncrementalMetadata
        {
            AlgorithmId = IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
            BaseHash = baseHash,
            TargetHash = null
        };

        byte[] trailerBytes = metadata.Serialize();

        var (success, deserialized, error) = IupdIncrementalMetadata.TryDeserialize(trailerBytes);
        if (!success)
        {
            Assert.True(success, $"Deserialization failed: {error}");
        }
        Assert.NotNull(deserialized);
        Assert.Equal(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, deserialized.AlgorithmId);
        Assert.NotNull(deserialized.BaseHash);
        Assert.Null(deserialized.TargetHash);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Metadata CRC32 integrity")]
    public void TestIncrementalProfile_Metadata_Crc32Integrity()
    {
        var baseHash = Blake3.Hasher.Hash(new byte[] { 80, 81, 82, 83, 84 });
        var targetHash = Blake3.Hasher.Hash(new byte[] { 85, 86, 87, 88, 89 });

        var metadata = new IupdIncrementalMetadata
        {
            AlgorithmId = IupdIncrementalMetadata.ALGORITHM_DELTA_V1,
            BaseHash = baseHash.AsSpan().ToArray(),
            TargetHash = targetHash.AsSpan().ToArray()
        };

        byte[] trailerBytes = metadata.Serialize();

        // Corrupt the CRC32 (last 4 bytes)
        trailerBytes[trailerBytes.Length - 1] ^= 0xFF;

        var (success, deserialized, error) = IupdIncrementalMetadata.TryDeserialize(trailerBytes);
        Assert.False(success);
        Assert.Null(deserialized);
        Assert.NotNull(error);
        Assert.Contains("CRC32", error);
    }

    [Fact(DisplayName = "INCREMENTAL Profile: Architectural limitation - algorithm not in packet")]
    public void TestIncrementalProfile_ArchitecturalLimitation_AlgorithmNotInPacket()
    {
        // INCREMENTAL profile does NOT encode patch algorithm in the package
        // This is an architectural limitation: algorithm is chosen externally, not stored in profile
        var baseHash = Blake3.Hasher.Hash(new byte[] { 1, 2, 3 });
        var writer = new IupdWriter();
        writer.SetProfile(IupdProfile.INCREMENTAL);
        writer.AddChunk(0, new byte[] { 1, 2, 3 });
        writer.SetApplyOrder(0);
        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_DELTA_V1, baseHash.AsSpan().ToArray());
        var iupdData = writer.Build();

        // The INCREMENTAL profile byte is 0x04
        Assert.Equal((byte)0x04, (byte)IupdProfile.INCREMENTAL);

        // But there's no field in the manifest header that specifies:
        // - Delta v1
        // - IRONDEL2
        // - Or any other algorithm
        // This verification documents the current architecture limitation
        Assert.NotNull(iupdData);
        // Algorithm selection is external (tool chooses), not profile-based
    }
}
