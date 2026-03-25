using IronFamily.MegaBench.Competitors.Profiles;
using System;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Compressors;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// ZIP compression codec for IUPD manifest.
/// Excluded in V1: requires streaming archive support.
/// </summary>
public class ZipManifestCodec : ICompetitorCodec
{
    public string Name => "ZIP";
    public CodecKind Kind => CodecKind.IUPD_Manifest;
    public CodecProfile Profile { get; }

    public ZipManifestCodec(CodecProfile profile = CodecProfile.BALANCED)
    {
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        throw new NotImplementedException(
            "ZIP codec requires streaming archive creation. " +
            "V2 candidate: use SharpCompress.Archives.Zip with in-memory streams.");
    }

    public byte[] Decode(byte[] encoded)
    {
        throw new NotImplementedException(
            "ZIP codec requires streaming archive extraction. " +
            "V2 candidate: use SharpCompress.Archives.Zip to read first entry.");
    }
}
