using System;
using System.Collections.Generic;
using System.IO;
using IronConfig;
using IronConfig.Iupd;
using IronConfig.Iupd.Delta;
using IronFamily.MegaBench.Validation;

namespace IronFamily.MegaBench.Datasets.IUpd;

/// <summary>
/// Deterministic IUPD dataset generator.
///
/// Standard datasets: 10KB, 100KB, 1MB, 10MB binary blobs
/// Delta datasets: base v1 + updated v2 with change rates: 1%, 10%, 50%, reorder
/// Profiles: MINIMAL, FAST, SECURE, OPTIMIZED, DELTA
/// Uses IRONFAMILY_DETERMINISTIC=1 for fixed seed
/// </summary>
public static class IUpdDatasetGenerator
{
    /// <summary>
    /// Generate a standard IUPD dataset (single chunk).
    /// Throws MegaBenchDatasetException on any failure (no silent returns).
    /// </summary>
    public static byte[] GenerateDataset(string sizeLabel, IupdProfile profile)
    {
        int targetSize = ParseSize(sizeLabel);
        if (targetSize == 0)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.SIZE_PARSE_FAILED,
                $"iupd_{profile}_{sizeLabel}",
                $"Unknown size label: {sizeLabel}",
                engine: "iupd",
                profile: profile.ToString(),
                sizeLabel: sizeLabel);
            ex.LogToArtifacts();
            throw ex;
        }

        // Use fixed seed if IRONFAMILY_DETERMINISTIC=1
        int seed = Environment.GetEnvironmentVariable("IRONFAMILY_DETERMINISTIC") == "1" ? 42 : Random.Shared.Next();
        var rng = new Random(seed);

        // Generate random binary blob
        var payload = new byte[targetSize];
        rng.NextBytes(payload);

        // Create IUPD file with single chunk
        byte[] encoded;
        try
        {
            var writer = new IupdWriter();
            writer.SetProfile(profile);
            writer.AddChunk(0, payload);
            writer.SetApplyOrder(0);
            ApplyRequiredProfileMetadata(writer, profile, payload, payload);
            encoded = writer.Build();
        }
        catch (Exception buildEx)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.ENCODE_FAILED,
                $"iupd_{profile}_{sizeLabel}",
                $"IUPD build failed: {buildEx.Message}",
                engine: "iupd",
                profile: profile.ToString(),
                sizeLabel: sizeLabel,
                innerException: buildEx);
            ex.LogToArtifacts();
            throw ex;
        }

        // Mandatory validation gate
        var datasetId = $"iupd_{profile}_{sizeLabel}";
        var outputDir = Path.Combine(GetRepoRoot(), "artifacts", "bench", "megabench_datasets", datasetId);

        System.IO.Directory.CreateDirectory(outputDir);

        var filePath = System.IO.Path.Combine(outputDir, $"{datasetId}.bin");
        System.IO.File.WriteAllBytes(filePath, encoded);

        var validator = new Validation.IupdDatasetValidator();
        var validationResults = validator.Validate(datasetId, sizeLabel, outputDir, filePath, profile.ToString());
        Validation.ValidationWriter.WriteResults(outputDir, validationResults);

        // Check validation results - strict must pass
        foreach (var result in validationResults)
        {
            if (!result.Passed && result.Mode == "Strict")
            {
                var ex = new MegaBenchDatasetException(
                    DatasetErrorCode.VALIDATION_FAILED,
                    datasetId,
                    $"IUPD validation failed in mode '{result.Mode}': {result.ErrorCode}",
                    engine: "iupd",
                    profile: profile.ToString(),
                    sizeLabel: sizeLabel);
                ex.LogToArtifacts();
                throw ex;
            }
        }

        return encoded;
    }

    /// <summary>
    /// Generate a delta IUPD dataset (base v1 + updated v2 with specified change rate).
    /// Returns (baseBytes, updatedBytes) tuple. Throws on any failure (no silent returns).
    /// </summary>
    public static (byte[], byte[]) GenerateDeltaDataset(string sizeLabel, string changeRate)
    {
        int targetSize = ParseSize(sizeLabel);
        if (targetSize == 0)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.SIZE_PARSE_FAILED,
                $"iupd_delta_{changeRate}_{sizeLabel}",
                $"Unknown size label: {sizeLabel}",
                engine: "iupd",
                profile: "DELTA",
                sizeLabel: sizeLabel);
            ex.LogToArtifacts();
            throw ex;
        }

        // Use fixed seed if IRONFAMILY_DETERMINISTIC=1
        int seed = Environment.GetEnvironmentVariable("IRONFAMILY_DETERMINISTIC") == "1" ? 42 : Random.Shared.Next();
        var rng = new Random(seed);

        // Generate base v1
        var basePayload = new byte[targetSize];
        rng.NextBytes(basePayload);

        // Generate updated v2 with specified change rate
        var updatedPayload = (byte[])basePayload.Clone();
        int changePercent = ParseChangeRate(changeRate);
        int bytesToChange = Math.Max(1, (targetSize * changePercent) / 100);

        if (changeRate == "reorder")
        {
            // Reorder blocks instead of changing bytes
            ReorderPayload(updatedPayload, rng);
        }
        else
        {
            // Change random bytes
            for (int i = 0; i < bytesToChange; i++)
            {
                int index = rng.Next(targetSize);
                updatedPayload[index] = (byte)rng.Next(256);
            }
        }

        // Create base IUPD file (full image package)
        var baseWriter = new IupdWriter();
        baseWriter.SetProfile(IupdProfile.MINIMAL);
        baseWriter.AddChunk(0, basePayload);
        baseWriter.SetApplyOrder(0);

        byte[] baseBytes;
        try
        {
            baseBytes = baseWriter.Build();
        }
        catch (Exception buildEx)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.ENCODE_FAILED,
                $"iupd_delta_{changeRate}_{sizeLabel}_base",
                $"Base IUPD build failed: {buildEx.Message}",
                engine: "iupd",
                profile: "INCREMENTAL",
                sizeLabel: sizeLabel,
                innerException: buildEx);
            ex.LogToArtifacts();
            throw ex;
        }

        // Create updated IUPD file as a true incremental patch package.
        byte[] updatedBytes;
        try
        {
            byte[] patchPayload = IronDel2.Create(basePayload, updatedPayload);
            var updatedWriter = new IupdWriter();
            updatedWriter.SetProfile(IupdProfile.INCREMENTAL);
            updatedWriter.AddChunk(0, patchPayload);
            updatedWriter.SetApplyOrder(0);
            ApplyRequiredProfileMetadata(updatedWriter, IupdProfile.INCREMENTAL, basePayload, updatedPayload);
            updatedBytes = updatedWriter.Build();
        }
        catch (Exception buildEx)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.ENCODE_FAILED,
                $"iupd_delta_{changeRate}_{sizeLabel}_updated",
                $"Updated IUPD build failed: {buildEx.Message}",
                engine: "iupd",
                profile: "INCREMENTAL",
                sizeLabel: sizeLabel,
                innerException: buildEx);
            ex.LogToArtifacts();
            throw ex;
        }

        return (baseBytes, updatedBytes);
    }

    /// <summary>
    /// Reorder blocks in payload (for testing structural changes).
    /// </summary>
    private static void ReorderPayload(byte[] payload, Random rng)
    {
        const int blockSize = 4096;
        int numBlocks = (payload.Length + blockSize - 1) / blockSize;

        if (numBlocks <= 1)
            return;

        // Swap first and last blocks
        var tempBlock = new byte[blockSize];
        int lastBlockStart = (numBlocks - 1) * blockSize;
        int lastBlockSize = payload.Length - lastBlockStart;

        Array.Copy(payload, 0, tempBlock, 0, Math.Min(blockSize, payload.Length));
        Array.Copy(payload, lastBlockStart, payload, 0, lastBlockSize);
        Array.Copy(tempBlock, 0, payload, lastBlockStart, Math.Min(blockSize, lastBlockSize));
    }

    /// <summary>
    /// Parse size label (10KB, 100KB, etc.) to bytes.
    /// </summary>
    private static int ParseSize(string label)
    {
        return label switch
        {
            "10KB" => 10 * 1024,
            "100KB" => 100 * 1024,
            "1MB" => 1 * 1024 * 1024,
            "10MB" => 10 * 1024 * 1024,
            _ => 0
        };
    }

    /// <summary>
    /// Parse change rate label to percent (or special cases like "reorder").
    /// </summary>
    private static int ParseChangeRate(string label)
    {
        return label switch
        {
            "1%" => 1,
            "10%" => 10,
            "50%" => 50,
            "reorder" => 10, // Default to 10% for reorder
            _ => 0
        };
    }

    private static void ApplyRequiredProfileMetadata(IupdWriter writer, IupdProfile profile, byte[] basePayload, byte[] targetPayload)
    {
        if (profile.RequiresSignatureStrict())
            writer.WithUpdateSequence(1);

        if (!profile.IsIncremental())
            return;

        byte[] baseHash = new byte[32];
        Blake3Ieee.Compute(basePayload, baseHash);

        byte[] targetHash = new byte[32];
        Blake3Ieee.Compute(targetPayload, targetHash);

        writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_IRONDEL2, baseHash, targetHash);
    }

    private static string GetRepoRoot()
    {
        string? current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "tools", "megabench", "MegaBench.csproj")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "tools", "megabench", "MegaBench.csproj")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
