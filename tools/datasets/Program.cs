using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatasetGenerator;

/// <summary>
/// Generates benchmark datasets with fixed RNG seed for reproducibility.
/// Supports single baseline generation or 10x variants per group (for mega bench).
/// </summary>
class Program
{
    private const int RNG_SEED = 42;
    private static readonly Random _rng = new(RNG_SEED);

    static void Main(string[] args)
    {
        string outputDir = "datasets";
        bool megaBench = false;

        // Parse arguments
        foreach (var arg in args)
        {
            if (arg.StartsWith("--out="))
                outputDir = arg.Substring(6);
            else if (arg == "--mega")
                megaBench = true;
        }

        Directory.CreateDirectory(outputDir);

        if (megaBench)
        {
            Console.WriteLine("Generating 10x variants per group for mega bench...");
            GenerateMegaBench(outputDir);
        }
        else
        {
            Console.WriteLine("Generating baseline benchmark datasets with seed={0}", RNG_SEED);
            GenerateTiny(Path.Combine(outputDir, "A_tiny.json"));
            GenerateMedium(Path.Combine(outputDir, "B_medium.json"));
            GenerateLarge(Path.Combine(outputDir, "C_large.json"));
            GenerateStress(Path.Combine(outputDir, "D_stress.json"));
        }

        Console.WriteLine("\nDatasets generated:");
        var files = Directory.GetFiles(outputDir, "*.json").OrderBy(f => f);
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            Console.WriteLine($"  {Path.GetFileName(file)}: {info.Length} bytes");
        }
    }

    static void GenerateMegaBench(string outputDir)
    {
        var manifest = new List<Dictionary<string, object>>();

        // Generate 10 variants for each group
        for (int variant = 1; variant <= 10; variant++)
        {
            int seed = RNG_SEED + variant;
            string prefix = variant.ToString("D2");

            // Tiny
            string tinyFile = Path.Combine(outputDir, $"A_{prefix}_tiny.json");
            GenerateTinyVariant(tinyFile, seed);
            manifest.Add(new()
            {
                { "file", $"A_{prefix}_tiny.json" },
                { "group", "A_tiny" },
                { "variant", variant },
                { "seed", seed },
                { "size_bytes", new FileInfo(tinyFile).Length }
            });

            // Medium
            string mediumFile = Path.Combine(outputDir, $"B_{prefix}_medium.json");
            GenerateMediumVariant(mediumFile, seed);
            manifest.Add(new()
            {
                { "file", $"B_{prefix}_medium.json" },
                { "group", "B_medium" },
                { "variant", variant },
                { "seed", seed },
                { "size_bytes", new FileInfo(mediumFile).Length }
            });

            // Large
            string largeFile = Path.Combine(outputDir, $"C_{prefix}_large.json");
            GenerateLargeVariant(largeFile, seed);
            manifest.Add(new()
            {
                { "file", $"C_{prefix}_large.json" },
                { "group", "C_large" },
                { "variant", variant },
                { "seed", seed },
                { "size_bytes", new FileInfo(largeFile).Length }
            });

            // Stress
            string stressFile = Path.Combine(outputDir, $"D_{prefix}_stress.json");
            GenerateStressVariant(stressFile, seed);
            manifest.Add(new()
            {
                { "file", $"D_{prefix}_stress.json" },
                { "group", "D_stress" },
                { "variant", variant },
                { "seed", seed },
                { "size_bytes", new FileInfo(stressFile).Length }
            });
        }

        // Write manifest
        string manifestPath = Path.Combine(outputDir, "manifest.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        string manifestJson = JsonSerializer.Serialize(manifest, options);
        File.WriteAllText(manifestPath, manifestJson);
        Console.WriteLine($"Manifest written to {manifestPath}");
    }

    static void GenerateTiny(string path)
    {
        var data = new
        {
            name = "tiny-config",
            version = "1.0.0",
            enabled = true,
            timeout_ms = 5000,
            servers = new[]
            {
                new { host = "localhost", port = 8080 },
                new { host = "192.168.1.1", port = 9000 }
            },
            features = new
            {
                logging = true,
                metrics = false,
                tracing = true
            }
        };

        WriteJson(path, data);
    }

    static void GenerateMedium(string path)
    {
        var services = new List<object>();
        for (int i = 0; i < 50; i++)
        {
            services.Add(new
            {
                name = $"service-{i:D3}",
                endpoint = $"http://localhost:{8000 + i}",
                version = $"{_rng.Next(1, 5)}.{_rng.Next(0, 10)}.{_rng.Next(0, 10)}",
                healthy = _rng.Next(2) == 1,
                latency_ms = _rng.Next(10, 1000),
                tags = new[] { "prod", "api", "v1" }
            });
        }

        var data = new
        {
            name = "medium-config",
            timestamp = DateTime.UtcNow.ToString("O"),
            cluster = new
            {
                id = "cluster-01",
                region = "us-east-1",
                zone = "1a",
                services = services
            },
            config = new
            {
                log_level = "INFO",
                max_connections = 1000,
                timeout_sec = 30,
                retry_count = 3,
                features = new
                {
                    cache = true,
                    compression = true,
                    auth = true,
                    tls = true
                }
            }
        };

        WriteJson(path, data);
    }

    static void GenerateLarge(string path)
    {
        var devices = new List<object>();
        for (int i = 0; i < 500; i++)
        {
            var sensors = new List<object>();
            for (int j = 0; j < 10; j++)
            {
                sensors.Add(new
                {
                    id = $"sensor-{i:D4}-{j:D2}",
                    type = RandomChoice(new[] { "temperature", "humidity", "pressure", "light" }),
                    value = _rng.Next(0, 100),
                    unit = "celsius",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
            }

            devices.Add(new
            {
                device_id = $"device-{i:D4}",
                name = $"IoT-Device-{i}",
                location = $"Building-{i % 5}-Floor-{i % 10}",
                status = RandomChoice(new[] { "online", "offline", "standby" }),
                battery_percent = _rng.Next(5, 100),
                sensors = sensors,
                metadata = new
                {
                    manufacturer = "Acme",
                    model = "IoT-2024",
                    firmware = "2.1.3",
                    last_update = "2024-01-09T12:00:00Z"
                }
            });
        }

        var data = new
        {
            name = "large-config",
            description = "Large IoT sensor network dataset",
            version = "2.0",
            devices = devices,
            statistics = new
            {
                total_devices = 500,
                total_sensors = 5000,
                active_devices = 485,
                data_points_per_hour = 18000
            }
        };

        WriteJson(path, data);
    }

    static void GenerateStress(string path)
    {
        // Stress test: deep nesting (but within JSON limit of 64)
        var current = (object)new { value = "leaf", depth = 32 };

        for (int i = 31; i >= 0; i--)
        {
            current = new
            {
                level = i,
                nested = current,
                data = RandomChoice(new[] { "data-a", "data-b", "data-c" })
            };
        }

        var data = new
        {
            name = "stress-test",
            description = "Stress test with nested objects and large arrays",
            deep_nesting = current,
            large_arrays = new
            {
                strings = GenerateStringArray(1000),
                numbers = GenerateNumberArray(1000),
                objects = GenerateObjectArray(100)
            },
            wide_object = GenerateWideObject(200)
        };

        WriteJson(path, data);
    }

    static Dictionary<string, object> GenerateWideObject(int fieldCount)
    {
        var result = new Dictionary<string, object>();
        for (int i = 0; i < fieldCount; i++)
        {
            result[$"field_{i:D3}"] = new
            {
                value = _rng.Next(1000),
                name = $"field-{i}",
                enabled = _rng.Next(2) == 1
            };
        }
        return result;
    }

    static string[] GenerateStringArray(int count)
    {
        var result = new string[count];
        var templates = new[] { "item", "value", "entry", "record", "data" };

        for (int i = 0; i < count; i++)
        {
            result[i] = $"{RandomChoice(templates)}-{i:D5}";
        }

        return result;
    }

    static int[] GenerateNumberArray(int count)
    {
        var result = new int[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = _rng.Next(0, 10000);
        }
        return result;
    }

    static object[] GenerateObjectArray(int count)
    {
        var result = new object[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new
            {
                id = i,
                name = $"object-{i}",
                value = _rng.Next(100),
                flag = _rng.Next(2) == 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
        return result;
    }

    static T RandomChoice<T>(T[] choices, Random rng)
    {
        return choices[rng.Next(choices.Length)];
    }

    static T RandomChoice<T>(T[] choices)
    {
        return choices[_rng.Next(choices.Length)];
    }

    static void WriteJson(string path, object data)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(path, json);
    }

    // ========== Variant generators for mega bench ==========

    static void GenerateTinyVariant(string path, int seed)
    {
        var rng = new Random(seed);
        var data = new
        {
            name = $"tiny-config-{seed}",
            version = $"{rng.Next(1, 5)}.{rng.Next(0, 10)}.{rng.Next(0, 10)}",
            enabled = rng.Next(2) == 1,
            timeout_ms = rng.Next(1000, 30000),
            servers = new[]
            {
                new { host = RandomChoice(new[] { "localhost", "127.0.0.1", "0.0.0.0" }, rng), port = 8000 + rng.Next(1000) },
                new { host = RandomChoice(new[] { "192.168.1.1", "10.0.0.1" }, rng), port = 9000 + rng.Next(1000) }
            },
            features = new
            {
                logging = rng.Next(2) == 1,
                metrics = rng.Next(2) == 1,
                tracing = rng.Next(2) == 1
            }
        };
        WriteJson(path, data);
    }

    static void GenerateMediumVariant(string path, int seed)
    {
        var rng = new Random(seed);
        var services = new List<object>();
        for (int i = 0; i < 50; i++)
        {
            services.Add(new
            {
                name = $"service-{i:D3}",
                endpoint = $"http://localhost:{8000 + i}",
                version = $"{rng.Next(1, 5)}.{rng.Next(0, 10)}.{rng.Next(0, 10)}",
                healthy = rng.Next(2) == 1,
                latency_ms = rng.Next(10, 1000),
                tags = new[] { "prod", "api", "v1" }
            });
        }

        var data = new
        {
            name = $"medium-config-{seed}",
            timestamp = DateTime.UtcNow.ToString("O"),
            cluster = new
            {
                id = $"cluster-{seed:D2}",
                region = RandomChoice(new[] { "us-east-1", "us-west-2", "eu-central-1" }, rng),
                zone = $"{(char)('a' + rng.Next(3))}",
                services = services
            },
            config = new
            {
                log_level = RandomChoice(new[] { "DEBUG", "INFO", "WARN", "ERROR" }, rng),
                max_connections = 500 + rng.Next(1500),
                timeout_sec = 10 + rng.Next(50),
                retry_count = rng.Next(1, 6),
                features = new
                {
                    cache = rng.Next(2) == 1,
                    compression = rng.Next(2) == 1,
                    auth = rng.Next(2) == 1,
                    tls = rng.Next(2) == 1
                }
            }
        };
        WriteJson(path, data);
    }

    static void GenerateLargeVariant(string path, int seed)
    {
        var rng = new Random(seed);
        var devices = new List<object>();
        for (int i = 0; i < 500; i++)
        {
            var sensors = new List<object>();
            for (int j = 0; j < 10; j++)
            {
                sensors.Add(new
                {
                    id = $"sensor-{i:D4}-{j:D2}",
                    type = RandomChoice(new[] { "temperature", "humidity", "pressure", "light" }, rng),
                    value = rng.Next(0, 100),
                    unit = RandomChoice(new[] { "celsius", "fahrenheit", "kelvin" }, rng),
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + rng.Next(-3600, 3600)
                });
            }

            devices.Add(new
            {
                device_id = $"device-{i:D4}",
                name = $"IoT-Device-{i}",
                location = $"Building-{i % 5}-Floor-{i % 10}",
                status = RandomChoice(new[] { "online", "offline", "standby", "error" }, rng),
                battery_percent = rng.Next(5, 100),
                sensors = sensors,
                metadata = new
                {
                    manufacturer = RandomChoice(new[] { "Acme", "TechCorp", "IoTPlus" }, rng),
                    model = $"IoT-{rng.Next(2000, 2030)}",
                    firmware = $"{rng.Next(1, 5)}.{rng.Next(0, 10)}.{rng.Next(0, 20)}",
                    last_update = DateTime.UtcNow.AddSeconds(-rng.Next(86400)).ToString("O")
                }
            });
        }

        var data = new
        {
            name = $"large-config-{seed}",
            description = "Large IoT sensor network dataset",
            version = $"{rng.Next(1, 5)}.0",
            devices = devices,
            statistics = new
            {
                total_devices = 500,
                total_sensors = 5000,
                active_devices = 480 + rng.Next(21),
                data_points_per_hour = 18000 + rng.Next(2000)
            }
        };
        WriteJson(path, data);
    }

    static void GenerateStressVariant(string path, int seed)
    {
        var rng = new Random(seed);
        // Stress test: deep nesting (32 levels) with randomized values
        var current = (object)new { value = RandomChoice(new[] { "leaf-a", "leaf-b", "leaf-c" }, rng), depth = 32 };

        for (int i = 31; i >= 0; i--)
        {
            current = new
            {
                level = i,
                nested = current,
                data = RandomChoice(new[] { "data-a", "data-b", "data-c", "data-d" }, rng)
            };
        }

        var data = new
        {
            name = $"stress-test-{seed}",
            description = "Stress test with nested objects and large arrays",
            deep_nesting = current,
            large_arrays = new
            {
                strings = GenerateStringArray(1000, rng),
                numbers = GenerateNumberArray(1000, rng),
                objects = GenerateObjectArray(100, rng)
            },
            wide_object = GenerateWideObject(200, rng)
        };
        WriteJson(path, data);
    }

    static Dictionary<string, object> GenerateWideObject(int fieldCount, Random rng)
    {
        var result = new Dictionary<string, object>();
        for (int i = 0; i < fieldCount; i++)
        {
            result[$"field_{i:D3}"] = new
            {
                value = rng.Next(1000),
                name = $"field-{i}",
                enabled = rng.Next(2) == 1
            };
        }
        return result;
    }

    static string[] GenerateStringArray(int count, Random rng)
    {
        var result = new string[count];
        var templates = new[] { "item", "value", "entry", "record", "data" };

        for (int i = 0; i < count; i++)
        {
            result[i] = $"{RandomChoice(templates, rng)}-{i:D5}";
        }

        return result;
    }

    static int[] GenerateNumberArray(int count, Random rng)
    {
        var result = new int[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = rng.Next(0, 10000);
        }
        return result;
    }

    static object[] GenerateObjectArray(int count, Random rng)
    {
        var result = new object[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new
            {
                id = i,
                name = $"object-{i}",
                value = rng.Next(100),
                flag = rng.Next(2) == 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + rng.Next(-86400, 86400)
            };
        }
        return result;
    }
}
