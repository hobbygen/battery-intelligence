using BatteryIntelligence.Core.Monitoring;

namespace BatteryIntelligence.Tests.Unit.Monitoring;

/// <summary>Exponential retry back-off (docs/monitoring-dataflow.md section 8).</summary>
public sealed class MonitoringBackoffTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(10);

    [Fact]
    public void ZeroFailures_ReturnsTheBaseInterval()
    {
        Assert.Equal(Base, MonitoringBackoff.NextInterval(Base, 0));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 80)]
    public void EachFailureDoublesTheInterval(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), MonitoringBackoff.NextInterval(Base, failures));
    }

    [Fact]
    public void GrowthIsCappedAtFiveMinutes()
    {
        TimeSpan huge = MonitoringBackoff.NextInterval(Base, 20);

        Assert.Equal(MonitoringBackoff.MaxBackoff, huge);
    }
}
