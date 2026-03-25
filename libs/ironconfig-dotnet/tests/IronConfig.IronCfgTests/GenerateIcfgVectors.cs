using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using IronConfig.IronCfg;

namespace IronConfig.IronCfgTests;

/// <summary>
/// Generate native C test vectors for ICFG (using proven TestLargeConfiguration pattern)
/// </summary>
public class GenerateIcfgVectors
{
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "libs", "ironconfig-dotnet", "IronConfig.sln");
            if (File.Exists(sln))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root not found (expected libs/ironconfig-dotnet/IronConfig.sln).");
    }

    [Fact]
    public void GenerateAllVectors()
    {
        string baseDir = FindRepositoryRoot();
        string outputDir = Path.Combine(baseDir, "artifacts", "vectors", "v1", "icfg");
        Directory.CreateDirectory(outputDir);

        // Vector 1: Minimal - small config
        {
            Span<byte> buf = new byte[2048];
            var fields = new List<IronCfgField>();
            var rootFields = new SortedDictionary<uint, IronCfgValue?>();

            fields.Add(new IronCfgField { FieldId = 0, FieldName = "test_field", FieldType = 0x20, IsRequired = true });
            rootFields.Add(0, new IronCfgString { Value = "minimal" });

            var schema = new IronCfgSchema { Fields = fields };
            var root = new IronCfgObject { Fields = rootFields };

            var err = IronCfgEncoder.Encode(root, schema, true, false, buf, out int size);
            Assert.True(err.IsOk);
            File.WriteAllBytes(Path.Combine(outputDir, "01_minimal.bin"), buf.Slice(0, size).ToArray());
            Console.WriteLine($"✅ 01_minimal.bin ({size} bytes)");
        }

        // Vector 2: Single field with content
        {
            Span<byte> buf = new byte[2048];
            var fields = new List<IronCfgField>();
            var rootFields = new SortedDictionary<uint, IronCfgValue?>();

            fields.Add(new IronCfgField { FieldId = 1, FieldName = "single_field", FieldType = 0x20, IsRequired = true });
            rootFields.Add(1, new IronCfgString { Value = "This is a test string with content" });

            var schema = new IronCfgSchema { Fields = fields };
            var root = new IronCfgObject { Fields = rootFields };

            var err = IronCfgEncoder.Encode(root, schema, true, false, buf, out int size);
            Assert.True(err.IsOk);
            File.WriteAllBytes(Path.Combine(outputDir, "02_single_int.bin"), buf.Slice(0, size).ToArray());
            Console.WriteLine($"✅ 02_single_int.bin ({size} bytes)");
        }

        // Vector 3: Multiple fields with varying sizes
        {
            Span<byte> buf = new byte[16384];
            var fields = new List<IronCfgField>();
            var rootFields = new SortedDictionary<uint, IronCfgValue?>();

            // Create 5 fields with varying string sizes
            for (uint i = 0; i < 5; i++)
            {
                fields.Add(new IronCfgField { FieldId = i, FieldName = $"field_{i}", FieldType = 0x20, IsRequired = true });
                char fillChar = (char)('A' + (i % 26));
                var fieldValue = new string(fillChar, (int)(100 * (i + 1)));
                rootFields.Add(i, new IronCfgString { Value = fieldValue });
            }

            var schema = new IronCfgSchema { Fields = fields };
            var root = new IronCfgObject { Fields = rootFields };

            var err = IronCfgEncoder.Encode(root, schema, true, false, buf, out int size);
            Assert.True(err.IsOk);
            File.WriteAllBytes(Path.Combine(outputDir, "03_multi_field.bin"), buf.Slice(0, size).ToArray());
            Console.WriteLine($"✅ 03_multi_field.bin ({size} bytes)");
        }

        Console.WriteLine($"\n✅ All ICFG vectors generated successfully");
    }
}
