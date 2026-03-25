using System;
using Xunit;

namespace IronFamily.MegaBench.Bench;

/// <summary>
/// Tests for statistics utility functions.
/// </summary>
public class StatsTests
{
    [Fact]
    public void Percentile_Median_P95_Order()
    {
        // Known dataset
        double[] samples = { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };

        double min = Stats.Percentile(samples, 0);
        double median = Stats.Percentile(samples, 0.5);
        double p95 = Stats.Percentile(samples, 0.95);
        double max = Stats.Percentile(samples, 1.0);

        // Order must be: min <= median <= p95 <= max
        Assert.True(min <= median, $"min ({min}) should be <= median ({median})");
        Assert.True(median <= p95, $"median ({median}) should be <= p95 ({p95})");
        Assert.True(p95 <= max, $"p95 ({p95}) should be <= max ({max})");
    }

    [Fact]
    public void Cv_ZeroMean_ReturnsInfinity()
    {
        double cv = Stats.Cv(0, 1.0);
        Assert.Equal(double.PositiveInfinity, cv);
    }

    [Fact]
    public void Cv_NonZeroMean_ReturnsStdevDividedByMean()
    {
        double cv = Stats.Cv(mean: 10, stdev: 2);
        Assert.Equal(0.2, cv, precision: 6);
    }

    [Fact]
    public void Compute_ReturnsValidSummary()
    {
        double[] samples = { 1.0, 2.0, 3.0, 4.0, 5.0 };
        var summary = Stats.Compute(samples);

        Assert.Equal(5, summary.Count);
        Assert.Equal(1.0, summary.Min);
        Assert.Equal(5.0, summary.Max);
        Assert.True(summary.Median > 0);
        Assert.True(summary.Mean > 0);
        Assert.True(summary.Stdev >= 0);
        Assert.True(summary.Cv >= 0);
    }
}
