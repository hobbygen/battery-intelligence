using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Analytics;

/// <summary>
/// The rolling runtime estimate — confidence tiers, "Calculating…" below the
/// floor, and no screen-off figure without screen-off history
/// (docs/estimation-strategy.md section 3). Traceability: R-080.
/// </summary>
public sealed class RuntimeEstimatorTests
{
    [Fact]
    public void BelowTheDataFloor_ReturnsCalculating()
    {
        RuntimeEstimate e = RuntimeEstimator.Estimate(new RuntimeEstimatorInputs(
            RemainingCapacityMwh: 20_000, BlendedRateMw: 10_000,
            ScreenOnRateMw: null, ScreenOffRateMw: null,
            DataSpanCurrentState: TimeSpan.FromSeconds(20),
            RateCoefficientOfVariation: 0.1, ComparablePeriods: 0));

        Assert.False(e.IsAvailable);
        Assert.Equal(EstimateConfidence.Calculating, e.Confidence);
        Assert.Null(e.AtCurrentUsage);
    }

    [Fact]
    public void NoRemainingCapacity_ReturnsCalculating()
    {
        RuntimeEstimate e = RuntimeEstimator.Estimate(new RuntimeEstimatorInputs(
            RemainingCapacityMwh: null, BlendedRateMw: 10_000,
            ScreenOnRateMw: 12_000, ScreenOffRateMw: 6_000,
            DataSpanCurrentState: TimeSpan.FromMinutes(20),
            RateCoefficientOfVariation: 0.1, ComparablePeriods: 5));

        Assert.Equal(EstimateConfidence.Calculating, e.Confidence);
    }

    [Fact]
    public void ScreenOffWithNoHistory_LeavesThatFigureUnavailable_NotExtrapolated()
    {
        RuntimeEstimate e = RuntimeEstimator.Estimate(new RuntimeEstimatorInputs(
            RemainingCapacityMwh: 20_000, BlendedRateMw: 10_000,
            ScreenOnRateMw: 10_000, ScreenOffRateMw: null,
            DataSpanCurrentState: TimeSpan.FromMinutes(10),
            RateCoefficientOfVariation: 0.2, ComparablePeriods: 2));

        Assert.NotNull(e.AtCurrentUsage);
        Assert.NotNull(e.ScreenOn);
        Assert.Null(e.ScreenOff);
    }

    [Fact]
    public void FifteenMinutesLowVarianceAndHistory_IsHighConfidence()
    {
        RuntimeEstimate e = RuntimeEstimator.Estimate(new RuntimeEstimatorInputs(
            RemainingCapacityMwh: 20_000, BlendedRateMw: 10_000,
            ScreenOnRateMw: 11_000, ScreenOffRateMw: 5_000,
            DataSpanCurrentState: TimeSpan.FromMinutes(18),
            RateCoefficientOfVariation: 0.12, ComparablePeriods: 4));

        Assert.Equal(EstimateConfidence.High, e.Confidence);
        Assert.Equal(2.0, e.AtCurrentUsage!.Value.TotalHours, 1);
    }

    [Theory]
    [InlineData(6, 0.3, EstimateConfidence.Medium)]
    [InlineData(2, 0.9, EstimateConfidence.Low)]
    public void MidTiers_MatchTheTable(double spanMinutes, double cov, EstimateConfidence expected)
    {
        RuntimeEstimate e = RuntimeEstimator.Estimate(new RuntimeEstimatorInputs(
            RemainingCapacityMwh: 20_000, BlendedRateMw: 10_000,
            ScreenOnRateMw: null, ScreenOffRateMw: null,
            DataSpanCurrentState: TimeSpan.FromMinutes(spanMinutes),
            RateCoefficientOfVariation: cov, ComparablePeriods: 1));

        Assert.Equal(expected, e.Confidence);
    }
}
