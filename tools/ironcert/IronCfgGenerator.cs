using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronConfig.IronCfg;

internal static class IronCfgGenerator
{
    private static readonly string TestVectorsDir = Path.Combine("vectors/small", "ironcfg");

    public static int Generate()
    {
        try
        {
            Directory.CreateDirectory(TestVectorsDir);
            Directory.CreateDirectory(Path.Combine(TestVectorsDir, "small"));
            Directory.CreateDirectory(Path.Combine(TestVectorsDir, "medium"));
            Directory.CreateDirectory(Path.Combine(TestVectorsDir, "large"));
            Directory.CreateDirectory(Path.Combine(TestVectorsDir, "mega"));

            var manifestEntries = new List<ManifestEntry>();

            // Generate small dataset
            var smallVectors = GenerateSmallDataset();
            manifestEntries.AddRange(SaveDataset("small", smallVectors));

            // Generate medium dataset
            var mediumVectors = GenerateMediumDataset();
            manifestEntries.AddRange(SaveDataset("medium", mediumVectors));

            // Generate large dataset
            var largeVectors = GenerateLargeDataset();
            manifestEntries.AddRange(SaveDataset("large", largeVectors));

            // Generate mega dataset
            var megaVectors = GenerateMegaDataset();
            manifestEntries.AddRange(SaveDataset("mega", megaVectors));

            // Update manifest
            UpdateManifest(manifestEntries);

            Console.WriteLine($"OK generated ironcfg golden vectors to {TestVectorsDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL generate ironcfg code=GENERATION_ERROR msg=\"{ex.Message}\"");
            return 1;
        }
    }

    private static List<DatasetVector> GenerateSmallDataset()
    {
        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new() { FieldId = 0, FieldName = "", FieldType = 0x11, IsRequired = true },      // uint64: no name (scalar)
                new() { FieldId = 1, FieldName = "name", FieldType = 0x20, IsRequired = true },  // string: has name
                new() { FieldId = 2, FieldName = "", FieldType = 0x01, IsRequired = true },      // bool: no name (scalar)
                new() { FieldId = 3, FieldName = "records", FieldType = 0x30, IsRequired = true } // array: has name
            }
        };

        var records = new List<IronCfgValue>
        {
            CreateObject(0, "config_a", true),
            CreateObject(1, "config_b", false),
            CreateObject(2, "config_c", true)
        };

        var recordsArray = CreateArrayValue(records);
        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?> { { 3, recordsArray } }
        };

        var sourceJson = new
        {
            description = "Small dataset (3 records, 3 fields)",
            records = new[]
            {
                new { id = 0UL, name = "config_a", enabled = true },
                new { id = 1UL, name = "config_b", enabled = false },
                new { id = 2UL, name = "config_c", enabled = true }
            }
        };

        return new List<DatasetVector>
        {
            new("small", sourceJson, schema, root, false),
            new("small_crc", sourceJson, schema, root, true),
        };
    }

    private static List<DatasetVector> GenerateMediumDataset()
    {
        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new() { FieldId = 0, FieldName = "", FieldType = 0x11, IsRequired = true },        // uint64: no name (scalar)
                new() { FieldId = 1, FieldName = "username", FieldType = 0x20, IsRequired = true }, // string: has name
                new() { FieldId = 2, FieldName = "email", FieldType = 0x20, IsRequired = true },    // string: has name
                new() { FieldId = 3, FieldName = "", FieldType = 0x10, IsRequired = true },        // int64: no name (scalar)
                new() { FieldId = 4, FieldName = "", FieldType = 0x01, IsRequired = true },        // bool: no name (scalar)
                new() { FieldId = 5, FieldName = "records", FieldType = 0x30, IsRequired = true }  // array: has name
            }
        };

        var records = new List<IronCfgValue>();
        for (uint i = 0; i < 10; i++)
        {
            records.Add(CreateUserObject(i, $"user_{i:D3}", $"user{i}@example.com", (long)(20 + i), i % 2 == 0));
        }

        var recordsArray = CreateArrayValue(records);
        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?> { { 5, recordsArray } }
        };

        var sourceJson = new
        {
            description = "Medium dataset (10 user records, 5 fields)",
            count = 10,
            records = records.Select((_, i) =>
            {
                var idx = (uint)i;
                return new { user_id = idx, username = $"user_{idx:D3}", email = $"user{idx}@example.com", age = 20L + idx, active = idx % 2 == 0 };
            }).ToList()
        };

        return new List<DatasetVector>
        {
            new("medium", sourceJson, schema, root, false),
            new("medium_crc", sourceJson, schema, root, true),
        };
    }

    private static List<DatasetVector> GenerateLargeDataset()
    {
        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new() { FieldId = 0, FieldName = "", FieldType = 0x11, IsRequired = true },           // uint64: no name (scalar)
                new() { FieldId = 1, FieldName = "sku", FieldType = 0x20, IsRequired = true },       // string: has name
                new() { FieldId = 2, FieldName = "name", FieldType = 0x20, IsRequired = true },      // string: has name
                new() { FieldId = 3, FieldName = "", FieldType = 0x12, IsRequired = true },          // float64: no name (scalar)
                new() { FieldId = 4, FieldName = "", FieldType = 0x11, IsRequired = true },          // uint64: no name (scalar)
                new() { FieldId = 5, FieldName = "category", FieldType = 0x20, IsRequired = true },  // string: has name
                new() { FieldId = 6, FieldName = "records", FieldType = 0x30, IsRequired = true }    // array: has name
            }
        };

        var records = new List<IronCfgValue>();
        for (uint i = 0; i < 100; i++)
        {
            records.Add(CreateProductObject(i, $"SKU{i:D5}", $"Product {i:D3}", 9.99 + (i * 0.5), i % 3 * 10 + 5, GetCategory(i)));
        }

        var recordsArray = CreateArrayValue(records);
        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?> { { 6, recordsArray } }
        };

        var sourceJson = new
        {
            description = "Large dataset (100 product records, 6 fields)",
            count = 100,
            categories = new[] { "Electronics", "Clothing", "Books" }
        };

        return new List<DatasetVector>
        {
            new("large", sourceJson, schema, root, false),
            new("large_crc", sourceJson, schema, root, true),
        };
    }

    private static List<DatasetVector> GenerateMegaDataset()
    {
        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new() { FieldId = 0, FieldName = "", FieldType = 0x11, IsRequired = true },      // uint64: no name (scalar)
                new() { FieldId = 1, FieldName = "", FieldType = 0x11, IsRequired = true },      // uint64: no name (scalar)
                new() { FieldId = 2, FieldName = "level", FieldType = 0x20, IsRequired = true }, // string: has name
                new() { FieldId = 3, FieldName = "message", FieldType = 0x20, IsRequired = true }, // string: has name
                new() { FieldId = 4, FieldName = "", FieldType = 0x12, IsRequired = true },      // float64: no name (scalar)
                new() { FieldId = 5, FieldName = "records", FieldType = 0x30, IsRequired = true } // array: has name
            }
        };

        var records = new List<IronCfgValue>();
        for (uint i = 0; i < 1000; i++)
        {
            var level = (i % 4) switch
            {
                0 => "DEBUG",
                1 => "INFO",
                2 => "WARN",
                _ => "ERROR"
            };
            var msg = $"Log entry {i:D5} - processing took {(i % 100 + 1) * 10}ms";
            records.Add(CreateLogObject(i, 1600000000UL + i * 1000, level, msg, 10.5 + (i % 50)));
        }

        var recordsArray = CreateArrayValue(records);
        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?> { { 5, recordsArray } }
        };

        var sourceJson = new
        {
            description = "Mega dataset (1000 log records, 5 fields)",
            count = 1000,
            levels = new[] { "DEBUG", "INFO", "WARN", "ERROR" }
        };

        return new List<DatasetVector>
        {
            new("mega", sourceJson, schema, root, false),
            new("mega_crc", sourceJson, schema, root, true),
        };
    }

    private static IronCfgValue CreateObject(ulong id, string name, bool enabled)
    {
        return new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgUInt64 { Value = id } },
                { 1, new IronCfgString { Value = name } },
                { 2, new IronCfgBool { Value = enabled } }
            }
        };
    }

    private static IronCfgValue CreateUserObject(uint userId, string username, string email, long age, bool active)
    {
        return new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgUInt64 { Value = userId } },
                { 1, new IronCfgString { Value = username } },
                { 2, new IronCfgString { Value = email } },
                { 3, new IronCfgInt64 { Value = age } },
                { 4, new IronCfgBool { Value = active } }
            }
        };
    }

    private static IronCfgValue CreateProductObject(uint productId, string sku, string name, double price, ulong stock, string category)
    {
        return new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgUInt64 { Value = productId } },
                { 1, new IronCfgString { Value = sku } },
                { 2, new IronCfgString { Value = name } },
                { 3, new IronCfgFloat64 { Value = price } },
                { 4, new IronCfgUInt64 { Value = stock } },
                { 5, new IronCfgString { Value = category } }
            }
        };
    }

    private static IronCfgValue CreateLogObject(uint logId, ulong timestamp, string level, string message, double durationMs)
    {
        return new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgUInt64 { Value = logId } },
                { 1, new IronCfgUInt64 { Value = timestamp } },
                { 2, new IronCfgString { Value = level } },
                { 3, new IronCfgString { Value = message } },
                { 4, new IronCfgFloat64 { Value = durationMs } }
            }
        };
    }

    private static IronCfgValue CreateArrayValue(List<IronCfgValue> items)
    {
        return new IronCfgArray { Elements = items.Cast<IronCfgValue?>().ToList() };
    }

    private static string GetCategory(uint index)
    {
        return (index % 3) switch
        {
            0 => "Electronics",
            1 => "Clothing",
            _ => "Books"
        };
    }

    private static List<ManifestEntry> SaveDataset(string datasetName, List<DatasetVector> vectors)
    {
        var entries = new List<ManifestEntry>();
        var datasetDir = Path.Combine(TestVectorsDir, datasetName);

        foreach (var vector in vectors)
        {
            var id = vector.Id;
            var variantSuffix = vector.UseCrc ? "_crc" : "";
            var jsonPath = Path.Combine(datasetDir, $"golden{variantSuffix}.json");
            var binPath = Path.Combine(datasetDir, $"golden{variantSuffix}.icfg");

            // Save JSON source
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(vector.SourceJson, jsonOptions));

            // Encode to binary
            Span<byte> buffer = new byte[10 * 1024 * 1024]; // 10MB max
            var encodeErr = IronCfgEncoder.Encode(vector.Root, vector.Schema, vector.UseCrc, false, buffer, out int size);

            if (!encodeErr.IsOk)
            {
                throw new Exception($"Failed to encode {id}: {encodeErr.Code} at offset {encodeErr.Offset}");
            }

            if (size < 64)
            {
                throw new Exception($"Encoded file {id} too small ({size} bytes) - likely missing data section");
            }

            var binData = buffer.Slice(0, size).ToArray();
            var dataSize = BitConverter.ToUInt32(binData.AsSpan(32, 4));
            if (dataSize == 0)
            {
                throw new Exception($"Data section is empty for {id} - encoder not writing data");
            }

            File.WriteAllBytes(binPath, binData);

            entries.Add(new ManifestEntry
            {
                Id = id,
                Bin = binPath,
                Crc = vector.UseCrc,
                Expect = "OK",
                Note = $"{datasetName.ToUpper()} golden vector - deterministic"
            });

            Console.WriteLine($"Generated {id}: {binPath} ({size} bytes)");
        }

        return entries;
    }

    private static void UpdateManifest(List<ManifestEntry> ironCfgEntries)
    {
        var manifestPath = Path.Combine("vectors/small", "manifest.json");
        var manifest = new Dictionary<string, object>();

        if (File.Exists(manifestPath))
        {
            using var fs = File.OpenRead(manifestPath);
            var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;

            if (root.TryGetProperty("engines", out var engines))
            {
                foreach (var prop in engines.EnumerateObject())
                {
                    var deserialized = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    if (deserialized != null)
                    {
                        manifest[prop.Name] = deserialized;
                    }
                }
            }
        }

        // Create or update IRONCFG engine entry
        var ironCfgVectors = new
        {
            magic = "ICFG",
            vectors = ironCfgEntries.Cast<object>().ToList()
        };

        var engineDicts = new Dictionary<string, object>(manifest)
        {
            ["ironcfg"] = ironCfgVectors
        };

        var fullManifest = new { engines = engineDicts };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(fullManifest, options));
    }

    private class DatasetVector
    {
        public string Id { get; }
        public object SourceJson { get; }
        public IronCfgSchema Schema { get; }
        public IronCfgValue Root { get; }
        public bool UseCrc { get; }

        public DatasetVector(string id, object sourceJson, IronCfgSchema schema, IronCfgValue root, bool useCrc)
        {
            Id = id;
            SourceJson = sourceJson;
            Schema = schema;
            Root = root;
            UseCrc = useCrc;
        }
    }

    private class ManifestEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("bin")]
        public string Bin { get; set; } = string.Empty;

        [JsonPropertyName("crc")]
        public bool Crc { get; set; }

        [JsonPropertyName("expect")]
        public string Expect { get; set; } = string.Empty;

        [JsonPropertyName("note")]
        public string Note { get; set; } = string.Empty;
    }
}
