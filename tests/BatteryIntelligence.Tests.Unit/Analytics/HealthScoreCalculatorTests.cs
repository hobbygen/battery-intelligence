using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Analytics;

/// <summary>
/// <c>HealthScoreV2</c> — weights renormalise across the available factors,
/// retention is mandatory and caps the result, usage-habit factors can only
/// subtract, and the factor set is stored for the explanation
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
        Assert.Equal(HealthScoreCalculator.Version, score.AlgorithmVersion);
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

        HealthFactor[] weighted = [.. score.Factors.Where(f => f.Role == HealthFactorRole.Weighted)];
        Assert.Equal(1.0, weighted.Where(f => f.Contributed).Sum(f => f.NormalisedWeight), 3);

        Assert.All(weighted.Where(f => f.Contributed), f => Assert.True(f.NormalisedWeight > f.Weight - 1e-9));
        Assert.Contains(score.Factors, f => f.Key == "cycles" && !f.Contributed && f.NormalisedWeight == 0);
        Assert.Contains(score.Factors, f => f.Key == "thermal" && !f.Contributed && f.NormalisedWeight == 0);

        // Penalty factors take no share of the weighted mean, however good they look.
        Assert.All(
            score.Factors.Where(f => f.Role == HealthFactorRole.Penalty),
            f => Assert.Equal(0, f.NormalisedWeight));
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

    /// <summary>
    /// The reported "Excellent for an almost dead battery": a worn pack that lives
    /// on AC has a flat trend, no deep discharges and a steady charge rate, so every
    /// factor but retention looked perfect and outvoted the one measurement that
    /// matters. Retention now caps the result.
    /// </summary>
    [Fact]
    public void AWornPackWithGentleHabits_CannotScoreAboveItsRetention()
    {
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(
                RetentionPercent: 52.0,
                DegradationSlopePercentPerMonth: 0.0,
                CycleCount: null,
                Chemistry: "LiP",
                ThermalExposureFraction: 0.0,
                AvgDepthOfDischargePercent: 5,
                ChargeRateCoefficientOfVariation: 0.02),
            Now);

        Assert.True(score.Score <= 52.0, $"Score {score.Score} exceeded retention of 52%.");
        Assert.Equal(HealthCategory.Poor, score.Category);
    }

    /// <summary>
    /// The reported "Poor for a nearly new battery": a new pack sheds its first
    /// points fastest and is usually the one actually being run down, so the
    /// degradation and habit factors used to bury a full-capacity reading.
    /// </summary>
    [Fact]
    public void ANewPackUsedHard_StaysInTheUpperBands()
    {
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(
                RetentionPercent: 100.0,
                DegradationSlopePercentPerMonth: -1.2,
                CycleCount: 30,
                Chemistry: "LiP",
                ThermalExposureFraction: null,
                AvgDepthOfDischargePercent: 85,
                ChargeRateCoefficientOfVariation: 0.9),
            Now);

        Assert.True(score.Score >= 75.0, $"Score {score.Score} put a full-capacity pack below Good.");
        Assert.True(
            score.Category is HealthCategory.Good or HealthCategory.Excellent,
            $"Unexpected category {score.Category}.");
    }

    /// <summary>
    /// Ordinary early ageing is not a fault: decline inside the dead band costs the
    /// degradation factor nothing, so a settling new pack is not marked down for it.
    /// </summary>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(-0.5)]
    public void DeclineInsideTheDeadBand_ScoresTheDegradationFactorFull(double slope)
    {
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(98.0, slope, null, "LiP", null, null, null),
            Now);

        HealthFactor degradation = score.Factors.Single(f => f.Key == "degradation");
        Assert.Equal(1.0, degradation.Score01!.Value, 3);
    }

    /// <summary>
    /// Habit factors subtract but never add: two packs identical except for how
    /// gently they are used must not differ by more than the capped penalty, and the
    /// gentle one must never come out above the pack measured on its own.
    /// </summary>
    [Fact]
    public void GentleHabits_DoNotRaiseTheScoreAboveTheSamePackWithoutThem()
    {
        HealthScoreInputs bare = new(88.0, -0.3, 150, "LiP", 0.0, null, null);
        HealthScore withoutHabits = HealthScoreCalculator.Compute(bare, Now);
        HealthScore withGentleHabits = HealthScoreCalculator.Compute(
            bare with { AvgDepthOfDischargePercent = 5, ChargeRateCoefficientOfVariation = 0.01 },
            Now);
        HealthScore withHarshHabits = HealthScoreCalculator.Compute(
            bare with { AvgDepthOfDischargePercent = 100, ChargeRateCoefficientOfVariation = 1.5 },
            Now);

        Assert.Equal(withoutHabits.Score!.Value, withGentleHabits.Score!.Value, 3);
        Assert.True(withHarshHabits.Score < withoutHabits.Score);
        Assert.True(withoutHabits.Score - withHarshHabits.Score <= 5.01);
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
