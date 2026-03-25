using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IronConfig.Crypto;

namespace IronConfig.Iupd;

/// <summary>
/// IUPD file writer/builder for creating IUPD update packages
/// </summary>
public class IupdWriter
{
    private const uint IUPD_MAGIC = 0x44505549;  // "IUPD"
    private const byte IUPD_VERSION_V2 = 0x02;
    private const int IUPD_FILE_HEADER_SIZE_V2 = 37;
    private const int IUPD_CHUNK_ENTRY_SIZE = 56;
    private const int IUPD_MANIFEST_HEADER_SIZE = 24;
    private const int IUPD_SIGNATURE_LENGTH = 64;
    private const int IUPD_WITNESS_HASH_LENGTH = 32;  // BLAKE3-256
    private const uint IUPD_WITNESS_ENABLED = 0x00000001;  // Witness flag in file header flags
    private const int IUPD_UPDATESEQ_TRAILER_SIZE = 21;  // Magic(8) + Length(4) + Version(1) + Sequence(8)
    private static readonly byte[] IUPD_UPDATESEQ_MAGIC = System.Text.Encoding.ASCII.GetBytes("IUPDSEQ1");  // 8 bytes

    // Bench keypair comes from unified helper (single source of truth)
    private static readonly byte[] BenchPrivateKey = IupdEd25519Keys.BenchSeed32;
    private static readonly byte[] BenchPublicKey = IupdEd25519Keys.BenchPublicKey32;

    private readonly List<ChunkData> _chunks = new();
    private readonly List<(uint from, uint to)> _dependencies = new();
    private readonly List<uint> _applyOrder = new();
    private IupdProfile _profile = IupdProfile.OPTIMIZED;

    // Production signing keys (optional, defaults to bench keys)
    private byte[] _privateKey = BenchPrivateKey;
    private byte[] _publicKey = BenchPublicKey;

    // UpdateSequence trailer (optional, for anti-replay protection)
    private ulong? _updateSequence = null;

    // INCREMENTAL metadata trailer (required for INCREMENTAL profile)
    private IupdIncrementalMetadata? _incrementalMetadata = null;

    /// <summary>
    /// Chunk data during building
    /// </summary>
    private class ChunkData
    {
        public uint ChunkIndex { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
        public uint PayloadCrc32 { get; set; }
        public byte[] PayloadBlake3 { get; set; } = new byte[32];
    }

    /// <summary>
    /// Add a chunk to the update package
    /// </summary>
    public void AddChunk(uint chunkIndex, ReadOnlySpan<byte> payload)
    {
        if (chunkIndex >= 1_000_000)
            throw new ArgumentException("Chunk index exceeds maximum", nameof(chunkIndex));

        if ((ulong)payload.Length > (1UL << 30))
            throw new ArgumentException("Chunk payload exceeds maximum size (1GB)", nameof(payload));

        // Compute CRC32
        uint crc32 = Crc32Ieee.Compute(payload);

        // Compute BLAKE3 (for all payloads, including empty)
        byte[] blake3 = new byte[32];
        Blake3Ieee.Compute(payload, blake3);

        _chunks.Add(new ChunkData
        {
            ChunkIndex = chunkIndex,
            Payload = payload.ToArray(),
            PayloadCrc32 = crc32,
            PayloadBlake3 = blake3
        });
    }

    /// <summary>
    /// Add a dependency: chunk 'from' must be applied before chunk 'to'
    /// </summary>
    public void AddDependency(uint from, uint to)
    {
        if (from == to)
            throw new ArgumentException("Self-dependency not allowed", nameof(from));

        _dependencies.Add((from, to));
    }

    /// <summary>
    /// Set the apply order (sequence in which chunks should be applied)
    /// </summary>
    public void SetApplyOrder(params uint[] order)
    {
        if (order.Length != _chunks.Count)
            throw new ArgumentException($"Apply order must contain {_chunks.Count} chunk indices", nameof(order));

        // Verify all chunks are included exactly once
        var seen = new bool[_chunks.Count];
        foreach (var idx in order)
        {
            if (idx >= _chunks.Count)
                throw new ArgumentException($"Apply order index {idx} out of range", nameof(order));

            if (seen[idx])
                throw new ArgumentException($"Chunk {idx} appears multiple times in apply order", nameof(order));

            seen[idx] = true;
        }

        _applyOrder.Clear();
        _applyOrder.AddRange(order);
    }

    /// <summary>
    /// Set the IUPD profile (determines compression, BLAKE3, and dependency support)
    /// </summary>
    public void SetProfile(IupdProfile profile)
    {
        _profile = profile;
    }

    /// <summary>
    /// Get the current profile
    /// </summary>
    public IupdProfile GetProfile() => _profile;

    /// <summary>
    /// Set production Ed25519 signing keys (overrides bench keys)
    /// </summary>
    public void WithSigningKey(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        if (privateKey.Length != 32)
            throw new ArgumentException("Private key must be 32 bytes", nameof(privateKey));
        if (publicKey.Length != 32)
            throw new ArgumentException("Public key must be 32 bytes", nameof(publicKey));

        _privateKey = privateKey.ToArray();
        _publicKey = publicKey.ToArray();
    }

    /// <summary>
    /// Set UpdateSequence for anti-replay protection (optional)
    /// </summary>
    public void WithUpdateSequence(ulong sequence)
    {
        _updateSequence = sequence;
    }

    /// <summary>
    /// Set INCREMENTAL profile metadata (required if profile is INCREMENTAL)
    /// </summary>
    public void WithIncrementalMetadata(byte algorithmId, byte[] baseHash, byte[]? targetHash = null)
    {
        _incrementalMetadata = new IupdIncrementalMetadata
        {
            AlgorithmId = algorithmId,
            BaseHash = baseHash,
            TargetHash = targetHash
        };
    }

    /// <summary>
    /// Build the IUPD file and return as byte array
    /// </summary>
    public byte[] Build()
    {
        if (_chunks.Count == 0)
            throw new InvalidOperationException("Cannot build IUPD file with no chunks");

        if (_chunks.Count != _applyOrder.Count)
            throw new InvalidOperationException("Must call SetApplyOrder before building");

        // Validate profile constraints
        if (_dependencies.Count > 0 && !_profile.SupportsDependencies())
            throw new InvalidOperationException($"Profile {_profile.GetDisplayName()} does not support dependencies, but {_dependencies.Count} dependencies were added");

        // INCREMENTAL profile requires patch algorithm metadata
        if (_profile.IsIncremental() && _incrementalMetadata == null)
            throw new InvalidOperationException("INCREMENTAL profile requires patch algorithm metadata (call WithIncrementalMetadata)");

        // Pre-compress all chunks in parallel (single pass, cached for all 3 phases)
        var chunkList = _chunks.OrderBy(c => c.ChunkIndex).ToList();
        var compressedPayloads = CompressAllParallel(chunkList);

        // Build fast O(1) lookup: ChunkIndex -> position in sorted chunkList
        var indexMap = new Dictionary<uint, int>(chunkList.Count);
        for (int i = 0; i < chunkList.Count; i++)
            indexMap[chunkList[i].ChunkIndex] = i;

        // PREPASS: Calculate all section sizes for offset computation
        // Offsets (v2 header is 37 bytes)
        ulong chunkTableOffset = IUPD_FILE_HEADER_SIZE_V2;
        ulong chunkTableSize = (ulong)_chunks.Count * IUPD_CHUNK_ENTRY_SIZE;
        ulong manifestOffset = chunkTableOffset + chunkTableSize;
        ulong manifestSize = CalculateManifestSize();

        // For SECURE/OPTIMIZED profiles, signature footer is written after manifest, before payloads
        // Signature footer: [signatureLength:4][signature:64][witnessHash32:32]
        // Size: 4 + 64 + 32 = 100 bytes (includes witness hash for v2+ SECURE/OPTIMIZED)
        ulong signatureFooterSize = (_profile.SupportsDependencies()) ? (4uL + IUPD_SIGNATURE_LENGTH + IUPD_WITNESS_HASH_LENGTH) : 0;

        // UpdateSequence trailer: [magic:8][length:4][version:1][sequence:8] = 21 bytes (optional)
        ulong trailerSize = _updateSequence.HasValue ? (ulong)IUPD_UPDATESEQ_TRAILER_SIZE : 0;

        // Payload offset accounts for all preceding sections but NOT incremental metadata
        // (incremental metadata is written AFTER payloads as a true trailer at end of file)
        ulong payloadOffset = IUPD_FILE_HEADER_SIZE_V2 + chunkTableSize + manifestSize + signatureFooterSize + trailerSize;

        // Calculate payload offsets for each chunk (using pre-compressed data)
        var payloadOffsets = CalculatePayloadOffsets(payloadOffset, compressedPayloads, indexMap);

        using var ms = new MemoryStream();

        // Write header (v2 with profile)
        WriteHeader(ms, chunkTableOffset, manifestOffset, payloadOffset);

        // Write chunk table with correct payload offsets (using pre-compressed data)
        WriteChunkTableWithOffsets(ms, chunkList, payloadOffsets, compressedPayloads, indexMap);

        // Write manifest
        WriteManifest(ms, manifestSize);

        // Write signature footer for SECURE and OPTIMIZED profiles (BEFORE payloads)
        // Must be written immediately after manifest so reader can find it at _manifestOffset + _manifestSize
        if (_profile.SupportsDependencies())  // SECURE and OPTIMIZED support dependencies
        {
            WriteSignatureFooter(ms, manifestOffset, manifestSize);
        }

        // Write UpdateSequence trailer (optional, BEFORE payloads)
        // Trailer structure: [magic:8][length:4][version:1][sequence:8] = 21 bytes
        if (_updateSequence.HasValue)
        {
            WriteUpdateSequenceTrailer(ms, _updateSequence.Value);
        }

        // Write payloads (using pre-compressed data)
        WritePayloads(ms, chunkList, compressedPayloads, indexMap);

        // Write INCREMENTAL metadata trailer (required for INCREMENTAL, optional otherwise)
        // Metadata is written AFTER payloads as a true "trailer" at the end of file.
        // This is the canonical layout: [headers][trailers][payloads][metadata]
        if (_incrementalMetadata != null)
        {
            byte[] metadataBytes = _incrementalMetadata.Serialize();
            ms.Write(metadataBytes);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Build and write to file
    /// </summary>
    public void BuildToFile(string filePath)
    {
        var data = Build();
        File.WriteAllBytes(filePath, data);
    }

    // --- Private methods ---

    /// <summary>
    /// Compress all chunks in parallel. Each chunk is compressed exactly once
    /// and the result cached for reuse in offset calculation, chunk table, and payload writing.
    /// This is the key optimization: reduces triple compression to single compression.
    /// </summary>
    private byte[][] CompressAllParallel(List<ChunkData> chunks)
    {
        var compressed = new byte[chunks.Count][];

        if (chunks.Count <= 3)
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                compressed[i] = IupdPayloadCompression.CompressForProfile(chunks[i].Payload, _profile);
            }
            return compressed;
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.For(0, chunks.Count, options, i =>
        {
            compressed[i] = IupdPayloadCompression.CompressForProfile(chunks[i].Payload, _profile);
        });

        return compressed;
    }

    private ulong CalculateManifestSize()
    {
        // Header (24) + Dependencies (8 * count) + ApplyOrder (4 * count) + Integrity (8)
        return 24 + (ulong)_dependencies.Count * 8 + (ulong)_applyOrder.Count * 4 + 8;
    }

    private void WriteHeader(MemoryStream ms, ulong chunkTableOffset, ulong manifestOffset, ulong payloadOffset)
    {
        // V2 format (37 bytes):
        // [0-3] Magic
        // [4] Version (0x02)
        // [5] Profile
        // [6-9] Flags
        // [10-11] Header size (37)
        // [12] Reserved
        // [13-20] Chunk table offset
        // [21-28] Manifest offset
        // [29-36] Payload offset

        Span<byte> header = stackalloc byte[IUPD_FILE_HEADER_SIZE_V2];

        // Magic
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(0, 4), IUPD_MAGIC);

        // Version (v2)
        header[4] = IUPD_VERSION_V2;

        // Profile
        header[5] = (byte)_profile;

        // Flags: Set IUPD_WITNESS_ENABLED for SECURE/OPTIMIZED profiles (fail-closed witness verification)
        uint flags = 0;
        if (_profile.RequiresWitnessStrict())
            flags |= IUPD_WITNESS_ENABLED;
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(6, 4), flags);

        // Header size
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(10, 2), IUPD_FILE_HEADER_SIZE_V2);

        // Reserved
        header[12] = 0;

        // Offsets
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(13, 8), chunkTableOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(21, 8), manifestOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(29, 8), payloadOffset);

        ms.Write(header);
    }

    private Dictionary<uint, ulong> CalculatePayloadOffsets(ulong baseOffset, byte[][] compressedPayloads, Dictionary<uint, int> indexMap)
    {
        var offsets = new Dictionary<uint, ulong>();
        ulong currentOffset = baseOffset;

        // Payloads are written in apply order
        // Use pre-compressed data (no re-compression)
        foreach (var chunkIdx in _applyOrder)
        {
            offsets[chunkIdx] = currentOffset;

            // Use cached compressed data
            byte[] wrappedData = compressedPayloads[indexMap[chunkIdx]];
            currentOffset += (ulong)wrappedData.Length;
        }

        return offsets;
    }

    private void WriteChunkTableWithOffsets(MemoryStream ms, List<ChunkData> chunks, Dictionary<uint, ulong> payloadOffsets, byte[][] compressedPayloads, Dictionary<uint, int> indexMap)
    {
        foreach (var chunk in chunks)
        {
            Span<byte> entry = stackalloc byte[IUPD_CHUNK_ENTRY_SIZE];

            // Chunk index
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0, 4), chunk.ChunkIndex);

            // Payload size - store wrapped size (including compression metadata)
            // Use pre-compressed data (no re-compression)
            byte[] wrappedData = compressedPayloads[indexMap[chunk.ChunkIndex]];
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(4, 8), (ulong)wrappedData.Length);

            // Payload offset
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(12, 8), payloadOffsets[chunk.ChunkIndex]);

            // CRC32 (compute on original uncompressed payload)
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(20, 4), chunk.PayloadCrc32);

            // BLAKE3 (compute on original uncompressed payload)
            chunk.PayloadBlake3.CopyTo(entry.Slice(24, 32));

            ms.Write(entry);
        }
    }

    private void WriteManifest(MemoryStream ms, ulong manifestSize)
    {
        var manifestStart = ms.Position;
        var manifestData = new MemoryStream();

        // Write manifest header
        Span<byte> header = stackalloc byte[IUPD_MANIFEST_HEADER_SIZE];

        // Version (use v2 for v2 files)
        header[0] = IUPD_VERSION_V2;

        // Reserved (3 bytes)
        header[1] = 0;
        header[2] = 0;
        header[3] = 0;

        // Target version (4 bytes) - now stores profile in v2
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), (byte)_profile);

        // Dependency count
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), (uint)_dependencies.Count);

        // Apply order count
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12, 4), (uint)_applyOrder.Count);

        // Manifest size
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(16, 8), manifestSize);

        manifestData.Write(header);

        // Write dependencies
        foreach (var (from, to) in _dependencies)
        {
            Span<byte> depEntry = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(depEntry.Slice(0, 4), from);
            BinaryPrimitives.WriteUInt32LittleEndian(depEntry.Slice(4, 4), to);
            manifestData.Write(depEntry);
        }

        // Write apply order
        foreach (var chunkIdx in _applyOrder)
        {
            Span<byte> orderEntry = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(orderEntry, chunkIdx);
            manifestData.Write(orderEntry);
        }

        // Calculate CRC32 over everything except last 8 bytes (header + deps + apply order)
        var manifestDataForCrc = manifestData.ToArray();
        uint manifestCrc32 = Crc32Ieee.Compute(manifestDataForCrc);

        // Write manifest data to main stream
        ms.Write(manifestDataForCrc);

        // Write manifest integrity (CRC32 + reserved)
        Span<byte> integrity = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(integrity.Slice(0, 4), manifestCrc32);
        // Reserved (4 bytes)
        integrity.Slice(4, 4).Clear();

        ms.Write(integrity);
    }

    private void WriteSignatureFooter(MemoryStream ms, ulong manifestOffset, ulong manifestSize)
    {
        // CANONICAL HASH RANGE (FROZEN for interface stability):
        // ============================================================
        // Range: manifestOffset + [0 .. manifestSize-9]
        // Byte count: manifestSize - 8 bytes (exclude CRC32[4] + reserved[4])
        //
        // Manifest structure (within canonical range):
        // [0-23]      Manifest header (24 bytes: depCount + applyOrderCount + integrityType)
        // [24...N]    Dependency entries (8 bytes each: fromChunk:uint32 + toChunk:uint32)
        // [N+1...M]   Apply order entries (4 bytes each: chunkIndex:uint32)
        // [M+1...M+7] CRC32 + reserved (8 bytes) -- EXPLICITLY EXCLUDED
        //
        // CRITICAL INVARIANT:
        // This range MUST be identical between writer and reader (see IupdReader.ParseHeader)
        // Any change to this range definition breaks ALL existing IUPD v2+ files.
        // Reordering any element in this range produces a different BLAKE3 hash (detectable).
        // ============================================================

        var allData = ms.ToArray();
        ulong manifestDataSize = manifestSize - 8;  // Exclude CRC32+reserved (frozen offset)

        var manifestData = new byte[manifestDataSize];
        // Extract CANONICAL range: [manifestOffset .. manifestOffset + manifestDataSize)
        Array.Copy(allData, (int)manifestOffset, manifestData, 0, (int)manifestDataSize);

        // Compute manifest hash once and reuse it for both signature and witness footer.
        byte[] hash = Blake3Ieee.Compute(manifestData);

        // Sign the manifest hash using Ed25519
        byte[] signature = new byte[64];
        Ed25519.Sign(_privateKey, hash, signature);

        // IUPD_WITNESS: Compute witness hash from CANONICAL manifest range
        // The witness hash detects:
        // - Any reordering of dependency entries
        // - Any reordering of apply order entries
        // - Any tampering with manifest header fields
        // This is fail-closed enforcement: if witness hash mismatches, validation fails.
        byte[] witnessHash = hash;

        // Write signature footer: [signatureLength:4][signature:64][witnessHash32:32]
        // Total: 100 bytes for SECURE/OPTIMIZED v2+
        Span<byte> sigFooter = stackalloc byte[4 + IUPD_SIGNATURE_LENGTH + IUPD_WITNESS_HASH_LENGTH];
        BinaryPrimitives.WriteUInt32LittleEndian(sigFooter.Slice(0, 4), IUPD_SIGNATURE_LENGTH);
        signature.CopyTo(sigFooter.Slice(4, IUPD_SIGNATURE_LENGTH));
        witnessHash.CopyTo(sigFooter.Slice(4 + IUPD_SIGNATURE_LENGTH, IUPD_WITNESS_HASH_LENGTH));

        ms.Write(sigFooter);
    }

    private void WritePayloads(MemoryStream ms, List<ChunkData> chunks, byte[][] compressedPayloads, Dictionary<uint, int> indexMap)
    {
        // Write all payloads in apply order using pre-compressed data (no re-compression)
        foreach (var chunkIdx in _applyOrder)
        {
            // Use cached compressed data - no compression call here
            byte[] dataToWrite = compressedPayloads[indexMap[chunkIdx]];
            ms.Write(dataToWrite);
        }
    }

    private void WriteUpdateSequenceTrailer(MemoryStream ms, ulong sequence)
    {
        // UpdateSequence trailer (optional, for anti-replay protection)
        // Structure: [magic:8][length:4][version:1][sequence:8]
        // Total: 21 bytes
        // Magic: "IUPDSEQ1" (ASCII)
        // Length: 21 (u32 LE)
        // Version: 1 (u8)
        // Sequence: u64 LE (update sequence number)

        Span<byte> trailer = stackalloc byte[IUPD_UPDATESEQ_TRAILER_SIZE];

        // Magic (8 bytes)
        IUPD_UPDATESEQ_MAGIC.CopyTo(trailer.Slice(0, 8));

        // Length (4 bytes, u32 LE)
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.Slice(8, 4), IUPD_UPDATESEQ_TRAILER_SIZE);

        // Version (1 byte)
        trailer[12] = 1;

        // Sequence (8 bytes, u64 LE)
        BinaryPrimitives.WriteUInt64LittleEndian(trailer.Slice(13, 8), sequence);

        ms.Write(trailer);
    }
}

/// <summary>
/// Fluent builder for IUPD files
/// </summary>
public class IupdBuilder
{
    private readonly IupdWriter _writer = new();
    private IupdProfile _profile = IupdProfile.OPTIMIZED;
    private ulong? _updateSequence = null;
    private IupdIncrementalMetadata? _incrementalMetadata = null;

    public IupdBuilder AddChunk(uint index, byte[] payload)
    {
        _writer.AddChunk(index, payload);
        return this;
    }

    public IupdBuilder AddChunk(uint index, ReadOnlySpan<byte> payload)
    {
        _writer.AddChunk(index, payload);
        return this;
    }

    public IupdBuilder AddDependency(uint from, uint to)
    {
        _writer.AddDependency(from, to);
        return this;
    }

    public IupdBuilder WithApplyOrder(params uint[] order)
    {
        _writer.SetApplyOrder(order);
        return this;
    }

    public IupdBuilder WithProfile(IupdProfile profile)
    {
        _profile = profile;
        _writer.SetProfile(profile);
        return this;
    }

    public IupdBuilder WithSigningKey(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        _writer.WithSigningKey(privateKey, publicKey);
        return this;
    }

    public IupdBuilder WithUpdateSequence(ulong sequence)
    {
        _updateSequence = sequence;
        _writer.WithUpdateSequence(sequence);
        return this;
    }

    public IupdBuilder WithIncrementalMetadata(byte algorithmId, byte[] baseHash, byte[]? targetHash = null)
    {
        _incrementalMetadata = new IupdIncrementalMetadata
        {
            AlgorithmId = algorithmId,
            BaseHash = baseHash,
            TargetHash = targetHash
        };
        _writer.WithIncrementalMetadata(algorithmId, baseHash, targetHash);
        return this;
    }

    public byte[] Build()
    {
        // Auto-inject UpdateSequence(1) for v2+ SECURE/OPTIMIZED if not explicitly set
        if (_updateSequence == null && (_profile == IupdProfile.SECURE || _profile == IupdProfile.OPTIMIZED))
        {
            _writer.WithUpdateSequence(1);
        }
        return _writer.Build();
    }

    public void BuildToFile(string path) => _writer.BuildToFile(path);
}
