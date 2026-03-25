namespace IronFamily.MegaBench.Competitors.Profiles;

/// <summary>
/// Codec profile matrix for controlled performance variation.
/// Each profile represents a specific trade-off configuration.
/// </summary>
public enum CodecProfile
{
    /// <summary>
    /// Speed-optimized: minimal processing, streaming encoding.
    /// - JSON: default serializer
    /// - MessagePack: LZ4Block disabled
    /// - CBOR: canonical=false
    /// - Zstd: compression_level=1
    /// </summary>
    FAST = 0,

    /// <summary>
    /// Balanced trade-off: moderate compression, reasonable speed.
    /// - JSON: no indentation, no whitespace
    /// - MessagePack: default settings
    /// - CBOR: canonical=true
    /// - Zstd: compression_level=3
    /// </summary>
    BALANCED = 1,

    /// <summary>
    /// Size-optimized: aggressive encoding, slow.
    /// - JSON: aggressive indentation removal + minification
    /// - MessagePack: LZ4Block enabled
    /// - CBOR: canonical=true + minimal_encoding
    /// - Zstd: compression_level=9
    /// </summary>
    SMALL = 2
}
