using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace IronFamily.MegaBench.Bench;

/// <summary>
/// Statistical computation utilities for benchmark sampling.
/// </summary>
public static class Stats
{
    /// <summary>
    /// Compute median of a sorted array.
    /// </summary>
    public static double Median(double[] samples)
    {
        if (samples.Length == 0)
            return 0;

        Array.Sort(samples);
        int mid = samples.Length / 2;

        if (samples.Length % 2 == 0)
            return (samples[mid - 1] + samples[mid]) / 2.0;
        else
            return samples[mid];
    }

    /// <summary>
    /// Compute percentile at position p (0..1).
    /// Uses deterministic method: sort + ceil(p*(n-1)).
    /// </summary>
    public static double Percentile(double[] samples, double p)
    {
        if (samples.Length == 0)
            return 0;

        if (p < 0 || p > 1)
            throw new ArgumentOutOfRangeException(nameof(p), "Must be between 0 and 1");

        Array.Sort(samples);

        // Deterministic: index = ceil(p * (n - 1))
        int index = (int)Math.Ceiling(p * (samples.Length - 1));
        index = Math.Min(index, samples.Length - 1);

        return samples[index];
    }

    /// <summary>
    /// Compute mean and standard deviation.
    /// </summary>
    public static (double mean, double stdev) MeanStdev(double[] samples)
    {
        if (samples.Length == 0)
            return (0, 0);

        double mean = samples.Average();
        double variance = samples.Average(x => (x - mean) * (x - mean));
        double stdev = Math.Sqrt(variance);

        return (mean, stdev);
    }

    /// <summary>
    /// Compute coefficient of variation (stdev / mean).
    /// Returns +Infinity if mean == 0.
    /// </summary>
    public static double Cv(double mean, double stdev)
    {
        if (mean == 0)
            return double.PositiveInfinity;

        return stdev / mean;
    }

    /// <summary>
    /// Compute Median Absolute Deviation (MAD).
    /// MAD = median(|x_i - median(x)|)
    /// </summary>
    public static double MedianAbsoluteDeviation(double[] samples)
    {
        if (samples.Length == 0)
            return 0;

        double median = Median(samples);
        var deviations = samples.Select(x => Math.Abs(x - median)).ToArray();
        return Median(deviations);
    }

    /// <summary>
    /// Bootstrap 95% confidence interval for median.
    /// Resamples data 1000 times, computes median each time.
    /// Returns (lower_95, upper_95) percentiles of bootstrap distribution.
    /// </summary>
    public static (double lower, double upper) BootstrapConfidenceInterval(
        double[] samples,
        int iterations = 1000)
    {
        if (samples.Length < 2)
            return (samples.FirstOrDefault(), samples.FirstOrDefault());

        var rng = new Random(42); // Deterministic seed
        var bootstrapMedians = new double[iterations];

        for (int i = 0; i < iterations; i++)
        {
            // Resample with replacement
            var resample = new double[samples.Length];
            for (int j = 0; j < samples.Length; j++)
            {
                resample[j] = samples[rng.Next(samples.Length)];
            }
            bootstrapMedians[i] = Median(resample);
        }

        // Return 2.5th and 97.5th percentiles
        double lower = Percentile(bootstrapMedians, 0.025);
        double upper = Percentile(bootstrapMedians, 0.975);

        return (lower, upper);
    }

    /// <summary>
    /// Remove outliers using Median Absolute Deviation method.
    /// Removes points where |x - median| > 3 * MAD.
    /// Returns (trimmed_samples, samples_removed).
    /// If &lt; 70% of original samples remain, returns null (too much data lost).
    /// </summary>
    public static (double[] trimmed, int removed)? TrimOutliers(double[] samples)
    {
        if (samples.Length < 2)
            return (samples, 0);

        double median = Median(samples);
        double mad = MedianAbsoluteDeviation(samples);

        // If MAD is 0, no trimming needed
        if (mad == 0)
            return (samples, 0);

        double threshold = 3 * mad;
        var trimmed = samples.Where(x => Math.Abs(x - median) <= threshold).ToArray();

        int removed = samples.Length - trimmed.Length;
        double retentionRate = (double)trimmed.Length / samples.Length;

        // Fail if we removed too much data (< 70% retention)
        if (retentionRate < 0.70)
            return null;

        return (trimmed, removed);
    }

    /// <summary>
    /// Complete statistical summary of samples.
    /// </summary>
    public static StatsSummary Compute(double[] samples)
    {
        if (samples.Length == 0)
            return new StatsSummary();

        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);

        double min = sorted[0];
        double max = sorted[sorted.Length - 1];
        double median = Median(sorted);
        double p95 = Percentile(sorted, 0.95);

        var (mean, stdev) = MeanStdev(sorted);
        double cv = Cv(mean, stdev);

        return new StatsSummary
        {
            Count = samples.Length,
            Min = min,
            Max = max,
            Mean = mean,
            Stdev = stdev,
            Cv = cv,
            Median = median,
            P95 = p95
        };
    }
}

/// <summary>
/// Statistical summary of a sample set with statistical hardening.
/// Includes p95, bootstrap CI, MAD-based trimming, outlier counts.
/// </summary>
public class StatsSummary
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; }

    [JsonPropertyName("mean")]
    public double Mean { get; set; }

    [JsonPropertyName("stdev")]
    public double Stdev { get; set; }

    [JsonPropertyName("cv")]
    public double Cv { get; set; }

    [JsonPropertyName("median")]
    public double Median { get; set; }

    [JsonPropertyName("p95")]
    public double P95 { get; set; }

    // Statistical Hardening (PHASE 2)

    [JsonPropertyName("mad")]
    public double Mad { get; set; }

    [JsonPropertyName("ci95Lower")]
    public double Ci95Lower { get; set; }

    [JsonPropertyName("ci95Upper")]
    public double Ci95Upper { get; set; }

    [JsonPropertyName("ciWidth")]
    public double CiWidth { get; set; }

    [JsonPropertyName("originalSamples")]
    public int OriginalSamples { get; set; }

    [JsonPropertyName("trimmedSamples")]
    public int TrimmedSamples { get; set; }

    [JsonPropertyName("outliersRemoved")]
    public int OutliersRemoved { get; set; }
}
