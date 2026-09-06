using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Monitoring;

namespace BatteryIntelligence.Tests.Unit.Monitoring;

/// <summary>The adaptive-sampling table (docs/monitoring-dataflow.md section 3), row by row.</summary>
public sealed class AdaptiveSamplingPolicyTests
{
    private static readonly TimeSpan BaseBattery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BaseProcess = TimeSpan.FromSeconds(10);

    private static SamplingConditions Conditions(
        ScreenState screen = ScreenState.On,
        BatteryState power = BatteryState.Discharging,
        double? percent = 70,
        bool windowVisible = true,
        bool chargingSession = false,
        bool paused = false,
        bool adaptive = true) =>
        new(screen, power, percent, windowVisible, chargingSession, paused, adaptive);

    [Fact]
    public void ScreenOn_OnBattery_UsesBaseRates()
    {
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(BaseBattery, BaseProcess, Conditions());

        Assert.Equal(BaseBattery, plan.BatteryInterval);
        Assert.Equal(BaseProcess, plan.ProcessInterval);
        Assert.False(plan.ProcessPaused);
        Assert.False(plan.AllStopped);
        Assert.False(plan.ChartFeedSuspended);
    }

    [Fact]
    public void ScreenOff_OnBattery_TriplesBothRates()
    {
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(
            BaseBattery, BaseProcess, Conditions(screen: ScreenState.Off));

        Assert.Equal(BaseBattery * 3, plan.BatteryInterval);
        Assert.Equal(BaseProcess * 3, plan.ProcessInterval);
    }

    [Fact]
    public void ScreenOff_OnAc_BatteryFull_SlowsBatteryAndPausesProcess()
    {
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(
            BaseBattery, BaseProcess, Conditions(screen: ScreenState.Off, power: BatteryState.Full, percent: 100));

        Assert.Equal(BaseBattery * 6, plan.BatteryInterval);
        Assert.True(plan.ProcessPaused);
        Assert.False(plan.AllStopped);
    }

    [Fact]
    public void LowBattery_Discharging_HalvesRates_AndWinsOverScreenOff()
    {
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(
            BaseBattery, BaseProcess, Conditions(screen: ScreenState.Off, percent: 12));

        Assert.Equal(BaseBattery * 0.5, plan.BatteryInterval);
        Assert.Equal(BaseProcess * 0.5, plan.ProcessInterval);
    }

    [Fact]
    public void ChargingSession_KeepsBaseRate_OverridingScreenOffBackoff()
    {
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(
            BaseBattery, BaseProcess,
            Conditions(screen: ScreenState.Off, power: BatteryState.Charging, percent: 60, chargingSession: true));

        Assert.Equal(BaseBattery, plan.BatteryInterval);
        Assert.Equal(BaseProcess, plan.ProcessInterval);
    }

    [Fact]
    public void WindowHidden_SuspendsTheChartFeed_ButNotSampling()
    {
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(
            BaseBattery, BaseProcess, Conditions(windowVisible: false));

        Assert.True(plan.ChartFeedSuspended);
        Assert.False(plan.AllStopped);
        Assert.Equal(BaseBattery, plan.BatteryInterval);
    }

    [Fact]
    public void MonitoringPaused_StopsEverything()
    {
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(
            BaseBattery, BaseProcess, Conditions(paused: true, screen: ScreenState.On));

        Assert.True(plan.AllStopped);
        Assert.True(plan.ProcessPaused);
        Assert.True(plan.ChartFeedSuspended);
    }

    [Fact]
    public void AdaptiveDisabled_KeepsBaseRates_ExceptTheChartFeedSkip()
    {
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(
            BaseBattery, BaseProcess,
            Conditions(screen: ScreenState.Off, percent: 10, windowVisible: false, adaptive: false));

        Assert.Equal(BaseBattery, plan.BatteryInterval);
        Assert.Equal(BaseProcess, plan.ProcessInterval);
        Assert.True(plan.ChartFeedSuspended);
        Assert.False(plan.ProcessPaused);
    }

    [Fact]
    public void ResolvedIntervalsAreClampedToTheMonitoringRange()
    {
        // A 60 s base × 6 = 360 s, past the 300 s ceiling.
        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60),
            Conditions(screen: ScreenState.Off, power: BatteryState.Full, percent: 100));

        Assert.Equal(AdaptiveSamplingPolicy.MaxInterval, plan.BatteryInterval);
    }
}
