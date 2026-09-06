using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Analytics;

/// <summary>
/// Discharge breakdown — the screen-on vs screen-off rate split, never
/// extrapolated (specification section 11). Traceability: R-048.
/// </summary>
public sealed class DischargeAnalyzerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyWindow_ReturnsEmpty()
    {
        Assert.Same(DischargeAnalysis.Empty, DischargeAnalyzer.Analyze(new RollingRate(TimeSpan.FromMinutes(15))));
    }

    [Fact]
    public void ScreenOnDrawsMoreThanScreenOff_AndBothAreReportedWhenBothExist()
    {
        RollingRate rate = new(TimeSpan.FromMinutes(30));
        for (int i = 0; i < 12; i++)
        {
            rate.Add(Start.AddSeconds(i * 30), 12_000, ScreenState.On);
        }

        for (int i = 12; i < 24; i++)
        {
            rate.Add(Start.AddSeconds(i * 30), 5_000, ScreenState.Off);
        }

        DischargeAnalysis analysis = DischargeAnalyzer.Analyze(rate);

        Assert.NotNull(analysis.ScreenOnRateMw);
        Assert.NotNull(analysis.ScreenOffRateMw);
        Assert.True(analysis.ScreenOnRateMw > analysis.ScreenOffRateMw);
        Assert.InRange(analysis.ScreenOnFraction, 0.49, 0.51);
    }

    [Fact]
    public void NoScreenOffSamples_LeavesTheScreenOffRateNull()
    {
        RollingRate rate = new(TimeSpan.FromMinutes(30));
        for (int i = 0; i < 10; i++)
        {
            rate.Add(Start.AddSeconds(i * 30), 9_000, ScreenState.On);
        }

        DischargeAnalysis analysis = DischargeAnalyzer.Analyze(rate);

        Assert.NotNull(analysis.ScreenOnRateMw);
        Assert.Null(analysis.ScreenOffRateMw);
    }
}
