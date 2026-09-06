using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Analytics;

/// <summary>
/// <c>HealthScoreV1</c> — weights renormalise across the available factors,
/// retention is mandatory, and the factor set is stored for the explanation
/// (docs/estimation-strategy.md section 4; specification section 19).
/// Traceability: R-025. This is a Phase 8 exit criterion.
/// </summary>
public sealed class HealthScoreCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RetentionAbsent_MakesTheWholeScoreUnavailable()
    {
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(
                RetentionPercent: null,
                DegradationSlopePercentPerMonth: -0.2,
                CycleCount: 120,
                Chemistry: "LiP",
                ThermalExposureFraction: 0.0,
                AvgDepthOfDischargePercent: 40,
                ChargeRateCoefficientOfVariation: 0.1),
            Now);

        Assert.Null(score.Score);
        Assert.Equal(HealthCategory.Unknown, score.Category);
        Assert.Equal("HealthScoreV1", score.AlgorithmVersion);
        // The factors are still reported so the UI can explain the absence.
        Assert.Contains(score.Factors, f => f.Key == "retention" && !f.Contributed);
    }

    [Fact]
    public void CycleCountAndTemperatureBothAbsent_RenormalisesAcrossTheRest()
    {
        // The reference machine's real configuration.
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(
                RetentionPercent: 82.0,
                DegradationSlopePercentPerMonth: -0.1,
                CycleCount: null,
                Chemistry: "LiP",
                ThermalExposureFraction: null,
                AvgDepthOfDischargePercent: 35,
                ChargeRateCoefficientOfVariation: 0.15),
            Now);

        Assert.NotNull(score.Score);

        double contributingWeight = score.Factors.Where(f => f.Contributed).Sum(f => f.NormalisedWeight);
        Assert.Equal(1.0, contributingWeight, 3);

        Assert.All(score.Factors.Where(f => f.Contributed), f => Assert.True(f.NormalisedWeight > f.Weight - 1e-9));
        Assert.Contains(score.Factors, f => f.Key == "cycles" && !f.Contributed && f.NormalisedWeight == 0);
        Assert.Contains(score.Factors, f => f.Key == "thermal" && !f.Contributed && f.NormalisedWeight == 0);
    }

    [Fact]
    public void OnlyRetentionAvailable_TheScoreEqualsRetention()
    {
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(40.0, null, null, "LiP", null, null, null),
            Now);

        Assert.Equal(40.0, score.Score!.Value, 1);
        Assert.Equal(HealthCategory.Poor, score.Category);
    }

    [Fact]
    public void EveryFactorPresent_WeightsSumToOne_AndScoreIsPlausible()
    {
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(95.0, 0.0, 80, "LiP", 0.0, 20, 0.05),
            Now);

        Assert.Equal(1.0, score.Factors.Sum(f => f.NormalisedWeight), 3);
        Assert.InRange(score.Score!.Value, 85, 100);
        Assert.Equal(HealthCategory.Excellent, score.Category);
    }

    [Theory]
    [InlineData(95, HealthCategory.Excellent)]
    [InlineData(80, HealthCategory.Good)]
    [InlineData(65, HealthCategory.Fair)]
    [InlineData(50, HealthCategory.Poor)]
    [InlineData(20, HealthCategory.Critical)]
    public void Categorise_MatchesTheSpecificationBands(double score, HealthCategory expected) =>
        Assert.Equal(expected, HealthScoreCalculator.Categorise(score));
}
