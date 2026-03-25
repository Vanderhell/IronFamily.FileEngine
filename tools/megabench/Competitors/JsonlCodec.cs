using IronFamily.MegaBench.Competitors.Profiles;
using System;
using System.Text;
using System.Text.Json;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Newline-delimited JSON codec for ILOG events.
/// For ICFG/IUPD: encodes binary as base64 with metadata.
/// </summary>
public class JsonlCodec : ICompetitorCodec
{
    public string Name => "JSONL";
    public CodecKind Kind { get; }
    public CodecProfile Profile { get; }

    public JsonlCodec(CodecKind kind, CodecProfile profile = CodecProfile.BALANCED)
    {
        Kind = kind;
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        if (Kind == CodecKind.ILOG)
        {
            throw new NotImplementedException("JSONL for ILOG requires parsing ILOG stream - excluded in V1");
        }

        // For ICFG and IUPD_Manifest: encode as base64 JSON object
        var dict = new System.Collections.Generic.Dictionary<string, object>
        {
            ["format"] = Kind == CodecKind.ICFG ? "icfg_jsonl" : "iupd_jsonl",
            ["data"] = Convert.ToBase64String(canonicalInput)
        };

        var json = JsonSerializer.Serialize(dict);
        return Encoding.UTF8.GetBytes(json + Environment.NewLine);
    }

    public byte[] Decode(byte[] encoded)
    {
        if (Kind == CodecKind.ILOG)
        {
            throw new NotImplementedException("JSONL for ILOG requires parsing ILOG stream - excluded in V1");
        }

        var json = Encoding.UTF8.GetString(encoded).TrimEnd();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var dataElem))
            throw new InvalidOperationException("Missing 'data' field in JSONL");

        var base64Data = dataElem.GetString();
        if (base64Data == null)
            throw new InvalidOperationException("'data' field is not a string");

        return Convert.FromBase64String(base64Data);
    }
}
