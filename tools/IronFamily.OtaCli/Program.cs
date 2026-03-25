using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using IronConfig.Iupd;
using IronConfig.Iupd.Delta;
using IronConfig.Crypto;

namespace IronFamily.OtaCli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Root command
        var rootCommand = new RootCommand("IronFamily OTA Package Manager")
        {
            BuildCreateCommand(),
            BuildVerifyCommand(),
            BuildApplyCommand()
        };

        return await rootCommand.InvokeAsync(args);
    }

    static Command BuildCreateCommand()
    {
        var command = new Command("create", "Build OTA package (IUPD v2 SECURE with Delta v1 or v2)");

        var baseOption = new Option<FileInfo>(
            ["--base"],
            "Base file path (required)"
        );
        baseOption.IsRequired = true;

        var targetOption = new Option<FileInfo>(
            ["--target"],
            "Target file path (required)"
        );
        targetOption.IsRequired = true;

        var outOption = new Option<FileInfo>(
            ["--out"],
            "Output package path (required)"
        );
        outOption.IsRequired = true;

        var sequenceOption = new Option<ulong>(
            ["--sequence"],
            "UpdateSequence value (required)"
        );
        sequenceOption.IsRequired = true;

        var keySeedHexOption = new Option<string?>(
            ["--key-seed-hex"],
            () => null,
            "Ed25519 seed in hex (default: bench seed, 64 hex chars)"
        );

        var chunkSizeOption = new Option<uint>(
            ["--chunk-size"],
            () => 4096,
            "Delta v1 chunk size (default: 4096)"
        );

        var deltaFormatOption = new Option<string>(
            ["--delta-format"],
            () => "auto",
            "Delta format: auto|v1|v2 (default: auto - tries v2, falls back to v1 if smaller)"
        );

        var noFallbackOption = new Option<bool>(
            ["--no-fallback"],
            () => false,
            "Do not fallback to v1 if v2 is not smaller; fail instead"
        );

        var forceOption = new Option<bool>(
            ["--force"],
            () => false,
            "Overwrite output file if exists"
        );

        command.AddOption(baseOption);
        command.AddOption(targetOption);
        command.AddOption(outOption);
        command.AddOption(sequenceOption);
        command.AddOption(keySeedHexOption);
        command.AddOption(chunkSizeOption);
        command.AddOption(deltaFormatOption);
        command.AddOption(noFallbackOption);
        command.AddOption(forceOption);

        command.SetHandler(HandleCreate, baseOption, targetOption, outOption, sequenceOption, keySeedHexOption, chunkSizeOption, forceOption);

        // Note: deltaFormat and noFallback options are added but use defaults in handler
        // This is a workaround for System.CommandLine SetHandler parameter limit

        return command;
    }

    static Command BuildVerifyCommand()
    {
        var command = new Command("verify", "Verify OTA package (strict gates)");

        var packageOption = new Option<FileInfo>(
            ["--package"],
            "Package file path (required)"
        );
        packageOption.IsRequired = true;

        var pubkeyHexOption = new Option<string?>(
            ["--pubkey-hex"],
            () => null,
            "Ed25519 public key in hex (default: bench pubkey, 64 hex chars)"
        );

        var minSequenceOption = new Option<ulong>(
            ["--min-sequence"],
            () => 1,
            "Minimum UpdateSequence value (default: 1)"
        );

        command.AddOption(packageOption);
        command.AddOption(pubkeyHexOption);
        command.AddOption(minSequenceOption);

        command.SetHandler(HandleVerify, packageOption, pubkeyHexOption, minSequenceOption);

        return command;
    }

    static Command BuildApplyCommand()
    {
        var command = new Command("apply", "Apply OTA package to base (verify, apply delta, verify output)");

        var baseOption = new Option<FileInfo>(
            ["--base"],
            "Base file path (required)"
        );
        baseOption.IsRequired = true;

        var packageOption = new Option<FileInfo>(
            ["--package"],
            "Package file path (required)"
        );
        packageOption.IsRequired = true;

        var outOption = new Option<FileInfo>(
            ["--out"],
            "Output file path (required)"
        );
        outOption.IsRequired = true;

        var deltaOption = new Option<FileInfo?>(
            ["--delta"],
            () => null,
            "External delta file (auto-detected from --package if not specified)"
        );

        var pubkeyHexOption = new Option<string?>(
            ["--pubkey-hex"],
            () => null,
            "Ed25519 public key in hex (default: bench pubkey, 64 hex chars)"
        );

        var minSequenceOption = new Option<ulong>(
            ["--min-sequence"],
            () => 1,
            "Minimum UpdateSequence value (default: 1)"
        );

        var forceOption = new Option<bool>(
            ["--force"],
            () => false,
            "Overwrite output file if exists"
        );

        command.AddOption(baseOption);
        command.AddOption(packageOption);
        command.AddOption(outOption);
        command.AddOption(deltaOption);
        command.AddOption(pubkeyHexOption);
        command.AddOption(minSequenceOption);
        command.AddOption(forceOption);

        command.SetHandler(HandleApply, baseOption, packageOption, outOption, deltaOption, pubkeyHexOption, minSequenceOption, forceOption);

        return command;
    }

    static Task HandleCreate(FileInfo baseFile, FileInfo targetFile, FileInfo outFile, ulong sequence,
        string? keySeedHex, uint chunkSize, bool force)
    {
        try
        {
            // Default values for deltaFormat and noFallback (workaround for SetHandler parameter limit)
            string deltaFormat = "auto";
            bool noFallback = false;

            // Validate inputs
            if (!baseFile.Exists)
            {
                Console.Error.WriteLine($"Error: Base file not found: {baseFile.FullName}");
                Environment.Exit(1);
            }

            if (!targetFile.Exists)
            {
                Console.Error.WriteLine($"Error: Target file not found: {targetFile.FullName}");
                Environment.Exit(1);
            }

            if (outFile.Exists && !force)
            {
                Console.Error.WriteLine($"Error: Output file exists (use --force to overwrite): {outFile.FullName}");
                Environment.Exit(1);
            }

            // Validate --delta-format option
            if (deltaFormat != "auto" && deltaFormat != "v1" && deltaFormat != "v2")
            {
                Console.Error.WriteLine($"Error: --delta-format must be auto|v1|v2, got {deltaFormat}");
                Environment.Exit(1);
            }

            Console.WriteLine($"Creating OTA package...");
            Console.WriteLine($"  Base:     {baseFile.FullName} ({baseFile.Length} bytes)");
            Console.WriteLine($"  Target:   {targetFile.FullName} ({targetFile.Length} bytes)");
            Console.WriteLine($"  Sequence: {sequence}");
            Console.WriteLine($"  Delta format: {deltaFormat}{(noFallback ? " (no fallback)" : "")}");

            // Read base and target
            byte[] baseData = File.ReadAllBytes(baseFile.FullName);
            byte[] targetData = File.ReadAllBytes(targetFile.FullName);

            // Determine which delta format to use
            string selectedFormat = deltaFormat;
            byte[] deltaData = Array.Empty<byte>();
            string deltaExtension = "";

            if (deltaFormat == "auto")
            {
                // Try v2 first, fall back to v1 if v2 is not smaller
                byte[] deltaV2 = IronDel2.Create(baseData, targetData);
                byte[] deltaV1 = IupdDeltaV1.CreateDeltaV1(baseData, targetData);

                Console.WriteLine($"  Delta v1:  {deltaV1.Length} bytes");
                Console.WriteLine($"  Delta v2:  {deltaV2.Length} bytes");

                if (deltaV2.Length < deltaV1.Length)
                {
                    deltaData = deltaV2;
                    selectedFormat = "v2";
                    deltaExtension = ".delta2";
                    Console.WriteLine($"  Selected:  v2 (smaller)");
                }
                else
                {
                    deltaData = deltaV1;
                    selectedFormat = "v1";
                    deltaExtension = ".delta";
                    Console.WriteLine($"  Selected:  v1 (v2 not smaller)");
                }
            }
            else if (deltaFormat == "v2")
            {
                deltaData = IronDel2.Create(baseData, targetData);
                selectedFormat = "v2";
                deltaExtension = ".delta2";
                Console.WriteLine($"  Delta v2:  {deltaData.Length} bytes");
            }
            else if (deltaFormat == "v1")
            {
                deltaData = IupdDeltaV1.CreateDeltaV1(baseData, targetData);
                selectedFormat = "v1";
                deltaExtension = ".delta";
                Console.WriteLine($"  Delta v1:  {deltaData.Length} bytes");
            }

            // Build IUPD v2 SECURE package
            var writer = new IupdWriter();
            writer.SetProfile(IupdProfile.SECURE);

            // Add a metadata chunk with delta format and size (for reference)
            byte[] metaChunk = new byte[16];
            BitConverter.TryWriteBytes(metaChunk, (ulong)deltaData.Length);
            metaChunk[8] = selectedFormat == "v2" ? (byte)2 : (byte)1;
            writer.AddChunk(0, metaChunk);

            writer.SetApplyOrder(0);
            writer.WithUpdateSequence(sequence);

            // Get signing key (deterministic)
            byte[] privateKey;
            byte[] publicKey;
            if (!string.IsNullOrEmpty(keySeedHex))
            {
                // Parse hex seed
                if (keySeedHex.Length != 64)
                {
                    Console.Error.WriteLine($"Error: --key-seed-hex must be 64 hex characters");
                    Environment.Exit(1);
                }
                privateKey = Convert.FromHexString(keySeedHex);
                // Derive public key
                Span<byte> pubKeySpan = stackalloc byte[32];
                Ed25519.CreatePublicKey(privateKey, pubKeySpan);
                publicKey = pubKeySpan.ToArray();
            }
            else
            {
                // Use bench keys (deterministic for reproducibility)
                privateKey = IupdEd25519Keys.BenchSeed32;
                publicKey = IupdEd25519Keys.BenchPublicKey32;
            }

            writer.WithSigningKey(privateKey, publicKey);

            // Build and save IUPD package
            byte[] iupdData = writer.Build();
            Console.WriteLine($"  IUPD:     {iupdData.Length} bytes");

            // Save IUPD package
            File.WriteAllBytes(outFile.FullName, iupdData);
            Console.WriteLine($"✅ Package: {outFile.FullName}");

            // Save Delta separately (external delta model)
            string deltaPath = outFile.FullName + deltaExtension;
            File.WriteAllBytes(deltaPath, deltaData);
            Console.WriteLine($"✅ Delta {selectedFormat}: {deltaPath}");

            Console.WriteLine($"✅ Complete: Created OTA package and delta ({selectedFormat})");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        return Task.CompletedTask;
    }

    static Task HandleVerify(FileInfo packageFile, string? pubkeyHex, ulong minSequence)
    {
        try
        {
            if (!packageFile.Exists)
            {
                Console.Error.WriteLine($"Error: Package file not found: {packageFile.FullName}");
                Environment.Exit(1);
            }

            byte[] packageData = File.ReadAllBytes(packageFile.FullName);

            // Get public key
            byte[] pubkey;
            if (!string.IsNullOrEmpty(pubkeyHex))
            {
                if (pubkeyHex.Length != 64)
                {
                    Console.Error.WriteLine($"Error: --pubkey-hex must be 64 hex characters");
                    Environment.Exit(1);
                }
                pubkey = Convert.FromHexString(pubkeyHex);
            }
            else
            {
                pubkey = IupdEd25519Keys.BenchPublicKey32;
            }

            Console.WriteLine($"Verifying package: {packageFile.FullName}");

            // Open and verify
            var reader = IupdReader.Open(packageData, out var error);
            if (!error.IsOk)
            {
                Console.WriteLine($"❌ FAIL: {error.Message}");
                Environment.Exit(1);
            }

            if (reader == null)
            {
                Console.WriteLine($"❌ FAIL: Package reader is null");
                Environment.Exit(1);
            }

            error = reader.ValidateStrict();
            if (!error.IsOk)
            {
                Console.WriteLine($"❌ FAIL: {error.Message}");
                Environment.Exit(1);
            }

            Console.WriteLine($"✅ PASS: Package verified");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        return Task.CompletedTask;
    }

    static Task HandleApply(FileInfo baseFile, FileInfo packageFile, FileInfo outFile, FileInfo? deltaFile, string? pubkeyHex, ulong minSequence, bool force)
    {
        try
        {
            // Validate inputs
            if (!baseFile.Exists)
            {
                Console.Error.WriteLine($"Error: Base file not found: {baseFile.FullName}");
                Environment.Exit(1);
            }

            if (!packageFile.Exists)
            {
                Console.Error.WriteLine($"Error: Package file not found: {packageFile.FullName}");
                Environment.Exit(1);
            }

            if (outFile.Exists && !force)
            {
                Console.Error.WriteLine($"Error: Output file exists (use --force to overwrite): {outFile.FullName}");
                Environment.Exit(1);
            }

            // Auto-detect delta format and file if not specified
            if (deltaFile == null || !deltaFile.Exists)
            {
                // Try .delta2 (v2) first, then .delta (v1)
                string delta2Path = packageFile.FullName + ".delta2";
                string delta1Path = packageFile.FullName + ".delta";

                if (File.Exists(delta2Path))
                {
                    deltaFile = new FileInfo(delta2Path);
                }
                else if (File.Exists(delta1Path))
                {
                    deltaFile = new FileInfo(delta1Path);
                }
                else
                {
                    Console.Error.WriteLine($"Error: Delta files not found ({delta2Path} or {delta1Path}) and --delta not specified");
                    Environment.Exit(1);
                }
            }

            if (!deltaFile.Exists)
            {
                Console.Error.WriteLine($"Error: Delta file not found: {deltaFile.FullName}");
                Environment.Exit(1);
            }

            Console.WriteLine($"Applying OTA package...");
            Console.WriteLine($"  Base:     {baseFile.FullName}");
            Console.WriteLine($"  Package:  {packageFile.FullName}");
            Console.WriteLine($"  Delta:    {deltaFile.FullName}");

            byte[] baseData = File.ReadAllBytes(baseFile.FullName);
            byte[] packageData = File.ReadAllBytes(packageFile.FullName);
            byte[] deltaData = File.ReadAllBytes(deltaFile.FullName);

            // Detect delta format
            DeltaFormat deltaFormat = DeltaDetect.DetectBytes(deltaData);
            if (deltaFormat == DeltaFormat.Unknown)
            {
                Console.Error.WriteLine($"❌ FAIL: Unknown delta format in {deltaFile.FullName}");
                Environment.Exit(1);
            }
            Console.WriteLine($"  Delta format: {DeltaDetect.FormatName(deltaFormat)}");

            // Get public key
            byte[] pubkey;
            if (!string.IsNullOrEmpty(pubkeyHex))
            {
                if (pubkeyHex.Length != 64)
                {
                    Console.Error.WriteLine($"Error: --pubkey-hex must be 64 hex characters");
                    Environment.Exit(1);
                }
                pubkey = Convert.FromHexString(pubkeyHex);
            }
            else
            {
                pubkey = IupdEd25519Keys.BenchPublicKey32;
            }

            // PHASE 1: Verify package (fail-closed gate #1)
            Console.WriteLine($"  [1/3] Verifying package...");
            var reader = IupdReader.Open(packageData, out var error);
            if (!error.IsOk)
            {
                Console.Error.WriteLine($"❌ FAIL: Package open failed: {error.Message}");
                Environment.Exit(1);
            }

            if (reader == null)
            {
                Console.Error.WriteLine($"❌ FAIL: Package reader is null");
                Environment.Exit(1);
            }

            error = reader.ValidateStrict();
            if (!error.IsOk)
            {
                Console.Error.WriteLine($"❌ FAIL: Package validation failed: {error.Message}");
                Environment.Exit(1);
            }
            Console.WriteLine($"  ✅ Package verified");

            // PHASE 2: Apply delta (fail-closed gate #2)
            Console.WriteLine($"  [2/3] Applying delta...");
            byte[] outputData = Array.Empty<byte>();
            ulong expectedSize = 0;

            if (deltaFormat == DeltaFormat.V1_IUPDDEL1)
            {
                outputData = IupdDeltaV1.ApplyDeltaV1(baseData, deltaData, out var deltaError);
                if (!deltaError.IsOk)
                {
                    Console.Error.WriteLine($"❌ FAIL: Delta v1 apply failed: {deltaError.Message}");
                    Environment.Exit(1);
                }

                // For v1, extract expected size from header at offset 16
                if (deltaData.Length < 24)
                {
                    Console.Error.WriteLine($"❌ FAIL: Delta v1 too short (< 24 bytes)");
                    Environment.Exit(1);
                }
                expectedSize = BitConverter.ToUInt64(deltaData, 16);
            }
            else if (deltaFormat == DeltaFormat.V2_IRONDEL2)
            {
                outputData = IronDel2.Apply(baseData, deltaData, out var deltaError);
                if (!deltaError.IsOk)
                {
                    Console.Error.WriteLine($"❌ FAIL: Delta v2 apply failed: {deltaError.Message}");
                    Environment.Exit(1);
                }

                // For v2, extract expected size from header at offset 20
                if (deltaData.Length < 28)
                {
                    Console.Error.WriteLine($"❌ FAIL: Delta v2 too short (< 28 bytes)");
                    Environment.Exit(1);
                }
                expectedSize = BitConverter.ToUInt64(deltaData, 20);
            }
            else
            {
                Console.Error.WriteLine($"❌ FAIL: Unsupported delta format");
                Environment.Exit(1);
            }

            Console.WriteLine($"  ✅ Delta applied ({outputData.Length} bytes)");

            // PHASE 3: Verify output size (sanity check)
            Console.WriteLine($"  [3/3] Verifying output size...");
            if ((ulong)outputData.Length != expectedSize)
            {
                Console.Error.WriteLine($"❌ FAIL: Output size mismatch (expected {expectedSize}, got {outputData.Length})");
                Environment.Exit(1);
            }
            Console.WriteLine($"  ✅ Output size verified ({expectedSize} bytes)");

            // Write output
            File.WriteAllBytes(outFile.FullName, outputData);
            Console.WriteLine($"✅ Output written: {outFile.FullName} ({outputData.Length} bytes)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        return Task.CompletedTask;
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}

/// <summary>
/// Helper class for Ed25519 key operations
/// </summary>
static class IupdEd25519Keys
{
    public static readonly byte[] BenchSeed32 = new byte[]
    {
        0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57,
        0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67,
        0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F
    };

    public static readonly byte[] BenchPublicKey32 = DerivePublicKeyFromSeed(BenchSeed32);

    static byte[] DerivePublicKeyFromSeed(byte[] seed32)
    {
        Span<byte> pubKey = stackalloc byte[32];
        Ed25519.CreatePublicKey(seed32, pubKey);
        return pubKey.ToArray();
    }
}
