using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Power;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Power;

/// <summary>The windowed min/max/avg accumulator behind the Power page's stats strip (specification section 13).</summary>
public sealed class RollingStatisticsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Snapshot_Empty_HasNoData()
    {
        RollingStatistics stats = new(TimeSpan.FromMinutes(1));

        MetricStatistics snapshot = stats.Snapshot();

        Assert.Equal(0, snapshot.Count);
        Assert.False(snapshot.HasData);
        Assert.Null(snapshot.Min);
    }

    [Fact]
    public void Snapshot_ComputesMinMaxAverage()
    {
        RollingStatistics stats = new(TimeSpan.FromMinutes(5));
        stats.Add(T0, 10, DataQuality.Measured);
        stats.Add(T0.AddSeconds(5), 30, DataQuality.Measured);
        stats.Add(T0.AddSeconds(10), 20, DataQuality.Measured);

        MetricStatistics snapshot = stats.Snapshot();

        Assert.Equal(10, snapshot.Min);
        Assert.Equal(30, snapshot.Max);
        Assert.Equal(20, snapshot.Average);
        Assert.Equal(3, snapshot.Count);
    }

    [Fact]
    public void Add_DropsSamplesThatAgeOutOfTheWindow()
    {
        RollingStatistics stats = new(TimeSpan.FromSeconds(30));
        stats.Add(T0, 100, DataQuality.Measured);
        stats.Add(T0.AddSeconds(45), 200, DataQuality.Measured); // T0 sample is now 45 s old, outside the 30 s window

        MetricStatistics snapshot = stats.Snapshot();

        Assert.Equal(1, snapshot.Count);
        Assert.Equal(200, snapshot.Min);
        Assert.Equal(200, snapshot.Max);
    }

    [Fact]
    public void Snapshot_GradeIsTheWorstContributingGrade()
    {
        RollingStatistics stats = new(TimeSpan.FromMinutes(1));
        stats.Add(T0, 10, DataQuality.Measured);
        stats.Add(T0.AddSeconds(1), 12, DataQuality.Estimated);

        Assert.Equal(DataQuality.Estimated, stats.Snapshot().Grade);
    }

    [Fact]
    public void SnapshotBetween_RestrictsToAnExplicitSubRange()
    {
        RollingStatistics stats = new(TimeSpan.FromMinutes(10));
        stats.Add(T0, 1, DataQuality.Measured);
        stats.Add(T0.AddMinutes(1), 2, DataQuality.Measured);
        stats.Add(T0.AddMinutes(2), 3, DataQuality.Measured);

        MetricStatistics window = stats.SnapshotBetween(T0.AddSeconds(30), T0.AddMinutes(1).AddSeconds(30));

        Assert.Equal(1, window.Count);
        Assert.Equal(2, window.Average);
    }
}
