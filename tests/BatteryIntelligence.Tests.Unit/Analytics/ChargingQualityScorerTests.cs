using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Analytics;

/// <summary>
/// <c>ChargingQualityV1</c> — scored against the battery's own baseline, Unavailable
/// below five prior sessions, thermal component dropped + renormalised with no
/// sensor (docs/estimation-strategy.md section 6). Traceability: R-081.
/// </summary>
public sealed class ChargingQualityScorerTests
{
    [Fact]
    public void FewerThanFivePriorSessions_IsUnavailable()
    {
        ChargingQuality q = ChargingQualityScorer.Score(
            new ChargingQualityInputs(
                SessionAvgRateMw: 30_000, SessionRateCoefficientOfVariation: 0.1,
                PersonalMedianRateMw: 30_000, PriorComparableSessions: 3,
                Interruptions: 0, SessionHours: 1.5, ThermalFractionAboveWarn: null),
            minPriorSessions: 5);

        Assert.Null(q.Score);
        Assert.Equal(EstimateConfidence.Calculating, q.Confidence);
    }

    [Fact]
    public void NoThermalSensor_DropsThatComponent_AndRenormalises()
    {
        ChargingQuality q = ChargingQualityScorer.Score(
            new ChargingQualityInputs(
                SessionAvgRateMw: 30_000, SessionRateCoefficientOfVariation: 0.1,
                PersonalMedianRateMw: 30_000, PriorComparableSessions: 20,
                Interruptions: 0, SessionHours: 1.5, ThermalFractionAboveWarn: null),
            minPriorSessions: 5);

        Assert.NotNull(q.Score);
        Assert.Equal(1.0, q.Components.Where(c => c.Contributed).Sum(c => c.NormalisedWeight), 3);
        Assert.Contains(q.Components, c => c.Key == "thermal" && !c.Contributed);
    }

    [Fact]
    public void AFastCleanCharge_ScoresHigh()
    {
        ChargingQuality q = ChargingQualityScorer.Score(
            new ChargingQualityInputs(
                SessionAvgRateMw: 33_000, SessionRateCoefficientOfVariation: 0.05,
                PersonalMedianRateMw: 30_000, PriorComparableSessions: 30,
                Interruptions: 0, SessionHours: 1.4, ThermalFractionAboveWarn: 0.0),
            minPriorSessions: 5);

        Assert.NotNull(q.Score);
        Assert.InRange(q.Score!.Value, 85, 100);
    }

    [Fact]
    public void ASlowInterruptedCharge_ScoresLow()
    {
        ChargingQuality q = ChargingQualityScorer.Score(
            new ChargingQualityInputs(
                SessionAvgRateMw: 14_000, SessionRateCoefficientOfVariation: 0.6,
                PersonalMedianRateMw: 30_000, PriorComparableSessions: 30,
                Interruptions: 4, SessionHours: 2.0, ThermalFractionAboveWarn: 0.1),
            minPriorSessions: 5);

        Assert.NotNull(q.Score);
        Assert.InRange(q.Score!.Value, 0, 45);
    }
}
