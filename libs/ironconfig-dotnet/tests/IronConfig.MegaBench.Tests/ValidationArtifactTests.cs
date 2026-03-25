using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronFamily.MegaBench.Validation;
using Xunit;

namespace IronConfig.MegaBench.Tests;

public class ValidationArtifactTests
{
    [Fact]
    public void ValidationArtifact_ValidDataset_PassesConstraints()
    {
        // Arrange
        var artifact = new ValidationArtifact
        {
            DatasetId = "test_1KB",
            Engine = "icfg",
            Profile = null,
            SizeLabel = "1KB",
            GeneratedUtc = DateTime.UtcNow.ToString("O"),
            Sha256 = "abc123",
            Bytes = 1024,
            Open = new ValidationMode { Ok = true, ErrorCode = "Ok", ElapsedMs = 10 },
            Fast = new ValidationMode { Ok = true, ErrorCode = "Ok", ElapsedMs = 15 },
            Strict = new ValidationMode { Ok = true, ErrorCode = "Ok", ElapsedMs = 20 }
        };

        // Act
        bool isValid = artifact.IsValid(out string? errorMsg);

        // Assert
        Assert.True(isValid, $"Validation failed: {errorMsg}");
    }

    [Fact]
    public void ValidationArtifact_StrictFails_InvalidArtifact()
    {
        // Arrange - strict is false (truth gate violation)
        var artifact = new ValidationArtifact
        {
            DatasetId = "test_1KB",
            Engine = "icfg",
            Strict = new ValidationMode { Ok = false, ErrorCode = "InvalidChecksum", Detail = "CRC32 mismatch" }
        };

        // Act
        bool isValid = artifact.IsValid(out string? errorMsg);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(errorMsg);
        Assert.Contains("STRICT validation failed", errorMsg);
    }

    [Fact]
    public void ValidationArtifact_ZeroElapsedMs_Invalid()
    {
        // Arrange
        var artifact = new ValidationArtifact
        {
            DatasetId = "test_1KB",
            Engine = "icfg",
            Open = new ValidationMode { Ok = true, ElapsedMs = 0 },
            Fast = new ValidationMode { Ok = true, ElapsedMs = 0 }, // Zero elapsed
            Strict = new ValidationMode { Ok = true, ElapsedMs = 20 }
        };

        // Act
        bool isValid = artifact.IsValid(out string? errorMsg);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(errorMsg);
        Assert.Contains("elapsedMs must be > 0", errorMsg);
    }

    [Fact]
    public void ValidationArtifact_OkTrueButErrorCode_Inconsistent()
    {
        // Arrange
        var artifact = new ValidationArtifact
        {
            DatasetId = "test_1KB",
            Engine = "icfg",
            Open = new ValidationMode { Ok = true, ErrorCode = "SomeError", ElapsedMs = 10 },
            Fast = new ValidationMode { Ok = true, ErrorCode = "Ok", ElapsedMs = 15 },
            Strict = new ValidationMode { Ok = true, ErrorCode = "Ok", ElapsedMs = 20 }
        };

        // Act
        bool isValid = artifact.IsValid(out string? errorMsg);

        // Assert
        Assert.False(isValid);
        Assert.Contains("ok=true but errorCode=SomeError", errorMsg);
    }

    [Fact]
    public void ValidationArtifact_SerializeDeserialize_Roundtrip()
    {
        // Arrange
        var original = new ValidationArtifact
        {
            DatasetId = "ilog_MINIMAL_10KB",
            Engine = "ilog",
            Profile = "MINIMAL",
            SizeLabel = "10KB",
            Sha256 = "abcdef0123456789",
            Bytes = 10240,
            Open = new ValidationMode { Ok = true, ErrorCode = "Ok", ElapsedMs = 5 },
            Fast = new ValidationMode { Ok = true, ErrorCode = "Ok", ElapsedMs = 10 },
            Strict = new ValidationMode { Ok = true, ErrorCode = "Ok", ElapsedMs = 25 }
        };

        // Act
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };
        string json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<ValidationArtifact>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.DatasetId, deserialized!.DatasetId);
        Assert.Equal(original.Engine, deserialized.Engine);
        Assert.Equal(original.Profile, deserialized.Profile);
        Assert.Equal(original.Strict.Ok, deserialized.Strict.Ok);
        Assert.Equal(original.Bytes, deserialized.Bytes);
    }

    [Fact]
    public void ValidationArtifact_WithoutProfile_Serializes()
    {
        // Arrange
        var artifact = new ValidationArtifact
        {
            DatasetId = "icfg_1KB",
            Engine = "icfg",
            Profile = null, // No profile for IRONCFG
            SizeLabel = "1KB",
            Sha256 = "xyz",
            Bytes = 1024,
            Open = new ValidationMode { Ok = true, ElapsedMs = 1 },
            Fast = new ValidationMode { Ok = true, ElapsedMs = 2 },
            Strict = new ValidationMode { Ok = true, ElapsedMs = 3 }
        };

        // Act
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        string json = JsonSerializer.Serialize(artifact, options);

        // Assert
        Assert.DoesNotContain("\"profile\"", json); // Profile should be omitted
        Assert.Contains("\"datasetId\":\"icfg_1KB\"", json);
    }
}
