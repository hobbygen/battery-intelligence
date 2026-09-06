using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Analytics;

/// <summary>
/// The degradation trend is smoothed with a Theil–Sen median slope so noise does
/// not move it (docs/estimation-strategy.md section 4). Traceability: R-027.
/// </summary>
public sealed class DegradationTrendTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BelowTheSpanFloor_ReportsCalculating()
    {
        List<RetentionPoint> points = [.. Enumerable.Range(0, 10)
            .Select(i => new RetentionPoint(Start.AddDays(i), 90.0))];

        DegradationTrend trend = DegradationTrendCalculator.Compute(points, minSpanDays: 30, minSamples: 8, Start.AddDays(10));

        Assert.False(trend.IsAvailable);
        Assert.Equal(EstimateConfidence.Calculating, trend.Confidence);
    }

    [Fact]
    public void AFlatNoisySeries_ProducesNoMeaningfulSlope()
    {
        // True slope is zero; ±1.5% of measurement jitter around 85%.
        double[] jitter = [0.8, -1.1, 0.4, -0.6, 1.3, -0.9, 0.2, -1.4, 0.7, -0.3, 1.0, -0.5, 0.9, -1.2, 0.1];
        List<RetentionPoint> points = [.. jitter.Select((j, i) => new RetentionPoint(Start.AddDays(i * 5), 85.0 + j))];

        DegradationTrend trend = DegradationTrendCalculator.Compute(points, minSpanDays: 30, minSamples: 8, points[^1].TimestampUtc);

        Assert.True(trend.IsAvailable);
        Assert.InRange(trend.SlopePercentPerMonth!.Value, -0.3, 0.3); // essentially flat
    }

    [Fact]
    public void ARealDecline_IsDetected()
    {
        // -0.5 %/month = -0.5/30 per day.
        List<RetentionPoint> points = [.. Enumerable.Range(0, 20)
            .Select(i => new RetentionPoint(Start.AddDays(i * 4), 90.0 - (i * 4 * 0.5 / 30.0)))];

        DegradationTrend trend = DegradationTrendCalculator.Compute(points, minSpanDays: 30, minSamples: 8, points[^1].TimestampUtc);

        Assert.True(trend.IsAvailable);
        Assert.InRange(trend.SlopePercentPerMonth!.Value, -0.6, -0.4);
        Assert.NotNull(trend.ProjectedRetentionPercentIn90Days);
    }

    [Fact]
    public void OneOutlierReading_DoesNotSwingTheSlope()
    {
        List<RetentionPoint> clean = [.. Enumerable.Range(0, 20)
            .Select(i => new RetentionPoint(Start.AddDays(i * 4), 88.0 - (i * 4 * 0.3 / 30.0)))];

        DegradationTrend cleanTrend = DegradationTrendCalculator.Compute(clean, 30, 8, clean[^1].TimestampUtc);

        // A single wild full-charge reading in the middle.
        List<RetentionPoint> withOutlier = [.. clean];
        withOutlier[10] = withOutlier[10] with { RetentionPercent = 130.0 };

        DegradationTrend outlierTrend = DegradationTrendCalculator.Compute(withOutlier, 30, 8, withOutlier[^1].TimestampUtc);

        Assert.Equal(cleanTrend.SlopePercentPerMonth!.Value, outlierTrend.SlopePercentPerMonth!.Value, 2);
    }
}
