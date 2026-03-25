namespace IronConfig.Iupd;

/// <summary>
/// IUPD Profile - determines which features are included in the update package.
/// Each profile targets different use cases with different size/feature tradeoffs.
/// </summary>
public enum IupdProfile : byte
{
    /// <summary>
    /// MINIMAL (0x00): Smallest overhead, no compression, no BLAKE3, no dependencies
    /// - CRC32 only for integrity
    /// - Sequential apply order
    /// - Best for: Fast updates without security requirements
    /// - Profile baseline (100% raw payload + minimal metadata)
    /// </summary>
    MINIMAL = 0x00,

    /// <summary>
    /// FAST (0x01): LZ4 compression, CRC32, no BLAKE3, no dependencies
    /// - Compressed payloads
    /// - Apply order support
    /// - CRC32 verification only
    /// - Best for: Speed with compression (e.g., log aggregation)
    /// - Size: ~50-70% vs MINIMAL (LZ4 compression benefit)
    /// </summary>
    FAST = 0x01,

    /// <summary>
    /// SECURE (0x02): BLAKE3 + dependencies, no compression
    /// - BLAKE3-256 per chunk for cryptographic verification
    /// - Dependency graph support
    /// - CRC32 + BLAKE3 integrity
    /// - Best for: Security-critical updates
    /// - Size: ~105-110% vs MINIMAL (adds BLAKE3 hashes, no compression)
    /// </summary>
    SECURE = 0x02,

    /// <summary>
    /// OPTIMIZED (0x03): All features - LZ4 + BLAKE3 + dependencies
    /// - Full LZ4 compression
    /// - BLAKE3-256 verification
    /// - Complete dependency support
    /// - Apply order with offset tracking
    /// - CRC32 + BLAKE3 integrity
    /// - Best for: Production use (current IUPD default)
    /// - Size: ~50-60% vs MINIMAL (LZ4 compression + BLAKE3 overhead)
    /// </summary>
    OPTIMIZED = 0x03,

    /// <summary>
    /// INCREMENTAL (0x04): Binary delta updates with bsdiff-style compression
    /// - Compressed binary deltas (future implementation)
    /// - BLAKE3 verification
    /// - Rollback information
    /// - Best for: Small incremental updates
    /// - Size: Delta package typically 5-20% of raw target size (highly variable by change type)
    /// </summary>
    INCREMENTAL = 0x04,
}

/// <summary>
/// Extension methods for IupdProfile
/// </summary>
public static class IupdProfileExtensions
{
    /// <summary>
    /// Check if profile requires BLAKE3 verification
    /// </summary>
    public static bool RequiresBlake3(this IupdProfile profile) => profile is IupdProfile.SECURE or IupdProfile.OPTIMIZED or IupdProfile.INCREMENTAL;

    /// <summary>
    /// Check if profile supports compression
    /// </summary>
    public static bool SupportsCompression(this IupdProfile profile) => profile is IupdProfile.FAST or IupdProfile.OPTIMIZED or IupdProfile.INCREMENTAL;

    /// <summary>
    /// Check if profile supports dependency graph
    /// </summary>
    public static bool SupportsDependencies(this IupdProfile profile) => profile is IupdProfile.SECURE or IupdProfile.OPTIMIZED or IupdProfile.INCREMENTAL;

    /// <summary>
    /// Check if profile requires cryptographic signature verification (fail-closed enforcement)
    /// SECURE, OPTIMIZED, and INCREMENTAL profiles require Ed25519 signature verification
    /// </summary>
    public static bool RequiresSignatureStrict(this IupdProfile profile) => profile is IupdProfile.SECURE or IupdProfile.OPTIMIZED or IupdProfile.INCREMENTAL;

    /// <summary>
    /// Check if profile requires witness hash verification (manifest integrity)
    /// SECURE, OPTIMIZED, and INCREMENTAL profiles enforce witness hash verification
    /// </summary>
    public static bool RequiresWitnessStrict(this IupdProfile profile) => profile is IupdProfile.SECURE or IupdProfile.OPTIMIZED or IupdProfile.INCREMENTAL;

    /// <summary>
    /// Check if profile uses incremental (delta) compression
    /// </summary>
    public static bool IsIncremental(this IupdProfile profile) => profile == IupdProfile.INCREMENTAL;

    /// <summary>
    /// Get human-readable profile name
    /// </summary>
    public static string GetDisplayName(this IupdProfile profile) => profile switch
    {
        IupdProfile.MINIMAL => "MINIMAL",
        IupdProfile.FAST => "FAST",
        IupdProfile.SECURE => "SECURE",
        IupdProfile.OPTIMIZED => "OPTIMIZED",
        IupdProfile.INCREMENTAL => "INCREMENTAL",
        _ => "UNKNOWN"
    };
}
