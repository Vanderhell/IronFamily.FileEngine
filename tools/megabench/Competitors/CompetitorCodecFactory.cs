using System;
using System.Collections.Generic;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Factory for creating competitor codec instances.
/// </summary>
public static class CompetitorCodecFactory
{
    /// <summary>
    /// Get all competitor codecs for a given format kind.
    /// </summary>
    public static IEnumerable<ICompetitorCodec> GetCodecsForKind(CodecKind kind)
    {
        yield return new JsonCanonicalCodec(kind);
        yield return new JsonlCodec(kind);
        yield return new MessagePackCodec(kind);
        yield return new CborCodec(kind);
        yield return new ProtobufCodec(kind);

        // V3: FlatBuffers is now implemented (real version)
        if (kind == CodecKind.ICFG)
        {
            yield return new FlatBuffersCodec(kind);
        }

        // IUPD only
        if (kind == CodecKind.IUPD_Manifest)
        {
            yield return new ZipManifestCodec();
        }
    }

    /// <summary>
    /// Get all available codecs across all kinds.
    /// </summary>
    public static IEnumerable<ICompetitorCodec> GetAllCodecs()
    {
        // ICFG
        foreach (var codec in GetCodecsForKind(CodecKind.ICFG))
            yield return codec;

        // ILOG
        foreach (var codec in GetCodecsForKind(CodecKind.ILOG))
            yield return codec;

        // IUPD
        foreach (var codec in GetCodecsForKind(CodecKind.IUPD_Manifest))
            yield return codec;
    }
}
