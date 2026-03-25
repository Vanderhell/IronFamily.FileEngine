using IronFamily.MegaBench.Competitors.Profiles;
using Google.FlatBuffers;
using MegaBench.Schemas.FlatBuffers;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// FlatBuffers codec using generated schema and Google.FlatBuffers runtime.
/// Real FlatBuffers implementation with FlatBufferBuilder and GetRootAs accessors.
/// </summary>
public class FlatBuffersCodec : ICompetitorCodec
{
    public string Name => "FlatBuffers";
    public CodecKind Kind { get; }
    public CodecProfile Profile { get; }

    public FlatBuffersCodec(CodecKind kind, CodecProfile profile = CodecProfile.BALANCED)
    {
        Kind = kind;
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        var builder = new FlatBufferBuilder(canonicalInput.Length + 128);

        // Create the canonical_json byte vector
        var jsonVectorOffset = CanonicalEnvelope.CreateCanonicalJsonVectorBlock(builder, canonicalInput);

        // Create the CanonicalEnvelope table
        CanonicalEnvelope.StartCanonicalEnvelope(builder);
        CanonicalEnvelope.AddCanonicalJson(builder, jsonVectorOffset);
        var envelopeOffset = CanonicalEnvelope.EndCanonicalEnvelope(builder);

        // Finish the buffer
        CanonicalEnvelope.FinishCanonicalEnvelopeBuffer(builder, envelopeOffset);

        // Return the complete serialized buffer
        return builder.DataBuffer.ToSizedArray();
    }

    public byte[] Decode(byte[] encoded)
    {
        if (encoded == null || encoded.Length == 0)
            return new byte[0];

        try
        {
            // Create ByteBuffer from encoded data
            var byteBuffer = new ByteBuffer(encoded);

            // Get the root CanonicalEnvelope object
            var envelope = CanonicalEnvelope.GetRootAsCanonicalEnvelope(byteBuffer);

            // Extract the canonical_json field as byte array
            byte[] jsonArray = envelope.GetCanonicalJsonArray();

            return jsonArray ?? new byte[0];
        }
        catch
        {
            return new byte[0];
        }
    }
}
