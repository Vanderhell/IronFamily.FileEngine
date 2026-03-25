using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronCert.Benchmarking;

internal static class Program
{
    private const int EXIT_OK = 0;
    private const int EXIT_FAIL = 1;
    private const int EXIT_USAGE = 2;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return Help();

            var cmd = args[0].ToLowerInvariant();

            return cmd switch
            {
                "help" or "--help" or "-h" => Help(),
                "list" => CmdList(),
                "validate" => CmdValidate(args.Skip(1).ToArray()),
                "vectors" => CmdVectors(args.Skip(1).ToArray()),
                "certify" => CmdCertify(args.Skip(1).ToArray()),
                "bench" => CmdBench(args.Skip(1).ToArray()),
                "benchmark" => CmdBenchmark(args.Skip(1).ToArray()),
                "generate" => CmdGenerate(args.Skip(1).ToArray()),
                "parity" => CmdParity(args.Skip(1).ToArray()),
                _ => Usage($"Unknown command: {args[0]}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL internal code=EXCEPTION offset=0 msg=\"{Short(ex.Message)}\"");
            return EXIT_FAIL;
        }
    }

    // ----------------------------
    // Commands
    // ----------------------------

    private static int CmdList()
    {
        // Keep this stable; it's referenced by docs/CI.
        Console.WriteLine("Engines:");
        Console.WriteLine("  ironcfg (.icfg)");
        Console.WriteLine("  bjv     (.bjv)");
        Console.WriteLine("  bjx     (.icfs)");
        Console.WriteLine("  icfx    (.icfx)");
        Console.WriteLine("  icxs    (.icxs)");
        Console.WriteLine("  icf2    (.icf2)");
        Console.WriteLine("  ilog    (.ilog)");
        Console.WriteLine("  iupd    (.iupd)");
        return EXIT_OK;
    }

    private static int CmdValidate(string[] args)
    {
        // ironcert validate <engine|auto> --file <path> [--fast|--strict]
        if (args.Length < 1) return Usage("validate requires <engine|auto>");

        var engine = args[0].ToLowerInvariant();
        string? file = null;
        var mode = ValidateMode.Fast;

        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--file" && i + 1 < args.Length)
            {
                file = args[++i];
                continue;
            }

            if (a == "--fast") { mode = ValidateMode.Fast; continue; }
            if (a == "--strict") { mode = ValidateMode.Strict; continue; }
        }

        if (string.IsNullOrWhiteSpace(file))
            return Usage("validate requires --file <path>");

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"FAIL {engine} {ModeName(mode)} {file} code=FILE_NOT_FOUND offset=0 msg=\"File not found\"");
            return EXIT_USAGE;
        }

        var bytes = File.ReadAllBytes(file);

        // Detect engine (auto or explicit)
        var detected = DetectEngine(bytes);

        if (engine == "auto")
        {
            if (detected == Engine.Unknown)
            {
                Console.Error.WriteLine($"FAIL auto {ModeName(mode)} {file} code=INVALID_MAGIC offset=0 msg=\"Unknown file magic\"");
                return EXIT_FAIL;
            }

            engine = EngineName(detected);
        }
        else
        {
            // engine mismatch check
            if (detected != Engine.Unknown && EngineName(detected) != engine)
            {
                Console.Error.WriteLine($"FAIL {engine} {ModeName(mode)} {file} code=ENGINE_MISMATCH offset=0 msg=\"File magic does not match engine\"");
                return EXIT_FAIL;
            }
        }

        // Basic validation: magic + minimal length
        var res = BasicValidate(engine, bytes, mode);

        if (res.Ok)
        {
            Console.WriteLine($"OK {Cap(engine)} {ModeName(mode)} {file} bytes={bytes.Length}");
            return EXIT_OK;
        }
        else
        {
            Console.Error.WriteLine($"FAIL {engine} {ModeName(mode)} {file} code={res.Code} offset={res.Offset} msg=\"{Short(res.Message)}\"");
            return EXIT_FAIL;
        }
    }

    private static int CmdVectors(string[] args)
    {
        // ironcert vectors <engine>
        if (args.Length < 1) return Usage("vectors requires <engine>");

        var engine = args[0].ToLowerInvariant();
        var manifestPath = Path.Combine("vectors/small", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine("FAIL vectors code=MANIFEST_MISSING offset=0 msg=\"vectors/small/manifest.json not found\"");
            return EXIT_FAIL;
        }

        var vectors = LoadVectors(manifestPath)
            .Where(v => string.Equals(v.Engine, engine, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (vectors.Count == 0)
        {
            Console.Error.WriteLine($"FAIL vectors code=NO_VECTORS offset=0 msg=\"No vectors for engine '{engine}'\"");
            return EXIT_FAIL;
        }

        Console.WriteLine($"{Cap(engine)}:");

        int ok = 0;
        int fail = 0;

        foreach (var v in vectors)
        {
            var label = v.Id ?? Path.GetFileNameWithoutExtension(v.Bin ?? v.Json ?? "vector");
            var path = v.Bin ?? v.Json ?? "";
            var pad = PadDots(label, 45);

            if (string.IsNullOrWhiteSpace(v.Bin))
            {
                Console.WriteLine($"  {label} {pad} SKIP");
                continue;
            }

            if (!File.Exists(v.Bin!))
            {
                Console.WriteLine($"  {label} {pad} FAIL");
                fail++;
                continue;
            }

            var bytes = File.ReadAllBytes(v.Bin!);

            // vectors command uses fast mode by design (platform rule)
            var r = BasicValidate(engine, bytes, ValidateMode.Fast);

            var expectOk = (v.Expect ?? "OK").Equals("OK", StringComparison.OrdinalIgnoreCase);

            var pass = expectOk ? r.Ok : !r.Ok;
            if (pass)
            {
                Console.WriteLine($"  {label} {pad} OK");
                ok++;
            }
            else
            {
                Console.WriteLine($"  {label} {pad} FAIL");
                fail++;
            }
        }

        if (fail == 0)
        {
            Console.WriteLine($"RESULT: PASS ({ok}/{vectors.Count})");
            return EXIT_OK;
        }
        else
        {
            Console.WriteLine($"RESULT: FAIL ({ok}/{vectors.Count})");
            return EXIT_FAIL;
        }
    }

    private static int CmdCertify(string[] args)
    {
        // ironcert certify <engine>
        if (args.Length < 1) return Usage("certify requires <engine>");
        var engine = args[0].ToLowerInvariant();

        // IRONCFG uses comprehensive certification gate
        if (engine == "ironcfg")
        {
            return IronCfgCertify.Run();
        }

        Console.WriteLine("========================================");
        Console.WriteLine($"Certification: {Cap(engine)}");
        Console.WriteLine("========================================");
        Console.WriteLine();

        // [1/3] vectors
        Console.WriteLine("[1/3] Validating golden vectors...");
        var vectorsExit = CmdVectors(new[] { engine });
        Console.WriteLine();

        if (vectorsExit != EXIT_OK)
        {
            Console.WriteLine("========================================");
            Console.WriteLine($"✗ Certification FAILED for {Cap(engine)}");
            Console.WriteLine("========================================");
            return EXIT_FAIL;
        }

        // [2/3] parity (optional)
        Console.WriteLine("[2/3] Checking parity...");
        var parityExit = TryRunParity(engine);
        if (parityExit == null)
        {
            Console.WriteLine("??  Parity testing not yet implemented");
        }
        else if (parityExit.Value == 0)
        {
            Console.WriteLine("✓  Parity VERIFIED");
        }
        else
        {
            Console.WriteLine("!! Parity FAILED");
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine($"✗ Certification FAILED for {Cap(engine)}");
            Console.WriteLine("========================================");
            return EXIT_FAIL;
        }
        Console.WriteLine();

        // [3/3] bench (optional)
        Console.WriteLine("[3/3] Running quick benchmark...");
        var benchExit = TryRunBench(engine);
        if (benchExit == null)
        {
            Console.WriteLine("??  Benchmarks not available for this engine");
        }
        else if (benchExit.Value == 0)
        {
            Console.WriteLine("✓  Bench completed");
        }
        else
        {
            Console.WriteLine("!! Bench FAILED");
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine($"✗ Certification FAILED for {Cap(engine)}");
            Console.WriteLine("========================================");
            return EXIT_FAIL;
        }

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine($"✓ Certification PASSED for {Cap(engine)}");
        Console.WriteLine("========================================");

        return EXIT_OK;
    }

    private static int CmdBench(string[] args)
    {
        // ironcert bench <engine> [--quick]
        if (args.Length < 1) return Usage("bench requires <engine>");
        var engine = args[0].ToLowerInvariant();
        bool quick = args.Any(a => a.Equals("--quick", StringComparison.OrdinalIgnoreCase));

        var rc = TryRunBench(engine, quick);
        if (rc == null)
        {
            Console.Error.WriteLine($"FAIL bench code=BENCH_NOT_AVAILABLE offset=0 msg=\"Bench not available for engine '{engine}'\"");
            return EXIT_FAIL;
        }
        return rc.Value == 0 ? EXIT_OK : EXIT_FAIL;
    }

    private static int CmdBenchmark(string[] args)
    {
        // ironcert benchmark <engine> --dataset <path> [--iterations <n>]
        return BenchmarkCommand.Execute(args);
    }

    private static int CmdGenerate(string[] args)
    {
        if (args.Length < 1) return Usage("generate requires <engine>");
        var engine = args[0].ToLowerInvariant();

        return engine switch
        {
            "ironcfg" => IronCfgGenerator.Generate(),
            "ilog" => IlogGenerator.Generate(),
            "iupd" => IupdGenerator.Generate(),
            _ => Usage($"Generate not implemented for engine '{engine}'")
        };
    }

    private static int CmdParity(string[] args)
    {
        if (args.Length < 1) return Usage("parity requires <engine>");
        var engine = args[0].ToLowerInvariant();

        return engine switch
        {
            "ironcfg" => IronCfgParity.Check(),
            _ => Usage($"Parity not implemented for engine '{engine}'")
        };
    }

    // ----------------------------
    // Validation core (safe + deterministic)
    // ----------------------------

    private static ValidationResult BasicValidate(string engine, byte[] bytes, ValidateMode mode)
    {
        if (bytes.Length < 4)
            return Fail("TRUNCATED", 0, "File too small");

        var magic = ReadMagic(bytes);

        // expected magic by engine (first 4 bytes)
        var expected = engine switch
        {
            "ironcfg" => "ICFG",
            "icf2" => "ICF2",
            "icfx" => "ICFX",
            "icxs" => "ICXS",
            "bjx" => "ICFS", // secure container
            "bjv" => "BJV2", // accept BJV2/BJV4 too (see below)
            "ilog" => "ILOG", // log / stream container
            "iupd" => "IUPD", // update / patch container
            _ => null
        };

        if (expected == null)
            return Fail("UNKNOWN_ENGINE", 0, "Unknown engine");

        if (engine == "bjv")
        {
            // allow BJV2/BJV4
            if (magic != "BJV2" && magic != "BJV4")
                return Fail("INVALID_MAGIC", 0, $"Expected BJV2/BJV4, got {magic}");
        }
        else
        {
            if (magic != expected)
                return Fail("INVALID_MAGIC", 0, $"Expected {expected}, got {magic}");
        }

        if (mode == ValidateMode.Strict)
        {
            // minimal strict: require at least header bytes
            // (We keep this intentionally generic—real semantic validation belongs in engine libs.)
            if (bytes.Length < 8)
                return Fail("TRUNCATED", 4, "Header truncated");
        }

        return Ok();
    }

    // ----------------------------
    // Manifest loading (robust)
    // ----------------------------

    private static List<VectorEntry> LoadVectors(string manifestPath)
    {
        using var fs = File.OpenRead(manifestPath);
        using var doc = JsonDocument.Parse(fs, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = doc.RootElement;

        // Supported shapes:
        // A) { "vectors": [ ... ] }
        // B) { "engines": { "icf2": [ ... ], "icfx": [ ... ] } }
        // C) { "icf2": [ ... ], "icfx": [ ... ] }  (direct engine keys)

        var list = new List<VectorEntry>();

        if (root.TryGetProperty("vectors", out var vectorsArr) && vectorsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in vectorsArr.EnumerateArray())
            {
                var v = ParseVectorEntry(e);
                if (v != null) list.Add(v);
            }
            return list;
        }

if (root.TryGetProperty("engines", out var enginesObj) && enginesObj.ValueKind == JsonValueKind.Object)
{
    foreach (var prop in enginesObj.EnumerateObject())
    {
        var engine = prop.Name;

        // Supported engine shapes:
        // 1) "icf2": [ ... ]
        // 2) "icf2": { "magic": "...", "vectors": [ ... ] }

        if (prop.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in prop.Value.EnumerateArray())
            {
                var v = ParseVectorEntry(e);
                if (v == null) continue;
                v.Engine ??= engine;
                list.Add(v);
            }
            continue;
        }

       if (prop.Value.ValueKind == JsonValueKind.Object &&
    prop.Value.TryGetProperty("vectors", out var engineVectorsArr) &&
    engineVectorsArr.ValueKind == JsonValueKind.Array)
{
    foreach (var e in engineVectorsArr.EnumerateArray())
    {
        var v = ParseVectorEntry(e);
        if (v == null) continue;
        v.Engine ??= engine;
        list.Add(v);
    }
    continue;
}
    }

    return list;
}


        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                // engine key arrays
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                var engine = prop.Name;

                foreach (var e in prop.Value.EnumerateArray())
                {
                    var v = ParseVectorEntry(e);
                    if (v == null) continue;
                    v.Engine ??= engine;
                    list.Add(v);
                }
            }
        }

        return list;
    }

    private static VectorEntry? ParseVectorEntry(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;

        string? engine = GetString(e, "engine") ?? GetString(e, "Engine");
        string? id = GetString(e, "id") ?? GetString(e, "Id");
        string? bin = GetString(e, "bin") ?? GetString(e, "Bin");
        string? json = GetString(e, "json") ?? GetString(e, "Json");
        string? expect = GetString(e, "expect") ?? GetString(e, "Expect");
        string? note = GetString(e, "note") ?? GetString(e, "Note");

        bool? crc = null;
        if (e.TryGetProperty("crc", out var crcEl))
        {
            if (crcEl.ValueKind == JsonValueKind.True) crc = true;
            else if (crcEl.ValueKind == JsonValueKind.False) crc = false;
        }

        // normalize relative paths
        if (!string.IsNullOrWhiteSpace(bin)) bin = NormalizePath(bin);
        if (!string.IsNullOrWhiteSpace(json)) json = NormalizePath(json);

        return new VectorEntry
        {
            Engine = engine?.ToLowerInvariant(),
            Id = id,
            Bin = bin,
            Json = json,
            Crc = crc,
            Expect = expect ?? "OK",
            Note = note
        };
    }

    private static string NormalizePath(string p)
    {
        // keep manifest portable; allow forward slashes
        p = p.Replace('/', Path.DirectorySeparatorChar);
        return p;
    }

    private static string? GetString(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    // ----------------------------
    // Engine detection (magic)
    // ----------------------------

    private static Engine DetectEngine(byte[] bytes)
    {
        if (bytes.Length < 4) return Engine.Unknown;
        var m = ReadMagic(bytes);

        return m switch
        {
            "ICF2" => Engine.Icf2,
            "ICFX" => Engine.Icfx,
            "ICXS" => Engine.Icxs,
            "ICFS" => Engine.Bjx,
            "BJV2" => Engine.Bjv,
            "BJV4" => Engine.Bjv,
            "ILOG" => Engine.Ilog,
            "IUPD" => Engine.Iupd,
            _ => Engine.Unknown
        };
    }

    private static string ReadMagic(byte[] bytes)
    {
        // first 4 bytes ASCII
        if (bytes.Length < 4) return "";
        return Encoding.ASCII.GetString(bytes, 0, 4);
    }

    private static string EngineName(Engine e) => e switch
    {
        Engine.Bjv => "bjv",
        Engine.Bjx => "bjx",
        Engine.Icfx => "icfx",
        Engine.Icxs => "icxs",
        Engine.Icf2 => "icf2",
        Engine.Ilog => "ilog",
        Engine.Iupd => "iupd",
        _ => "unknown"
    };

    // ----------------------------
    // Optional parity / bench hooks
    // ----------------------------

    private static int? TryRunParity(string engine)
    {
        // Only ICF2 parity is expected today.
        if (engine != "icf2") return null;

        // Try common locations (Windows + Linux):
        var candidates = new[]
        {
            Path.Combine("libs", "ironcfg-c", "build", "Release", "parity_icf2.exe"),
            Path.Combine("libs", "ironcfg-c", "build", "Release", "parity_icf2"),
            Path.Combine("libs", "ironcfg-c", "build", "parity_icf2.exe"),
            Path.Combine("libs", "ironcfg-c", "build", "parity_icf2"),
            Path.Combine("build", "Release", "parity_icf2.exe"),
            Path.Combine("build", "Release", "parity_icf2"),
            Path.Combine("build", "parity_icf2.exe"),
            Path.Combine("build", "parity_icf2"),
        };

        var exe = candidates.FirstOrDefault(File.Exists);
        if (exe == null) return null;

        return RunProcess(exe, "");
    }

    private static int? TryRunBench(string engine, bool quick = true)
    {
        if (engine == "ironcfg")
        {
            return IronCfgBench.Run(quick);
        }

        if (engine != "icf2") return null;

        // Prefer `dotnet run` for CI robustness:
        var benchCsproj = Path.Combine("benchmarks", "Icf2Bench", "Icf2Bench.csproj");
        if (File.Exists(benchCsproj))
        {
            var args = quick ? "--quick" : "";
            return RunProcess("dotnet", $"run --project \"{benchCsproj}\" -c Release -- {args}".Trim());
        }

        // Fallback to prebuilt dll if exists:
        var dll = Path.Combine("benchmarks", "Icf2Bench", "bin", "Release", "net8.0", "Icf2Bench.dll");
        if (File.Exists(dll))
        {
            var args = quick ? "--quick" : "";
            return RunProcess("dotnet", $"\"{dll}\" {args}".Trim());
        }

        return null;
    }

    private static int RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
        };

        using var p = Process.Start(psi);
        if (p == null) return 1;
        p.WaitForExit();
        return p.ExitCode;
    }

    // ----------------------------
    // Helpers
    // ----------------------------

    private static int Help()
    {
        Console.WriteLine("ironcert - IRONCFG certification tool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ironcert help");
        Console.WriteLine("  ironcert list");
        Console.WriteLine("  ironcert validate <engine|auto> --file <path> [--fast|--strict]");
        Console.WriteLine("  ironcert vectors <engine>");
        Console.WriteLine("  ironcert certify <engine>");
        Console.WriteLine("  ironcert bench <engine> [--quick]");
        Console.WriteLine("  ironcert generate <engine>   (stub)");
        Console.WriteLine();
        Console.WriteLine("Engines: bjv, bjx, icfx, icxs, icf2, ilog, iupd");
        return EXIT_OK;
    }

    private static int Usage(string msg)
    {
        Console.Error.WriteLine(msg);
        return Help() == EXIT_OK ? EXIT_USAGE : EXIT_USAGE;
    }

    private static string ModeName(ValidateMode m) => m == ValidateMode.Strict ? "strict" : "fast";
    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string PadDots(string label, int width)
    {
        // produce "....." aligned columns
        var n = Math.Max(3, width - label.Length);
        return new string('.', n);
    }

    private static string Short(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Error";
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        if (s.Length > 120) s = s[..120];
        return s;
    }

    private static ValidationResult Ok() => new(true, "OK", 0, "");
    private static ValidationResult Fail(string code, int offset, string msg) => new(false, code, offset, msg);

    // ----------------------------
    // Types
    // ----------------------------

    private enum ValidateMode { Fast, Strict }

    private enum Engine
    {
        Unknown = 0,
        Bjv,
        Bjx,
        Icfx,
        Icxs,
        Icf2,
        Ilog,
        Iupd
    }

    private sealed class VectorEntry
    {
        [JsonPropertyName("engine")]
        public string? Engine { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("bin")]
        public string? Bin { get; set; }

        [JsonPropertyName("json")]
        public string? Json { get; set; }

        [JsonPropertyName("crc")]
        public bool? Crc { get; set; }

        [JsonPropertyName("expect")]
        public string? Expect { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    private readonly record struct ValidationResult(bool Ok, string Code, int Offset, string Message);
}
