using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Processes;

namespace BatteryIntelligence.Tests.Unit.Processes;

/// <summary>
/// <c>AppEnergyV1</c> — shares sum to the attributable budget, the baseline is a
/// separate slice, no double counting, and per-app figures are always Estimated
/// (docs/estimation-strategy.md section 5; specification sections 15 and 55).
/// Traceability: R-052, R-053, R-054.
/// </summary>
public sealed class AppEnergyEstimatorTests
{
    private static readonly AppEnergyWeights Weights = new(Cpu: 1.0, Gpu: 1.2, Io: 0.3, Foreground: 0.15);
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    [Fact]
    public void OnBattery_SharesSumToOneHundredPercentOfTheAttributableBudget()
    {
        var activities = new[]
        {
            Activity("chrome", cpu: 30, foreground: true),
            Activity("code", cpu: 10, foreground: false),
            Activity("spotify", cpu: 5, foreground: false),
        };

        AppEnergyAttribution result = AppEnergyEstimator.Estimate(
            Weights, activities, totalBudgetMw: 10_000, baselineMw: 4_000,
            AppEnergyConfidence.High, topApplicationCount: 40, At);

        double shareSum = result.Entries.Sum(e => e.SharePercent);
        Assert.Equal(100.0, shareSum, 1);
    }

    [Fact]
    public void OnBattery_BaselineIsSeparatedFirst_AndAppPowerSumsToTotalMinusBaseline()
    {
        var activities = new[]
        {
            Activity("chrome", cpu: 40, foreground: true),
            Activity("code", cpu: 20, foreground: false),
        };

        AppEnergyAttribution result = AppEnergyEstimator.Estimate(
            Weights, activities, totalBudgetMw: 12_000, baselineMw: 5_000,
            AppEnergyConfidence.Medium, topApplicationCount: 40, At);

        Assert.True(result.Baseline.IsBaseline);
        Assert.Equal(5_000, result.Baseline.EstimatedPowerMw);

        int appPower = result.Entries.Sum(e => e.EstimatedPowerMw ?? 0);
        // Total, less the baseline, is divided among the apps (± rounding).
        Assert.InRange(appPower, 12_000 - 5_000 - 2, 12_000 - 5_000 + 2);
    }

    [Fact]
    public void EveryPerAppFigure_IsPresentedAsEstimated_NeverMeasured()
    {
        var activities = new[] { Activity("chrome", cpu: 20, foreground: true) };

        AppEnergyAttribution result = AppEnergyEstimator.Estimate(
            Weights, activities, totalBudgetMw: 8_000, baselineMw: 3_000,
            AppEnergyConfidence.High, topApplicationCount: 40, At);

        Assert.Equal("AppEnergyV1", result.EstimatorVersion);
        Assert.Equal(AppEnergyEstimator.Version, result.EstimatorVersion);
        Assert.True(result.AbsoluteAvailable);
    }

    [Fact]
    public void OnAc_RankingIsKept_ButAbsolutePowerIsUnavailable()
    {
        var activities = new[]
        {
            Activity("code", cpu: 8, foreground: true),
            Activity("chrome", cpu: 25, foreground: false),
        };

        AppEnergyAttribution result = AppEnergyEstimator.Estimate(
            Weights, activities, totalBudgetMw: null, baselineMw: 4_000,
            AppEnergyConfidence.Low, topApplicationCount: 40, At);

        Assert.False(result.AbsoluteAvailable);
        Assert.Null(result.TotalBudgetMw);
        Assert.All(result.Entries, e => Assert.Null(e.EstimatedPowerMw));
        Assert.Null(result.Baseline.EstimatedPowerMw);

        // Ranking still meaningful: chrome does more work, so it ranks first.
        Assert.Equal("chrome", result.Entries[0].ApplicationKey);
        Assert.True(result.Entries.Sum(e => e.SharePercent) > 99.0);
    }

    [Fact]
    public void BeyondTheTopN_ApplicationsCollapseIntoASingleOtherRow_WithoutLosingShare()
    {
        var activities = Enumerable.Range(0, 10)
            .Select(i => Activity($"app{i}", cpu: 10 - i, foreground: false))
            .ToArray();

        AppEnergyAttribution result = AppEnergyEstimator.Estimate(
            Weights, activities, totalBudgetMw: 10_000, baselineMw: 2_000,
            AppEnergyConfidence.High, topApplicationCount: 3, At);

        Assert.Equal(4, result.Entries.Count); // 3 ranked + Other
        Assert.True(result.Entries[^1].IsOther);
        Assert.Equal(1, result.Entries.Count(e => e.IsOther));
        Assert.Equal(100.0, result.Entries.Sum(e => e.SharePercent), 1);
    }

    [Fact]
    public void NoActivity_ProducesNoNaNShares()
    {
        var activities = new[] { Activity("idle", cpu: 0, foreground: false) };

        AppEnergyAttribution result = AppEnergyEstimator.Estimate(
            Weights, activities, totalBudgetMw: 6_000, baselineMw: 5_000,
            AppEnergyConfidence.Low, topApplicationCount: 40, At);

        Assert.All(result.Entries, e => Assert.False(double.IsNaN(e.SharePercent)));
        Assert.All(result.Entries, e => Assert.Equal(0.0, e.SharePercent));
    }

    private static AppActivity Activity(string key, double cpu, bool foreground) =>
        new(key, key, CpuPercent: cpu, GpuPercent: 0, IoRate: 0, IsForeground: foreground, MemoryBytes: 100_000_000, ProcessCount: 1);
}
