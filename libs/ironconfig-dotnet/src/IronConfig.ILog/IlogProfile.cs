namespace IronConfig.ILog;

/// <summary>
/// ILOG Profile - determines which layers (blocks) are included in the structured log file.
/// Each profile targets different use cases with different feature/size tradeoffs.
/// </summary>
public enum IlogProfile
{
    /// <summary>
    /// MINIMAL (flags: 0x01): L0 (DATA) + L1 (TOC) only
    /// - No compression, no indexing, no signing
    /// - Best for: Basic logging with minimal overhead
    /// </summary>
    MINIMAL,

    /// <summary>
    /// INTEGRITY (flags: 0x03): L0 + L1 + L4 (CRC32 seal)
    /// - CRC32 verification for corruption detection
    /// - No compression, no indexing
    /// - Best for: Corruption detection in storage
    /// </summary>
    INTEGRITY,

    /// <summary>
    /// SEARCHABLE (flags: 0x09): L0 + L1 + L2 (sorted index)
    /// - L2 provides byte-offset index for fast record lookup
    /// - No compression, no sealing
    /// - Best for: Fast record lookup in large logs
    /// </summary>
    SEARCHABLE,

    /// <summary>
    /// ARCHIVED (flags: 0x11): L1 + L3 (storage-first compression)
    /// - L3 is the primary payload carrier for archived logs
    /// - Omits raw L0 to prioritize size reduction
    /// - Best for: Long-term storage and transfer size reduction
    /// </summary>
    ARCHIVED,

    /// <summary>
    /// AUDITED (flags: 0x27): L0 + L1 + L4 (BLAKE3 seal with Ed25519)
    /// - BLAKE3-256 verification + Ed25519 signing (if key provided)
    /// - Tamper-proof logging with cryptographic assurance
    /// - Best for: Compliance and tamper-proof logging
    /// </summary>
    AUDITED
}

/// <summary>
/// Extension methods for IlogProfile
/// </summary>
public static class IlogProfileExtensions
{
    /// <summary>
    /// Check if profile requires BLAKE3 verification
    /// </summary>
    public static bool RequiresBlake3(this IlogProfile profile) =>
        profile is IlogProfile.AUDITED;

    /// <summary>
    /// Check if profile includes CRC32 integrity
    /// </summary>
    public static bool SupportsCrc32(this IlogProfile profile) =>
        profile is IlogProfile.INTEGRITY or IlogProfile.AUDITED;

    /// <summary>
    /// Check if profile supports compression (L3 ARCHIVE)
    /// </summary>
    public static bool SupportsCompression(this IlogProfile profile) =>
        profile is IlogProfile.ARCHIVED;

    /// <summary>
    /// Check if profile supports search indexing (L2 INDEX)
    /// </summary>
    public static bool SupportsSearch(this IlogProfile profile) =>
        profile is IlogProfile.SEARCHABLE;

    /// <summary>
    /// Check if profile includes sealing block (L4 SEAL)
    /// </summary>
    public static bool HasSealing(this IlogProfile profile) =>
        profile is IlogProfile.INTEGRITY or IlogProfile.AUDITED;

    /// <summary>
    /// Check if profile is AUDITED (full cryptographic assurance)
    /// </summary>
    public static bool IsAudited(this IlogProfile profile) =>
        profile == IlogProfile.AUDITED;

    /// <summary>
    /// Get human-readable profile name
    /// </summary>
    public static string GetDisplayName(this IlogProfile profile) =>
        profile switch
        {
            IlogProfile.MINIMAL => "MINIMAL",
            IlogProfile.INTEGRITY => "INTEGRITY",
            IlogProfile.SEARCHABLE => "SEARCHABLE",
            IlogProfile.ARCHIVED => "ARCHIVED",
            IlogProfile.AUDITED => "AUDITED",
            _ => "UNKNOWN"
        };

    /// <summary>
    /// Get profile flags byte for binary encoding
    /// Flags: Bit 0=LittleEndian (always 1), Bit 1=CRC32, Bit 2=BLAKE3, Bit 3=L2, Bit 4=L3, Bit 5=WITNESS_ENABLED
    /// </summary>
    public static byte GetProfileFlags(this IlogProfile profile)
    {
        byte flags = 0x01; // Bit 0: LittleEndian (always 1)

        return profile switch
        {
            IlogProfile.MINIMAL => flags,
            IlogProfile.INTEGRITY => (byte)(flags | 0x02),                    // Bit 1: CRC32
            IlogProfile.SEARCHABLE => (byte)(flags | 0x08),                   // Bit 3: L2
            IlogProfile.ARCHIVED => (byte)(flags | 0x10),                     // Bit 4: L3
            IlogProfile.AUDITED => (byte)(flags | 0x06 | 0x20),               // Bits 1+2: CRC32 | BLAKE3, Bit 5: WITNESS_ENABLED
            _ => flags
        };
    }
}
