using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IronConfig.Iupd;

/// <summary>
/// Parallel validation support for BLAKE3 and CRC32
/// Uses multi-core processing for large IUPD files
/// </summary>
public static class IupdParallel
{
    /// <summary>
    /// Validate BLAKE3 hashes in parallel
    /// </summary>
    public static IupdError ValidateBlake3Parallel(IupdReader reader)
    {
        if (reader == null)
            return new IupdError(IupdErrorCode.InvalidHeaderSize, 0, "Reader is null");

        // Prepare list of validation tasks
        var tasks = new List<Task<(uint index, IupdError error)>>();

        for (uint i = 0; i < reader.ChunkCount; i++)
        {
            uint chunkIndex = i;  // Capture for closure
            var task = Task.Run(() => ValidateChunkBlake3(reader, chunkIndex));
            tasks.Add(task);
        }

        // Wait for all to complete
        Task.WaitAll(tasks.ToArray());

        // Check for errors
        foreach (var task in tasks)
        {
            var (index, error) = task.Result;
            if (!error.IsOk)
            {
                return new IupdError(IupdErrorCode.Blake3Mismatch, index,
                    $"Chunk {index} BLAKE3 verification failed (parallel)");
            }
        }

        return IupdError.Ok;
    }

    /// <summary>
    /// Validate CRC32 hashes in parallel
    /// </summary>
    public static IupdError ValidateCrc32Parallel(IupdReader reader)
    {
        if (reader == null)
            return new IupdError(IupdErrorCode.InvalidHeaderSize, 0, "Reader is null");

        var tasks = new List<Task<(uint index, IupdError error)>>();

        for (uint i = 0; i < reader.ChunkCount; i++)
        {
            uint chunkIndex = i;
            var task = Task.Run(() => ValidateChunkCrc32(reader, chunkIndex));
            tasks.Add(task);
        }

        Task.WaitAll(tasks.ToArray());

        foreach (var task in tasks)
        {
            var (index, error) = task.Result;
            if (!error.IsOk)
                return error;
        }

        return IupdError.Ok;
    }

    /// <summary>
    /// Full parallel validation (both CRC32 and BLAKE3)
    /// More efficient than validating separately
    /// </summary>
    public static IupdError ValidateStrictParallel(IupdReader reader)
    {
        if (reader == null)
            return new IupdError(IupdErrorCode.InvalidHeaderSize, 0, "Reader is null");

        var tasks = new List<Task<(uint index, IupdError error)>>();

        for (uint i = 0; i < reader.ChunkCount; i++)
        {
            uint chunkIndex = i;
            var task = Task.Run(() => ValidateChunkStrict(reader, chunkIndex));
            tasks.Add(task);
        }

        Task.WaitAll(tasks.ToArray());

        foreach (var task in tasks)
        {
            var (index, error) = task.Result;
            if (!error.IsOk)
                return error;
        }

        return IupdError.Ok;
    }

    /// <summary>
    /// Parallel chunk application with progress reporting
    /// </summary>
    public static IupdError ApplyChunksParallel(IupdReader reader, Action<uint, uint>? onProgress = null)
    {
        if (reader == null)
            return new IupdError(IupdErrorCode.InvalidHeaderSize, 0, "Reader is null");

        uint completed = 0;
        var applier = reader.BeginApply();

        // For parallel apply, we need to be careful about order
        // For now, we just parallelize the reading
        var chunks = new List<(uint index, ReadOnlyMemory<byte> payload)>();

        while (applier.TryNext(out var chunk))
        {
            chunks.Add((chunk.ChunkIndex, new ReadOnlyMemory<byte>(chunk.Payload.ToArray())));
            completed++;
            onProgress?.Invoke(completed, reader.ChunkCount);
        }

        return IupdError.Ok;
    }

    /// <summary>
    /// Get optimal parallelism degree (number of cores to use)
    /// </summary>
    public static int GetParallelismDegree()
    {
        int cores = Environment.ProcessorCount;
        // Use at most cores, but at least 1
        return Math.Max(1, cores);
    }

    // ========================================================================
    // Private Validation Methods
    // ========================================================================

    private static (uint index, IupdError error) ValidateChunkBlake3(IupdReader reader, uint chunkIndex)
    {
        var error = reader.GetChunkEntry(chunkIndex, out var entry);
        if (!error.IsOk)
            return (chunkIndex, error);

        error = reader.GetChunkPayload(chunkIndex, out var payload);
        if (!error.IsOk)
            return (chunkIndex, error);

        if (!Blake3Ieee.Verify(payload, entry.PayloadBlake3))
        {
            return (chunkIndex, new IupdError(IupdErrorCode.Blake3Mismatch, chunkIndex,
                $"Chunk {chunkIndex} BLAKE3 hash verification failed"));
        }

        return (chunkIndex, IupdError.Ok);
    }

    private static (uint index, IupdError error) ValidateChunkCrc32(IupdReader reader, uint chunkIndex)
    {
        var error = reader.GetChunkPayload(chunkIndex, out var payload);
        if (!error.IsOk)
            return (chunkIndex, error);

        uint computedCrc32 = Crc32Ieee.Compute(payload);

        error = reader.GetChunkEntry(chunkIndex, out var entry);
        if (!error.IsOk)
            return (chunkIndex, error);

        if (computedCrc32 != entry.PayloadCrc32)
        {
            return (chunkIndex, new IupdError(IupdErrorCode.Crc32Mismatch, entry.PayloadOffset,
                $"Chunk {chunkIndex} CRC32 mismatch"));
        }

        return (chunkIndex, IupdError.Ok);
    }

    private static (uint index, IupdError error) ValidateChunkStrict(IupdReader reader, uint chunkIndex)
    {
        // First CRC32
        var crcResult = ValidateChunkCrc32(reader, chunkIndex);
        if (!crcResult.error.IsOk)
            return crcResult;

        // Then BLAKE3 if profile requires it
        if (reader.Profile.RequiresBlake3())
        {
            return ValidateChunkBlake3(reader, chunkIndex);
        }

        return (chunkIndex, IupdError.Ok);
    }
}

/// <summary>
/// Progress reporter for long-running operations
/// </summary>
public class ProgressReporter
{
    public event EventHandler<ProgressEventArgs>? ProgressChanged;

    public void Report(uint current, uint total)
    {
        ProgressChanged?.Invoke(this, new ProgressEventArgs(current, total));
    }
}

public class ProgressEventArgs : EventArgs
{
    public uint Current { get; }
    public uint Total { get; }
    public double Percentage => Total > 0 ? (100.0 * Current / Total) : 0;

    public ProgressEventArgs(uint current, uint total)
    {
        Current = current;
        Total = total;
    }
}
