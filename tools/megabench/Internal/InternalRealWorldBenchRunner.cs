using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Buffers.Binary;
using IronConfig;
using IronConfig.IronCfg;
using IronConfig.ILog;
using IronConfig.Iupd;
using IronConfig.Iupd.Delta;
using IronFamily.MegaBench.Bench;
using IronFamily.MegaBench.Datasets.RealWorld;

namespace IronFamily.MegaBench.Internal;

public static class InternalRealWorldBenchRunner
{
    private const int WarmupIterations = 3;
    private const int MeasureIterations = 10;

    public static int Run(string[] args)
    {
        string engine = "all";
        bool ciMode = args.Contains("--ci-mode");

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--engine" && i + 1 < args.Length)
            {
                engine = args[i + 1].ToLowerInvariant();
                i++;
            }
        }

        if (engine is not ("all" or "icfg" or "ilog" or "iupd"))
        {
            Console.Error.WriteLine($"Unsupported engine filter: {engine}");
            return 2;
        }

        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        Environment.SetEnvironmentVariable("IRONFAMILY_BENCH_ALLOW_ALL_IUPD_PROFILES", "1");

        var report = new InternalBenchReport
        {
            RunAtUtc = DateTime.UtcNow.ToString("O"),
            EngineFilter = engine,
            CiMode = ciMode,
            Source = "realworld-only"
        };

        try
        {
            if (engine is "all" or "ilog")
                report.IlogProfiles = BenchIlogProfiles(ciMode);

            if (engine is "all" or "iupd")
                report.IupdProfiles = BenchIupdProfiles(ciMode);

            if (engine is "all" or "icfg")
                report.IcfgLayers = BenchIcfgLayers(ciMode);

            report.EngineBaselines = BuildEngineBaselines(report);
            report.ProfileDiffs = BuildProfileDiffs(report);

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string outDir = Path.Combine("artifacts", "_current", $"internal_realworld_{stamp}");
            Directory.CreateDirectory(outDir);

            string jsonPath = Path.Combine(outDir, "internal_realworld_bench.json");
            string mdPath = Path.Combine(outDir, "internal_realworld_bench.md");

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(mdPath, RenderMarkdown(report));

            Console.WriteLine("=== Internal RealWorld Benchmark Complete ===");
            Console.WriteLine($"Results JSON: {jsonPath}");
            Console.WriteLine($"Results MD:   {mdPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Internal realworld benchmark failed: {ex.Message}");
            return 2;
        }
    }

    private static List<IlogProfileResult> BenchIlogProfiles(bool ciMode)
    {
        var outputs = new List<IlogProfileResult>();
        var datasets = ciMode
            ? new[] { RealWorldDatasetId.RW_ILOG_PLC_EVENTS_1MB }
            : new[] { RealWorldDatasetId.RW_ILOG_PLC_EVENTS_1MB, RealWorldDatasetId.RW_ILOG_PLC_EVENTS_10MB };

        foreach (var dataset in datasets)
        {
            byte[] encodedIntegrity = RealWorldDatasetGenerator.GenerateDataset(dataset);
            byte[] eventBytes = ExtractIlogEventData(encodedIntegrity);
            string datasetName = dataset.ToString();

            foreach (IlogProfile profile in Enum.GetValues(typeof(IlogProfile)))
            {
                byte[]? encodedSample = null;

                var encSamples = MeasureUs(() =>
                {
                    var encoder = new IlogEncoder();
                    encodedSample = encoder.Encode(eventBytes, profile);
                });

                var decSamples = MeasureUs(() =>
                {
                    if (encodedSample == null)
                        throw new InvalidOperationException("Encode sample is null");

                    var openErr = IlogReader.Open(encodedSample, out var view);
                    if (openErr != null || view == null)
                        throw new InvalidOperationException(openErr?.Message ?? "ILOG open failed");

                    var strictErr = IlogReader.ValidateStrict(view);
                    if (strictErr != null)
                        throw new InvalidOperationException(strictErr.Message);
                });

                outputs.Add(new IlogProfileResult
                {
                    DatasetId = datasetName,
                    Profile = profile.ToString(),
                    InputBytes = eventBytes.Length,
                    OutputBytes = encodedSample?.Length ?? 0,
                    Encode = Stats.Compute(encSamples),
                    Decode = Stats.Compute(decSamples)
                });
            }
        }

        return outputs;
    }

    private static List<IupdProfileResult> BenchIupdProfiles(bool ciMode)
    {
        var outputs = new List<IupdProfileResult>();
        var datasets = ciMode
            ? new[] { RealWorldDatasetId.RW_IUPD_MANIFEST_1MB }
            : new[] { RealWorldDatasetId.RW_IUPD_MANIFEST_1MB, RealWorldDatasetId.RW_IUPD_MANIFEST_10MB };

        foreach (var dataset in datasets)
        {
            byte[] seedPackage = RealWorldDatasetGenerator.GenerateDataset(dataset);
            byte[] firmwarePayload = ExtractIupdFirmwarePayload(seedPackage);
            byte[] incrementalTargetPayload = CreateIncrementalTargetPayload(firmwarePayload);
            string datasetName = dataset.ToString();

            foreach (IupdProfile profile in Enum.GetValues(typeof(IupdProfile)))
            {
                byte[]? package = null;
                string? decodeError = null;
                int chunkCount = 1;
                bool compressionAttempted = profile.SupportsCompression();
                bool compressionApplied = false;
                string diagnostics = string.Empty;
                byte[] logicalPayload = profile.IsIncremental()
                    ? IronDel2.Create(firmwarePayload, incrementalTargetPayload)
                    : firmwarePayload;

                // Probe compression behavior for this profile on real payload.
                // Writer uses the same compression entry point per chunk.
                if (compressionAttempted)
                {
                    var probe = IupdPayloadCompression.CompressForProfile(logicalPayload, profile);
                    compressionApplied = IsWrappedCompressedPayload(probe, logicalPayload.Length);
                }

                var encSamples = MeasureUs(() =>
                {
                    package = BuildIupdPackage(firmwarePayload, incrementalTargetPayload, profile);
                });
                if (profile.IsIncremental())
                {
                    var d = IronDel2.LastCreateDiagnostics;
                    diagnostics =
                        $"irondel2(total={d.TotalUs}us hash={d.HashUs}us baseChunk={d.BaseChunkingUs}us targetChunk={d.TargetChunkingUs}us baseIndex={d.BaseIndexUs}us match={d.MatchingUs}us merge={d.MergeUs}us encode={d.EncodeUs}us baseChunks={d.BaseChunkCount} targetChunks={d.TargetChunkCount} ops={d.OperationCount})";
                }

                double[] decSamples;
                try
                {
                    decSamples = MeasureUs(() =>
                    {
                        if (package == null)
                            throw new InvalidOperationException("IUPD package is null");

                        var reader = IupdReader.Open(package, out var openErr);
                        if (reader == null || !openErr.IsOk)
                            throw new InvalidOperationException(openErr.Message);

                        var strictErr = reader.ValidateStrict();
                        if (!strictErr.IsOk)
                            throw new InvalidOperationException(strictErr.Message);

                        if (profile.IsIncremental())
                        {
                            var applyEngine = new IupdApplyEngine(reader, new byte[32], ".");
                            var applyErr = applyEngine.ApplyIncremental(firmwarePayload, out var result);
                            if (!applyErr.IsOk)
                                throw new InvalidOperationException(applyErr.Message);
                            if (!result.AsSpan().SequenceEqual(incrementalTargetPayload))
                                throw new InvalidOperationException("Incremental apply output mismatch");
                        }
                        else
                        {
                            byte[] extractedPayload = ExtractIupdFirmwarePayload(package);
                            if (!extractedPayload.AsSpan().SequenceEqual(firmwarePayload))
                                throw new InvalidOperationException("Decoded payload mismatch");
                        }
                    });
                }
                catch (Exception ex)
                {
                    decodeError = ex.Message;
                    decSamples = Array.Empty<double>();
                }

                outputs.Add(new IupdProfileResult
                {
                    DatasetId = datasetName,
                    Profile = profile.ToString(),
                    InputBytes = firmwarePayload.Length,
                    OutputBytes = package?.Length ?? 0,
                    Encode = Stats.Compute(encSamples),
                    Decode = Stats.Compute(decSamples),
                    DecodeStatus = string.IsNullOrEmpty(decodeError) ? "ok" : $"unsupported_or_failed: {decodeError}",
                    ChunkCount = chunkCount,
                    CompressionAttempted = compressionAttempted,
                    CompressionApplied = compressionApplied,
                    CompressedChunkCount = compressionApplied ? chunkCount : 0,
                    CompressionMode = !compressionAttempted
                        ? "not_supported"
                        : (compressionApplied ? "compressed" : "raw_fallback"),
                    Diagnostics = diagnostics
                });
            }
        }

        return outputs;
    }

    private static List<IcfgLayerResult> BenchIcfgLayers(bool ciMode)
    {
        var outputs = new List<IcfgLayerResult>();
        var datasets = ciMode
            ? new[] { RealWorldDatasetId.RW_ICFG_DEVICE_TREE_10KB }
            : new[] { RealWorldDatasetId.RW_ICFG_DEVICE_TREE_10KB, RealWorldDatasetId.RW_ICFG_DEVICE_TREE_100KB };

        foreach (var dataset in datasets)
        {
            byte[] icfg = RealWorldDatasetGenerator.GenerateDataset(dataset);
            string datasetName = dataset.ToString();

            IronCfgValidator.Open(icfg, out var view);
            var header = view.Header;

            var openSamples = MeasureUs(() =>
            {
                var err = IronCfgValidator.Open(icfg, out _);
                if (!err.IsOk)
                    throw new InvalidOperationException($"Open failed: {err.Code}");
            });

            var schemaSamples = MeasureUs(() =>
            {
                var err = IronCfgValidator.Open(icfg, out var localView);
                if (!err.IsOk)
                    throw new InvalidOperationException($"Open failed: {err.Code}");

                var schemaErr = localView.GetSchema(out _);
                if (!schemaErr.IsOk)
                    throw new InvalidOperationException($"GetSchema failed: {schemaErr.Code}");
            });

            var dataSamples = MeasureUs(() =>
            {
                var err = IronCfgValidator.Open(icfg, out var localView);
                if (!err.IsOk)
                    throw new InvalidOperationException($"Open failed: {err.Code}");

                var rootErr = localView.GetRoot(out _);
                if (!rootErr.IsOk)
                    throw new InvalidOperationException($"GetRoot failed: {rootErr.Code}");
            });

            var strictSamples = MeasureUs(() =>
            {
                IronCfgValidator.ResetStrictMetadataCache();
                var openErr = IronCfgValidator.Open(icfg, out var strictView);
                if (!openErr.IsOk)
                    throw new InvalidOperationException($"Open failed: {openErr.Code}");

                var strictErr = IronCfgValidator.ValidateStrict(icfg, strictView);
                if (!strictErr.IsOk)
                    throw new InvalidOperationException($"ValidateStrict failed: {strictErr.Code}");
            });

            outputs.Add(new IcfgLayerResult
            {
                DatasetId = datasetName,
                FileBytes = icfg.Length,
                HasCrc32 = header.HasCrc32,
                HasBlake3 = header.HasBlake3,
                SchemaBytes = header.SchemaSize,
                StringPoolBytes = header.StringPoolSize,
                DataBytes = header.DataSize,
                OpenLayer = Stats.Compute(openSamples),
                SchemaLayer = Stats.Compute(schemaSamples),
                DataLayer = Stats.Compute(dataSamples),
                StrictLayer = Stats.Compute(strictSamples)
            });
        }

        return outputs;
    }

    private static bool IsWrappedCompressedPayload(byte[] payload, int expectedOriginalSize)
    {
        if (payload.Length < 9)
            return false;

        ulong originalSize = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(0, 8));
        byte marker = payload[8];
        return marker == 0x01 && originalSize == (ulong)expectedOriginalSize;
    }

    private static double[] MeasureUs(Action action)
    {
        for (int i = 0; i < WarmupIterations; i++)
            action();

        var samples = new double[MeasureIterations];
        for (int i = 0; i < MeasureIterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            long stop = Stopwatch.GetTimestamp();
            double us = (stop - start) * 1_000_000.0 / Stopwatch.Frequency;
            samples[i] = us;
        }

        return samples;
    }

    private static byte[] ExtractIlogEventData(byte[] ilogFile)
    {
        const int fileHeader = 16;
        const int blockHeader = 72;
        if (ilogFile.Length < fileHeader + blockHeader + 13)
            throw new InvalidOperationException("ILOG file too small for L0 extraction");

        ushort blockType = BinaryPrimitives.ReadUInt16LittleEndian(ilogFile.AsSpan(fileHeader + 4, 2));
        if (blockType != 0x0001)
            throw new InvalidOperationException("First ILOG block is not L0_DATA");

        uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(ilogFile.AsSpan(fileHeader + 0x10, 4));
        int payloadOffset = fileHeader + blockHeader;
        if (payloadOffset + payloadSize > ilogFile.Length)
            throw new InvalidOperationException("L0 payload out of bounds");

        var payload = ilogFile.AsSpan(payloadOffset, (int)payloadSize);
        if (payload.Length < 13)
            throw new InvalidOperationException("L0 payload too short");

        return payload[13..].ToArray();
    }

    private static byte[] ExtractIupdFirmwarePayload(byte[] iupdPackage)
    {
        var reader = IupdReader.Open(iupdPackage, out var err);
        if (reader == null || !err.IsOk)
            throw new InvalidOperationException($"IUPD open failed: {err.Message}");

        using var ms = new MemoryStream();
        for (uint i = 0; i < reader.ChunkCount; i++)
        {
            var payloadErr = reader.GetChunkPayload(i, out var payload);
            if (!payloadErr.IsOk)
                throw new InvalidOperationException($"GetChunkPayload({i}) failed: {payloadErr.Message}");
            ms.Write(payload);
        }

        return ms.ToArray();
    }

    private static byte[] BuildIupdPackage(byte[] baseFirmware, byte[] targetFirmware, IupdProfile profile)
    {
        var writer = new IupdWriter();
        writer.SetProfile(profile);
        if (profile.RequiresSignatureStrict())
            writer.WithUpdateSequence(1);

        if (profile.IsIncremental())
        {
            byte[] patch = IronDel2.Create(baseFirmware, targetFirmware);
            writer.AddChunk(0, patch);
            writer.SetApplyOrder(0);

            byte[] baseHash = new byte[32];
            Blake3Ieee.Compute(baseFirmware, baseHash);
            byte[] targetHash = new byte[32];
            Blake3Ieee.Compute(targetFirmware, targetHash);
            writer.WithIncrementalMetadata(IupdIncrementalMetadata.ALGORITHM_IRONDEL2, baseHash, targetHash);
        }
        else
        {
            writer.AddChunk(0, baseFirmware);
            writer.SetApplyOrder(0);
        }

        return writer.Build();
    }

    private static byte[] CreateIncrementalTargetPayload(byte[] baseFirmware)
    {
        byte[] target = (byte[])baseFirmware.Clone();
        int windowCount = Math.Max(4, Math.Min(16, target.Length / (512 * 1024) + 4));
        int windowSize = Math.Max(256, Math.Min(4096, target.Length / (windowCount * 32)));

        for (int window = 0; window < windowCount; window++)
        {
            int start = (int)(((long)(window + 1) * target.Length) / (windowCount + 1));
            start = Math.Max(0, Math.Min(start, Math.Max(0, target.Length - windowSize)));

            for (int i = 0; i < windowSize && start + i < target.Length; i++)
            {
                int index = start + i;
                target[index] = (byte)(target[index] ^ (byte)((window * 17 + i * 31 + 0x5A) & 0xFF));
            }
        }

        return target;
    }

    private static string RenderMarkdown(InternalBenchReport report)
    {
        var lines = new List<string>
        {
            "# Internal RealWorld Bench",
            "",
            $"RunAtUtc: {report.RunAtUtc}",
            $"EngineFilter: {report.EngineFilter}",
            $"CiMode: {report.CiMode}",
            $"Source: {report.Source}",
            ""
        };

        if (report.EngineBaselines.Count > 0)
        {
            lines.Add("## ENGINE Baselines");
            foreach (var row in report.EngineBaselines)
            {
                lines.Add($"- {row.Engine}: baseline={row.BaselineProfile} enc={row.BaselineEncodeUs:F2}us dec={row.BaselineDecodeUs:F2}us avgEnc={row.AverageEncodeUs:F2}us avgDec={row.AverageDecodeUs:F2}us");
            }
            lines.Add("");
        }

        if (report.IlogProfiles.Count > 0)
        {
            lines.Add("## ILOG Profiles");
            foreach (var row in report.IlogProfiles)
            {
                lines.Add($"- {row.DatasetId} {row.Profile}: enc={row.Encode.Median:F2}us dec={row.Decode.Median:F2}us out={row.OutputBytes}");
            }
            lines.Add("");
        }

        if (report.IupdProfiles.Count > 0)
        {
            lines.Add("## IUPD Profiles");
            foreach (var row in report.IupdProfiles)
            {
                string diagnostics = string.IsNullOrWhiteSpace(row.Diagnostics) ? string.Empty : $" {row.Diagnostics}";
                lines.Add($"- {row.DatasetId} {row.Profile}: enc={row.Encode.Median:F2}us dec={row.Decode.Median:F2}us status={row.DecodeStatus} out={row.OutputBytes} chunks={row.ChunkCount} compAttempted={row.CompressionAttempted} compApplied={row.CompressionApplied} compressedChunks={row.CompressedChunkCount} compMode={row.CompressionMode}{diagnostics}");
            }
            lines.Add("");
        }

        if (report.IcfgLayers.Count > 0)
        {
            lines.Add("## ICFG Layers");
            foreach (var row in report.IcfgLayers)
            {
                lines.Add($"- {row.DatasetId}: open={row.OpenLayer.Median:F2}us schema={row.SchemaLayer.Median:F2}us data={row.DataLayer.Median:F2}us strict={row.StrictLayer.Median:F2}us crc={row.HasCrc32} blake3={row.HasBlake3}");
            }
            lines.Add("");
        }

        if (report.ProfileDiffs.Count > 0)
        {
            lines.Add("## Profile Diffs vs Engine Baseline");
            foreach (var row in report.ProfileDiffs.OrderBy(r => r.Engine).ThenBy(r => r.Profile))
            {
                lines.Add($"- {row.Engine} {row.Profile} vs {row.BaselineProfile}: encDelta={row.EncodeDeltaPct:+0.00;-0.00;0.00}% decDelta={row.DecodeDeltaPct:+0.00;-0.00;0.00}% sizeDelta={row.SizeDeltaPct:+0.00;-0.00;0.00}%");
            }
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static List<EngineBaselineSummary> BuildEngineBaselines(InternalBenchReport report)
    {
        var output = new List<EngineBaselineSummary>();

        if (report.IlogProfiles.Count > 0)
        {
            double fullEncode = report.IlogProfiles.Sum(r => r.Encode.Median);
            double fullDecode = report.IlogProfiles.Sum(r => r.Decode.Median);

            output.Add(new EngineBaselineSummary
            {
                Engine = "ILOG",
                BaselineProfile = "ENGINE_FULL",
                BaselineEncodeUs = fullEncode,
                BaselineDecodeUs = fullDecode,
                AverageEncodeUs = report.IlogProfiles.Average(r => r.Encode.Median),
                AverageDecodeUs = report.IlogProfiles.Average(r => r.Decode.Median),
                FastestEncodeProfile = report.IlogProfiles.OrderBy(r => r.Encode.Median).First().Profile,
                FastestDecodeProfile = report.IlogProfiles.OrderBy(r => r.Decode.Median).First().Profile
            });
        }

        if (report.IupdProfiles.Count > 0)
        {
            double fullEncode = report.IupdProfiles.Sum(r => r.Encode.Median);
            double fullDecode = report.IupdProfiles.Sum(r => r.Decode.Median);

            output.Add(new EngineBaselineSummary
            {
                Engine = "IUPD",
                BaselineProfile = "ENGINE_FULL",
                BaselineEncodeUs = fullEncode,
                BaselineDecodeUs = fullDecode,
                AverageEncodeUs = report.IupdProfiles.Average(r => r.Encode.Median),
                AverageDecodeUs = report.IupdProfiles.Average(r => r.Decode.Median),
                FastestEncodeProfile = report.IupdProfiles.OrderBy(r => r.Encode.Median).First().Profile,
                FastestDecodeProfile = report.IupdProfiles.OrderBy(r => r.Decode.Median).First().Profile
            });
        }

        if (report.IcfgLayers.Count > 0)
        {
            var rows = report.IcfgLayers.Select(r => new
            {
                Total = r.OpenLayer.Median + r.SchemaLayer.Median + r.DataLayer.Median + r.StrictLayer.Median,
                Open = r.OpenLayer.Median,
                Schema = r.SchemaLayer.Median,
                Data = r.DataLayer.Median,
                Strict = r.StrictLayer.Median
            }).ToList();

            output.Add(new EngineBaselineSummary
            {
                Engine = "ICFG",
                BaselineProfile = "ENGINE_ALL_LAYERS",
                BaselineEncodeUs = rows.Average(r => r.Total),
                BaselineDecodeUs = rows.Average(r => r.Total),
                AverageEncodeUs = rows.Average(r => r.Total / 4.0),
                AverageDecodeUs = rows.Average(r => r.Total / 4.0),
                FastestEncodeProfile = "OPEN",
                FastestDecodeProfile = "OPEN"
            });
        }

        return output;
    }

    private static List<ProfileDiffRow> BuildProfileDiffs(InternalBenchReport report)
    {
        var output = new List<ProfileDiffRow>();

        if (report.IlogProfiles.Count > 0)
        {
            double baseEncode = report.IlogProfiles.Sum(r => r.Encode.Median);
            double baseDecode = report.IlogProfiles.Sum(r => r.Decode.Median);
            int baseSize = report.IlogProfiles.Sum(r => r.OutputBytes);

            foreach (var row in report.IlogProfiles)
            {
                output.Add(new ProfileDiffRow
                {
                    Engine = "ILOG",
                    Profile = row.Profile,
                    BaselineProfile = "ENGINE_FULL",
                    EncodeDeltaPct = PercentDelta(row.Encode.Median, baseEncode),
                    DecodeDeltaPct = PercentDelta(row.Decode.Median, baseDecode),
                    SizeDeltaPct = PercentDelta(row.OutputBytes, baseSize)
                });
            }
        }

        if (report.IupdProfiles.Count > 0)
        {
            double baseEncode = report.IupdProfiles.Sum(r => r.Encode.Median);
            double baseDecode = report.IupdProfiles.Sum(r => r.Decode.Median);
            int baseSize = report.IupdProfiles.Sum(r => r.OutputBytes);

            foreach (var row in report.IupdProfiles)
            {
                output.Add(new ProfileDiffRow
                {
                    Engine = "IUPD",
                    Profile = row.Profile,
                    BaselineProfile = "ENGINE_FULL",
                    EncodeDeltaPct = PercentDelta(row.Encode.Median, baseEncode),
                    DecodeDeltaPct = PercentDelta(row.Decode.Median, baseDecode),
                    SizeDeltaPct = PercentDelta(row.OutputBytes, baseSize)
                });
            }
        }

        if (report.IcfgLayers.Count > 0)
        {
            foreach (var row in report.IcfgLayers)
            {
                double total = row.OpenLayer.Median + row.SchemaLayer.Median + row.DataLayer.Median + row.StrictLayer.Median;

                output.Add(new ProfileDiffRow
                {
                    Engine = "ICFG",
                    Profile = "OPEN",
                    BaselineProfile = "ENGINE_ALL_LAYERS",
                    EncodeDeltaPct = PercentDelta(row.OpenLayer.Median, total),
                    DecodeDeltaPct = PercentDelta(row.OpenLayer.Median, total),
                    SizeDeltaPct = 0
                });

                output.Add(new ProfileDiffRow
                {
                    Engine = "ICFG",
                    Profile = "SCHEMA",
                    BaselineProfile = "ENGINE_ALL_LAYERS",
                    EncodeDeltaPct = PercentDelta(row.SchemaLayer.Median, total),
                    DecodeDeltaPct = PercentDelta(row.SchemaLayer.Median, total),
                    SizeDeltaPct = 0
                });

                output.Add(new ProfileDiffRow
                {
                    Engine = "ICFG",
                    Profile = "DATA",
                    BaselineProfile = "ENGINE_ALL_LAYERS",
                    EncodeDeltaPct = PercentDelta(row.DataLayer.Median, total),
                    DecodeDeltaPct = PercentDelta(row.DataLayer.Median, total),
                    SizeDeltaPct = 0
                });

                output.Add(new ProfileDiffRow
                {
                    Engine = "ICFG",
                    Profile = "STRICT",
                    BaselineProfile = "ENGINE_ALL_LAYERS",
                    EncodeDeltaPct = PercentDelta(row.StrictLayer.Median, total),
                    DecodeDeltaPct = PercentDelta(row.StrictLayer.Median, total),
                    SizeDeltaPct = 0
                });
            }
        }

        return output;
    }

    private static double PercentDelta(double value, double baseline)
    {
        if (Math.Abs(baseline) < 1e-9)
            return 0;
        return ((value - baseline) / baseline) * 100.0;
    }

    private static double PercentDelta(int value, int baseline)
    {
        if (baseline == 0)
            return 0;
        return ((value - baseline) * 100.0) / baseline;
    }
}

public class InternalBenchReport
{
    public string RunAtUtc { get; set; } = string.Empty;
    public string EngineFilter { get; set; } = "all";
    public bool CiMode { get; set; }
    public string Source { get; set; } = "realworld-only";
    public List<IlogProfileResult> IlogProfiles { get; set; } = new();
    public List<IupdProfileResult> IupdProfiles { get; set; } = new();
    public List<IcfgLayerResult> IcfgLayers { get; set; } = new();
    public List<EngineBaselineSummary> EngineBaselines { get; set; } = new();
    public List<ProfileDiffRow> ProfileDiffs { get; set; } = new();
}

public class IlogProfileResult
{
    public string DatasetId { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public int InputBytes { get; set; }
    public int OutputBytes { get; set; }
    public StatsSummary Encode { get; set; } = new();
    public StatsSummary Decode { get; set; } = new();
}

public class IupdProfileResult
{
    public string DatasetId { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public int InputBytes { get; set; }
    public int OutputBytes { get; set; }
    public StatsSummary Encode { get; set; } = new();
    public StatsSummary Decode { get; set; } = new();
    public string DecodeStatus { get; set; } = "ok";
    public int ChunkCount { get; set; }
    public bool CompressionAttempted { get; set; }
    public bool CompressionApplied { get; set; }
    public int CompressedChunkCount { get; set; }
    public string CompressionMode { get; set; } = "unknown";
    public string Diagnostics { get; set; } = string.Empty;
}

public class IcfgLayerResult
{
    public string DatasetId { get; set; } = string.Empty;
    public int FileBytes { get; set; }
    public bool HasCrc32 { get; set; }
    public bool HasBlake3 { get; set; }
    public uint SchemaBytes { get; set; }
    public uint StringPoolBytes { get; set; }
    public uint DataBytes { get; set; }
    public StatsSummary OpenLayer { get; set; } = new();
    public StatsSummary SchemaLayer { get; set; } = new();
    public StatsSummary DataLayer { get; set; } = new();
    public StatsSummary StrictLayer { get; set; } = new();
}

public class EngineBaselineSummary
{
    public string Engine { get; set; } = string.Empty;
    public string BaselineProfile { get; set; } = string.Empty;
    public double BaselineEncodeUs { get; set; }
    public double BaselineDecodeUs { get; set; }
    public double AverageEncodeUs { get; set; }
    public double AverageDecodeUs { get; set; }
    public string FastestEncodeProfile { get; set; } = string.Empty;
    public string FastestDecodeProfile { get; set; } = string.Empty;
}

public class ProfileDiffRow
{
    public string Engine { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string BaselineProfile { get; set; } = string.Empty;
    public double EncodeDeltaPct { get; set; }
    public double DecodeDeltaPct { get; set; }
    public double SizeDeltaPct { get; set; }
}
