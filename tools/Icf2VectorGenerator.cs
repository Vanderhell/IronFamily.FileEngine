/// <summary>
/// ICF2 Golden Vector Generator
/// Generates deterministic test files from JSON
/// Usage: dotnet run tools/Icf2VectorGenerator.cs <vectors-path>
/// </summary>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IronConfig.Icf2;

class Icf2VectorGenerator
{
    static void Main(string[] args)
    {
        string vectorPath = args.Length > 0 ? args[0] : "vectors/small/icf2";

        if (!Directory.Exists(vectorPath))
        {
            Directory.CreateDirectory(vectorPath);
        }

        Console.WriteLine($"Generating ICF2 vectors in {vectorPath}");

        // Generate golden vectors
        GenerateSmallVectors(vectorPath);
        GenerateLargeSchemaVectors(vectorPath);

        // Generate medium, mega, and stress vectors from inputs/
        GenerateInputVectors(vectorPath);

        Console.WriteLine("Done.");
    }

    static void GenerateSmallVectors(string basePath)
    {
        string jsonPath = Path.Combine(basePath, "golden_small.json");

        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"Warning: {jsonPath} not found");
            return;
        }

        Console.WriteLine($"Generating from {jsonPath}");

        string json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // With CRC
        {
            var encoder = new Icf2Encoder(useCrc32: true, useBlake3: false);
            byte[] bytes = encoder.Encode(root);
            string outPath = Path.Combine(basePath, "golden_small.icf2");
            File.WriteAllBytes(outPath, bytes);
            Console.WriteLine($"  Ă˘â€ â€™ {outPath} ({bytes.Length} bytes)");
        }

        // Without CRC
        {
            var encoder = new Icf2Encoder(useCrc32: false, useBlake3: false);
            byte[] bytes = encoder.Encode(root);
            string outPath = Path.Combine(basePath, "golden_small_nocrc.icf2");
            File.WriteAllBytes(outPath, bytes);
            Console.WriteLine($"  Ă˘â€ â€™ {outPath} ({bytes.Length} bytes)");
        }
    }

    static void GenerateLargeSchemaVectors(string basePath)
    {
        string jsonPath = Path.Combine(basePath, "golden_large_schema.json");

        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"Warning: {jsonPath} not found");
            return;
        }

        Console.WriteLine($"Generating from {jsonPath}");

        string json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var encoder = new Icf2Encoder(useCrc32: true, useBlake3: false);
        byte[] bytes = encoder.Encode(root);
        string outPath = Path.Combine(basePath, "golden_large_schema.icf2");
        File.WriteAllBytes(outPath, bytes);
        Console.WriteLine($"  Ă˘â€ â€™ {outPath} ({bytes.Length} bytes)");
    }

    static void GenerateInputVectors(string basePath)
    {
        string inputsPath = Path.Combine(basePath, "inputs");

        if (!Directory.Exists(inputsPath))
        {
            Console.WriteLine($"Warning: {inputsPath} directory not found");
            return;
        }

        var jsonFiles = Directory.GetFiles(inputsPath, "*.json");
        if (jsonFiles.Length == 0)
        {
            Console.WriteLine($"Warning: No JSON files found in {inputsPath}");
            return;
        }

        Console.WriteLine($"Generating from {jsonFiles.Length} input files in inputs/");

        foreach (var jsonPath in jsonFiles)
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Generate with CRC
                var encoder = new Icf2Encoder(useCrc32: true, useBlake3: false);
                byte[] bytes = encoder.Encode(root);

                string filename = Path.GetFileNameWithoutExtension(jsonPath);
                string outPath = Path.Combine(inputsPath, filename + ".icf2");
                File.WriteAllBytes(outPath, bytes);
                Console.WriteLine($"  Ă˘â€ â€™ {filename}.icf2 ({bytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR in {Path.GetFileName(jsonPath)}: {ex.Message}");
            }
        }
    }
}
