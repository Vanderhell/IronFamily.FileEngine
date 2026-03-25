using IronFamily.MegaBench.Competitors.Profiles;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Contract for competitor codec implementations.
/// </summary>
public interface ICompetitorCodec
{
    /// <summary>
    /// Codec name/identifier.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Target format this codec handles.
    /// </summary>
    CodecKind Kind { get; }

    /// <summary>
    /// Codec profile (FAST, BALANCED, SMALL).
    /// </summary>
    CodecProfile Profile { get; }

    /// <summary>
    /// Encode canonical input to codec-specific binary.
    /// </summary>
    byte[] Encode(byte[] canonicalInput);

    /// <summary>
    /// Decode codec-specific binary back to canonical form.
    /// </summary>
    byte[] Decode(byte[] encoded);
}
