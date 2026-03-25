using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ed25519Vendor.SommerEngineering;
using IronConfig;
using IronConfig.Crypto;
using IronConfig.ILog;
using IronConfig.Iupd;
using IronFamily.MegaBench.Datasets.ILog;
using IronFamily.MegaBench.Datasets.IronCfg;
using IronFamily.MegaBench.Datasets.IUpd;
using IronFamily.MegaBench.Datasets.RealWorld;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Child process benchmark runner: executes a single benchmark job and outputs JSON result to stdout.
/// Launched by parent with isolated environment (no TieredPGO, ReadyToRun, QuickJit).
/// </summary>
public static class CompetitorsBenchChild
{
    /// <summary>
    /// Run a single benchmark job and output CompetitorResult JSON to stdout.
    /// Expected environment:
    ///   BENCH_ENGINE={engine}
    ///   BENCH_SIZE={size}
    ///   BENCH_PROFILE={profile}
    ///   BENCH_CODEC={codec}
    ///   BENCH_MODE={encode|decode}
    ///   BENCH_REALWORLD={true|false}
    /// </summary>
    public static int RunJob()
    {
        try
        {
            string engine = Environment.GetEnvironmentVariable("BENCH_ENGINE") ?? "";
            string size = Environment.GetEnvironmentVariable("BENCH_SIZE") ?? "";
            string profile = Environment.GetEnvironmentVariable("BENCH_PROFILE") ?? "";
            string codec = Environment.GetEnvironmentVariable("BENCH_CODEC") ?? "";
            string mode = Environment.GetEnvironmentVariable("BENCH_MODE") ?? "encode";
            bool isRealWorld = Environment.GetEnvironmentVariable("BENCH_REALWORLD") == "true";

            if (string.IsNullOrEmpty(engine) || string.IsNullOrEmpty(size) || string.IsNullOrEmpty(codec))
            {
                WriteErrorJson("Missing environment variables", null);
                return 2;
            }

            // Set deterministic seed
            Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");

            // Load or generate dataset
            byte[] payload;
            string datasetId;
            string sizeLabel;

            if (isRealWorld)
            {
                // Load real-world dataset based on engine and size
                var realWorldId = MapToRealWorldDatasetId(engine, size);
                if (realWorldId == null)
                {
                    WriteErrorJson($"Unsupported realworld dataset: {engine}_{size}", null);
                    return 2;
                }

                payload = RealWorldDatasetGenerator.GenerateDataset(realWorldId.Value);
                datasetId = realWorldId.Value.ToString();
                sizeLabel = size;
            }
            else
            {
                // Load synthetic dataset based on engine, size, and profile
                datasetId = $"{engine}_{size}";
                sizeLabel = size;

                if (engine == "icfg")
                {
                    payload = IronCfgDatasetGenerator.GenerateDataset(size);
                }
                else if (engine == "ilog")
                {
                    if (string.IsNullOrEmpty(profile))
                    {
                        WriteErrorJson("ILOG requires BENCH_PROFILE", null);
                        return 2;
                    }
                    if (!Enum.TryParse<IlogProfile>(profile, out var ilogProfile))
                    {
                        WriteErrorJson($"Unknown ILOG profile: {profile}", null);
                        return 2;
                    }
                    payload = ILogDatasetGenerator.GenerateDataset(size, ilogProfile);
                }
                else if (engine == "iupd")
                {
                    if (string.IsNullOrEmpty(profile))
                    {
                        WriteErrorJson("IUPD requires BENCH_PROFILE", null);
                        return 2;
                    }
                    if (!Enum.TryParse<IupdProfile>(profile, out var iupdProfile))
                    {
                        WriteErrorJson($"Unknown IUPD profile: {profile}", null);
                        return 2;
                    }
                    payload = IUpdDatasetGenerator.GenerateDataset(size, iupdProfile);
                }
                else
                {
                    WriteErrorJson($"Unknown engine: {engine}", null);
                    return 2;
                }
            }

            // Run warmup (5 iterations)
            for (int i = 0; i < 5; i++)
            {
                RunBenchmarkIteration(engine, profile, codec, mode, payload);
            }

            // Run measurement (7 iterations)
            var timings = new long[7];
            var allocations = new long[7];

            for (int i = 0; i < 7; i++)
            {
                var (elapsed, allocated) = RunBenchmarkIteration(engine, profile, codec, mode, payload);
                timings[i] = elapsed.Ticks;
                allocations[i] = allocated;
            }

            // Convert ticks to microseconds
            var timingsUs = new double[timings.Length];
            for (int i = 0; i < timings.Length; i++)
                timingsUs[i] = timings[i] / 10.0;

            // Compute stats
            Array.Sort(timingsUs);
            Array.Sort(allocations);

            // Measure signing separately for ILOG/AUDITED and IUPD/SECURE/OPTIMIZED
            double[]? signSamplesUs = null;
            long signatureLenBytes = 0;

            if (engine == "ilog" && profile == "AUDITED")
            {
                // Extract the real hash from the encoded ILOG data
                byte[]? realHash = ExtractHashFromIlog(payload);

                if (realHash != null && realHash.Length == 32)
                {
                    // Warmup
                    for (int i = 0; i < 5; i++)
                    {
                        MeasureSign(realHash);
                    }

                    // Measurement
                    var signTimings = new long[7];
                    for (int i = 0; i < 7; i++)
                    {
                        signTimings[i] = MeasureSign(realHash).Ticks;
                    }

                    signSamplesUs = new double[signTimings.Length];
                    for (int i = 0; i < signTimings.Length; i++)
                        signSamplesUs[i] = signTimings[i] / 10.0;

                    Array.Sort(signSamplesUs);
                    signatureLenBytes = 64; // Ed25519 signature is 64 bytes
                }
            }
            else if (engine == "iupd" && (profile == "SECURE" || profile == "OPTIMIZED"))
            {
                // Extract the real manifest hash from the encoded IUPD data
                byte[]? manifestHash = ExtractHashFromIupd(payload);

                if (manifestHash != null && manifestHash.Length == 32)
                {
                    // Warmup
                    for (int i = 0; i < 5; i++)
                    {
                        MeasureSign(manifestHash);
                    }

                    // Measurement
                    var signTimings = new long[7];
                    for (int i = 0; i < 7; i++)
                    {
                        signTimings[i] = MeasureSign(manifestHash).Ticks;
                    }

                    signSamplesUs = new double[signTimings.Length];
                    for (int i = 0; i < signTimings.Length; i++)
                        signSamplesUs[i] = signTimings[i] / 10.0;

                    Array.Sort(signSamplesUs);
                    signatureLenBytes = 64; // Ed25519 signature is 64 bytes
                }
            }

            // Measure witness verification for ILOG/AUDITED profile
            double[]? witnessVerifySamplesUs = null;

            if (engine == "ilog" && profile == "AUDITED")
            {
                // Encode to get the ILOG data for witness verification
                var encoder = new IlogEncoder();
                var encodeOptions = new IlogEncodeOptions
                {
                    Ed25519PrivateKey32 = new byte[32].AsMemory(),
                    Ed25519PublicKey32 = new byte[32].AsMemory()
                };
                byte[] ilogEncoded = encoder.Encode(payload, IlogProfile.AUDITED, encodeOptions);

                // Warmup
                for (int i = 0; i < 5; i++)
                {
                    MeasureWitnessVerify(ilogEncoded);
                }

                // Measurement
                var witnessVerifyTimings = new long[7];
                for (int i = 0; i < 7; i++)
                {
                    witnessVerifyTimings[i] = MeasureWitnessVerify(ilogEncoded).Ticks;
                }

                witnessVerifySamplesUs = new double[witnessVerifyTimings.Length];
                for (int i = 0; i < witnessVerifyTimings.Length; i++)
                    witnessVerifySamplesUs[i] = witnessVerifyTimings[i] / 10.0;

                Array.Sort(witnessVerifySamplesUs);
            }

            var result = new CompetitorResult
            {
                CodecName = codec,
                Engine = engine,
                Profile = string.IsNullOrEmpty(profile) ? null : profile,
                SizeLabel = sizeLabel,
                InputBytes = payload.Length,
                EncodedBytes = payload.Length, // Placeholder, parent will verify
                DecodedBytes = payload.Length,
                AllocBytes = allocations[3], // Median allocation
                EncodeSamplesUs = timingsUs,
                SignSamplesUs = signSamplesUs,
                SignatureLenBytes = signatureLenBytes,
                WitnessVerifySamplesUs = witnessVerifySamplesUs,
                RoundtripOk = true // Verify roundtrip in parent
            };

            // Output JSON to stdout (1 line)
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            string json = JsonSerializer.Serialize(result, options);
            Console.WriteLine(json);

            return 0;
        }
        catch (Exception ex)
        {
            WriteErrorJson(ex.Message, ex.StackTrace);
            return 2;
        }
    }

    private static (TimeSpan elapsed, long allocated) RunBenchmarkIteration(
        string engine, string profile, string codec, string mode, byte[] payload)
    {
        // Get codec instance based on engine and codec name
        var codecKind = engine switch
        {
            "icfg" => CodecKind.ICFG,
            "ilog" => CodecKind.ILOG,
            "iupd" => CodecKind.IUPD_Manifest,
            _ => throw new InvalidOperationException($"Unknown engine: {engine}")
        };

        ICompetitorCodec codecInstance;

        if (codec == "protobuf")
        {
            codecInstance = new ProtobufCodec(codecKind);
        }
        else if (codec == "messagepack")
        {
            codecInstance = new MessagePackCodec(codecKind);
        }
        else if (codec == "cbor")
        {
            codecInstance = new CborCodec(codecKind);
        }
        else if (codec == "flatbuffers" && engine == "icfg")
        {
            codecInstance = new FlatBuffersCodec(codecKind);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported codec for {engine}: {codec}");
        }

        var sw = Stopwatch.StartNew();
        var memBefore = GC.GetTotalMemory(true);

        // Encode
        byte[] encoded = codecInstance.Encode(payload);

        // Decode
        byte[] decoded = codecInstance.Decode(encoded);

        sw.Stop();
        var memAfter = GC.GetTotalMemory(false);

        // Verify roundtrip (basic check)
        if (decoded.Length != payload.Length)
        {
            throw new InvalidOperationException($"Roundtrip failed: decoded length {decoded.Length} != input {payload.Length}");
        }

        return (sw.Elapsed, Math.Max(0, memAfter - memBefore));
    }

    private static TimeSpan MeasureSign(byte[] realHash)
    {
        // These are the hardcoded bench keypair from IlogEncoder/IupdWriter
        byte[] privateKey = new byte[]
        {
            0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57,
            0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
            0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67,
            0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F
        };

        // Sign the real hash extracted from the ILOG/IUPD encoded data
        byte[] signature = new byte[64];
        var sw = Stopwatch.StartNew();
        Ed25519.Sign(privateKey, realHash, signature);
        sw.Stop();

        return sw.Elapsed;
    }

    private static byte[]? ExtractHashFromIlog(byte[] ilogData)
    {
        try
        {
            if (ilogData == null || ilogData.Length < 120)
                return null;

            // Find L4 SEAL block in the ILOG data
            // Search for block magic (0x424F4C42) and type 0x0005 (little-endian)
            const uint blockMagic = 0x424F4C42;
            const ushort l4Type = 0x0005;

            for (int i = 16; i < ilogData.Length - 56; i++)
            {
                var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(ilogData.AsSpan(i, 4));
                if (magic == blockMagic)
                {
                    var type = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(ilogData.AsSpan(i + 4, 2));
                    if (type == l4Type)
                    {
                        // Found L4 block. Hash is at payload offset 4-35 (32 bytes)
                        // L4 block header is 56 bytes, so hash starts at i+56+4
                        if (i + 60 + 32 <= ilogData.Length)
                        {
                            var hash = new byte[32];
                            Array.Copy(ilogData, i + 60, hash, 0, 32);
                            return hash;
                        }
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ExtractHashFromIupd(byte[] iupdData)
    {
        try
        {
            if (iupdData == null || iupdData.Length < 100)
                return null;

            // Parse IUPD structure to extract manifest hash
            // IUPD file format: Header -> ChunkTable -> Manifest -> [Signature] -> Payloads
            // Manifest contains: Header(24) + Dependencies(8*N) + ApplyOrder(4*M) + CRC32+Reserved(8) + [Signature(68)]

            // Read manifest offset from header
            if (iupdData.Length < 37)
                return null;

            // Header format (V2): [magic:4][version:1][profile:1][flags:4][headerSize:2][reserved:1][ctOffset:8][mOffset:8][pOffset:8]
            ulong manifestOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(iupdData.AsSpan(21, 8));

            if (manifestOffset >= (ulong)iupdData.Length || manifestOffset + 24 > (ulong)iupdData.Length)
                return null;

            // Parse manifest header to determine size
            var depCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(iupdData.AsSpan((int)manifestOffset + 8, 4));
            var applyOrderCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(iupdData.AsSpan((int)manifestOffset + 12, 4));
            ulong manifestSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(iupdData.AsSpan((int)manifestOffset + 16, 8));

            // Manifest hash is BLAKE3 of manifest data EXCLUDING the last 8 bytes (CRC32+reserved)
            // But for simplicity in extraction, derive it fresh by computing BLAKE3 of manifest structure
            // This is the same as what's signed in the file

            if (manifestOffset + manifestSize > (ulong)iupdData.Length)
                return null;

            // Extract manifest data (excluding CRC32+reserved footer)
            int manifestDataLength = (int)(manifestSize - 8);
            byte[] manifestData = new byte[manifestDataLength];
            Array.Copy(iupdData, (int)manifestOffset, manifestData, 0, manifestDataLength);

            // Compute BLAKE3-32 hash
            byte[] hash = new byte[32];
            Blake3Ieee.Compute(manifestData, hash);

            return hash;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Map (engine, size) to RealWorldDatasetId enum value.
    /// Returns null if mapping is not supported.
    /// </summary>
    private static RealWorldDatasetId? MapToRealWorldDatasetId(string engine, string size)
    {
        return (engine, size) switch
        {
            ("icfg", "10KB") => RealWorldDatasetId.RW_ICFG_DEVICE_TREE_10KB,
            ("icfg", "100KB") => RealWorldDatasetId.RW_ICFG_DEVICE_TREE_100KB,
            ("ilog", "1MB") => RealWorldDatasetId.RW_ILOG_PLC_EVENTS_1MB,
            ("ilog", "10MB") => RealWorldDatasetId.RW_ILOG_PLC_EVENTS_10MB,
            ("iupd", "1MB") => RealWorldDatasetId.RW_IUPD_MANIFEST_1MB,
            ("iupd", "10MB") => RealWorldDatasetId.RW_IUPD_MANIFEST_10MB,
            _ => null
        };
    }

    private static TimeSpan MeasureWitnessVerify(byte[] ilogData)
    {
        // Measure witness chain verification time for ILOG/AUDITED profile
        var decoder = new IlogDecoder();
        var sw = Stopwatch.StartNew();
        decoder.Verify(ilogData);
        sw.Stop();
        return sw.Elapsed;
    }

    private static void WriteErrorJson(string message, string? stackTrace)
    {
        var error = new { error = message, stackTrace = stackTrace };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string json = JsonSerializer.Serialize(error, options);
        Console.WriteLine(json);
    }
}
