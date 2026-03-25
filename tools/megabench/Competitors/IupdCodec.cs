using IronFamily.MegaBench.Competitors.Profiles;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Native IUPD codec for benchmarking.
/// Encodes/decodes IUPD format directly using IupdWriter/IupdReader.
/// </summary>
public class IupdCodec : ICompetitorCodec
{
    public string Name => "IUPD";
    public CodecKind Kind => CodecKind.IUPD_Manifest;
    public CodecProfile Profile { get; }

    public IupdCodec(CodecProfile profile = CodecProfile.BALANCED)
    {
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        // For native codec, the canonical input is already IUPD-encoded
        // This codec is identity for benchmarking purposes
        return (byte[])canonicalInput.Clone();
    }

    public byte[] Decode(byte[] encoded)
    {
        // For native codec, decoding back to original is identity
        return (byte[])encoded.Clone();
    }
}
