using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using IronFamily.MegaBench.Bench;

namespace IronFamily.MegaBench.Competitors;

/// <summary>
/// Common harness for measuring competitor codecs.
/// </summary>
public static class CompetitorRunner
{
    private const int SamplesCount = 21;
    private const int WarmupCount = 3;
    private const long TargetTotalUs = 5000; // 5ms per sample

    /// <summary>
    /// Run codec benchmark with statistical sampling.
    /// </summary>
    public static CompetitorResult RunCodec(
        ICompetitorCodec codec,
        byte[] canonicalInput,
        string engine,
        string? profile,
        string sizeLabel)
    {
        var result = new CompetitorResult
        {
            CodecName = codec.Name,
            Engine = engine,
            Profile = profile,
            SizeLabel = sizeLabel,
            InputBytes = canonicalInput.Length
        };

        try
        {
            // Collect GC before measurements
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Record allocation baseline
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);

            // Encode samples
            var encodeSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int i = 0; i < batchN; i++)
                {
                    _ = codec.Encode(canonicalInput);
                }
                return batchN;
            });
            result.EncodeSamplesUs = encodeSamples;
            result.EncodeSummary = Stats.Compute(encodeSamples);

            // Get encoded size
            byte[] encoded = codec.Encode(canonicalInput);
            result.EncodedBytes = encoded.Length;

            // Decode samples
            var decodeSamples = MeasurePerOpUsSamples(batchN =>
            {
                for (int i = 0; i < batchN; i++)
                {
                    _ = codec.Decode(encoded);
                }
                return batchN;
            });
            result.DecodeSamplesUs = decodeSamples;
            result.DecodeSummary = Stats.Compute(decodeSamples);

            // Roundtrip verification
            byte[] decoded = codec.Decode(encoded);
            string inputHash = Convert.ToHexString(SHA256.HashData(canonicalInput));
            string decodedHash = Convert.ToHexString(SHA256.HashData(decoded));
            result.RoundtripOk = inputHash == decodedHash;
            result.DecodedBytes = decoded.Length;

            // Record allocation delta
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            result.AllocBytes = Math.Max(0, allocAfter - allocBefore);
            result.Gen0 = GC.CollectionCount(0) - gen0Before;
            result.Gen1 = GC.CollectionCount(1) - gen1Before;
            result.Gen2 = GC.CollectionCount(2) - gen2Before;

            // Compute normalized metrics (PHASE 4)
            result.ComputeNormalizedMetrics();
        }
        catch (NotImplementedException)
        {
            result.Excluded = true;
            result.ExclusionReason = "NotImplementedCompetitor";
        }
        catch (Exception ex)
        {
            result.Excluded = true;
            result.ExclusionReason = $"Exception: {ex.GetType().Name}: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Measure per-operation microseconds with adaptive batching.
    /// </summary>
    private static double[] MeasurePerOpUsSamples(Func<int, int> opBatch)
    {
        var samples = new List<double>();

        // Warmup
        for (int w = 0; w < WarmupCount; w++)
        {
            _ = opBatch(1);
        }

        // Collect samples
        for (int s = 0; s < SamplesCount; s++)
        {
            int batchN = 1;
            long elapsedUs = 0;

            while (elapsedUs < TargetTotalUs)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int executed = opBatch(batchN);
                sw.Stop();

                elapsedUs = (sw.ElapsedTicks * 1_000_000) / System.Diagnostics.Stopwatch.Frequency;

                if (elapsedUs < TargetTotalUs && elapsedUs > 0)
                {
                    double ratio = (double)TargetTotalUs / elapsedUs;
                    batchN = Math.Max(batchN + 1, (int)(batchN * ratio * 1.1));
                }
            }

            double perOpUs = (double)elapsedUs / batchN;
            samples.Add(perOpUs);
        }

        return samples.ToArray();
    }
}
