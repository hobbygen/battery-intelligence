using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Analytics;

/// <summary>
/// The smoothed capacity-retention trend (docs/estimation-strategy.md section 4;
/// specification section 53). Uses a Theil–Sen median slope so a single anomalous
/// full-charge reading cannot swing the line
/// (docs/traceability.md R-027 "noise does not move the trend").
/// </summary>
public static class DegradationTrendCalculator
{
    /// <param name="points">Retention-over-time samples, any order.</param>
    /// <param name="minSpanDays">Minimum span before a trend is offered.</param>
    /// <param name="minSamples">Minimum sample count before a trend is offered.</param>
    /// <param name="nowUtc">Unused directly; kept for signature symmetry with the other calculators.</param>
    public static DegradationTrend Compute(
        IReadOnlyList<RetentionPoint> points, int minSpanDays, int minSamples, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(points);
        _ = nowUtc;

        if (points.Count == 0)
        {
            return DegradationTrend.NotEnoughData(0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        }

        List<RetentionPoint> ordered = [.. points.OrderBy(p => p.TimestampUtc)];
        DateTimeOffset from = ordered[0].TimestampUtc;
        DateTimeOffset to = ordered[^1].TimestampUtc;
        double spanDays = (to - from).TotalDays;

        if (ordered.Count < minSamples || spanDays < minSpanDays)
        {
            return DegradationTrend.NotEnoughData(ordered.Count, from, to);
        }

        double[] xs = [.. ordered.Select(p => (p.TimestampUtc - from).TotalDays)];
        double[] ys = [.. ordered.Select(p => p.RetentionPercent)];

        double? slopePerDay = LinearFit.TheilSenSlope(xs, ys);
        if (slopePerDay is not double slope)
        {
            return DegradationTrend.NotEnoughData(ordered.Count, from, to);
        }

        double intercept = LinearFit.InterceptThroughMedian(xs, ys, slope);
        double residual = LinearFit.MedianAbsoluteResidual(xs, ys, slope, intercept);

        double projectionDay = spanDays + 90.0;
        double projected = Math.Clamp((slope * projectionDay) + intercept, 0.0, 100.0);

        EstimateConfidence confidence =
            spanDays >= 90 && ordered.Count >= 20 && residual < 1.0 ? EstimateConfidence.High
            : spanDays >= 45 && ordered.Count >= 12 && residual < 2.0 ? EstimateConfidence.Medium
            : EstimateConfidence.Low;

        return new DegradationTrend(
            Math.Round(slope * 30.0, 3),
            Math.Round(projected, 1),
            confidence,
            ordered.Count,
            from,
            to);
    }
}
