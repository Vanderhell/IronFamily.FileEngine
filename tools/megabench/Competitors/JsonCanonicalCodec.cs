using IronFamily.MegaBench.Competitors.Profiles;
using System;
using System.Text;
using System.Text.Json;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// JSON canonical codec using System.Text.Json for serialization.
/// Encodes binary as UTF-8 JSON with deterministic formatting (V3: real implementation).
/// </summary>
public class JsonCanonicalCodec : ICompetitorCodec
{
    public string Name => "JSON";
    public CodecKind Kind { get; }
    public CodecProfile Profile { get; }

    public JsonCanonicalCodec(CodecKind kind, CodecProfile profile = CodecProfile.BALANCED)
    {
        Kind = kind;
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        // Encode binary as base64 within JSON object
        var dict = new System.Collections.Generic.Dictionary<string, object>
        {
            ["format"] = Kind == CodecKind.ICFG ? "icfg_json" : Kind == CodecKind.ILOG ? "ilog_json" : "iupd_json",
            ["payload"] = Convert.ToBase64String(canonicalInput)
        };

        var options = new JsonSerializerOptions { WriteIndented = false };
        var json = JsonSerializer.Serialize(dict, options);
        return Encoding.UTF8.GetBytes(json);
    }

    public byte[] Decode(byte[] encoded)
    {
        var json = Encoding.UTF8.GetString(encoded);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("payload", out var payloadElem))
            throw new InvalidOperationException("JSON: missing 'payload' field");

        var base64Data = payloadElem.GetString();
        if (base64Data == null)
            throw new InvalidOperationException("JSON: 'payload' is not a string");

        return Convert.FromBase64String(base64Data);
    }
}
