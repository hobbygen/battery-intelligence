using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Processes;

namespace BatteryIntelligence.Tests.Unit.Processes;

/// <summary>
/// The non-attributable baseline is the user's own idle-draw history, split by
/// screen state, with a conservative default and Low confidence until enough data
/// exists (docs/estimation-strategy.md section 5, step 2). Traceability: R-054.
/// </summary>
public sealed class BaselineEstimatorTests
{
    [Fact]
    public void BeforeEnoughObservations_ReturnsTheConservativeDefault_AtLowConfidence()
    {
        BaselineEstimator estimator = new(defaultBaselineMw: 4_500);

        for (int i = 0; i < 5; i++)
        {
            estimator.Observe(drawMw: 6_000, systemIdle: true, screenOn: true);
        }

        (double baseline, AppEnergyConfidence confidence) = estimator.Estimate(screenOn: true);

        Assert.Equal(4_500, baseline);
        Assert.Equal(AppEnergyConfidence.Low, confidence);
    }

    [Fact]
    public void NonIdleObservations_AreIgnored()
    {
        BaselineEstimator estimator = new(defaultBaselineMw: 4_000);

        for (int i = 0; i < 50; i++)
        {
            estimator.Observe(drawMw: 20_000, systemIdle: false, screenOn: true);
        }

        Assert.Equal(0, estimator.ScreenOnObservations);
        (double baseline, AppEnergyConfidence confidence) = estimator.Estimate(screenOn: true);
        Assert.Equal(4_000, baseline);
        Assert.Equal(AppEnergyConfidence.Low, confidence);
    }

    [Fact]
    public void WithEnoughIdleHistoryForBothScreenStates_TheScreenOnBaselineIsHigherAndHighConfidence()
    {
        BaselineEstimator estimator = new(defaultBaselineMw: 4_000);

        for (int i = 0; i < 40; i++)
        {
            estimator.Observe(drawMw: 3_000, systemIdle: true, screenOn: false); // platform idle
            estimator.Observe(drawMw: 7_000, systemIdle: true, screenOn: true);  // + display
        }

        (double onBaseline, AppEnergyConfidence onConfidence) = estimator.Estimate(screenOn: true);
        (double offBaseline, AppEnergyConfidence offConfidence) = estimator.Estimate(screenOn: false);

        Assert.True(onBaseline > offBaseline);
        Assert.Equal(AppEnergyConfidence.High, onConfidence);
        Assert.Equal(AppEnergyConfidence.High, offConfidence);
        Assert.InRange(onBaseline, 6_000, 8_000);
        Assert.InRange(offBaseline, 2_000, 4_000);
    }

    [Fact]
    public void ScreenOnLearned_ButScreenOffNot_IsOnlyMediumConfidence()
    {
        BaselineEstimator estimator = new(defaultBaselineMw: 4_000);

        for (int i = 0; i < 40; i++)
        {
            estimator.Observe(drawMw: 7_000, systemIdle: true, screenOn: true);
        }

        (_, AppEnergyConfidence confidence) = estimator.Estimate(screenOn: true);
        Assert.Equal(AppEnergyConfidence.Medium, confidence);
    }
}
