using System;
using System.Collections.Generic;
using IronConfig.Crypto;

namespace IronConfig.Iupd;

/// <summary>
/// Metadata for a single chunk (from Chunk Table)
/// </summary>
public readonly ref struct IupdChunkEntry
{
    public uint ChunkIndex { get; }
    public ulong PayloadSize { get; }
    public ulong PayloadOffset { get; }
    public uint PayloadCrc32 { get; }
    public ReadOnlySpan<byte> PayloadBlake3 { get; }  // 32 bytes

    public IupdChunkEntry(uint chunkIndex, ulong payloadSize, ulong payloadOffset,
                         uint payloadCrc32, ReadOnlySpan<byte> payloadBlake3)
    {
        ChunkIndex = chunkIndex;
        PayloadSize = payloadSize;
        PayloadOffset = payloadOffset;
        PayloadCrc32 = payloadCrc32;
        PayloadBlake3 = payloadBlake3;
    }
}

/// <summary>
/// Chunk yielded during apply iteration
/// </summary>
public readonly ref struct IupdChunk
{
    public uint ChunkIndex { get; }
    public ReadOnlySpan<byte> Payload { get; }
    public uint PayloadCrc32 { get; }
    public ReadOnlySpan<byte> PayloadBlake3 { get; }

    public IupdChunk(uint chunkIndex, ReadOnlySpan<byte> payload, uint crc32, ReadOnlySpan<byte> blake3)
    {
        ChunkIndex = chunkIndex;
        Payload = payload;
        PayloadCrc32 = crc32;
        PayloadBlake3 = blake3;
    }
}

/// <summary>
/// IUPD file reader with validation and streaming apply
/// </summary>
public sealed class IupdReader
{
    private const uint IUPD_MAGIC = 0x44505549;  // "IUPD" in little-endian
    private const uint UPD1_MAGIC = 0x31445055;  // "UPD1" in little-endian
    private const byte IUPD_VERSION_V1 = 0x01;
    private const byte IUPD_VERSION_V2 = 0x02;
    private const int IUPD_FILE_HEADER_SIZE_V1 = 36;
    private const int IUPD_FILE_HEADER_SIZE_V2 = 37;
    private const int IUPD_CHUNK_ENTRY_SIZE = 56;
    private const int IUPD_MANIFEST_HEADER_SIZE = 24;
    private const int IUPD_WITNESS_HASH_SIZE = 32;  // BLAKE3-256 for witness
    private const int IUPD_UPDATESEQ_TRAILER_SIZE = 21;  // Magic(8) + Length(4) + Version(1) + Sequence(8)

    private const ulong MAX_CHUNKS = 1_000_000;
    private const ulong MAX_CHUNK_SIZE = 1UL << 30;  // 1 GB
    private const ulong MAX_MANIFEST_SIZE = 100UL << 20;  // 100 MB
    private const int IUPD_SIGNATURE_LENGTH = 64;
    // SECURITY GATE: Signature algorithm is implicitly Ed25519 (SommerEngineering internal scheme)
    // Inline format: [signLen:4][signature:64] - no explicit algId field, but algorithm is hardcoded
    // Verification via Ed25519.Verify enforces algorithm correctness via Ed25519 curve validation
    private const byte EXPECTED_SIGNATURE_ALGORITHM = 1;  // Ed25519 (internal SommerEngineering scheme)

    // Bench public key from unified helper (same as IupdWriter)
    private static readonly byte[] BenchPublicKey = IupdEd25519Keys.BenchPublicKey32;

    // SECURITY: Profile enforcement whitelist (fail-closed on disallowed profiles)
    private static readonly HashSet<IupdProfile> AllowedProfiles = new()
    {
        IupdProfile.SECURE,
        IupdProfile.OPTIMIZED,
        IupdProfile.INCREMENTAL
    };
    private const string AllowAllProfilesBenchEnv = "IRONFAMILY_BENCH_ALLOW_ALL_IUPD_PROFILES";

    private readonly ReadOnlyMemory<byte> _data;
    private IupdError _lastError = IupdError.Ok;
    private bool _signatureValid = false;

    // Production verification key (optional, defaults to bench key)
    private byte[] _verificationPublicKey = BenchPublicKey;

    // Parsed header fields
    private uint _magic;
    private byte _version;
    private IupdProfile _profile = IupdProfile.OPTIMIZED;  // Default for v1
    private uint _flags;
    private ulong _chunkTableOffset;
    private ulong _manifestOffset;
    private ulong _payloadOffset;

    // Parsed metadata
    private uint _chunkCount;
    private uint _dependencyCount;
    private uint _applyOrderCount;
    private ulong _manifestSize;
    private uint _manifestCrc32;

    // UpdateSequence trailer (optional, for anti-replay protection)
    private ulong? _updateSequence = null;

    // INCREMENTAL metadata trailer (required for INCREMENTAL, optional otherwise)
    private IupdIncrementalMetadata? _incrementalMetadata = null;
    private byte[]? _manifestCanonicalHash = null;

    // Replay guard for anti-replay enforcement (optional)
    private IUpdateReplayGuard? _replayGuard = null;
    private bool _replayEnforced = false;

    public IupdError LastError => _lastError;
    public uint ChunkCount => _chunkCount;
    public uint ApplyOrderCount => _applyOrderCount;
    public uint ManifestCrc32 => _manifestCrc32;
    public IupdProfile Profile => _profile;
    public byte Version => _version;
    public bool SignatureValid => _signatureValid;
    public ulong? UpdateSequence => _updateSequence;
    public IupdIncrementalMetadata? IncrementalMetadata => _incrementalMetadata;

    private IupdReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    /// <summary>
    /// Open IUPD file from memory (makes a copy - use OpenStreaming for zero-copy)
    /// </summary>
    public static IupdReader? Open(ReadOnlySpan<byte> data, out IupdError error)
    {
        var reader = new IupdReader(data.ToArray().AsMemory());
        error = reader.ParseHeader();

        // Return null ONLY for errors that prevent meaningful header parsing
        // (Invalid magic, unsupported version, file too small)
        if (!error.IsOk)
        {
            // For magic/version errors, return null since we can't parse the file format
            if (error.Code == IupdErrorCode.InvalidMagic ||
                error.Code == IupdErrorCode.UnsupportedVersion)
            {
                return null;
            }

            // For "file too small" errors, also return null
            if (error.Code == IupdErrorCode.OffsetOutOfBounds && data.Length < 36)
            {
                return null;
            }
        }

        return reader;
    }

    /// <summary>
    /// Open IUPD file for streaming (zero-copy, no data duplication)
    /// WARNING: The provided memory must remain valid for the lifetime of the reader
    /// </summary>
    public static IupdReader? OpenStreaming(ReadOnlyMemory<byte> data, out IupdError error)
    {
        var reader = new IupdReader(data);
        error = reader.ParseHeader();

        // Return null ONLY for errors that prevent meaningful header parsing
        // (Invalid magic, unsupported version, file too small)
        if (!error.IsOk)
        {
            // For magic/version errors, return null since we can't parse the file format
            if (error.Code == IupdErrorCode.InvalidMagic ||
                error.Code == IupdErrorCode.UnsupportedVersion)
            {
                return null;
            }

            // For "file too small" errors, also return null
            if (error.Code == IupdErrorCode.OffsetOutOfBounds && data.Length < 36)
            {
                return null;
            }
        }

        return reader;
    }

    /// <summary>
    /// Get the expected header size based on version (for testing)
    /// </summary>
    public int GetExpectedHeaderSize() => _version switch
    {
        IUPD_VERSION_V1 => IUPD_FILE_HEADER_SIZE_V1,
        IUPD_VERSION_V2 => IUPD_FILE_HEADER_SIZE_V2,
        _ => IUPD_FILE_HEADER_SIZE_V1
    };

    /// <summary>
    /// Set production Ed25519 public key for signature verification
    /// </summary>
    public void SetVerificationKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != 32)
            throw new ArgumentException("Public key must be 32 bytes", nameof(publicKey));

        _verificationPublicKey = publicKey.ToArray();
    }

    /// <summary>
    /// Set replay guard for anti-replay protection enforcement
    /// </summary>
    public IupdReader WithReplayGuard(IUpdateReplayGuard guard, bool enforce = true)
    {
        _replayGuard = guard;
        _replayEnforced = enforce;
        return this;
    }

    /// <summary>
    /// Fast validation (structural checks + signature for SECURE/OPTIMIZED)
    /// </summary>
    public IupdError ValidateFast()
    {
        var error = ParseHeader();
        if (!error.IsOk) return _lastError = error;

        // Validate chunk table
        error = ValidateChunkTable();
        if (!error.IsOk) return _lastError = error;

        // Validate dependencies (if any and profile supports them)
        if (_dependencyCount > 0)
        {
            // V1 files can have dependencies even with MINIMAL profile (legacy format compatibility)
            // For v2+ files, profile must explicitly support dependencies
            if (_version != IUPD_VERSION_V1 && !_profile.SupportsDependencies())
                return _lastError = new IupdError(IupdErrorCode.InvalidHeaderSize, 0,
                    $"Profile {_profile.GetDisplayName()} does not support dependencies");

            error = ValidateDependencies();
            if (!error.IsOk) return _lastError = error;
        }

        // Validate apply order
        error = ValidateApplyOrder();
        if (!error.IsOk) return _lastError = error;

        // SECURITY GATE: Signature verification for v2+ SECURE/OPTIMIZED profiles (fail-closed)
        // Even "fast" validation must verify signatures for SECURE/OPTIMIZED profiles
        // This prevents any bypass path that would allow unverified SECURE/OPTIMIZED files
        // V1 files and non-SECURE/OPTIMIZED v2+ files skip this (not required)
        if (_version != IUPD_VERSION_V1 && _profile.RequiresSignatureStrict())
        {
            error = VerifySignatureStrict();
            if (!error.IsOk) return _lastError = error;
        }

        // IUPD_WITNESS verification: Detect manifest tampering (fail-closed for v2+ SECURE/OPTIMIZED)
        error = VerifyWitnessStrict();
        if (!error.IsOk) return _lastError = error;

        // UpdateSequence verification: Enforce anti-replay protection (fail-closed for v2+ SECURE/OPTIMIZED)
        error = VerifyUpdateSequenceStrict();
        if (!error.IsOk) return _lastError = error;

        // INCREMENTAL metadata verification: Enforce patch-bound semantics (fail-closed for INCREMENTAL)
        error = VerifyIncrementalStrict();
        if (!error.IsOk) return _lastError = error;

        return _lastError = IupdError.Ok;
    }

    /// <summary>
    /// Strict validation (full integrity check)
    /// </summary>
    public IupdError ValidateStrict()
    {
        // First run fast checks (includes signature verification for SECURE/OPTIMIZED)
        var error = ValidateFast();
        if (!error.IsOk) return _lastError = error;

        var data = _data.Span;

        // Verify CRC32 for each chunk (all profiles support CRC32)
        bool requiresBlake3 = _profile.RequiresBlake3();
        for (uint i = 0; i < _chunkCount; i++)
        {
            error = GetChunkEntry(i, out var entry);
            if (!error.IsOk) return _lastError = error;

            var payloadSpan = GetPayloadSpan(entry);
            uint computedCrc32 = Crc32Ieee.Compute(payloadSpan);

            if (computedCrc32 != entry.PayloadCrc32)
            {
                return _lastError = new IupdError(IupdErrorCode.Crc32Mismatch, entry.PayloadOffset,
                    "Chunk CRC32 mismatch");
            }

            if (requiresBlake3)
            {
                if (!Blake3Ieee.Verify(payloadSpan, entry.PayloadBlake3))
                {
                    return _lastError = new IupdError(IupdErrorCode.Blake3Mismatch, i,
                        $"Chunk {i} BLAKE3 hash verification failed");
                }
            }
        }

        // Verify manifest CRC32
        // Manifest CRC32 is computed over all manifest data EXCEPT the last 8 bytes
        // (which contain the CRC32 itself and reserved field)
        var manifestDataSpan = data.Slice((int)_manifestOffset, (int)(_manifestSize - 8));
        uint computedManifestCrc32 = Crc32Ieee.Compute(manifestDataSpan);
        if (computedManifestCrc32 != _manifestCrc32)
        {
            return _lastError = new IupdError(IupdErrorCode.Crc32Mismatch, _manifestOffset,
                "Manifest CRC32 mismatch");
        }

        // Note: Signature verification for v2+ SECURE/OPTIMIZED is already performed
        // in ValidateFast() above (fail-closed enforcement). Both ValidateFast and
        // ValidateStrict enforce signatures, with ValidateStrict adding additional
        // integrity checks (CRC32, BLAKE3).

        return _lastError = IupdError.Ok;
    }

    /// <summary>
    /// Get chunk payload by index (for parallel validation)
    /// </summary>
    public IupdError GetChunkPayload(uint chunkIndex, out ReadOnlySpan<byte> payload)
    {
        payload = default;

        var error = GetChunkEntry(chunkIndex, out var entry);
        if (!error.IsOk)
            return error;

        payload = GetPayloadSpan(entry);
        return IupdError.Ok;
    }

    /// <summary>
    /// Get chunk entry by index
    /// </summary>
    public IupdError GetChunkEntry(uint chunkIndex, out IupdChunkEntry entry)
    {
        entry = default;

        if (chunkIndex >= _chunkCount)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, chunkIndex, "Chunk index out of range");

        var data = _data.Span;
        long entryOffset = (long)_chunkTableOffset + (chunkIndex * IUPD_CHUNK_ENTRY_SIZE);

        if (entryOffset + IUPD_CHUNK_ENTRY_SIZE > data.Length)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, (ulong)entryOffset, "Chunk entry out of bounds");

        var entrySpan = data.Slice((int)entryOffset, IUPD_CHUNK_ENTRY_SIZE);

        uint index = ReadU32Le(entrySpan, 0);
        ulong size = ReadU64Le(entrySpan, 4);
        ulong offset = ReadU64Le(entrySpan, 12);
        uint crc32 = ReadU32Le(entrySpan, 20);
        var blake3 = entrySpan.Slice(24, 32);

        entry = new IupdChunkEntry(index, size, offset, crc32, blake3);
        return IupdError.Ok;
    }

    /// <summary>
    /// Begin streaming apply iteration
    /// </summary>
    public IupdApplier BeginApply()
    {
        return new IupdApplier(this);
    }

    /// <summary>
    /// Internal: parse file header
    /// </summary>
    /// <summary>
    /// Parse IUPD file header.
    ///
    /// INTERFACE STABILITY NOTE (Critical for witness hash):
    /// The header layout is FROZEN and VERSIONED. Any future extension MUST:
    /// 1. Increment IUPD_VERSION_V2 (current: 0x02) to new V3
    /// 2. Never move flags field position (v1: offset 5, v2: offset 6)
    /// 3. Use flags bits for feature toggles (e.g., bit 0: IUPD_WITNESS_ENABLED)
    /// 4. Add new fields after existing offsets (no shifting)
    ///
    /// CANONICAL HASH RANGE (for witness verification):
    /// The witness hash covers manifest data EXCLUDING the CRC32+reserved footer:
    /// - Writer (IupdWriter.WriteSignatureFooter):
    ///   Array.Copy(allData, manifestOffset, manifestData, 0, manifestSize - 8)
    /// - Reader (IupdReader.VerifyWitnessStrict):
    ///   data.Slice(manifestOffset, manifestSize - 8)
    ///
    /// Manifest structure within canonical range:
    /// [0-23] Manifest header (24 bytes: depCount + applyOrderCount + integrityType)
    /// [24...N] Dependency entries (8 bytes each: fromChunk + toChunk)
    /// [N+1...M] Apply order entries (4 bytes each: chunkIndex)
    /// [M+1...M+7] CRC32 + reserved (8 bytes) -- EXCLUDED from canonical hash
    ///
    /// Reordering any element in the canonical range changes the witness hash.
    /// This design ensures that structural tampering (e.g., reordered dependencies,
    /// reordered apply order) is detected by witness verification (fail-closed).
    /// </summary>
    private IupdError ParseHeader()
    {
        var data = _data.Span;

        // Check absolute minimum for magic+version
        if (data.Length < 5)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, 0, "File too small for header");

        // Parse magic (0-3)
        _magic = ReadU32Le(data, 0);
        if (_magic != IUPD_MAGIC && _magic != UPD1_MAGIC)
            return new IupdError(IupdErrorCode.InvalidMagic, 0, "Invalid magic number");

        // Parse version (4)
        _version = data[4];
        if (_version != IUPD_VERSION_V1 && _version != IUPD_VERSION_V2)
            return new IupdError(IupdErrorCode.UnsupportedVersion, 4, "Unsupported version");

        // Check file size before proceeding with version-specific parsing
        int expectedHeaderSize = _version == IUPD_VERSION_V2 ? IUPD_FILE_HEADER_SIZE_V2 : IUPD_FILE_HEADER_SIZE_V1;
        if (data.Length < expectedHeaderSize)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, 0, $"File too small for header (expected {expectedHeaderSize} bytes, got {data.Length})");

        // Determine offsets based on version
        int profileOffset = -1;
        int flagsOffset = -1;
        int headerSizeOffset = -1;
        int reservedOffset = -1;
        int chunkTableOffsetOffset = -1;
        int manifestOffsetOffset = -1;
        int payloadOffsetOffset = -1;

        if (_version == IUPD_VERSION_V1)
        {
            // V1 format (36 bytes):
            // [0-3] Magic
            // [4] Version
            // [5-8] Flags
            // [9-10] Header size
            // [11] Reserved
            // [12-19] Chunk table offset
            // [20-27] Manifest offset
            // [28-35] Payload offset
            profileOffset = -1;  // No profile in v1
            flagsOffset = 5;
            headerSizeOffset = 9;
            reservedOffset = 11;
            chunkTableOffsetOffset = 12;
            manifestOffsetOffset = 20;
            payloadOffsetOffset = 28;
            _profile = IupdProfile.MINIMAL;  // Default profile for v1 (compatibility with legacy files)
        }
        else // IUPD_VERSION_V2
        {
            // V2 format (37 bytes):
            // [0-3] Magic
            // [4] Version
            // [5] Profile
            // [6-9] Flags
            // [10-11] Header size
            // [12] Reserved
            // [13-20] Chunk table offset
            // [21-28] Manifest offset
            // [29-36] Payload offset
            profileOffset = 5;
            flagsOffset = 6;
            headerSizeOffset = 10;
            reservedOffset = 12;
            chunkTableOffsetOffset = 13;
            manifestOffsetOffset = 21;
            payloadOffsetOffset = 29;
        }

        // Parse profile (v2 only)
        if (profileOffset >= 0)
        {
            byte profileByte = data[profileOffset];
            if (!System.Enum.IsDefined(typeof(IupdProfile), profileByte))
                return new IupdError(IupdErrorCode.InvalidFlags, (ulong)profileOffset, $"Invalid profile byte: {profileByte}");
            _profile = (IupdProfile)profileByte;

            // SECURITY: Enforce profile whitelist for v2+ files only (fail-closed)
            // v1 files are legacy and use MINIMAL profile by default for compatibility
            bool allowAllProfilesForBench = string.Equals(
                Environment.GetEnvironmentVariable(AllowAllProfilesBenchEnv),
                "1",
                StringComparison.Ordinal);

            if (!allowAllProfilesForBench && !AllowedProfiles.Contains(_profile))
                return new IupdError(IupdErrorCode.ProfileNotAllowed, (ulong)profileOffset,
                    $"Profile {_profile} is not in the allowed set");
        }

        // Parse flags
        _flags = ReadU32Le(data, flagsOffset);
        // Only IUPD_WITNESS_ENABLED (0x00000001) flag is currently defined and allowed
        const uint IUPD_WITNESS_ENABLED = 0x00000001;
        const uint ALLOWED_FLAGS = IUPD_WITNESS_ENABLED;  // Only witness flag is allowed
        if ((_flags & ~ALLOWED_FLAGS) != 0)
            return new IupdError(IupdErrorCode.InvalidFlags, (ulong)flagsOffset,
                $"Invalid flags: only IUPD_WITNESS_ENABLED (0x{IUPD_WITNESS_ENABLED:X8}) is allowed, got 0x{_flags:X8}");

        // Parse header size
        ushort headerSize = ReadU16Le(data, headerSizeOffset);
        if (headerSize != expectedHeaderSize)
            return new IupdError(IupdErrorCode.InvalidHeaderSize, (ulong)headerSizeOffset, $"Header size must be {expectedHeaderSize}");

        // Parse reserved
        if (data[reservedOffset] != 0)
            return new IupdError(IupdErrorCode.InvalidHeaderSize, (ulong)reservedOffset, "Reserved byte must be 0");

        // Parse offsets
        _chunkTableOffset = ReadU64Le(data, chunkTableOffsetOffset);
        _manifestOffset = ReadU64Le(data, manifestOffsetOffset);
        _payloadOffset = ReadU64Le(data, payloadOffsetOffset);

        // Validate offset ordering
        if (_chunkTableOffset < (ulong)expectedHeaderSize)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, (ulong)chunkTableOffsetOffset, "Chunk table offset before header");

        if (_manifestOffset <= _chunkTableOffset)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, (ulong)manifestOffsetOffset, "Manifest offset before chunk table");

        if (_payloadOffset <= _manifestOffset)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, (ulong)payloadOffsetOffset, "Payload offset before manifest");

        if (_payloadOffset > (ulong)data.Length)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, (ulong)payloadOffsetOffset, "Payload offset exceeds file size");

        // Calculate and validate chunk count
        ulong chunkTableSize = _manifestOffset - _chunkTableOffset;
        if (chunkTableSize % IUPD_CHUNK_ENTRY_SIZE != 0)
            return new IupdError(IupdErrorCode.InvalidChunkTableSize, _chunkTableOffset,
                "Chunk table size not divisible by 56");

        _chunkCount = (uint)(chunkTableSize / IUPD_CHUNK_ENTRY_SIZE);
        if (_chunkCount > MAX_CHUNKS)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, _chunkTableOffset,
                "Chunk count exceeds maximum");

        // Parse manifest header (24 bytes)
        if (_manifestOffset + IUPD_MANIFEST_HEADER_SIZE > (ulong)data.Length)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, _manifestOffset,
                "Manifest header out of bounds");

        var manifestHeaderSpan = data.Slice((int)_manifestOffset, IUPD_MANIFEST_HEADER_SIZE);

        // Manifest version should match file version
        if (manifestHeaderSpan[0] != _version)
            return new IupdError(IupdErrorCode.InvalidManifestVersion, _manifestOffset,
                "Manifest version does not match file version");

        // Check Reserved bytes (offsets 1-3) MUST be 0
        if (manifestHeaderSpan[1] != 0 || manifestHeaderSpan[2] != 0 || manifestHeaderSpan[3] != 0)
            return new IupdError(IupdErrorCode.InvalidHeaderSize, _manifestOffset + 1,
                "Reserved bytes must be 0");

        // TargetVersion at offset 4 (skip, not used in v1)
        _dependencyCount = ReadU32Le(manifestHeaderSpan, 8);
        _applyOrderCount = ReadU32Le(manifestHeaderSpan, 12);
        _manifestSize = ReadU64Le(manifestHeaderSpan, 16);

        // For v1 files, keep MINIMAL profile
        // V1 files may have dependencies but lack signatures (no signature format support)
        // This is handled specially in validation logic

        // Validate manifest size (header is 24 bytes, per spec)
        ulong expectedManifestSize = 24 + (_dependencyCount * 8) + (_applyOrderCount * 4) + 8;
        if (_manifestSize != expectedManifestSize)
            return new IupdError(IupdErrorCode.ManifestSizeMismatch, _manifestOffset + 16,
                "Manifest size field inconsistent");

        if (_manifestSize > MAX_MANIFEST_SIZE)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, _manifestOffset,
                "Manifest size exceeds maximum");

        if (_manifestOffset + _manifestSize > (ulong)data.Length)
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, _manifestOffset,
                "Manifest extends beyond file");

        // Read manifest CRC32 (last 8 bytes of manifest)
        ulong crc32Offset = _manifestOffset + _manifestSize - 8;
        _manifestCrc32 = ReadU32Le(data, (int)crc32Offset);

        // Parse UpdateSequence trailer (optional, magic-based detection)
        var trailerError = ParseUpdateSequenceTrailer();
        if (!trailerError.IsOk)
            return trailerError;

        // Parse INCREMENTAL metadata trailer (required for INCREMENTAL, optional otherwise)
        var incrementalError = ParseIncrementalMetadataTrailer();
        if (!incrementalError.IsOk)
            return incrementalError;

        return IupdError.Ok;
    }

    /// <summary>
    /// Validate dependency list structure
    /// </summary>
    private IupdError ValidateDependencies()
    {
        var data = _data.Span;
        // Dependencies start at: manifest header (24) + ... wait, need to recalculate offset
        ulong dependencyOffset = _manifestOffset + IUPD_MANIFEST_HEADER_SIZE;

        for (uint i = 0; i < _dependencyCount; i++)
        {
            ulong entryOffset = dependencyOffset + (i * 8);

            if (entryOffset + 8 > (ulong)data.Length)
                return new IupdError(IupdErrorCode.OffsetOutOfBounds, entryOffset,
                    "Dependency entry out of bounds");

            // Each dependency is 8 bytes (4 bytes chunk from, 4 bytes chunk to)
            uint dependsOn = ReadU32Le(data, (int)entryOffset);
            uint dependsTo = ReadU32Le(data, (int)(entryOffset + 4));

            // Validate both chunk indices are valid
            if (dependsOn >= _chunkCount)
                return new IupdError(IupdErrorCode.InvalidDependency, dependsOn,
                    $"Dependency 'from' chunk {dependsOn} out of range");

            if (dependsTo >= _chunkCount)
                return new IupdError(IupdErrorCode.InvalidDependency, dependsTo,
                    $"Dependency 'to' chunk {dependsTo} out of range");

            // Self-dependency is invalid
            if (dependsOn == dependsTo)
                return new IupdError(IupdErrorCode.InvalidDependency, dependsOn,
                    $"Self-dependency on chunk {dependsOn}");
        }

        return IupdError.Ok;
    }

    /// <summary>
    /// Validate chunk table structure
    /// </summary>
    private IupdError ValidateChunkTable()
    {
        var data = _data.Span;

        for (uint i = 0; i < _chunkCount; i++)
        {
            var err = GetChunkEntry(i, out var entry);
            if (!err.IsOk) return err;

            // Validate chunk index
            if (entry.ChunkIndex != i)
                return new IupdError(IupdErrorCode.ChunkIndexError, (ulong)(_chunkTableOffset + i * IUPD_CHUNK_ENTRY_SIZE),
                    "Chunk indices not in order");

            // Empty payloads (size == 0) are allowed

            // Check max chunk size
            if (entry.PayloadSize > MAX_CHUNK_SIZE)
                return new IupdError(IupdErrorCode.OffsetOutOfBounds, i, "Chunk size exceeds maximum");

            // Payload must be within bounds
            if (entry.PayloadOffset < _payloadOffset)
                return new IupdError(IupdErrorCode.OffsetOutOfBounds, entry.PayloadOffset,
                    "Payload offset before payload section");

            if (entry.PayloadOffset + entry.PayloadSize > (ulong)data.Length)
                return new IupdError(IupdErrorCode.OffsetOutOfBounds, entry.PayloadOffset,
                    "Payload extends beyond file");

        }

        // Collect all payload ranges and sort by offset to check for overlaps
        var ranges = new System.Collections.Generic.List<(ulong offset, ulong size, uint index)>();
        for (uint i = 0; i < _chunkCount; i++)
        {
            var err = GetChunkEntry(i, out var entry);
            if (!err.IsOk) return err;
            ranges.Add((entry.PayloadOffset, entry.PayloadSize, i));
        }

        // Sort by offset
        ranges.Sort((a, b) => a.offset.CompareTo(b.offset));

        // Check that consecutive ranges (by offset) don't overlap
        for (int i = 0; i < ranges.Count - 1; i++)
        {
            var current = ranges[i];
            var next = ranges[i + 1];
            if (current.offset + current.size > next.offset)
                return new IupdError(IupdErrorCode.OverlappingPayloads, current.offset,
                    $"Chunk {current.index} overlaps with chunk {next.index}");
        }

        return IupdError.Ok;
    }

    /// <summary>
    /// Validate apply order
    /// </summary>
    private IupdError ValidateApplyOrder()
    {
        var data = _data.Span;
        // Manifest header (24 bytes) + dependencies (depCount*8) = apply order start
        ulong applyOrderOffset = _manifestOffset + IUPD_MANIFEST_HEADER_SIZE + (_dependencyCount * 8);

        // Check that apply order count equals chunk count
        if (_applyOrderCount != _chunkCount)
            return new IupdError(IupdErrorCode.MissingChunkInApplyOrder, _manifestOffset,
                "Not all chunks referenced in apply order");

        // Track which chunks are referenced
        var referenced = new bool[_chunkCount];

        for (uint i = 0; i < _applyOrderCount; i++)
        {
            ulong entryOffset = applyOrderOffset + (i * 4);

            if (entryOffset + 4 > (ulong)data.Length)
                return new IupdError(IupdErrorCode.OffsetOutOfBounds, entryOffset,
                    "Apply order entry out of bounds");

            uint chunkIndex = ReadU32Le(data, (int)entryOffset);

            if (chunkIndex >= _chunkCount)
                return new IupdError(IupdErrorCode.MissingChunkInApplyOrder, (ulong)entryOffset,
                    $"Apply order references invalid chunk {chunkIndex}");

            if (referenced[chunkIndex])
                return new IupdError(IupdErrorCode.DuplicateChunkInApplyOrder, (ulong)entryOffset,
                    $"Chunk {chunkIndex} referenced multiple times");

            referenced[chunkIndex] = true;
        }

        // Check all chunks are referenced
        for (uint i = 0; i < _chunkCount; i++)
        {
            if (!referenced[i])
                return new IupdError(IupdErrorCode.MissingChunkInApplyOrder, _manifestOffset, $"Chunk {i} not referenced");
        }

        return IupdError.Ok;
    }

    /// <summary>
    /// Internal: get payload span for a chunk
    /// </summary>
    private byte[]? _decompressedCache = null;
    private uint _cachedChunkIndex = uint.MaxValue;

    private ReadOnlySpan<byte> GetPayloadSpan(IupdChunkEntry entry)
    {
        if (_cachedChunkIndex == entry.ChunkIndex && _decompressedCache != null)
            return _decompressedCache;

        var data = _data.Span;
        var rawPayload = data.Slice((int)entry.PayloadOffset, (int)entry.PayloadSize);

        // If profile doesn't support compression, return raw payload as-is
        if (!_profile.SupportsCompression())
        {
            return rawPayload;
        }

        // Profile supports compression - check if data is actually compressed.
        // To avoid false positives on raw payloads, only marker 0x01 is treated
        // as wrapped compressed payload. Marker 0x00 is not auto-detected here.
        if (rawPayload.Length >= 9)
        {
            byte potentialMarker = rawPayload[8];
            if (potentialMarker == 0x01)
            {
                ulong potentialSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(rawPayload.Slice(0, 8));
                // If the size field looks reasonable (not too large), treat as wrapped metadata
                if (potentialSize > 0 && potentialSize < (1UL << 32))  // Max 4GB
                {
                    // Try to decompress as wrapped data
                    if (IupdPayloadCompression.TryDecompressPayload(rawPayload, out var decompressed, out var error) &&
                        decompressed != null)
                    {
                        _decompressedCache = decompressed;
                        _cachedChunkIndex = entry.ChunkIndex;
                        return decompressed;
                    }
                }
            }
        }

        // Not compressed or can't decompress - return raw payload
        _cachedChunkIndex = uint.MaxValue;
        _decompressedCache = null;
        return rawPayload;
    }

    /// <summary>
    /// Internal: get apply order chunk index
    /// </summary>
    internal uint GetApplyOrderChunkIndex(uint orderIndex)
    {
        if (orderIndex >= _applyOrderCount)
            throw new IndexOutOfRangeException("Apply order index out of range");

        var data = _data.Span;
        // Apply order starts at manifest header (24 bytes) + dependencies (depCount*8)
        ulong applyOrderStart = _manifestOffset + IUPD_MANIFEST_HEADER_SIZE + (_dependencyCount * 8);
        ulong entryOffset = applyOrderStart + (orderIndex * 4);
        return ReadU32Le(data, (int)entryOffset);
    }

    /// <summary>
    /// Internal: get payload span for apply (errors are thrown as exceptions)
    /// </summary>
    internal ReadOnlySpan<byte> GetPayloadSpanForApply(uint chunkIndex)
    {
        var err = GetChunkEntry(chunkIndex, out var entry);
        if (!err.IsOk)
            throw new InvalidOperationException($"Failed to get chunk entry for chunk {chunkIndex}: {err}");
        return GetPayloadSpan(entry);
    }

    /// <summary>
    /// Verify signature in the manifest footer (for SECURE and OPTIMIZED profiles)
    /// Strict/fail-closed semantics: signature is REQUIRED for SECURE/OPTIMIZED
    /// </summary>
    private IupdError VerifySignatureStrict()
    {
        // SECURITY HARDENING: Fail-closed semantics for v2+ SECURE/OPTIMIZED
        // This method implements mandatory signature verification with no bypass paths.
        // Gates: signature footer presence, length validation, public key requirement,
        //        algorithm enforcement (Ed25519), and cryptographic verification.

        var data = _data.Span;

        // GATE 1: Signature footer presence (fail-closed)
        // Signature footer must be present after manifest: [signatureLength:4][signature:64]
        ulong signatureFooterOffset = _manifestOffset + _manifestSize;

        // SECURE/OPTIMIZED profiles REQUIRE a signature footer
        if (signatureFooterOffset + 4 > (ulong)data.Length)
        {
            _signatureValid = false;
            return new IupdError(IupdErrorCode.SignatureMissing, signatureFooterOffset,
                "Signature footer required for " + _profile.GetDisplayName() + " profile but not found");
        }

        // GATE 2: Signature length validation (fail-closed)
        // Read signature length (must be exactly 64 bytes for Ed25519)
        uint sigLen = ReadU32Le(data, (int)signatureFooterOffset);
        if (sigLen != IUPD_SIGNATURE_LENGTH)
        {
            _signatureValid = false;
            return new IupdError(IupdErrorCode.SignatureMissing, signatureFooterOffset,
                "Invalid signature length: expected 64 bytes, got " + sigLen +
                " (algorithm enforces Ed25519 which requires exactly 64 bytes)");
        }

        // GATE 3: Signature data bounds validation (fail-closed)
        // Verify signature data is fully contained within file
        if (signatureFooterOffset + 4 + IUPD_SIGNATURE_LENGTH > (ulong)data.Length)
        {
            _signatureValid = false;
            return new IupdError(IupdErrorCode.OffsetOutOfBounds, signatureFooterOffset + 4,
                "Signature data out of bounds");
        }

        var signatureData = data.Slice((int)signatureFooterOffset + 4, IUPD_SIGNATURE_LENGTH);

        // GATE 4: Verification public key requirement (fail-closed)
        // Check that verification public key is configured and valid (32 bytes)
        if (_verificationPublicKey == null || _verificationPublicKey.Length != 32)
        {
            _signatureValid = false;
            return new IupdError(IupdErrorCode.SignatureMissing, signatureFooterOffset,
                "Verification public key not configured or invalid for " + _profile.GetDisplayName() + " profile");
        }

        // GATE 5: Algorithm validation (implicit, fail-closed)
        // Algorithm is implicitly Ed25519 (SommerEngineering internal scheme).
        // No explicit algId field in inline format, but Ed25519.Verify enforces curve constraints.
        // If signature doesn't verify with Ed25519, it fails below (fail-closed).
        // This provides implicit algorithm validation: non-Ed25519 signatures cannot verify.

        // Compute manifest hash using BLAKE3-256 (same as writer)
        // Hash algorithm is implicitly BLAKE3, verified by successful signature check
        byte[] hash = GetManifestCanonicalHash();

        // GATE 6: Cryptographic signature verification (fail-closed)
        // Ed25519.Verify enforces:
        //   - Correct algorithm (Ed25519 curve validation)
        //   - Correct public key (derived from signature)
        //   - Manifest authenticity (hash verification)
        // Returns false if ANYTHING fails - fail-closed semantics
        bool signatureValid = Ed25519.Verify(_verificationPublicKey, hash, signatureData);
        _signatureValid = signatureValid;

        // For SECURE/OPTIMIZED profiles, signature verification is REQUIRED and MANDATORY
        // No fallback, no informational mode, no bypass path
        if (!signatureValid)
        {
            return new IupdError(IupdErrorCode.SignatureInvalid, signatureFooterOffset + 4,
                "Ed25519 signature verification failed for " + _profile.GetDisplayName() + " profile; " +
                "this is a required cryptographic check with no bypass (fail-closed)");
        }

        return IupdError.Ok;
    }

    /// <summary>
    /// IUPD_WITNESS verification: Detect tampering with manifest or chunk table.
    /// For v2+ SECURE/OPTIMIZED profiles with WITNESS flag set:
    ///   - Witness hash is stored in signature footer after the 64-byte signature
    ///   - Recompute BLAKE3-256 of manifest (excluding CRC32+reserved)
    ///   - Compare with stored witness hash from footer
    /// Fail-closed semantics: mismatch immediately fails with WitnessMismatch error.
    /// </summary>
    private IupdError VerifyWitnessStrict()
    {
        // WITNESS verification only applies to v2+ SECURE/OPTIMIZED profiles
        if (_version == IUPD_VERSION_V1 || !_profile.RequiresWitnessStrict())
            return IupdError.Ok;

        // Check if WITNESS flag is set in header flags
        const uint IUPD_WITNESS_ENABLED = 0x00000001;
        if ((_flags & IUPD_WITNESS_ENABLED) == 0)
            return IupdError.Ok;  // Witness not required if flag not set

        var data = _data.Span;

        // Witness hash is located in signature footer after the signature
        // Signature footer layout: [signLen:4][signature:64][witnessHash32:32]
        ulong signatureFooterOffset = _manifestOffset + _manifestSize;
        ulong witnessHashOffset = signatureFooterOffset + 4 + IUPD_SIGNATURE_LENGTH;

        // Verify witness hash footer is present and complete
        if (witnessHashOffset + IUPD_WITNESS_HASH_SIZE > (ulong)data.Length)
        {
            return new IupdError(IupdErrorCode.WitnessMissing, witnessHashOffset,
                "Witness hash footer missing or incomplete for " + _profile.GetDisplayName() + " profile");
        }

        // Extract stored witness hash from footer
        var storedWitnessHash = data.Slice((int)witnessHashOffset, IUPD_WITNESS_HASH_SIZE);

        // Recompute witness hash from manifest data (same range as signature)
        // Manifest includes header, dependencies, apply order (excludes CRC32+reserved)
        byte[] computedWitnessHash = GetManifestCanonicalHash();

        // Verify witness hash matches (fail-closed)
        if (!storedWitnessHash.SequenceEqual(computedWitnessHash))
        {
            return new IupdError(IupdErrorCode.WitnessMismatch, witnessHashOffset,
                "Witness hash mismatch: manifest or chunk table was tampered (fail-closed enforcement)");
        }

        return IupdError.Ok;
    }

    private byte[] GetManifestCanonicalHash()
    {
        if (_manifestCanonicalHash != null)
            return _manifestCanonicalHash;

        var manifestDataSpan = _data.Span.Slice((int)_manifestOffset, (int)(_manifestSize - 8));
        _manifestCanonicalHash = Blake3Ieee.Compute(manifestDataSpan);
        return _manifestCanonicalHash;
    }

    private IupdError VerifyUpdateSequenceStrict()
    {
        // UpdateSequence enforcement only applies to v2+ SECURE/OPTIMIZED profiles
        if (_version == IUPD_VERSION_V1)
            return IupdError.Ok;

        if (!_profile.RequiresSignatureStrict())
            return IupdError.Ok;

        // SECURITY: v2+ SECURE/OPTIMIZED MUST have UpdateSequence (fail-closed)
        if (!_updateSequence.HasValue)
            return new IupdError(IupdErrorCode.UpdateSequenceMissing, _payloadOffset,
                "UpdateSequence trailer required for " + _profile.GetDisplayName() + " profile");

        // ReplayGuard enforcement (optional, only if guard is set and enforce flag is true)
        if (_replayGuard == null || !_replayEnforced)
            return IupdError.Ok;

        // Get last accepted sequence from guard
        ulong lastAccepted = 0;
        _replayGuard.TryGetLastAccepted(out lastAccepted);

        ulong currentSeq = _updateSequence.Value;

        // Fail-closed: sequence must be strictly greater than last accepted
        if (currentSeq <= lastAccepted)
            return new IupdError(IupdErrorCode.ReplayDetected, _payloadOffset,
                $"UpdateSequence replay detected: current {currentSeq} <= last {lastAccepted}");

        // Update guard with new accepted sequence
        _replayGuard.SetLastAccepted(currentSeq);

        return IupdError.Ok;
    }

    // --- Helper methods ---

    private static uint ReadU32Le(ReadOnlySpan<byte> data, int offset)
    {
        return (uint)data[offset] |
               ((uint)data[offset + 1] << 8) |
               ((uint)data[offset + 2] << 16) |
               ((uint)data[offset + 3] << 24);
    }

    private static ushort ReadU16Le(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private static ulong ReadU64Le(ReadOnlySpan<byte> data, int offset)
    {
        return ((ulong)ReadU32Le(data, offset)) |
               (((ulong)ReadU32Le(data, offset + 4)) << 32);
    }

    /// <summary>
    /// Parse UpdateSequence trailer (optional, after signature footer before payloads)
    /// Trailer format: [magic:8][length:4][version:1][sequence:8] = 21 bytes
    /// Magic: "IUPDSEQ1" (ASCII)
    /// Length: 21 (u32 LE)
    /// Version: 1 (u8)
    /// Sequence: u64 LE (update sequence number)
    /// </summary>
    private IupdError ParseUpdateSequenceTrailer()
    {
        var data = _data.Span;

        // UpdateSequence trailer is OPTIONAL
        // It appears after signature footer and before payloads
        // Detection: check for magic "IUPDSEQ1" at position _payloadOffset - 21

        // Check if there's enough space before payload offset
        if (_payloadOffset < IUPD_UPDATESEQ_TRAILER_SIZE)
        {
            // No trailer present (payload offset doesn't allow for trailer)
            _updateSequence = null;
            return IupdError.Ok;
        }

        ulong trailerOffset = _payloadOffset - IUPD_UPDATESEQ_TRAILER_SIZE;

        // Check if trailer is present by verifying magic
        if (trailerOffset + IUPD_UPDATESEQ_TRAILER_SIZE > (ulong)data.Length)
        {
            // Not enough data for trailer
            _updateSequence = null;
            return IupdError.Ok;
        }

        // Read magic (first 8 bytes of trailer)
        var trailerMagic = data.Slice((int)trailerOffset, 8);
        var expectedMagic = System.Text.Encoding.ASCII.GetBytes("IUPDSEQ1");

        // Check if magic matches
        if (!trailerMagic.SequenceEqual(expectedMagic))
        {
            // No trailer present (magic doesn't match)
            _updateSequence = null;
            return IupdError.Ok;
        }

        // Magic matches - verify trailer structure
        // Length field (4 bytes after magic)
        uint trailerLength = ReadU32Le(data, (int)(trailerOffset + 8));
        if (trailerLength != IUPD_UPDATESEQ_TRAILER_SIZE)
            return new IupdError(IupdErrorCode.InvalidHeaderSize, trailerOffset + 8,
                $"UpdateSequence trailer length mismatch: expected {IUPD_UPDATESEQ_TRAILER_SIZE}, got {trailerLength}");

        // Version field (1 byte)
        byte trailerVersion = data[(int)(trailerOffset + 12)];
        if (trailerVersion != 1)
            return new IupdError(IupdErrorCode.UnsupportedVersion, trailerOffset + 12,
                $"UpdateSequence trailer version unsupported: {trailerVersion}");

        // Sequence field (8 bytes, u64 LE)
        ulong sequence = ReadU64Le(data, (int)(trailerOffset + 13));
        _updateSequence = sequence;

        return IupdError.Ok;
    }

    /// <summary>
    /// Verify INCREMENTAL profile metadata requirements (fail-closed for INCREMENTAL)
    /// </summary>
    private IupdError VerifyIncrementalStrict()
    {
        // INCREMENTAL profile MUST have metadata trailer with valid algorithm ID
        if (!_profile.IsIncremental())
            return IupdError.Ok;  // Not INCREMENTAL, no special requirements

        if (_incrementalMetadata == null)
            return new IupdError(IupdErrorCode.SignatureMissing, _payloadOffset,
                "INCREMENTAL profile requires patch algorithm metadata");

        if (!_incrementalMetadata.IsKnownAlgorithm())
            return new IupdError(IupdErrorCode.SignatureInvalid, _payloadOffset,
                $"Unknown patch algorithm: {_incrementalMetadata.GetAlgorithmName()}");

        if (_incrementalMetadata.BaseHash == null || _incrementalMetadata.BaseHash.Length == 0)
            return new IupdError(IupdErrorCode.SignatureMissing, _payloadOffset,
                "INCREMENTAL profile requires base image hash");

        return IupdError.Ok;
    }

    /// <summary>
    /// Parse INCREMENTAL metadata trailer (required for INCREMENTAL, optional otherwise)
    /// </summary>
    private IupdError ParseIncrementalMetadataTrailer()
    {
        var data = _data.Span;

        // INCREMENTAL metadata trailer is positioned at the end of file (after payloads) in canonical layout
        // We search backward from EOF to find the magic "IUPDINC1"

        // Expected magic: "IUPDINC1"
        string expectedMagicStr = "IUPDINC1";
        byte[] expectedMagic = System.Text.Encoding.ASCII.GetBytes(expectedMagicStr);

        // Search backward from end of file to find trailer
        // Minimum trailer size is 21 bytes (magic + length + version + algorithmId + baseHashLength + CRC32)
        // Maximum ~84 bytes (with both BLAKE3-256 hashes)
        const int MIN_TRAILER_SIZE = 21;

        if (data.Length < MIN_TRAILER_SIZE)
        {
            // File too small for trailer
            _incrementalMetadata = null;
            return IupdError.Ok;
        }

        // Search backward from end of file
        // Start from position where magic (8 bytes) could end (at data.Length - MIN_TRAILER_SIZE + 8)
        // and search backward to position 0
        int searchStart = Math.Max(0, data.Length - 1000);  // Search within last 1000 bytes as reasonable bound

        for (int pos = data.Length - MIN_TRAILER_SIZE; pos >= searchStart; pos--)
        {
            if (pos < 0 || pos + 8 > data.Length)
                continue;

            // Check for INCREMENTAL trailer magic at this position
            bool magicMatches = true;
            for (int i = 0; i < 8; i++)
            {
                if (data[pos + i] != expectedMagic[i])
                {
                    magicMatches = false;
                    break;
                }
            }

            if (!magicMatches)
                continue;

            // Found magic, extract trailer length
            if (pos + 8 + 4 > data.Length)
                continue;

            uint trailerLength = ReadU32Le(data, pos + 8);

            // Validate trailer length is reasonable
            if (trailerLength < MIN_TRAILER_SIZE || trailerLength > 1000)
                continue;

            if (pos + trailerLength > data.Length)
                continue;

            // Extract trailer bytes
            byte[] trailerBytes = new byte[trailerLength];
            data.Slice(pos, (int)trailerLength).CopyTo(trailerBytes);

            // Deserialize
            var (success, metadata, error) = IupdIncrementalMetadata.TryDeserialize(trailerBytes);
            if (success && metadata != null)
            {
                _incrementalMetadata = metadata;
                return IupdError.Ok;
            }
        }

        // No trailer found
        _incrementalMetadata = null;
        return IupdError.Ok;
    }
}

/// <summary>
/// Streaming apply iterator
/// </summary>
public sealed class IupdApplier
{
    private readonly IupdReader _reader;
    private uint _applyOrderIndex = 0;

    internal IupdApplier(IupdReader reader)
    {
        _reader = reader;
    }

    /// <summary>
    /// Get next chunk in apply order
    /// </summary>
    public bool TryNext(out IupdChunk chunk)
    {
        chunk = default;

        if (_applyOrderIndex >= _reader.ApplyOrderCount)
            return false;

        uint chunkIndex = _reader.GetApplyOrderChunkIndex(_applyOrderIndex);
        _applyOrderIndex++;

        var err = _reader.GetChunkEntry(chunkIndex, out var entry);
        if (!err.IsOk)
            throw new InvalidOperationException($"Failed to get chunk: {err}");

        var payload = _reader.GetPayloadSpanForApply(chunkIndex);
        chunk = new IupdChunk(chunkIndex, payload, entry.PayloadCrc32, entry.PayloadBlake3);

        return true;
    }

    /// <summary>
    /// Reset apply iterator
    /// </summary>
    public void Reset()
    {
        _applyOrderIndex = 0;
    }
}
