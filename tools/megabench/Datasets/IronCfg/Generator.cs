using System;
using System.Collections.Generic;
using System.IO;
using IronConfig.IronCfg;
using IronFamily.MegaBench.Validation;

namespace IronFamily.MegaBench.Datasets.IronCfg;

/// <summary>
/// Deterministic IronCfg dataset generator.
///
/// Schema: { name: string, value: int64, data: bytes, nested: { id: int64, enabled: bool } }
/// Sizes: 1KB, 10KB, 100KB, 1MB, 10MB
/// Uses IRONFAMILY_DETERMINISTIC=1 for fixed seed
/// </summary>
public static class IronCfgDatasetGenerator
{
    private const string SCHEMA_NAME = "BenchmarkObject";

    /// <summary>
    /// Generate a deterministic IronCfg dataset of specified size.
    /// Throws MegaBenchDatasetException on any failure (no silent returns).
    /// </summary>
    public static byte[] GenerateDataset(string sizeLabel, bool useCrc32 = true)
    {
        int targetSize = ParseSize(sizeLabel);
        if (targetSize == 0)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.SIZE_PARSE_FAILED,
                $"ironcfg_{sizeLabel}",
                $"Unknown size label: {sizeLabel}",
                engine: "ironcfg",
                sizeLabel: sizeLabel);
            ex.LogToArtifacts();
            throw ex;
        }

        // Use fixed seed if IRONFAMILY_DETERMINISTIC=1
        int seed = Environment.GetEnvironmentVariable("IRONFAMILY_DETERMINISTIC") == "1" ? 42 : Random.Shared.Next();
        var rng = new Random(seed);

        // Create schema: { name: string, value: int64, data: bytes, nested: object }
        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "name", FieldType = 0x20, IsRequired = true },
                new IronCfgField { FieldId = 1, FieldName = "value", FieldType = 0x10, IsRequired = true },
                new IronCfgField { FieldId = 2, FieldName = "data", FieldType = 0x22, IsRequired = true },
                new IronCfgField
                {
                    FieldId = 3,
                    FieldName = "nested",
                    FieldType = 0x40,
                    IsRequired = true,
                    ElementSchema = new IronCfgSchema
                    {
                        Fields = new List<IronCfgField>
                        {
                            new IronCfgField { FieldId = 0, FieldName = "id", FieldType = 0x10, IsRequired = true },
                            new IronCfgField { FieldId = 1, FieldName = "enabled", FieldType = 0x01, IsRequired = true }
                        }
                    }
                }
            }
        };

        byte[]? encoded = null;
        int attempts = 0;
        int bufferSize = Math.Max(targetSize * 2, 1024 * 1024); // 2x target or 1MB minimum
        int desiredDataBytes = EstimateDataPayloadBytes(targetSize);

        while (attempts < 10)
        {
            var buffer = new byte[bufferSize];
            var root = GenerateTestObject(rng, desiredDataBytes);

            var err = IronCfgEncoder.Encode(root, schema, useCrc32, false, buffer, out int encodedSize);
            if (!err.IsOk)
            {
                bufferSize *= 2;
                attempts++;
                continue;
            }

            encoded = new byte[encodedSize];
            Array.Copy(buffer, encoded, encodedSize);

            // Check if we're close enough to target size (within 10%)
            if (encoded.Length >= targetSize * 0.9 && encoded.Length <= targetSize * 1.1)
            {
                // Validate before returning
                var datasetId = $"ironcfg_{sizeLabel}";
                var outputDir = Path.Combine(GetRepoRoot(), "artifacts", "bench", "megabench_datasets", datasetId);

                System.IO.Directory.CreateDirectory(outputDir);

                var filePath = System.IO.Path.Combine(outputDir, $"{datasetId}.bin");
                System.IO.File.WriteAllBytes(filePath, encoded);

                var validator = new Validation.IronCfgDatasetValidator();
                var validationResults = validator.Validate(datasetId, sizeLabel, outputDir, filePath, null);
                Validation.ValidationWriter.WriteResults(outputDir, validationResults);

                foreach (var result in validationResults)
                {
                    if (!result.Passed && result.Mode == "Strict")
                    {
                        var ex = new MegaBenchDatasetException(
                            DatasetErrorCode.VALIDATION_FAILED,
                            datasetId,
                            $"IronCfg validation failed in mode '{result.Mode}': {result.ErrorCode}",
                            engine: "ironcfg",
                            sizeLabel: sizeLabel);
                        ex.LogToArtifacts();
                        throw ex;
                    }
                }

                return encoded;
            }

            // Adjust payload size to converge on target output size.
            int delta = targetSize - encoded.Length;
            desiredDataBytes = Math.Max(128, desiredDataBytes + delta);
            bufferSize = Math.Max(bufferSize * 2, (int)(targetSize / 0.8));
            attempts++;
        }

        if (encoded == null)
        {
            var ex = new MegaBenchDatasetException(
                DatasetErrorCode.ENCODE_FAILED,
                $"ironcfg_{sizeLabel}",
                $"Failed to generate IronCfg dataset after {attempts} attempts. Target size: {targetSize}",
                engine: "ironcfg",
                sizeLabel: sizeLabel);
            ex.LogToArtifacts();
            throw ex;
        }

        // Final validation before returning
        var finalDatasetId = $"ironcfg_{sizeLabel}";
        var finalOutputDir = Path.Combine(GetRepoRoot(), "artifacts", "bench", "megabench_datasets", finalDatasetId);

        System.IO.Directory.CreateDirectory(finalOutputDir);

        var finalFilePath = System.IO.Path.Combine(finalOutputDir, $"{finalDatasetId}.bin");
        System.IO.File.WriteAllBytes(finalFilePath, encoded);

        var finalValidator = new Validation.IronCfgDatasetValidator();
        var finalValidationResults = finalValidator.Validate(finalDatasetId, sizeLabel, finalOutputDir, finalFilePath, null);
        Validation.ValidationWriter.WriteResults(finalOutputDir, finalValidationResults);

        foreach (var result in finalValidationResults)
        {
            if (!result.Passed && result.Mode == "Strict")
            {
                var ex = new MegaBenchDatasetException(
                    DatasetErrorCode.VALIDATION_FAILED,
                    finalDatasetId,
                    $"IronCfg validation failed in mode '{result.Mode}': {result.ErrorCode}",
                    engine: "ironcfg",
                    sizeLabel: sizeLabel);
                ex.LogToArtifacts();
                throw ex;
            }
        }

        return encoded;
    }

    /// <summary>
    /// Generate a single test object with nested structure.
    /// </summary>
    private static IronCfgObject GenerateTestObject(Random rng, int dataBytesLength)
    {
        var bytes = new byte[dataBytesLength];
        rng.NextBytes(bytes);

        var nested = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgInt64 { Value = rng.NextInt64() } },
                { 1, new IronCfgBool { Value = rng.Next(2) == 1 } }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgString { Value = $"object_{rng.Next(10000)}" } },
                { 1, new IronCfgInt64 { Value = rng.NextInt64() } },
                { 2, new IronCfgBytes { Data = bytes } },
                { 3, nested }
            }
        };

        return root;
    }

    private static int EstimateDataPayloadBytes(int targetTotalSize)
    {
        // Approximate schema/header/object overhead so output converges near requested size.
        // The data blob dominates total size by design for benchmarking scalability.
        const int estimatedOverhead = 180;
        return Math.Max(128, targetTotalSize - estimatedOverhead);
    }

    /// <summary>
    /// Parse size label (1KB, 10KB, etc.) to bytes.
    /// </summary>
    private static int ParseSize(string label)
    {
        return label switch
        {
            "1KB" => 1 * 1024,
            "10KB" => 10 * 1024,
            "100KB" => 100 * 1024,
            "1MB" => 1 * 1024 * 1024,
            "10MB" => 10 * 1024 * 1024,
            _ => 0
        };
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
