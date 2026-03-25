using System;
using System.IO;
using System.Text.Json;
using IronConfig.Icf2;

class Program
{
    static void Main(string[] args)
    {
        string vectorDir = "../vectors/small/icf2";

        // Generate golden_small vectors
        string smallJson = Path.Combine(vectorDir, "golden_small.json");
        if (File.Exists(smallJson))
        {
            string json = File.ReadAllText(smallJson);
            var doc = JsonDocument.Parse(json);

            // With CRC
            var encoder = new Icf2Encoder(useCrc32: true);
            byte[] bytes = encoder.Encode(doc.RootElement);
            File.WriteAllBytes(Path.Combine(vectorDir, "golden_small.icf2"), bytes);
            Console.WriteLine($"Generated: golden_small.icf2 ({bytes.Length} bytes)");

            // Without CRC
            var encoder2 = new Icf2Encoder(useCrc32: false);
            byte[] bytes2 = encoder2.Encode(doc.RootElement);
            File.WriteAllBytes(Path.Combine(vectorDir, "golden_small_nocrc.icf2"), bytes2);
            Console.WriteLine($"Generated: golden_small_nocrc.icf2 ({bytes2.Length} bytes)");
        }

        // Generate golden_large_schema vectors
        string largeJson = Path.Combine(vectorDir, "golden_large_schema.json");
        if (File.Exists(largeJson))
        {
            string json = File.ReadAllText(largeJson);
            var doc = JsonDocument.Parse(json);

            var encoder = new Icf2Encoder(useCrc32: true);
            byte[] bytes = encoder.Encode(doc.RootElement);
            File.WriteAllBytes(Path.Combine(vectorDir, "golden_large_schema.icf2"), bytes);
            Console.WriteLine($"Generated: golden_large_schema.icf2 ({bytes.Length} bytes)");
        }

        Console.WriteLine("Done!");
    }
}
