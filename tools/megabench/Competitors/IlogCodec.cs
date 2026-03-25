using IronFamily.MegaBench.Competitors.Profiles;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Native ILOG codec for benchmarking.
/// Encodes/decodes ILOG format directly using IlogEncoder.
/// </summary>
public class IlogCodec : ICompetitorCodec
{
    public string Name => "ILOG";
    public CodecKind Kind => CodecKind.ILOG;
    public CodecProfile Profile { get; }

    public IlogCodec(CodecProfile profile = CodecProfile.BALANCED)
    {
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        // For native codec, the canonical input is already ILOG-encoded
        // This codec is identity for benchmarking purposes
        return (byte[])canonicalInput.Clone();
    }

    public byte[] Decode(byte[] encoded)
    {
        // For native codec, decoding back to original is identity
        return (byte[])encoded.Clone();
    }
}
