using System;
using System.Collections.Generic;
using PeterO.Cbor;
using IronFamily.MegaBench.Competitors.Profiles;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// CBOR (Concise Binary Object Representation) codec.
/// Encodes binary data with format metadata.
/// </summary>
public class CborCodec : ICompetitorCodec
{
    public string Name => "CBOR";
    public CodecKind Kind { get; }
    public CodecProfile Profile { get; }

    public CborCodec(CodecKind kind, CodecProfile profile = CodecProfile.BALANCED)
    {
        Kind = kind;
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        var mapBuilder = CBORObject.NewMap();
        mapBuilder["format"] = CBORObject.FromObject(Kind == CodecKind.ICFG ? "icfg_cbor" : Kind == CodecKind.ILOG ? "ilog_cbor" : "iupd_cbor");
        mapBuilder["data"] = CBORObject.FromObject(canonicalInput);

        return mapBuilder.EncodeToBytes();
    }

    public byte[] Decode(byte[] encoded)
    {
        var obj = CBORObject.DecodeFromBytes(encoded);

        if (!obj.ContainsKey("data"))
            throw new InvalidOperationException("Missing 'data' field in CBOR");

        var dataObj = obj["data"];
        if (dataObj.Type != CBORType.ByteString)
            throw new InvalidOperationException("'data' field is not a byte string");

        return dataObj.GetByteString();
    }
}
