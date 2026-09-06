using BatteryIntelligence.Core.Processes;

namespace BatteryIntelligence.Tests.Unit.Processes;

/// <summary>
/// CPU percent comes from cumulative-time deltas, never busy sampling
/// (docs/monitoring-dataflow.md section 5). Traceability: R-050.
/// </summary>
public sealed class ProcessCpuCalculatorTests
{
    [Fact]
    public void OneFullCoreForTheWholeInterval_IsOneHundredPercent()
    {
        double percent = ProcessCpuCalculator.Percent(
            cpuDelta: TimeSpan.FromSeconds(10), wallDelta: TimeSpan.FromSeconds(10), coreCount: 8);

        Assert.Equal(100.0, percent, 3);
    }

    [Fact]
    public void HalfACoreForHalfTheInterval_IsProportional()
    {
        double percent = ProcessCpuCalculator.Percent(
            cpuDelta: TimeSpan.FromSeconds(2.5), wallDelta: TimeSpan.FromSeconds(10), coreCount: 4);

        Assert.Equal(25.0, percent, 3);
    }

    [Fact]
    public void ASingleProcessIsCappedAtOneHundredTimesTheCoreCount()
    {
        // A wildly implausible delta must not blow the ranking out.
        double percent = ProcessCpuCalculator.Percent(
            cpuDelta: TimeSpan.FromSeconds(1000), wallDelta: TimeSpan.FromSeconds(10), coreCount: 4);

        Assert.Equal(400.0, percent, 3);
    }

    [Fact]
    public void NegativeCpuDelta_FromAReusedPid_ReturnsZero()
    {
        double percent = ProcessCpuCalculator.Percent(
            cpuDelta: TimeSpan.FromSeconds(-5), wallDelta: TimeSpan.FromSeconds(10), coreCount: 8);

        Assert.Equal(0.0, percent);
    }

    [Fact]
    public void NonPositiveWallDelta_ReturnsZero()
    {
        Assert.Equal(0.0, ProcessCpuCalculator.Percent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 8));
        Assert.Equal(0.0, ProcessCpuCalculator.Percent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1), 8));
    }

    [Fact]
    public void ZeroCoreCount_IsTreatedAsOne()
    {
        double percent = ProcessCpuCalculator.Percent(
            cpuDelta: TimeSpan.FromSeconds(5), wallDelta: TimeSpan.FromSeconds(10), coreCount: 0);

        Assert.Equal(50.0, percent, 3);
    }
}
