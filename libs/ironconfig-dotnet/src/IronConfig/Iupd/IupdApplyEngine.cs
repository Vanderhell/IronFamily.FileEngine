using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using IronConfig.Iupd.Delta;

namespace IronConfig.Iupd;

/// <summary>
/// Crash-safe staged apply engine for IUPD updates.
/// Implements 3-phase commit: Stage → Commit Marker → Atomic Swap
/// </summary>
public sealed class IupdApplyEngine
{
    private readonly string _targetDir;
    private readonly IupdReader _reader;
    private readonly byte[] _manifestHash;

    private const string StagingDirName = ".ironupd_stage";
    private const string BackupDirName = ".ironupd_backup";
    private const string CommitMarkerName = ".ironupd_commit.json";

    public IupdApplyEngine(IupdReader reader, byte[] manifestHash, string targetDir)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _manifestHash = manifestHash ?? throw new ArgumentNullException(nameof(manifestHash));
        _targetDir = targetDir ?? throw new ArgumentNullException(nameof(targetDir));
    }

    /// <summary>
    /// Phase 1: Validate IUPD and prepare staging directory.
    /// Returns error if validation fails (staging is cleaned up on error).
    /// </summary>
    public IupdError StageUpdate()
    {
        try
        {
            // Clean any previous interrupted staging
            var stagingPath = Path.Combine(_targetDir, StagingDirName);
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);

            Directory.CreateDirectory(stagingPath);

            // Validate and extract all chunks to staging
            var applier = _reader.BeginApply();
            uint chunkCount = 0;

            while (applier.TryNext(out var chunk))
            {
                var chunkPath = Path.Combine(stagingPath, $"chunk_{chunk.ChunkIndex:D8}");
                var chunkDir = Path.GetDirectoryName(chunkPath)!;
                if (!Directory.Exists(chunkDir))
                    Directory.CreateDirectory(chunkDir);

                // Write chunk with validation
                var err = ValidateAndWriteChunk(chunk, chunkPath);
                if (!err.IsOk)
                {
                    // Rollback staging on error
                    Directory.Delete(stagingPath, recursive: true);
                    return err;
                }

                chunkCount++;
            }

            return IupdError.Ok;
        }
        catch (Exception)
        {
            var stagingPath = Path.Combine(_targetDir, StagingDirName);
            try
            {
                if (Directory.Exists(stagingPath))
                    Directory.Delete(stagingPath, recursive: true);
            }
            catch { /* best effort cleanup */ }

            return new IupdError(IupdErrorCode.ApplyError, 0);
        }
    }

    /// <summary>
    /// Phase 2: Create deterministic commit marker.
    /// This marks staging as ready for swap (idempotent).
    /// </summary>
    public IupdError CreateCommitMarker(uint fileCount)
    {
        try
        {
            var marker = new IupdCommitMarker
            {
                ManifestHash = Convert.ToHexString(_manifestHash),
                FileCount = fileCount,
                State = "ready"
            };

            var markerPath = Path.Combine(_targetDir, CommitMarkerName);
            var json = marker.ToJson();
            File.WriteAllText(markerPath, json);

            return IupdError.Ok;
        }
        catch (Exception)
        {
            return new IupdError(IupdErrorCode.ApplyError, 0);
        }
    }

    /// <summary>
    /// Phase 3: Atomic finalization - swap staging to current, delete marker.
    /// </summary>
    public IupdError FinalizeSwap()
    {
        try
        {
            var stagingPath = Path.Combine(_targetDir, StagingDirName);
            var currentPath = Path.Combine(_targetDir, "current");
            var backupPath = Path.Combine(_targetDir, BackupDirName);
            var markerPath = Path.Combine(_targetDir, CommitMarkerName);

            if (!Directory.Exists(stagingPath))
                return new IupdError(IupdErrorCode.ApplyError, 0);

            // Backup existing current (if exists)
            if (Directory.Exists(currentPath))
            {
                if (Directory.Exists(backupPath))
                    Directory.Delete(backupPath, recursive: true);
                Directory.Move(currentPath, backupPath);
            }

            // Atomic swap: staging → current
            Directory.Move(stagingPath, currentPath);

            // Delete backup and marker
            try
            {
                if (Directory.Exists(backupPath))
                    Directory.Delete(backupPath, recursive: true);
            }
            catch { /* best effort */ }

            try
            {
                if (File.Exists(markerPath))
                    File.Delete(markerPath);
            }
            catch { /* best effort */ }

            return IupdError.Ok;
        }
        catch (Exception)
        {
            return new IupdError(IupdErrorCode.ApplyError, 0);
        }
    }

    /// <summary>
    /// Apply INCREMENTAL delta patch with algorithm dispatch.
    /// Validates base image, applies delta based on package-declared algorithm, validates result.
    /// Returns error if validation fails or unknown algorithm.
    /// </summary>
    public IupdError ApplyIncremental(ReadOnlySpan<byte> baseImage, out byte[] resultImage)
    {
        resultImage = null!;

        // Validate INCREMENTAL profile and metadata
        if (_reader.Profile != IupdProfile.INCREMENTAL)
            return new IupdError(IupdErrorCode.ProfileNotAllowed, 0);

        if (_reader.IncrementalMetadata == null)
            return new IupdError(IupdErrorCode.SignatureMissing, 0);

        var metadata = _reader.IncrementalMetadata;

        // Validate algorithm ID first (fail-closed on unknown)
        if (!metadata.IsKnownAlgorithm())
            return new IupdError(IupdErrorCode.SignatureInvalid, 0);

        // Validate base image hash
        if (metadata.BaseHash == null || metadata.BaseHash.Length == 0)
            return new IupdError(IupdErrorCode.SignatureMissing, 0);

        var computedBaseHash = Blake3.Hasher.Hash(baseImage);
        var computedBaseHex = Convert.ToHexString(computedBaseHash.AsSpan());
        var expectedBaseHex = Convert.ToHexString(metadata.BaseHash.AsSpan());

        if (computedBaseHex != expectedBaseHex)
            return new IupdError(IupdErrorCode.Blake3Mismatch, 0);

        // Extract delta patch from all chunks (concatenated)
        byte[] deltaPatch;
        try
        {
            using (var ms = new System.IO.MemoryStream())
            {
                var applier = _reader.BeginApply();
                while (applier.TryNext(out var chunk))
                {
                    ms.Write(chunk.Payload);
                }
                deltaPatch = ms.ToArray();
            }
        }
        catch (Exception)
        {
            return new IupdError(IupdErrorCode.ApplyError, 0);
        }

        // Dispatch to appropriate delta apply algorithm.
        // Active path: IRONDEL2 (0x02) - Content-defined chunking for superior compression
        // Legacy path: DELTA_V1 (0x01) - Fixed-chunk algorithm for backward compatibility
        byte[] applyResult;
        IupdError applyError;

        switch (metadata.AlgorithmId)
        {
            case IupdIncrementalMetadata.ALGORITHM_DELTA_V1:
                // Legacy V1 path - fixed 4096-byte chunks
                applyResult = IupdDeltaV1.ApplyDeltaV1(baseImage, deltaPatch, out applyError);
                if (!applyError.IsOk)
                    return applyError;
                break;

            case IupdIncrementalMetadata.ALGORITHM_IRONDEL2:
                applyResult = IupdDeltaV2Cdc.ApplyDeltaV2(baseImage, deltaPatch, out applyError);
                if (!applyError.IsOk)
                    return applyError;
                break;

            default:
                // Should not reach here (IsKnownAlgorithm already validated above)
                return new IupdError(IupdErrorCode.SignatureInvalid, 0);
        }

        // Validate target hash if present
        if (metadata.TargetHash != null && metadata.TargetHash.Length > 0)
        {
            var computedTargetHash = Blake3.Hasher.Hash(applyResult);
            var computedTargetHex = Convert.ToHexString(computedTargetHash.AsSpan());
            var expectedTargetHex = Convert.ToHexString(metadata.TargetHash.AsSpan());

            if (computedTargetHex != expectedTargetHex)
                return new IupdError(IupdErrorCode.Blake3Mismatch, 0);
        }

        resultImage = applyResult;
        return IupdError.Ok;
    }

    /// <summary>
    /// Validate chunk integrity and write to staging path.
    /// </summary>
    private IupdError ValidateAndWriteChunk(IupdChunk chunk, string chunkPath)
    {
        try
        {
            var payload = chunk.Payload;

            // Verify CRC32
            uint computedCrc32 = Crc32Ieee.Compute(payload);
            if (computedCrc32 != chunk.PayloadCrc32)
                return new IupdError(IupdErrorCode.Crc32Mismatch, 0);

            // Verify BLAKE3 if present
            if (chunk.PayloadBlake3 != null && chunk.PayloadBlake3.Length > 0)
            {
                var computedBlake3 = Blake3.Hasher.Hash(payload);
                var computedHex = Convert.ToHexString(computedBlake3.AsSpan());
                var expectedHex = Convert.ToHexString(chunk.PayloadBlake3);
                if (computedHex != expectedHex)
                    return new IupdError(IupdErrorCode.Blake3Mismatch, 0);
            }

            // Write validated chunk
            File.WriteAllBytes(chunkPath, payload.ToArray());
            return IupdError.Ok;
        }
        catch (Exception)
        {
            return new IupdError(IupdErrorCode.ApplyError, 0);
        }
    }
}

/// <summary>
/// Deterministic commit marker - tracks staging state.
/// </summary>
public class IupdCommitMarker
{
    public string ManifestHash { get; set; } = "";
    public uint FileCount { get; set; }
    public string State { get; set; } = "ready";

    public string ToJson()
    {
        // Stable field order: manifest_hash, file_count, state
        return $$"""
{
  "manifest_hash": "{{ManifestHash}}",
  "file_count": {{FileCount}},
  "state": "{{State}}"
}
""";
    }

    public static IupdCommitMarker? FromJson(string json)
    {
        try
        {
            // Simple deterministic parsing (no external JSON lib dependency)
            var marker = new IupdCommitMarker();

            // Extract manifest_hash
            var hashStart = json.IndexOf("\"manifest_hash\": \"") + "\"manifest_hash\": \"".Length;
            var hashEnd = json.IndexOf("\"", hashStart);
            marker.ManifestHash = json.Substring(hashStart, hashEnd - hashStart);

            // Extract file_count
            var countStart = json.IndexOf("\"file_count\": ") + "\"file_count\": ".Length;
            var countEnd = json.IndexOf(",", countStart);
            if (uint.TryParse(json.Substring(countStart, countEnd - countStart), out var count))
                marker.FileCount = count;

            // Extract state
            var stateStart = json.IndexOf("\"state\": \"") + "\"state\": \"".Length;
            var stateEnd = json.IndexOf("\"", stateStart);
            marker.State = json.Substring(stateStart, stateEnd - stateStart);

            return marker;
        }
        catch
        {
            return null;
        }
    }
}
