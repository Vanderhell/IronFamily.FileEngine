using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace IronConfig.Iupd;

/// <summary>
/// Payload compression support for IUPD profiles (FAST and OPTIMIZED)
/// Uses simple byte-run compression for repetitive data
///
/// Compressed payload format:
/// [original_size:8B LE][is_compressed:1B][compressed_data]
/// If is_compressed=0, data is not compressed and original_size=0
/// </summary>
public static class IupdPayloadCompression
{
    private const byte COMPRESSION_MARKER = 0x01;
    private const byte NO_COMPRESSION_MARKER = 0x00;
    private const int CompressionProbeBytes = 32 * 1024;

    /// <summary>
    /// Compress payload data with metadata header for compressed data only
    /// Returns raw data if not compressed, or [size:8B][is_compressed:1B][data] if compressed
    /// </summary>
    public static byte[] CompressForProfile(ReadOnlySpan<byte> data, IupdProfile profile)
    {
        if (data.Length == 0)
            return Array.Empty<byte>();

        // Only compress for profiles that support it
        if (!profile.SupportsCompression())
        {
            // No compression support - return raw data
            return data.ToArray();
        }

        // FAST profile prioritizes throughput over ratio.
        var compressionLevel = profile == IupdProfile.FAST ? CompressionLevel.Fastest : CompressionLevel.Optimal;

        // Avoid paying full Deflate cost on obviously incompressible inputs.
        // Probe a representative prefix first; if even the probe does not shrink,
        // skip the full compression pass and keep the payload raw.
        if (data.Length >= CompressionProbeBytes * 2)
        {
            int probeLength = Math.Min(CompressionProbeBytes, data.Length);
            var probeCompressed = CompressLz4Style(data.Slice(0, probeLength), compressionLevel);
            if (probeCompressed.Length >= probeLength)
                return data.ToArray();
        }

        // Try compression
        var compressed = CompressLz4Style(data, compressionLevel);

        // Only use compression if it saves space
        if (compressed.Length >= data.Length)
        {
            // Compression didn't help - store uncompressed
            return data.ToArray();
        }

        // Use compression - wrap with metadata
        var output = new byte[9 + compressed.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(0, 8), (ulong)data.Length);
        output[8] = COMPRESSION_MARKER;
        compressed.CopyTo(output.AsSpan(9));
        return output;
    }

    /// <summary>
    /// Decompress payload with metadata header
    /// </summary>
    public static bool TryDecompressPayload(ReadOnlySpan<byte> payload, out byte[]? result, out string? error)
    {
        result = null;
        error = null;

        if (payload.Length < 9)
        {
            error = "Payload too small for compression metadata";
            return false;
        }

        ulong originalSize = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(0, 8));
        byte isCompressed = payload[8];

        if (isCompressed == NO_COMPRESSION_MARKER)
        {
            // Data not compressed
            result = payload.Slice(9).ToArray();
            return true;
        }

        if (isCompressed != COMPRESSION_MARKER)
        {
            error = $"Invalid compression marker: {isCompressed}";
            return false;
        }

        // Decompress
        try
        {
            var compressedData = payload.Slice(9);
            result = DecompressLz4Style(compressedData, (int)originalSize);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Decompression failed: {ex.Message}";
            return false;
        }
    }

    // ========================================================================
    // DEFLATE Compression (using built-in System.IO.Compression)
    // ========================================================================

    private static byte[] CompressLz4Style(ReadOnlySpan<byte> data, CompressionLevel compressionLevel)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, compressionLevel, leaveOpen: true))
        {
            deflate.Write(data);
        }
        return output.ToArray();
    }

    private static byte[] DecompressLz4Style(ReadOnlySpan<byte> compressed, int originalSize)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true))
        {
            deflate.CopyTo(output);
        }

        var result = output.ToArray();
        if (result.Length != originalSize)
            throw new InvalidOperationException($"Decompressed size {result.Length} doesn't match expected {originalSize}");

        return result;
    }
}
