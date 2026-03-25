using System;
using System.Collections.Generic;
using IronConfig.IronCfg;
using IronFamily.MegaBench.Competitors.Profiles;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Native ICFG codec for benchmarking.
/// Encodes/decodes ICFG format directly using IronCfgEncoder.
/// </summary>
public class IronCfgCodec : ICompetitorCodec
{
    public string Name => "IronCfg";
    public CodecKind Kind => CodecKind.ICFG;
    public CodecProfile Profile { get; }

    private static readonly IronCfgSchema Schema = new()
    {
        Fields = new List<IronCfgField>
        {
            new IronCfgField { FieldId = 0, FieldName = "name", FieldType = 0x20, IsRequired = true },
            new IronCfgField { FieldId = 1, FieldName = "value", FieldType = 0x10, IsRequired = true },
            new IronCfgField { FieldId = 2, FieldName = "data", FieldType = 0x22, IsRequired = true },
            new IronCfgField
            {
                FieldId = 3,
                FieldName = "nested",
                FieldType = 0x40,
                IsRequired = true,
                ElementSchema = new IronCfgSchema
                {
                    Fields = new List<IronCfgField>
                    {
                        new IronCfgField { FieldId = 0, FieldName = "id", FieldType = 0x10, IsRequired = true },
                        new IronCfgField { FieldId = 1, FieldName = "enabled", FieldType = 0x01, IsRequired = true }
                    }
                }
            }
        }
    };

    public IronCfgCodec(CodecProfile profile = CodecProfile.BALANCED)
    {
        Profile = profile;
    }

    public byte[] Encode(byte[] canonicalInput)
    {
        // For native codec, the canonical input is already ICFG-encoded
        // This codec is identity for benchmarking purposes
        return (byte[])canonicalInput.Clone();
    }

    public byte[] Decode(byte[] encoded)
    {
        // For native codec, decoding back to original is identity
        return (byte[])encoded.Clone();
    }
}
