using System;
using System.IO;

namespace IronConfig.Iupd;

/// <summary>
/// Recovery logic for IUPD staged apply on runtime initialization.
/// Handles crashes at any point in the 3-phase commit cycle.
/// </summary>
public sealed class IupdApplyRecovery
{
    private readonly string _targetDir;

    private const string StagingDirName = ".ironupd_stage";
    private const string BackupDirName = ".ironupd_backup";
    private const string CommitMarkerName = ".ironupd_commit.json";

    public IupdApplyRecovery(string targetDir)
    {
        _targetDir = targetDir ?? throw new ArgumentNullException(nameof(targetDir));
    }

    /// <summary>
    /// Recovery entry point - called on runtime initialization.
    /// Deterministically handles any leftover state from a crash.
    /// </summary>
    public IupdError Recover()
    {
        try
        {
            var stagingPath = Path.Combine(_targetDir, StagingDirName);
            var backupPath = Path.Combine(_targetDir, BackupDirName);
            var markerPath = Path.Combine(_targetDir, CommitMarkerName);
            var currentPath = Path.Combine(_targetDir, "current");

            // Case 1: Commit marker exists
            // This means we reached Phase 2 (CreateCommitMarker succeeded)
            if (File.Exists(markerPath))
            {
                // Case 1a: Crash after marker but before/during swap
                // Staging directory may or may not exist
                if (Directory.Exists(stagingPath))
                {
                    // Finalize the swap (idempotent completion of Phase 3)
                    return FinalizeSwapRecovery(stagingPath, currentPath, backupPath, markerPath);
                }
                else if (Directory.Exists(backupPath))
                {
                    // Case 1b: Swap already completed (staging gone, backup exists)
                    // Just clean up the marker and backup
                    return CleanupAfterSwap(backupPath, markerPath);
                }
                else
                {
                    // Case 1c: Swap fully completed (both staging and backup gone)
                    // Just delete the marker
                    return CleanupMarkerOnly(markerPath);
                }
            }

            // Case 2: Staging exists but no commit marker
            // This means crash occurred during Phase 1 (StageUpdate) - before marker was written
            if (Directory.Exists(stagingPath))
            {
                // Rollback: Delete the incomplete staging directory
                return RollbackStaging(stagingPath);
            }

            // Case 3: Neither marker nor staging exists - normal state
            return IupdError.Ok;
        }
        catch (Exception)
        {
            return new IupdError(IupdErrorCode.ApplyError, 0);
        }
    }

    /// <summary>
    /// Finalize the swap during recovery (idempotent completion of Phase 3).
    /// </summary>
    private IupdError FinalizeSwapRecovery(string stagingPath, string currentPath, string backupPath, string markerPath)
    {
        try
        {
            // If current exists, backup it first (in case this is a retry)
            if (Directory.Exists(currentPath))
            {
                if (Directory.Exists(backupPath))
                    Directory.Delete(backupPath, recursive: true);
                Directory.Move(currentPath, backupPath);
            }

            // Atomic swap: staging → current
            Directory.Move(stagingPath, currentPath);

            // Best-effort cleanup
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
    /// Cleanup after successful swap (delete backup and marker).
    /// </summary>
    private IupdError CleanupAfterSwap(string backupPath, string markerPath)
    {
        try
        {
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
    /// Cleanup marker file only.
    /// </summary>
    private IupdError CleanupMarkerOnly(string markerPath)
    {
        try
        {
            if (File.Exists(markerPath))
                File.Delete(markerPath);
            return IupdError.Ok;
        }
        catch (Exception)
        {
            return new IupdError(IupdErrorCode.ApplyError, 0);
        }
    }

    /// <summary>
    /// Rollback incomplete staging (crash during Phase 1).
    /// </summary>
    private IupdError RollbackStaging(string stagingPath)
    {
        try
        {
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
            return IupdError.Ok;
        }
        catch (Exception)
        {
            return new IupdError(IupdErrorCode.ApplyError, 0);
        }
    }
}
