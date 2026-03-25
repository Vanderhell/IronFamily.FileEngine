/// <summary>
/// ICFG Golden Vector Generator
/// Generates canonical test vectors for .NET vs C parity testing
/// </summary>

using System;
using System.Collections.Generic;
using System.IO;
using IronConfig.IronCfg;

class IcfgGoldenVectorGen
{
    static void Main()
    {
        string vectorDir = "artifacts/vectors/v1/icfg";
        Directory.CreateDirectory(vectorDir);

        // Vector 1: Simple flat config (single level object, scalar types)
        var flat = new IronCfgValue.Object(new SortedDictionary<uint, IronCfgValue?>
        {
            { 0, new IronCfgValue.Int64(-42) },
            { 1, new IronCfgValue.UInt64(123456789) },
            { 2, new IronCfgValue.Float64(3.14159) },
            { 3, new IronCfgValue.String("hello world") },
            { 4, new IronCfgValue.Bool(true) },
            { 5, new IronCfgValue.Bool(false) }
        });

        var flatSchema = new IronCfgSchema(new[]
        {
            new IronCfgFieldDef { FieldId = 0, TypeCode = 0x10, FieldName = "count" },
            new IronCfgFieldDef { FieldId = 1, TypeCode = 0x11, FieldName = "total" },
            new IronCfgFieldDef { FieldId = 2, TypeCode = 0x12, FieldName = "ratio" },
            new IronCfgFieldDef { FieldId = 3, TypeCode = 0x20, FieldName = "name" },
            new IronCfgFieldDef { FieldId = 4, TypeCode = 0x01, FieldName = "active" },
            new IronCfgFieldDef { FieldId = 5, TypeCode = 0x02, FieldName = "archived" }
        });

        // Vector 2: Nested config (object within object)
        var nested = new IronCfgValue.Object(new SortedDictionary<uint, IronCfgValue?>
        {
            { 0, new IronCfgValue.String("database") },
            { 1, new IronCfgValue.Object(new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgValue.String("localhost") },
                { 1, new IronCfgValue.UInt64(5432) }
            }) }
        });

        var nestedSchema = new IronCfgSchema(new[]
        {
            new IronCfgFieldDef { FieldId = 0, TypeCode = 0x20, FieldName = "type" },
            new IronCfgFieldDef { FieldId = 1, TypeCode = 0x40, FieldName = "config" }
        });

        // Vector 3: Array config (homogeneous array)
        var arrayConfig = new IronCfgValue.Object(new SortedDictionary<uint, IronCfgValue?>
        {
            { 0, new IronCfgValue.Array(new[] {
                new IronCfgValue.Int64(1),
                new IronCfgValue.Int64(2),
                new IronCfgValue.Int64(3)
            }) }
        });

        var arraySchema = new IronCfgSchema(new[]
        {
            new IronCfgFieldDef { FieldId = 0, TypeCode = 0x30, FieldName = "ids" }
        });

        // Vector 4: Binary payload
        var binaryConfig = new IronCfgValue.Object(new SortedDictionary<uint, IronCfgValue?>
        {
            { 0, new IronCfgValue.Bytes(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }) }
        });

        var binarySchema = new IronCfgSchema(new[]
        {
            new IronCfgFieldDef { FieldId = 0, TypeCode = 0x22, FieldName = "signature" }
        });

        // Generate and save vectors
        Console.WriteLine("Generating golden vectors...");

        GenerateVector(vectorDir, "01_flat.bin", flat, flatSchema, computeCrc32: true);
        GenerateVector(vectorDir, "02_nested.bin", nested, nestedSchema, computeCrc32: true);
        GenerateVector(vectorDir, "03_array.bin", arrayConfig, arraySchema, computeCrc32: true);
        GenerateVector(vectorDir, "04_binary.bin", binaryConfig, binarySchema, computeCrc32: true);

        Console.WriteLine($"✅ Golden vectors saved to {vectorDir}");
    }

    static void GenerateVector(string dir, string name, IronCfgValue root, IronCfgSchema schema, bool computeCrc32)
    {
        byte[] buffer = new byte[4096];
        var err = IronCfgEncoder.Encode(root, schema, computeCrc32, false, buffer, out int size);

        if (!err.IsOk)
        {
            Console.WriteLine($"❌ Failed to encode {name}: {err.Code}");
            return;
        }

        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, buffer[..size]);
        Console.WriteLine($"  ✓ {name} ({size} bytes)");
    }
}
