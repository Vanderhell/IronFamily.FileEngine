using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronConfig;
using IronConfig.Common;

namespace IronConfig.ILog.Runtime;

/// <summary>
/// Runtime verify command for ILOG files with unified error handling.
/// Maps IlogError codes to IronEdgeError categories for consistent runtime contract.
/// </summary>
public static class RuntimeVerifyIlogCommand
{
    public static string Execute(string filePath, out int exitCode)
    {
        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(filePath))
            {
                exitCode = 3; // Args error
                return CreateErrorJson("File path is required", "InvalidArgument", 0x01);
            }

            // File I/O
            if (!File.Exists(filePath))
            {
                exitCode = 2; // IO error
                return CreateErrorJson($"File not found: {Path.GetFileName(filePath)}", "Io", 0x02);
            }

            byte[] fileData;
            try
            {
                fileData = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                exitCode = 2; // IO error
                return CreateErrorJson($"I/O error: {ex.Message}", "Io", 0x02);
            }

            // ILOG validation
            var error = IlogReader.Open(fileData, out var view);

            if (error != null)
            {
                exitCode = 1; // Validation error
                var (category, code) = MapIlogErrorToIronEdge(error.Code);
                return CreateErrorJson(error.Message, category, code);
            }

            if (view == null)
            {
                exitCode = 1; // Validation error
                return CreateErrorJson("ILOG view is null", "CorruptData", 0x06);
            }

            // Success
            exitCode = 0;
            return CreateSuccessJson(view);
        }
        catch (Exception ex)
        {
            exitCode = 10; // Internal error
            return CreateErrorJson($"Internal error: {ex.Message}", "Unknown", 0x00);
        }
    }

    private static string CreateSuccessJson(IlogView view)
    {
        // Deterministic JSON: stable field order
        var result = new
        {
            ok = true,
            engine = "ILog",
            file_size = view.Size,
            event_count = view.EventCount,
            version = view.Version,
            has_l2 = view.Flags.HasLayerL2,
            has_l3 = view.Flags.HasLayerL3,
            has_l4 = view.Flags.HasLayerL4,
            has_crc32 = view.Flags.HasCrc32,
            has_blake3 = view.Flags.HasBlake3,
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        return JsonSerializer.Serialize(result, options);
    }

    private static string CreateErrorJson(string message, string category, byte code)
    {
        // Deterministic JSON: stable field order
        var result = new
        {
            ok = false,
            engine = "ILog",
            error = new
            {
                category = category,
                code = code.ToString("X2"),
                message = message,
            },
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        return JsonSerializer.Serialize(result, options);
    }

    private static (string category, byte code) MapIlogErrorToIronEdge(IlogErrorCode ilogCode)
    {
        // Map ILOG error codes to IronEdgeErrorCategory + canonical code
        return ilogCode switch
        {
            IlogErrorCode.InvalidMagic => ("InvalidMagic", 0x80),
            IlogErrorCode.UnsupportedVersion => ("UnsupportedVersion", 0x81),
            IlogErrorCode.CorruptedHeader => ("CorruptData", 0x82),
            IlogErrorCode.MissingLayer => ("CorruptData", 0x83),
            IlogErrorCode.MalformedBlock => ("CorruptData", 0x84),
            IlogErrorCode.BlockOutOfBounds => ("CorruptData", 0x85),
            IlogErrorCode.InvalidBlockType => ("CorruptData", 0x86),
            IlogErrorCode.SchemaValidation => ("SchemaError", 0x87),
            IlogErrorCode.OutOfBoundsRef => ("IndexError", 0x88),
            IlogErrorCode.DictLookup => ("IndexError", 0x89),
            IlogErrorCode.VarintDecode => ("CorruptData", 0x8A),
            IlogErrorCode.Crc32Mismatch => ("InvalidChecksum", 0x8B),
            IlogErrorCode.Blake3Mismatch => ("InvalidChecksum", 0x8C),
            IlogErrorCode.CompressionFailed => ("CompressionError", 0x8D),
            IlogErrorCode.RecordTruncated => ("Truncated", 0x8E),
            IlogErrorCode.DepthLimit => ("CorruptData", 0x8F),
            IlogErrorCode.FileSizeLimit => ("CorruptData", 0x90),
            IlogErrorCode.RecordCountLimit => ("CorruptData", 0x91),
            IlogErrorCode.StringLengthLimit => ("CorruptData", 0x92),
            IlogErrorCode.CriticalFlag => ("CorruptData", 0x93),
            _ => ("Unknown", 0x00),
        };
    }
}
