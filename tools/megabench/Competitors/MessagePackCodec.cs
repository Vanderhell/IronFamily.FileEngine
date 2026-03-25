using IronFamily.MegaBench.Competitors.Profiles;
using System;
using System.Collections.Generic;
using MessagePack;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// MessagePack binary format codec.
/// Encodes binary data with format metadata.
/// </summary>
public class MessagePackCodec : ICompetitorCodec
{
    public string Name => "MessagePack";
    public CodecKind Kind { get; }
    public CodecProfile Profile { get; }

    public MessagePackCodec(CodecKind kind, CodecProfile profile = CodecProfile.BALANCED)
    {
        Kind = kind;
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        var obj = new Dictionary<string, object>
        {
            ["format"] = Kind == CodecKind.ICFG ? "icfg_msgpack" : Kind == CodecKind.ILOG ? "ilog_msgpack" : "iupd_msgpack",
            ["data"] = canonicalInput
        };

        return MessagePackSerializer.Serialize(obj);
    }

    public byte[] Decode(byte[] encoded)
    {
        var obj = MessagePackSerializer.Deserialize<Dictionary<string, object>>(encoded);

        if (!obj.TryGetValue("data", out var dataObj))
            throw new InvalidOperationException("Missing 'data' field in MessagePack");

        if (dataObj is not byte[] data)
            throw new InvalidOperationException("'data' field is not bytes");

        return data;
    }
}
