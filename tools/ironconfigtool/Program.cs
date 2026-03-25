using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IronConfig;
using IronConfig.Crypto;
using IronConfig.Icxs;

namespace IronConfigTool;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("IronConfig Engine v0.1.0");

        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        try
        {
            string command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "pack":
                case "encode":
                    CmdPack(args);
                    break;

                case "packx":
                    CmdPackX(args);
                    break;

                case "packxs":
                    CmdPackXs(args);
                    break;

                case "pack2":
                    CmdPack2(args);
                    break;

                case "validate2":
                    CmdValidate2(args);
                    break;

                case "validate":
                    CmdValidate(args);
                    break;

                case "dump":
                    CmdDump(args);
                    break;

                case "tojson":
                case "decode":
                    CmdToJson(args);
                    break;

                case "stats":
                    CmdStats(args);
                    break;

                case "encrypt":
                case "encode-secure":
                    CmdEncrypt(args);
                    break;

                case "decrypt":
                    CmdDecrypt(args);
                    break;

                case "bench":
                    CmdBench(args);
                    break;

                case "bench2":
                    CmdBench2(args);
                    break;

                case "printcrc":
                    CmdPrintCrc(args);
                    break;

                case "printicf2strings":
                    CmdPrintIcf2Strings(args);
                    break;

                case "help":
                case "-h":
                case "--help":
                    PrintUsage();
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static void CmdPack(string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException("pack: usage pack <input.json> <output.bjv> [options]");

        string inputJson = args[1];
        string outputBjv = args[2];

        if (!File.Exists(inputJson))
            throw new FileNotFoundException($"Input file not found: {inputJson}");

        string jsonText = File.ReadAllText(inputJson);
        var jsonDoc = JsonDocument.Parse(jsonText);

        bool isBjv4 = HasOption(args, "--keyid", "32");
        bool useVsp = !HasOption(args, "--vsp", "off");
        bool useCrc = HasOption(args, "--crc", "on");

        var bjvNode = JsonConverter.FromJson(jsonDoc.RootElement);
        var encoder = new BjvEncoder(isBjv4, useVsp, useCrc);
        var result = encoder.EncodeWithStats(bjvNode);

        File.WriteAllBytes(outputBjv, result.Data);

        Console.WriteLine($"✓ Packed {inputJson} to {outputBjv}");
        Console.WriteLine($"  Size: {result.FileSize} bytes");
        Console.WriteLine($"  Dictionary: {result.DictionaryCount} keys");
        Console.WriteLine($"  VSP: {result.VspCount} strings");

        if (HasFlag(args, "--stats"))
        {
            Console.WriteLine($"  Compression: {(double)result.FileSize / jsonText.Length:P1}");
        }
    }

    static void CmdPackX(string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException("packx: usage packx <input.json> <output.icfx> [--crc on/off] [--vsp on/off] [--index off/on/auto]");

        string inputJson = args[1];
        string outputIcfx = args[2];

        if (!File.Exists(inputJson))
            throw new FileNotFoundException($"Input file not found: {inputJson}");

        string jsonText = File.ReadAllText(inputJson);
        var jsonDoc = JsonDocument.Parse(jsonText);

        bool useVsp = !HasOption(args, "--vsp", "off");
        bool useCrc = HasOption(args, "--crc", "on");
        string indexMode = ParseIndexMode(args);

        var bjvNode = JsonConverter.FromJson(jsonDoc.RootElement);
        var encoder = new IronConfig.Icfx.IcfxEncoder(useVsp, useCrc, indexMode);
        byte[] data = encoder.Encode(bjvNode);

        File.WriteAllBytes(outputIcfx, data);

        Console.WriteLine($"✓ Packed {inputJson} to {outputIcfx}");
        Console.WriteLine($"  Size: {data.Length} bytes");
        Console.WriteLine($"  Dictionary: {encoder.Dictionary.Count} keys");
        Console.WriteLine($"  VSP: {encoder.Vsp.Count} strings");
        if (indexMode != "off")
        {
            Console.WriteLine($"  Indexing: {indexMode}");
        }

        if (HasFlag(args, "--stats"))
        {
            Console.WriteLine($"  Compression: {(double)data.Length / jsonText.Length:P1}");
        }
    }

    static void CmdPackXs(string[] args)
    {
        if (args.Length < 4)
            throw new ArgumentException("packxs: usage packxs <schema.json> <input.json> <output.icxs> [--crc on/off]");

        string schemaPath = args[1];
        string inputJson = args[2];
        string outputIcxs = args[3];

        if (!File.Exists(schemaPath))
            throw new FileNotFoundException($"Schema file not found: {schemaPath}");

        if (!File.Exists(inputJson))
            throw new FileNotFoundException($"Input file not found: {inputJson}");

        // Load schema
        var schema = IcxsSchema.LoadFromFile(schemaPath);

        // Load input JSON (must be array of objects)
        string jsonText = File.ReadAllText(inputJson);
        var jsonDoc = JsonDocument.Parse(jsonText);

        if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Input JSON must be an array of objects");

        bool useCrc = HasOption(args, "--crc", "on");

        // Encode using schema
        var encoder = new IcxsEncoder(schema, useCrc);
        byte[] data = encoder.Encode(jsonDoc.RootElement);

        File.WriteAllBytes(outputIcxs, data);

        Console.WriteLine($"✓ Packed {inputJson} to {outputIcxs}");
        Console.WriteLine($"  Size: {data.Length} bytes");
        Console.WriteLine($"  Schema: {schema.Name} (v{schema.Version})");
        Console.WriteLine($"  Fields: {schema.Fields.Count}");
        if (useCrc)
        {
            Console.WriteLine($"  CRC: enabled");
        }

        if (HasFlag(args, "--stats"))
        {
            Console.WriteLine($"  Compression: {(double)data.Length / jsonText.Length:P1}");
        }
    }

    static void CmdPack2(string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException("pack2: usage pack2 <input.json> <output.icf2> [--crc on/off] [--blake3 on/off]");

        string inputJson = args[1];
        string outputIcf2 = args[2];

        if (!File.Exists(inputJson))
            throw new FileNotFoundException($"Input file not found: {inputJson}");

        string jsonText = File.ReadAllText(inputJson);
        var jsonDoc = JsonDocument.Parse(jsonText);

        if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Input JSON must be an array of objects");

        bool useCrc = HasOption(args, "--crc", "on");
        bool useBlake3 = HasOption(args, "--blake3", "on");

        var encoder = new IronConfig.Icf2.Icf2Encoder(useCrc, useBlake3);
        byte[] data = encoder.Encode(jsonDoc.RootElement);

        File.WriteAllBytes(outputIcf2, data);

        Console.WriteLine($"✓ Packed {inputJson} to {outputIcf2}");
        Console.WriteLine($"  Size: {data.Length} bytes");
        if (useCrc)
        {
            Console.WriteLine($"  CRC: enabled");
        }
        if (useBlake3)
        {
            Console.WriteLine($"  BLAKE3: enabled");
        }

        if (HasFlag(args, "--stats"))
        {
            Console.WriteLine($"  Compression: {(double)data.Length / jsonText.Length:P1}");
        }
    }

    static void CmdValidate2(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("validate2: usage validate2 <file.icf2>");

        string filePath = args[1];

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        byte[] data = File.ReadAllBytes(filePath);

        try
        {
            var view = IronConfig.Icf2.Icf2View.Open(data);
            Console.WriteLine($"✓ ICF2 valid");
            Console.WriteLine($"  Rows: {view.RowCount}");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("CRC"))
        {
            Console.WriteLine($"✗ FAIL: {ex.Message}");
            throw;
        }
    }

    static void CmdValidate(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("validate: usage validate <file.bjv|file.bjx|file.icfx|file.icxs> [options]");

        string filePath = args[1];

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        byte[] data = File.ReadAllBytes(filePath);

        try
        {
            if (IsIcxs(data))
            {
                ValidateIcxs(data);
            }
            else if (IsIcfx(data))
            {
                ValidateIcfx(data);
            }
            else if (IsBjx(data))
            {
                string? password = ExtractPassword(args);
                byte[] plainBjv = Bjx.DecryptBjx1(data, password, null);
                Console.WriteLine($"✓ BJX decrypted successfully ({plainBjv.Length} bytes)");
                ValidateBjv(plainBjv);
            }
            else
            {
                ValidateBjv(data);
            }

            Console.WriteLine("✓ File is valid");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("CRC"))
        {
            Console.WriteLine($"✗ FAIL: {ex.Message}");
            throw;
        }
    }

    static void CmdDump(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("dump: usage dump <file.bjv|file.bjx> [options]");

        string filePath = args[1];

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        byte[] data = File.ReadAllBytes(filePath);

        if (IsBjx(data))
        {
            string? password = ExtractPassword(args);
            data = Bjx.DecryptBjx1(data, password, null);
        }

        try
        {
            var doc = BjvDocument.Parse(data);
            Console.WriteLine($"BJV Version: BJV{doc.Version}");
            Console.WriteLine($"CRC: {(doc.HasCrc ? "present" : "absent")}");
            Console.WriteLine($"VSP: {(doc.HasVsp ? "present" : "absent")}");
            Console.WriteLine($"Dictionary keys: {doc.Dictionary.Count}");
            Console.WriteLine($"Root type: {doc.Root.Type}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse: {ex.Message}");
        }
    }

    static void CmdToJson(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("tojson: usage tojson <file.bjv|file.bjx|file.icfx|file.icxs> [--out output.json] [options]");

        string filePath = args[1];

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        byte[] data = File.ReadAllBytes(filePath);

        string jsonOutput;

        if (IsIcxs(data))
        {
            // For ICXS, need to find and load schema file
            string? schemaPath = ExtractOption(args, "--schema");
            if (schemaPath == null)
            {
                // Try to find schema in same directory with .schema.json extension
                string baseName = Path.GetFileNameWithoutExtension(filePath);
                schemaPath = Path.Combine(Path.GetDirectoryName(filePath) ?? ".", baseName + ".schema.json");
                if (!File.Exists(schemaPath))
                    throw new FileNotFoundException($"Schema file not found. Use --schema <schema.json> or place {baseName}.schema.json in same directory");
            }

            var schema = IcxsSchema.LoadFromFile(schemaPath);
            var view = new IcxsView(data, schema);
            jsonOutput = ConvertIcxsToJson(view);
        }
        else if (IsIcfx(data))
        {
            var view = new IronConfig.Icfx.IcfxView(data);
            jsonOutput = ConvertIcfxToJson(view.Root);
        }
        else
        {
            if (IsBjx(data))
            {
                string? password = ExtractPassword(args);
                data = Bjx.DecryptBjx1(data, password, null);
            }

            var doc = BjvDocument.Parse(data);
            jsonOutput = JsonConverter.ToJson(ConvertFromBjv(doc.Root, doc));
        }

        string? outputPath = null;
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--out")
                outputPath = args[i + 1];
        }

        if (outputPath != null)
        {
            File.WriteAllText(outputPath, jsonOutput);
            Console.WriteLine($"✓ JSON written to {outputPath}");
        }
        else
        {
            Console.WriteLine(jsonOutput);
        }
    }

	static void CmdStats(string[] args)
	{
		if (args.Length < 2)
			throw new ArgumentException("stats: usage stats <file.bjv|file.bjx> [options]");

		string filePath = args[1];

		if (!File.Exists(filePath))
			throw new FileNotFoundException($"File not found: {filePath}");

		byte[] fileBytes = File.ReadAllBytes(filePath);
		byte[] data = fileBytes;

		if (IsBjx(data))
		{
			string? password = ExtractPassword(args);
			data = Bjx.DecryptBjx1(data, password, null);
		}

		Console.WriteLine($"On-disk size: {fileBytes.Length} bytes");
		Console.WriteLine($"Plain size:   {data.Length} bytes");

		if (IsBjx(fileBytes))
			Console.WriteLine($"Overhead:     {(fileBytes.Length - data.Length)} bytes");

		try
		{
			var doc = BjvDocument.Parse(data);
			Console.WriteLine($"Dictionary: {doc.Dictionary.Count} keys");
			Console.WriteLine($"VSP: {doc.Vsp.Count} strings");
		}
		catch { }
	}


    static void CmdEncrypt(string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException("encrypt: usage encrypt <input.bjv> <output.bjx> --password <pass> | --keyhex <hex>");

        string inputBjv = args[1];
        string outputBjx = args[2];

        if (!File.Exists(inputBjv))
            throw new FileNotFoundException($"Input file not found: {inputBjv}");

        byte[] plainBjv = File.ReadAllBytes(inputBjv);

        string? password = ExtractPassword(args);
		byte[] encryptedData = Bjx.EncryptBjx1(plainBjv, password, null);
		File.WriteAllBytes(outputBjx, encryptedData);
		Console.WriteLine($"✓ Encrypted {inputBjv} to {outputBjx} ({encryptedData.Length} bytes)");
    }

    static void CmdDecrypt(string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException("decrypt: usage decrypt <input.bjx> <output.bjv> --password <pass> | --keyhex <hex> [--validate]");

        string inputBjx = args[1];
        string outputBjv = args[2];

        if (!File.Exists(inputBjx))
            throw new FileNotFoundException($"Input file not found: {inputBjx}");

        byte[] bjxData = File.ReadAllBytes(inputBjx);
        string? password = ExtractPassword(args);

        byte[] plainBjv = Bjx.DecryptBjx1(bjxData, password, null);

        if (HasFlag(args, "--validate"))
        {
            try
            {
                BjvDocument.Parse(plainBjv);
                Console.WriteLine("✓ Decrypted BJV is valid");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"✗ Decrypted BJV is invalid: {ex.Message}");
            }
        }

        File.WriteAllBytes(outputBjv, plainBjv);
        Console.WriteLine($"✓ Decrypted {inputBjx} to {outputBjv} ({plainBjv.Length} bytes)");
    }

    static void ValidateBjv(byte[] data)
    {
        try
        {
            var doc = BjvDocument.Parse(data);
            Console.WriteLine($"✓ BJV{doc.Version} valid");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid BJV: {ex.Message}");
        }
    }

    static bool IsIcxs(byte[] data)
    {
        return data.Length >= 4 && data[0] == 'I' && data[1] == 'C' && data[2] == 'X' && data[3] == 'S';
    }

    static bool IsIcfx(byte[] data)
    {
        return data.Length >= 4 && data[0] == 'I' && data[1] == 'C' && data[2] == 'F' && data[3] == 'X';
    }

    static bool IsIcf2(byte[] data)
    {
        return data.Length >= 4 && data[0] == 'I' && data[1] == 'C' && data[2] == 'F' && data[3] == '2';
    }

    static void ValidateIcxs(byte[] data)
    {
        try
        {
            // Note: Without access to the schema, we can only validate the header structure
            // A proper ICXS validation requires the schema file
            if (!IcxsHeader.TryParse(data, out var header))
                throw new InvalidOperationException("Invalid ICXS header");

            if (!header.ValidateOffsets((uint)data.Length))
                throw new InvalidOperationException("Invalid ICXS offsets");

            Console.WriteLine($"✓ ICXS valid (header size: {IcxsHeader.HEADER_SIZE}, file size: {data.Length})");
            Console.WriteLine($"  Schema hash: {BitConverter.ToString(header.SchemaHash).Replace("-", "").ToLower()}");
            Console.WriteLine($"  CRC: {(header.HasCrc ? "present" : "absent")}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid ICXS: {ex.Message}");
        }
    }

    static void ValidateIcfx(byte[] data)
    {
        try
        {
            var view = new IronConfig.Icfx.IcfxView(data);
            Console.WriteLine($"✓ ICFX valid (header size: {view.Header.HeaderSize}, file size: {view.Header.TotalFileSize})");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid ICFX: {ex.Message}");
        }
    }

    // Convert ICXS to JSON array
    static string ConvertIcxsToJson(IcxsView view)
    {
        var items = new List<string>();

        foreach (var record in view.EnumerateRecords())
        {
            var obj = new Dictionary<string, string>();

            foreach (var field in view.Schema.GetFieldsSorted())
            {
                string? value = null;

                if (field.Type == "i64" && record.TryGetInt64(field.Id, out long i64Val))
                    value = i64Val.ToString();
                else if (field.Type == "u64" && record.TryGetUInt64(field.Id, out ulong u64Val))
                    value = u64Val.ToString();
                else if (field.Type == "f64" && record.TryGetFloat64(field.Id, out double f64Val))
                    value = f64Val.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
                else if (field.Type == "bool" && record.TryGetBool(field.Id, out bool boolVal))
                    value = boolVal ? "true" : "false";
                else if (field.Type == "str" && record.TryGetString(field.Id, out string? strVal))
                    value = JsonSerializer.Serialize(strVal);

                if (value != null)
                    obj[field.Name] = value;
            }

            // Build JSON object string
            var objEntries = obj.Select(kv => $"\"{kv.Key}\":{kv.Value}");
            items.Add("{" + string.Join(",", objEntries) + "}");
        }

        return "[" + string.Join(",", items) + "]";
    }

    // Depth tracking for ICFX conversion (prevent stack overflow)
    static string ConvertIcfxToJson(IronConfig.Icfx.IcfxValueView value)
    {
        return ConvertIcfxToJsonImpl(value, 0);
    }

    static string ConvertIcfxToJsonImpl(IronConfig.Icfx.IcfxValueView value, int depth)
    {
        const int MAX_DEPTH = 256;
        if (depth > MAX_DEPTH)
            throw new InvalidOperationException($"ICFX max recursion depth ({MAX_DEPTH}) exceeded");

        byte typeByte = value.TypeByte;

        if (value.IsNull)
            return "null";

        if (typeByte == 0x01 || typeByte == 0x02)
        {
            bool? bval = value.GetBool();
            return bval.HasValue ? (bval.Value ? "true" : "false") : "null";
        }

        if (typeByte == 0x10)
        {
            long? ival = value.GetInt64();
            return ival?.ToString() ?? "null";
        }

        if (typeByte == 0x11)
        {
            ulong? uval = value.GetUInt64();
            return uval?.ToString() ?? "null";
        }

        if (typeByte == 0x12)
        {
            double? dval = value.GetFloat64();
            if (dval.HasValue)
                return dval.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
            return "null";
        }

        if (typeByte == 0x20 || typeByte == 0x22)
        {
            string str = value.GetString();
            return JsonSerializer.Serialize(str);
        }

        if (typeByte == 0x30) // Array
        {
            var arr = value.GetArray();
            if (arr == null)
                return "[]";

            var items = new List<string>();
            uint elemIndex = 0;
            foreach (var elem in arr.Value)
            {
                if (elemIndex > 100000) // Sanity check for runaway arrays
                    throw new InvalidOperationException("ICFX array has excessive elements (>100000)");
                items.Add(ConvertIcfxToJsonImpl(elem, depth + 1));
                elemIndex++;
            }
            return "[" + string.Join(",", items) + "]";
        }

        if (typeByte == 0x40 || typeByte == 0x41) // Object (0x40) or IndexedObject (0x41)
        {
            var obj = value.GetObject();
            if (obj == null)
                return "{}";

            var items = new List<string>();
            uint fieldCount = 0;
            foreach (var (key, val) in obj.Value)
            {
                if (fieldCount > 100000) // Sanity check for runaway objects
                    throw new InvalidOperationException("ICFX object has excessive fields (>100000)");
                string jsonVal = ConvertIcfxToJsonImpl(val, depth + 1);
                items.Add($"\"{key}\":{jsonVal}");
                fieldCount++;
            }
            return "{" + string.Join(",", items) + "}";
        }

        return "null";
    }

    static bool IsBjx(byte[] data)
    {
        return data.Length >= 4 && data[0] == 'B' && data[1] == 'J' && data[2] == 'X' && data[3] == '1';
    }

    static string? ExtractPassword(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--password")
                return args[i + 1];
        }
        return null;
    }

    static string? ExtractOption(string[] args, string option)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == option)
                return args[i + 1];
        }
        return null;
    }

    static bool HasFlag(string[] args, string flag)
    {
        return Array.IndexOf(args, flag) >= 0;
    }

    static bool HasOption(string[] args, string option, string expectedValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == option && args[i + 1] == expectedValue)
                return true;
        }
        return false;
    }

    static string ParseIndexMode(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--index")
            {
                string value = args[i + 1].ToLowerInvariant();
                if (value == "off" || value == "on" || value == "auto")
                    return value;
                throw new ArgumentException($"Invalid --index value: {value}. Must be: off, on, or auto");
            }
        }
        // Default to "off" if not specified
        return "off";
    }

    static BjvValueNode ConvertFromBjv(BjvValue bjvVal, BjvDocument? doc = null)
    {
        return bjvVal.Type switch
        {
            BjvType.Null => new BjvNullValue(),
            BjvType.True => new BjvBoolValue { Value = true },
            BjvType.False => new BjvBoolValue { Value = false },
            BjvType.I64 => new BjvInt64Value { Value = bjvVal.AsInt64() ?? 0 },
            BjvType.U64 => new BjvUInt64Value { Value = bjvVal.AsUInt64() ?? 0 },
            BjvType.F64 => new BjvFloat64Value { Value = bjvVal.AsFloat64() ?? 0 },
            BjvType.String => new BjvStringValue { Value = bjvVal.AsString() ?? string.Empty },
            BjvType.StringId => doc is null ? new BjvStringValue { Value = string.Empty } : ConvertStringIdFromBjv(bjvVal, doc),
            BjvType.Bytes => new BjvBytesValue { Value = bjvVal.AsBytes() ?? Array.Empty<byte>() },
			BjvType.Array => doc is null ? new BjvArrayValue() : ConvertArrayFromBjv(bjvVal, doc),
			BjvType.Object => doc is null ? new BjvObjectValue() : ConvertObjectFromBjv(bjvVal, doc),
            _ => new BjvNullValue()
        };
    }

    static BjvValueNode ConvertStringIdFromBjv(BjvValue bjvVal, BjvDocument doc)
    {
        // StringId is an index into the VSP
        int? stringId = bjvVal.AsStringId();
        if (stringId.HasValue && stringId.Value >= 0 && stringId.Value < doc.Vsp.Count)
        {
            return new BjvStringValue { Value = doc.Vsp[stringId.Value] };
        }
        return new BjvStringValue { Value = string.Empty };
    }

    static BjvValueNode ConvertArrayFromBjv(BjvValue bjvVal, BjvDocument doc)
    {
        var arr = new BjvArrayValue();
        for (int i = 0; i < bjvVal.ArrayLength; i++)
        {
            var element = bjvVal.GetArrayElement(i, doc.IsBjv4);
            arr.Elements.Add(ConvertFromBjv(element, doc));
        }
        return arr;
    }

    static BjvValueNode ConvertObjectFromBjv(BjvValue bjvVal, BjvDocument doc)
    {
        var obj = new BjvObjectValue();
        for (int i = 0; i < bjvVal.ObjectLength; i++)
        {
			string? key = bjvVal.GetObjectKey(i, doc.Dictionary, doc.IsBjv4);
			if (key is null)
				continue;

			var value = bjvVal.GetObjectValue(i, doc.IsBjv4);
			obj.Fields[key] = ConvertFromBjv(value, doc);
        }
        return obj;
    }

    static void CmdBench(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("bench: usage bench <input_dir> [--outdir <dir>] [--keyid 16|32] [--vsp off|auto|force] [--crc on|off]");

        string inputDir = args[1];
        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"Input directory not found: {inputDir}");

        string? outDir = null;
        bool isBjv4 = false;
        bool useVsp = true;
        bool useCrc = false;

        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--outdir")
                outDir = args[i + 1];
            else if (args[i] == "--keyid" && args[i + 1] == "32")
                isBjv4 = true;
            else if (args[i] == "--vsp" && args[i + 1] == "off")
                useVsp = false;
            else if (args[i] == "--crc" && args[i + 1] == "on")
                useCrc = true;
        }

        var results = new List<BenchmarkResult>();

        foreach (var jsonFile in Directory.GetFiles(inputDir, "*.json").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(jsonFile);
            long jsonSize = new FileInfo(jsonFile).Length;

            string jsonText = File.ReadAllText(jsonFile);
            var jsonDoc = JsonDocument.Parse(jsonText);
            var bjvNode = JsonConverter.FromJson(jsonDoc.RootElement);
            var encoder = new BjvEncoder(isBjv4, useVsp, useCrc);
            var result = encoder.EncodeWithStats(bjvNode);

            long bjvSize = result.FileSize;
            double compressionRatio = (double)bjvSize / jsonSize;

            // Benchmark validation (10 iterations, take median)
            var times = new List<long>();
            for (int i = 0; i < 10; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var doc = BjvDocument.Parse(result.Data);
                Validate(doc);
                sw.Stop();
                times.Add(sw.ElapsedMilliseconds);
            }
            long medianTime = times.OrderBy(t => t).Skip(times.Count / 2).First();

            results.Add(new BenchmarkResult
            {
                Dataset = name,
                JsonBytes = jsonSize,
                BjvBytes = bjvSize,
                CompressionRatio = compressionRatio,
                ValidateMs = medianTime,
                DictKeys = result.DictionaryCount,
                VspStrings = result.VspCount
            });
        }

        // Output markdown table
        PrintBenchmarkTable(results, isBjv4, useVsp, useCrc);

        // Save to file if requested
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
            string outputPath = Path.Combine(outDir, $"benchmark_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
            using (var writer = new StreamWriter(outputPath))
            {
                PrintBenchmarkTable(results, isBjv4, useVsp, useCrc, writer);
            }
            Console.WriteLine($"Benchmark results saved to: {outputPath}");
        }
    }

    static void PrintBenchmarkTable(List<BenchmarkResult> results, bool isBjv4, bool useVsp, bool useCrc, StreamWriter? writer = null)
    {
        var output = new Action<string>(line =>
        {
            Console.WriteLine(line);
            writer?.WriteLine(line);
        });

        output($"# Benchmark Results (BJV{(isBjv4 ? "4" : "2")}, VSP={(useVsp ? "on" : "off")}, CRC={(useCrc ? "on" : "off")})");
        output("");
        output("| Dataset | JSON (bytes) | BJV (bytes) | Ratio | Validate (ms) | Dict Keys | VSP Strings |");
        output("|---------|----------|-----------|-------|---------------|-----------|-------------|");

        foreach (var r in results)
        {
            output($"| {r.Dataset,-20} | {r.JsonBytes,10:N0} | {r.BjvBytes,10:N0} | {r.CompressionRatio,6:P1} | {r.ValidateMs,13} | {r.DictKeys,9} | {r.VspStrings,11} |");
        }

        output("");
        output($"**Summary:**");
        output($"- Total datasets: {results.Count}");
        output($"- Average compression: {results.Average(r => r.CompressionRatio):P1}");
        output($"- Median validate time: {results.Select(r => r.ValidateMs).OrderBy(t => t).Skip(results.Count / 2).First()}ms");
    }

    class BenchmarkResult
    {
        public string Dataset { get; set; } = "";
        public long JsonBytes { get; set; }
        public long BjvBytes { get; set; }
        public double CompressionRatio { get; set; }
        public long ValidateMs { get; set; }
        public int DictKeys { get; set; }
        public int VspStrings { get; set; }
    }

    static void Validate(BjvDocument doc)
    {
        // Simple validation - just check root is not invalid
        if (doc.Root.Type == BjvType.Invalid)
            throw new InvalidOperationException("Invalid root");
    }

    static void CmdBench2(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("bench2: usage bench2 <input_dir> [--outdir <dir>] [--runs <n>] [--warmup <n>]");

        string inputDir = args[1];
        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"Input directory not found: {inputDir}");

        string outDir = GetOptionValue(args, "--outdir") ?? Path.Combine(Directory.GetCurrentDirectory(), "bench2_results");
        int defaultRuns = 10;
        int defaultWarmup = 3;

        if (!string.IsNullOrEmpty(GetOptionValue(args, "--runs")))
            defaultRuns = int.Parse(GetOptionValue(args, "--runs")!);
        if (!string.IsNullOrEmpty(GetOptionValue(args, "--warmup")))
            defaultWarmup = int.Parse(GetOptionValue(args, "--warmup")!);

        // Ensure output directory exists
        Directory.CreateDirectory(outDir);
        string absoluteOutDir = Path.GetFullPath(outDir);
        Console.WriteLine($"Output directory: {absoluteOutDir}");

        // Print environment header
        PrintBench2Header();

        // Check for debug mode
        bool debugMode = HasFlag(args, "--debug-measure");

        // Config matrix: keyId (16, 32) × VSP (off, auto, force) × CRC (off, on)
        var allConfigs = new List<(int keyId, string vsp, bool crc)>
        {
            (16, "off", false),
            (16, "off", true),
            (16, "auto", false),
            (16, "auto", true),
            (16, "force", false),
            (16, "force", true),
            (32, "off", false),
            (32, "off", true),
            (32, "auto", false),
            (32, "auto", true),
            (32, "force", false),
            (32, "force", true),
        };

        var allResults = new List<Bench2Result>();
        // Only include A_*.json, B_*.json, C_*.json, D_*.json files (exclude manifest.json)
        var jsonFiles = Directory.GetFiles(inputDir, "*.json")
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^[A-D]_.*\.json$"))
            .OrderBy(f => f)
            .ToList();

        Console.WriteLine($"Found {jsonFiles.Count} JSON files");
        Console.WriteLine($"Benchmarking with {allConfigs.Count} configurations per file");
        if (debugMode) Console.WriteLine("Debug mode: ON");
        Console.WriteLine();

        bool isFirstFile = true;
        foreach (var jsonFile in jsonFiles)
        {
            string fileName = Path.GetFileName(jsonFile);
            string group = GetDatasetGroup(fileName);

            long jsonSize = new FileInfo(jsonFile).Length;
            byte[] jsonBytes = File.ReadAllBytes(jsonFile);
            string jsonText = System.Text.Encoding.UTF8.GetString(jsonBytes);

            // Determine runs/warmup based on file size
            int runs = defaultRuns;
            int warmup = defaultWarmup;
            if (jsonSize > 1_000_000)
            {
                runs = 5;
                warmup = 2;
                Console.WriteLine($"[{fileName}] Large file ({jsonSize:N0} bytes), reducing to runs={runs}, warmup={warmup}");
            }

            Console.WriteLine($"Benchmarking {fileName} ({jsonSize:N0} bytes)...");

            // Randomize config order per file for fairness
            var configs = allConfigs.OrderBy(x => System.Guid.NewGuid()).ToList();

            foreach (var (keyId, vspMode, crc) in configs)
            {
                bool isBjv4 = keyId == 32;
                bool useVsp = vspMode != "off";

                try
                {
                    var result = RunBench2(jsonFile, jsonText, jsonSize, group, isBjv4, useVsp, crc, runs, warmup, outDir,
                        debugMode && isFirstFile, group, vspMode, keyId);
                    allResults.Add(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error with config keyId={keyId} vsp={vspMode} crc={crc}: {ex.GetBaseException().Message}");
                    // Don't skip - report failure but continue
                }
            }

            isFirstFile = false;
        }

        Console.WriteLine();
        if (allResults.Count == 0)
        {
            Console.WriteLine("WARNING: No successful benchmarks recorded. Check for errors above.");
            return;
        }

        // Write CSV
        try
        {
            string csvPath = Path.Combine(outDir, "MEGA_BENCH.csv");
            WriteBench2Csv(allResults, csvPath);
            Console.WriteLine($"✓ CSV written: {Path.GetFullPath(csvPath)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing CSV: {ex.Message}");
        }

        // Write Markdown tables grouped by dataset
        try
        {
            string mdPath = Path.Combine(outDir, "MEGA_BENCH.md");
            WriteBench2Markdown(allResults, mdPath);
            Console.WriteLine($"✓ Markdown written: {Path.GetFullPath(mdPath)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing Markdown: {ex.Message}");
        }

        // Write summary
        try
        {
            string summaryPath = Path.Combine(outDir, "MEGA_BENCH_summary.md");
            WriteBench2Summary(allResults, summaryPath);
            Console.WriteLine($"✓ Summary written: {Path.GetFullPath(summaryPath)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing Summary: {ex.Message}");
        }

        // List artifact directories
        try
        {
            string artifactDir = Path.Combine(outDir, "artifacts");
            if (Directory.Exists(artifactDir))
            {
                var configDirs = Directory.GetDirectories(artifactDir);
                Console.WriteLine($"✓ Artifacts: {configDirs.Length} config directories");
                foreach (var configDir in configDirs.OrderBy(d => d))
                {
                    var files = Directory.GetFiles(configDir);
                    Console.WriteLine($"  - {Path.GetFileName(configDir)}: {files.Length} files");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error listing artifacts: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine($"Benchmark complete! Results saved to {absoluteOutDir}");
    }

    static void PrintBench2Header()
    {
        Console.WriteLine("=== MEGA BENCH - Stage 11 ===");
        Console.WriteLine($"OS: {Environment.OSVersion}");
        Console.WriteLine($".NET Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"Timestamp: {DateTime.UtcNow:O}");
        Console.WriteLine();
    }

    static string GetDatasetGroup(string fileName)
    {
        if (fileName.StartsWith("A_")) return "A_tiny";
        if (fileName.StartsWith("B_")) return "B_medium";
        if (fileName.StartsWith("C_")) return "C_large";
        if (fileName.StartsWith("D_")) return "D_stress";
        return "unknown";
    }

    static string? GetOptionValue(string[] args, string option)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == option)
                return args[i + 1];
        }
        return null;
    }

    static long GetMedian(List<long> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(x => x).ToList();
        return sorted[sorted.Count / 2];
    }

    static Bench2Result RunBench2(string jsonFile, string jsonText, long jsonSize, string group,
        bool isBjv4, bool useVsp, bool useCrc, int runs, int warmup, string outDir,
        bool debug = false, string debugGroup = "", string debugVsp = "", int debugKeyId = 0)
    {
        var result = new Bench2Result
        {
            FileName = Path.GetFileName(jsonFile),
            Group = group,
            KeyId = isBjv4 ? 32 : 16,
            Vsp = debugVsp,  // Store the actual VSP mode (off/auto/force), not just on/off
            Crc = useCrc ? "on" : "off",
            JsonBytes = jsonSize
        };

        // Get benchmark paths early for debug output and lookup benchmark
        var paths = GetBenchmarkPaths(group);

        // Debug: Print measurement plan for first file
        if (debug)
        {
            Console.WriteLine();
            Console.WriteLine("=== DEBUG: Measurement Plan ===");
            Console.WriteLine($"Config: BJV{debugKeyId}, VSP={debugVsp}, CRC={useCrc}");
            Console.WriteLine($"Lookup paths: {paths.Length}, Loops per path: 100");
            Console.WriteLine($"Total lookup ops: {paths.Length * 100}");
            Console.WriteLine("Measurements (excluding file I/O):");
            Console.WriteLine("  - encode: JSON node -> BJV bytes (BjvEncoder.EncodeWithStats)");
            Console.WriteLine("  - open_validate: parse BJV + strict validation (BjvDocument.Parse + ValidateBjvStrict)");
            Console.WriteLine("  - decode_tojson: parse BJV + convert to JSON (BjvDocument.Parse + JsonConverter.ToJson)");
            Console.WriteLine("  - lookup: 10 path traversals × 100 loops per config");
            Console.WriteLine();
        }

        // Parse JSON and encode to BJV (NOT timed - file I/O)
        var jsonDoc = JsonDocument.Parse(jsonText);
        var bjvNode = JsonConverter.FromJson(jsonDoc.RootElement);

        // Encode benchmark with warmup
        var encoder = new BjvEncoder(isBjv4, useVsp, useCrc);
        var encodeStats = encoder.EncodeWithStats(bjvNode);
        byte[] bjvData = encodeStats.Data;

        result.BjvBytes = bjvData.Length;
        result.RatioPercent = (double)bjvData.Length / jsonSize * 100;

        // Time encoding (runs)
        var encodeTimes = new List<long>();
        for (int i = 0; i < warmup; i++)
        {
            var _ = new BjvEncoder(isBjv4, useVsp, useCrc);
            _.EncodeWithStats(bjvNode);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < runs; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var enc = new BjvEncoder(isBjv4, useVsp, useCrc);
            enc.EncodeWithStats(bjvNode);
            sw.Stop();
            encodeTimes.Add((long)sw.Elapsed.TotalMicroseconds);
        }
        result.EncodeMs = GetMedian(encodeTimes);

        // Time validation/open (runs)
        var validateTimes = new List<long>();
        for (int i = 0; i < warmup; i++)
            BjvDocument.Parse(bjvData);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < runs; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = BjvDocument.Parse(bjvData);
            // Validate with depth check
            ValidateBjvStrict(doc.Root, 0, 256, doc.IsBjv4);
            sw.Stop();
            validateTimes.Add((long)sw.Elapsed.TotalMicroseconds);
        }
        result.OpenValidateMs = GetMedian(validateTimes);

        // Time decode to JSON (runs)
        var decodeTimes = new List<long>();
        for (int i = 0; i < warmup; i++)
        {
            var doc = BjvDocument.Parse(bjvData);
            _ = JsonConverter.ToJson(ConvertFromBjv(doc.Root, doc));
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < runs; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = BjvDocument.Parse(bjvData);
            _ = JsonConverter.ToJson(ConvertFromBjv(doc.Root, doc));
            sw.Stop();
            decodeTimes.Add((long)sw.Elapsed.TotalMicroseconds);
        }
        result.DecodeTojsonMs = GetMedian(decodeTimes);

        // Lookup benchmark (paths already loaded above)
        var lookupTimes = new List<long>();
        for (int i = 0; i < warmup; i++)
        {
            var doc = BjvDocument.Parse(bjvData);
            foreach (var path in paths)
            {
                _ = LookupPath(doc.Root, path, doc);
            }
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();

        int loopsPerPath = 100;
        for (int i = 0; i < runs; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = BjvDocument.Parse(bjvData);
            for (int j = 0; j < loopsPerPath; j++)
            {
                foreach (var path in paths)
                {
                    _ = LookupPath(doc.Root, path, doc);
                }
            }
            sw.Stop();
            lookupTimes.Add((long)sw.Elapsed.TotalMicroseconds);
        }
        result.LookupMs = GetMedian(lookupTimes);
        result.LookupOps = (long)paths.Length * loopsPerPath;

        // Encryption benchmark (compute one encrypted file for size/overhead measurement)
        byte[] bjxData = Bjx.EncryptBjx1(bjvData, "bench-pass", null);
        result.BjxBytes = bjxData.Length;
        result.OverheadPercent = (double)(bjxData.Length - bjvData.Length) / bjvData.Length * 100;

        var encryptTimes = new List<long>();
        for (int i = 0; i < warmup; i++)
            _ = Bjx.EncryptBjx1(bjvData, "bench-pass", null);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < runs; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _ = Bjx.EncryptBjx1(bjvData, "bench-pass", null);
            sw.Stop();
            encryptTimes.Add((long)sw.Elapsed.TotalMicroseconds);
        }
        result.EncryptMs = GetMedian(encryptTimes);

        var decryptTimes = new List<long>();
        // Create fresh encrypted copies for each decrypt test run
        for (int i = 0; i < warmup; i++)
        {
            byte[] testBjx = Bjx.EncryptBjx1(bjvData, "bench-pass", null);
            _ = Bjx.DecryptBjx1(testBjx, "bench-pass", null);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < runs; i++)
        {
            byte[] testBjx = Bjx.EncryptBjx1(bjvData, "bench-pass", null);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _ = Bjx.DecryptBjx1(testBjx, "bench-pass", null);
            sw.Stop();
            decryptTimes.Add((long)sw.Elapsed.TotalMicroseconds);
        }
        result.DecryptMs = GetMedian(decryptTimes);

        var decryptValidateTimes = new List<long>();
        for (int i = 0; i < warmup; i++)
        {
            byte[] testBjx = Bjx.EncryptBjx1(bjvData, "bench-pass", null);
            var decrypted = Bjx.DecryptBjx1(testBjx, "bench-pass", null);
            var doc = BjvDocument.Parse(decrypted);
            ValidateBjvStrict(doc.Root, 0, 256, doc.IsBjv4);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < runs; i++)
        {
            byte[] testBjx = Bjx.EncryptBjx1(bjvData, "bench-pass", null);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var decrypted = Bjx.DecryptBjx1(testBjx, "bench-pass", null);
            var doc = BjvDocument.Parse(decrypted);
            ValidateBjvStrict(doc.Root, 0, 256, doc.IsBjv4);
            sw.Stop();
            decryptValidateTimes.Add((long)sw.Elapsed.TotalMicroseconds);
        }
        result.DecryptValidateMs = GetMedian(decryptValidateTimes);

        // Save produced files
        string configPath = $"keyid_{result.KeyId}_vsp_{result.Vsp}_crc_{result.Crc}";
        string artifactDir = Path.Combine(outDir, "artifacts", configPath);
        Directory.CreateDirectory(artifactDir);

        string bjvPath = Path.Combine(artifactDir, Path.GetFileNameWithoutExtension(result.FileName) + ".bjv");
        string bjxPath = Path.Combine(artifactDir, Path.GetFileNameWithoutExtension(result.FileName) + ".bjx");

        File.WriteAllBytes(bjvPath, bjvData);
        File.WriteAllBytes(bjxPath, bjxData);

        return result;
    }

    static string[] GetBenchmarkPaths(string group)
    {
        return group switch
        {
            "A_tiny" => new[]
            {
                "servers[0].port",
                "features.logging",
                "name",
                "enabled",
                "timeout_ms",
                "version",
                "servers[1].host",
                "features.metrics",
                "features.tracing",
                "servers"
            },
            "B_medium" => new[]
            {
                "cluster.services[0].name",
                "config.max_connections",
                "name",
                "cluster.id",
                "config.log_level",
                "cluster.services[25].healthy",
                "cluster.region",
                "config.features.cache",
                "cluster.zone",
                "cluster.services"
            },
            "C_large" => new[]
            {
                "devices[0].device_id",
                "devices[0].sensors[0].type",
                "statistics.total_devices",
                "name",
                "devices[250].status",
                "devices[0].metadata.firmware",
                "devices[100].sensors[5].value",
                "statistics.active_devices",
                "description",
                "devices"
            },
            "D_stress" => new[]
            {
                "deep_nesting.nested.nested.nested.level",
                "large_arrays.strings[500]",
                "name",
                "large_arrays.numbers[100]",
                "wide_object.field_100",
                "description",
                "large_arrays.objects[50].name",
                "deep_nesting.level",
                "large_arrays.objects",
                "wide_object"
            },
            _ => Array.Empty<string>()
        };
    }

    static object? LookupPath(BjvValue value, string path, BjvDocument doc)
    {
        try
        {
            var parts = path.Split(new[] { '.', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                if (value.Type == BjvType.Object)
                {
                    bool found = false;
                    for (int i = 0; i < value.ObjectLength; i++)
                    {
                        string? key = value.GetObjectKey(i, doc.Dictionary, doc.IsBjv4);
                        if (key == part)
                        {
                            value = value.GetObjectValue(i, doc.IsBjv4);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        return null;
                }
                else if (value.Type == BjvType.Array && int.TryParse(part, out int index))
                {
                    if (index < 0 || index >= value.ArrayLength)
                        return null;
                    value = value.GetArrayElement(index, doc.IsBjv4);
                }
                else
                {
                    return null;
                }
            }

            return value.Type switch
            {
                BjvType.Null => null,
                BjvType.True => true,
                BjvType.False => false,
                BjvType.I64 => value.AsInt64(),
                BjvType.U64 => value.AsUInt64(),
                BjvType.F64 => value.AsFloat64(),
                BjvType.String => value.AsString(),
                BjvType.StringId => value.AsStringId(),
                BjvType.Bytes => value.AsBytes(),
                BjvType.Array => $"Array({value.ArrayLength})",
                BjvType.Object => $"Object({value.ObjectLength})",
                _ => null
            };
        }
        catch (Exception)
        {
            // Catch all exceptions (IndexOutOfRange, NullRef, etc.) and return null
            return null;
        }
    }

    static void ValidateBjvStrict(BjvValue value, int depth, int maxDepth, bool isBjv4 = false)
    {
        if (depth > maxDepth)
            throw new InvalidOperationException("Max depth exceeded");

        switch (value.Type)
        {
            case BjvType.Array:
                for (int i = 0; i < value.ArrayLength; i++)
                    ValidateBjvStrict(value.GetArrayElement(i, isBjv4), depth + 1, maxDepth, isBjv4);
                break;
            case BjvType.Object:
                for (int i = 0; i < value.ObjectLength; i++)
                {
                    var objVal = value.GetObjectValue(i, isBjv4);
                    ValidateBjvStrict(objVal, depth + 1, maxDepth, isBjv4);
                }
                break;
        }
    }

    static void WriteBench2Csv(List<Bench2Result> results, string path)
    {
        using (var writer = new StreamWriter(path))
        {
            writer.WriteLine("FileName,Group,KeyId,VSP,CRC,JsonBytes,BjvBytes,RatioPercent,EncodeUs,OpenValidateUs,DecodeTojsonUs,LookupUs,LookupOps,BjxBytes,OverheadPercent,EncryptUs,DecryptUs,DecryptValidateUs");

            foreach (var r in results)
            {
                writer.WriteLine($"{r.FileName},{r.Group},{r.KeyId},{r.Vsp},{r.Crc},{r.JsonBytes},{r.BjvBytes},{r.RatioPercent:F2},{r.EncodeMs},{r.OpenValidateMs},{r.DecodeTojsonMs},{r.LookupMs},{r.LookupOps},{r.BjxBytes},{r.OverheadPercent:F2},{r.EncryptMs},{r.DecryptMs},{r.DecryptValidateMs}");
            }
            writer.Flush();
        }
    }

    static void WriteBench2Markdown(List<Bench2Result> results, string path)
    {
        using (var writer = new StreamWriter(path))
        {
            writer.WriteLine("# MEGA BENCH Results - Stage 11");
            writer.WriteLine();

            var groups = results.GroupBy(r => r.Group).OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                writer.WriteLine($"## {group.Key}");
                writer.WriteLine();
                writer.WriteLine("| File | KeyId | VSP | CRC | JSON (B) | BJV (B) | Ratio % | Encode (us) | Open+Val (us) | Decode (us) | Lookup (us) | Ops | BJX (B) | OH % | Enc (us) | Dec (us) | DecVal (us) |");
                writer.WriteLine("|------|-------|-----|-----|----------|---------|---------|------------|---------------|------------|-------------|-----|---------|------|---------|---------|-------------|");

                foreach (var r in group.OrderBy(x => x.FileName))
                {
                    writer.WriteLine($"| {r.FileName,-20} | {r.KeyId} | {r.Vsp,-3} | {r.Crc,-3} | {r.JsonBytes,8} | {r.BjvBytes,7} | {r.RatioPercent,7:F1} | {r.EncodeMs,10} | {r.OpenValidateMs,13} | {r.DecodeTojsonMs,10} | {r.LookupMs,11} | {r.LookupOps,5} | {r.BjxBytes,7} | {r.OverheadPercent,4:F1} | {r.EncryptMs,7} | {r.DecryptMs,7} | {r.DecryptValidateMs,11} |");
                }

                writer.WriteLine();
            }
            writer.Flush();
        }
    }

    static void WriteBench2Summary(List<Bench2Result> results, string path)
    {
        using (var writer = new StreamWriter(path))
        {
            writer.WriteLine("# MEGA BENCH Summary - Stage 11");
            writer.WriteLine();

            if (results.Count == 0)
            {
                writer.WriteLine("No benchmark results available.");
                writer.Flush();
                return;
            }

            // Define all 12 possible configurations
            var allConfigs = new[]
            {
                (16, "off", false),
                (16, "off", true),
                (16, "auto", false),
                (16, "auto", true),
                (16, "force", false),
                (16, "force", true),
                (32, "off", false),
                (32, "off", true),
                (32, "auto", false),
                (32, "auto", true),
                (32, "force", false),
                (32, "force", true),
            };

            // Key findings
            var bestCompressionFile = results.OrderBy(r => r.RatioPercent).First();
            var fastestEncode = results.OrderBy(r => r.EncodeMs).First();
            var fastestValidate = results.OrderBy(r => r.OpenValidateMs).First();
            var fastestLookup = results.OrderBy(r => r.LookupMs).First();
            var avgEncryptOverhead = results.Average(r => r.OverheadPercent);

            writer.WriteLine("## Key Findings");
            writer.WriteLine();
            writer.WriteLine($"- **Best compression**: {bestCompressionFile.FileName} ({bestCompressionFile.RatioPercent:F1}%)");
            writer.WriteLine($"- **Fastest encode**: {fastestEncode.FileName} ({fastestEncode.EncodeMs}us)");
            writer.WriteLine($"- **Fastest validation**: {fastestValidate.FileName} ({fastestValidate.OpenValidateMs}us)");
            writer.WriteLine($"- **Fastest lookup**: {fastestLookup.FileName} ({fastestLookup.LookupMs}us, {fastestLookup.LookupOps} ops)");
            writer.WriteLine($"- **Average encryption overhead**: {avgEncryptOverhead:F1}%");
            writer.WriteLine();

            // Stats by dataset group and configuration (all 12 configs per group)
            writer.WriteLine("## Performance by Dataset Group");
            writer.WriteLine();

            var groups = results.GroupBy(r => r.Group).OrderBy(g => g.Key);

            foreach (var datasetGroup in groups)
            {
                writer.WriteLine($"### {datasetGroup.Key}");
                writer.WriteLine();

                var dataByConfig = datasetGroup.GroupBy(r => new { r.KeyId, r.Vsp, r.Crc })
                    .ToDictionary(g => (g.Key.KeyId, g.Key.Vsp, g.Key.Crc == "on"));

                foreach (var (keyId, vspMode, crcBool) in allConfigs)
                {
                    var crcStr = crcBool ? "on" : "off";
                    var configKey = (keyId, vspMode, crcBool);

                    if (dataByConfig.TryGetValue(configKey, out var configData))
                    {
                        var medianEncode = GetMedian(configData.Select(r => r.EncodeMs).ToList());
                        var medianValidate = GetMedian(configData.Select(r => r.OpenValidateMs).ToList());
                        var medianDecode = GetMedian(configData.Select(r => r.DecodeTojsonMs).ToList());
                        var medianLookup = GetMedian(configData.Select(r => r.LookupMs).ToList());
                        var medianRatio = GetMedian(configData.Select(r => (long)(r.RatioPercent * 10)).ToList()) / 10.0;
                        var medianLookupOps = GetMedian(configData.Select(r => r.LookupOps).ToList());
                        var lookupPerOp = medianLookupOps > 0 ? medianLookup / (double)medianLookupOps : 0;

                        writer.WriteLine($"- **BJV{keyId}, VSP={vspMode}, CRC={crcStr}**");
                        writer.WriteLine($"  - Compression: {medianRatio:F1}%");
                        writer.WriteLine($"  - Timings: encode={medianEncode}us, validate={medianValidate}us, decode={medianDecode}us");
                        writer.WriteLine($"  - Lookup: {medianLookup}us total ({medianLookupOps} ops, {lookupPerOp:F2}us per op)");
                    }
                    else
                    {
                        writer.WriteLine($"- **BJV{keyId}, VSP={vspMode}, CRC={crcStr}** - N/A (no data)");
                    }
                }

                writer.WriteLine();
            }

            // Overall stats across all configurations
            writer.WriteLine("## Overall Statistics (Median-of-Medians)");
            writer.WriteLine();

            foreach (var (keyId, vspMode, crcBool) in allConfigs)
            {
                var crcStr = crcBool ? "on" : "off";
                var configResults = results.Where(r => r.KeyId == keyId && r.Vsp == vspMode && r.Crc == crcStr).ToList();

                if (configResults.Count > 0)
                {
                    var medianEncode = GetMedian(configResults.Select(r => r.EncodeMs).ToList());
                    var medianValidate = GetMedian(configResults.Select(r => r.OpenValidateMs).ToList());
                    var medianDecode = GetMedian(configResults.Select(r => r.DecodeTojsonMs).ToList());
                    var medianLookup = GetMedian(configResults.Select(r => r.LookupMs).ToList());
                    var medianRatio = GetMedian(configResults.Select(r => (long)(r.RatioPercent * 10)).ToList()) / 10.0;
                    var medianLookupOps = GetMedian(configResults.Select(r => r.LookupOps).ToList());
                    var lookupPerOp = medianLookupOps > 0 ? medianLookup / (double)medianLookupOps : 0;

                    writer.WriteLine($"- **BJV{keyId}, VSP={vspMode}, CRC={crcStr}**");
                    writer.WriteLine($"  - Compression: {medianRatio:F1}%");
                    writer.WriteLine($"  - Timings: encode={medianEncode}us, validate={medianValidate}us, decode={medianDecode}us");
                    writer.WriteLine($"  - Lookup: {medianLookup}us total ({medianLookupOps} ops, {lookupPerOp:F2}us per op)");
                }
                else
                {
                    writer.WriteLine($"- **BJV{keyId}, VSP={vspMode}, CRC={crcStr}** - N/A (no data across all datasets)");
                }
            }

            writer.Flush();
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: ironconfigtool <command> [options]

            Commands:
              encode <in.json> <out.ironcfg>      Encode JSON to IronConfig
              encode-secure <in.json> <out.ironcfs>  Encode JSON with encryption
              decode <file.ironcfg|file.ironcfs> Decode IronConfig file
              validate <file.ironcfg|file.ironcfs>   Validate IronConfig file
              dump <file.ironcfg|file.ironcfs>   Dump IronConfig structure
              tojson <file.ironcfg|file.ironcfs> Convert to JSON
              stats <file.ironcfg|file.ironcfs>  Show statistics
              packx <in.json> <out.icfx>         Pack JSON to ICFX (zero-copy format)
              packxs <schema.json> <in.json> <out.icxs>  Pack JSON to ICXS (schema-based)
              pack2 <in.json> <out.icf2>         Pack JSON array to ICF2 (columnar format)
              validate <file.icfx|file.icxs|...> Validate binary file
              validate2 <file.icf2>               Validate ICF2 file
              tojson <file.icfx|file.icxs|...>   Convert to JSON
              bench <input_dir>                   Run benchmarks on JSON files
              bench2 <input_dir>                  MEGA BENCH - comprehensive benchmarks

            Legacy Commands (still supported):
              pack <in.json> <out.bjv>           Pack JSON to BJV
              encrypt <in.bjv> <out.bjx>         Encrypt to BJX
              decrypt <in.bjx> <out.bjv>         Decrypt BJX

            Options:
              --password <pass>                   Set password for encrypt/decrypt
              --keyhex <hex>                      Use raw 32-byte hex key
              --validate                          Validate after decrypt
              --out <file>                        Output file for tojson
              --schema <file.json>                Schema file for ICXS tojson
              --stats                             Show statistics
              --keyid 16|32                       Use BJV2 (16) or BJV4 (32)
              --vsp off|auto|force                VSP mode
              --crc on|off                        Include CRC32
              --outdir <dir>                      Output directory for bench2
              --runs <n>                          Number of benchmark runs
              --warmup <n>                        Number of warmup runs

            File Formats:
              .ironcfg   IronConfig unencrypted format (BJV binary)
              .ironcfs   IronConfig encrypted format (BJX encrypted)
              .icfx      ICFX zero-copy format (high-performance)
              .icxs      ICXS schema-based format (O(1) field access)
              .icf2      ICF2 columnar format (deterministic, schema-less)
              .bjv       Legacy BJV binary format
              .bjx       Legacy BJX encrypted format

            Examples:
              ironconfigtool encode config.json config.ironcfg
              ironconfigtool encode-secure config.json config.ironcfs --password "secret"
              ironconfigtool decode config.ironcfg --out config.json
              ironconfigtool validate config.ironcfg
              ironconfigtool tojson config.ironcfg --out config.json
              ironconfigtool packx config.json config.icfx --crc on
              ironconfigtool validate config.icfx
              ironconfigtool tojson config.icfx --out config.json
              ironconfigtool packxs item.schema.json items.json items.icxs --crc on
              ironconfigtool validate items.icxs
              ironconfigtool tojson items.icxs --out items.json
              ironconfigtool pack2 data.json data.icf2 --crc on
              ironconfigtool validate2 data.icf2
              ironconfigtool bench2 ./datasets/test-datasets --outdir ./results
              ironconfigtool printcrc config.icfx
            """);
    }

    static void CmdPrintCrc(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("printcrc: usage printcrc <file.icfx>");

        string filePath = args[1];

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        CrcDiagnostic.PrintCrcInfo(filePath);
    }

    static void CmdPrintIcf2Strings(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("printicf2strings: usage printicf2strings <file.icf2>");

        string filePath = args[1];

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        // Diagnostic: trace string column encoding
        byte[] data = File.ReadAllBytes(filePath);

        try
        {
            var view = IronConfig.Icf2.Icf2View.Open(data);
            Console.WriteLine($"File: {Path.GetFileName(filePath)}");
            Console.WriteLine($"Rows: {view.RowCount}");

            // For now, just report success
            Console.WriteLine("✓ File opened successfully");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading ICF2: {ex.Message}");
        }
    }
}

class Bench2Result
{
    public string FileName { get; set; } = "";
    public string Group { get; set; } = "";
    public int KeyId { get; set; }
    public string Vsp { get; set; } = "";
    public string Crc { get; set; } = "";
    public long JsonBytes { get; set; }
    public long BjvBytes { get; set; }
    public double RatioPercent { get; set; }
    public long EncodeMs { get; set; }
    public long OpenValidateMs { get; set; }
    public long DecodeTojsonMs { get; set; }
    public long LookupMs { get; set; }
    public long LookupOps { get; set; }
    public long BjxBytes { get; set; }
    public double OverheadPercent { get; set; }
    public long EncryptMs { get; set; }
    public long DecryptMs { get; set; }
    public long DecryptValidateMs { get; set; }
}
