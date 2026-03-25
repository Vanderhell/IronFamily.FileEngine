using IronFamily.MegaBench.Competitors.Profiles;
using MegaBench.Schemas.Protobuf;
using System.IO;
using Google.Protobuf;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Protocol Buffers codec using generated CanonicalEnvelope schema.
/// Encodes/decodes canonical JSON using protobuf framing.
/// </summary>
public class ProtobufCodec : ICompetitorCodec
{
    public string Name => "Protobuf";
    public CodecKind Kind { get; }
    public CodecProfile Profile { get; }

    public ProtobufCodec(CodecKind kind, CodecProfile profile = CodecProfile.BALANCED)
    {
        Kind = kind;
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        var envelope = new CanonicalEnvelope
        {
            CanonicalJson = ByteString.CopyFrom(canonicalInput)
        };

        using (var ms = new MemoryStream())
        {
            using (var cos = new CodedOutputStream(ms))
            {
                envelope.WriteTo(cos);
            }
            return ms.ToArray();
        }
    }

    public byte[] Decode(byte[] encoded)
    {
        var envelope = CanonicalEnvelope.Parser.ParseFrom(encoded);
        return envelope.CanonicalJson.ToByteArray();
    }
}
