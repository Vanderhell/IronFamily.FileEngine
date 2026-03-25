using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronConfig.Icxs;

/// <summary>
/// Field data type in ICXS schema
/// </summary>
public enum IcxsFieldType : byte
{
    Int64 = 1,
    UInt64 = 2,
    Float64 = 3,
    Bool = 4,
    String = 5,
}

/// <summary>
/// Schema field metadata
/// </summary>
public class IcxsField
{
    [JsonPropertyName("id")]
    public uint Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>
    /// Get byte size for fixed-region storage
    /// </summary>
    public int GetFixedSize()
    {
        return Type switch
        {
            "i64" => 8,
            "u64" => 8,
            "f64" => 8,
            "bool" => 1,
            "str" => 4, // Offset u32
            _ => throw new InvalidOperationException($"Unknown type: {Type}")
        };
    }

    /// <summary>
    /// Check if this is a variable-size field
    /// </summary>
    public bool IsVariable => Type == "str";

    /// <summary>
    /// Convert type string to IcxsFieldType enum
    /// </summary>
    public IcxsFieldType ToFieldType()
    {
        return Type switch
        {
            "i64" => IcxsFieldType.Int64,
            "u64" => IcxsFieldType.UInt64,
            "f64" => IcxsFieldType.Float64,
            "bool" => IcxsFieldType.Bool,
            "str" => IcxsFieldType.String,
            _ => throw new InvalidOperationException($"Unknown type: {Type}")
        };
    }
}

/// <summary>
/// ICXS schema definition
/// </summary>
public class IcxsSchema
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("fields")]
    public List<IcxsField> Fields { get; set; } = new();

    /// <summary>
    /// Load schema from JSON file
    /// </summary>
    public static IcxsSchema LoadFromFile(string path)
    {
        var json = System.IO.File.ReadAllText(path);
        return LoadFromJson(json);
    }

    /// <summary>
    /// Load schema from JSON string
    /// </summary>
    public static IcxsSchema LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var schema = JsonSerializer.Deserialize<IcxsSchema>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize schema");

        // Validate
        ValidateSchema(schema);
        return schema;
    }

    /// <summary>
    /// Validate schema consistency
    /// </summary>
    private static void ValidateSchema(IcxsSchema schema)
    {
        if (string.IsNullOrWhiteSpace(schema.Name))
            throw new InvalidOperationException("Schema name is required");

        if (schema.Fields == null || schema.Fields.Count == 0)
            throw new InvalidOperationException("Schema must have at least one field");

        var ids = new HashSet<uint>();
        var names = new HashSet<string>();

        foreach (var field in schema.Fields)
        {
            if (field.Id == 0)
                throw new InvalidOperationException("Field ID must be > 0");

            if (ids.Contains(field.Id))
                throw new InvalidOperationException($"Duplicate field ID: {field.Id}");
            ids.Add(field.Id);

            if (string.IsNullOrWhiteSpace(field.Name))
                throw new InvalidOperationException("Field name is required");

            if (names.Contains(field.Name))
                throw new InvalidOperationException($"Duplicate field name: {field.Name}");
            names.Add(field.Name);

            if (string.IsNullOrWhiteSpace(field.Type))
                throw new InvalidOperationException($"Field {field.Name} missing type");

            // Validate type
            _ = field.ToFieldType();
        }
    }

    /// <summary>
    /// Get canonical JSON representation (no whitespace, sorted keys)
    /// </summary>
    public string ToCanonicalJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(this, options);

        // Ensure no whitespace and parse/reserialize to guarantee sorted keys
        var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc, options);
    }

    /// <summary>
    /// Compute deterministic schema hash (SHA-256, first 16 bytes)
    /// </summary>
    public byte[] ComputeHash()
    {
        var canonical = ToCanonicalJson();
        var bytes = Encoding.UTF8.GetBytes(canonical);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        // Return first 16 bytes
        var result = new byte[16];
        Array.Copy(hash, result, 16);
        return result;
    }

    /// <summary>
    /// Get field by ID
    /// </summary>
    public IcxsField? GetFieldById(uint id)
    {
        return Fields.FirstOrDefault(f => f.Id == id);
    }

    /// <summary>
    /// Get field by name
    /// </summary>
    public IcxsField? GetFieldByName(string name)
    {
        return Fields.FirstOrDefault(f => f.Name == name);
    }

    /// <summary>
    /// Get fields sorted by ID (ascending)
    /// </summary>
    public IEnumerable<IcxsField> GetFieldsSorted()
    {
        return Fields.OrderBy(f => f.Id);
    }

    /// <summary>
    /// Calculate fixed record stride (total bytes for fixed-size fields)
    /// </summary>
    public uint CalculateRecordStride()
    {
        uint stride = 0;
        foreach (var field in GetFieldsSorted())
        {
            stride += (uint)field.GetFixedSize();
        }
        return stride;
    }

    /// <summary>
    /// Extract schema from embedded ICXS schema block (self-contained mode)
    /// </summary>
    public static IcxsSchema ExtractFromEmbedded(byte[] buffer, uint schemaBlockOffset)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        if (schemaBlockOffset >= buffer.Length)
            throw new InvalidOperationException("Schema block offset out of bounds");

        // Read field count (varint format)
        uint fieldCount = ReadVarUInt(buffer, schemaBlockOffset, out uint varIntLen);

        if (fieldCount == 0)
            throw new InvalidOperationException("Schema has zero fields");

        var fields = new List<IcxsField>();
        uint offset = schemaBlockOffset + varIntLen;

        for (uint i = 0; i < fieldCount; i++)
        {
            // Read fieldId (u32 LE)
            if (offset + 4 > buffer.Length)
                throw new InvalidOperationException("Schema block truncated (fieldId)");

            uint fieldId = ReadUInt32LE(buffer, offset);
            offset += 4;

            // Read fieldType (u8)
            if (offset >= buffer.Length)
                throw new InvalidOperationException("Schema block truncated (fieldType)");

            byte fieldTypeByte = buffer[offset];
            offset += 1;

            // Convert type byte to string
            string typeString = fieldTypeByte switch
            {
                1 => "i64",
                2 => "u64",
                3 => "f64",
                4 => "bool",
                5 => "str",
                _ => throw new InvalidOperationException($"Unknown field type: {fieldTypeByte}")
            };

            // Create field with synthetic name (embedded schema doesn't store names)
            var field = new IcxsField
            {
                Id = fieldId,
                Name = $"field_{fieldId}",  // Synthetic name for introspection
                Type = typeString
            };

            fields.Add(field);
        }

        // Sort fields by ID to match expected order
        fields.Sort((a, b) => a.Id.CompareTo(b.Id));

        // Create schema with synthetic name
        var schema = new IcxsSchema
        {
            Name = "EmbeddedSchema",
            Version = 1,
            Fields = fields
        };

        return schema;
    }

    /// <summary>
    /// Helper: Read little-endian u32 from buffer
    /// </summary>
    private static uint ReadUInt32LE(byte[] buffer, uint offset)
    {
        return (uint)(
            buffer[(int)offset] |
            (buffer[(int)(offset + 1)] << 8) |
            (buffer[(int)(offset + 2)] << 16) |
            (buffer[(int)(offset + 3)] << 24)
        );
    }

    /// <summary>
    /// Helper: Read varint-encoded u32 from buffer
    /// </summary>
    private static uint ReadVarUInt(byte[] buffer, uint offset, out uint bytesRead)
    {
        uint value = 0;
        int shift = 0;
        bytesRead = 0;

        while (offset + bytesRead < buffer.Length && bytesRead < 5)
        {
            byte b = buffer[(int)(offset + bytesRead)];
            bytesRead++;

            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
            shift += 7;
        }

        throw new InvalidOperationException("Invalid varint format in schema block");
    }
}
