using BatteryIntelligence.Core.Power;
using BatteryIntelligence.Core.Primitives;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Power;

/// <summary>
/// The raw live buffer: two independent bounds (max age, hard point cap) so an
/// hour of 5-second samples cannot grow without limit (docs/architecture.md
/// driver 3; specification section 45).
/// </summary>
public sealed class BoundedTimeSeriesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Add_EnforcesThePointCap_EvictingOldestFirst()
    {
        BoundedTimeSeries series = new(TimeSpan.FromDays(1), maxPoints: 10);

        for (int i = 0; i < 25; i++)
        {
            series.Add(new TimePoint(T0.AddSeconds(i), i));
        }

        IReadOnlyList<TimePoint> points = series.Points;
        Assert.Equal(10, points.Count);
        Assert.Equal(15, points[0].Value);  // 0..14 evicted
        Assert.Equal(24, points[^1].Value);
    }

    [Fact]
    public void Add_DropsPointsOlderThanTheMaxAge()
    {
        BoundedTimeSeries series = new(TimeSpan.FromMinutes(1), maxPoints: 10_000);

        series.Add(new TimePoint(T0, 1));
        series.Add(new TimePoint(T0.AddSeconds(30), 2));
        series.Add(new TimePoint(T0.AddSeconds(90), 3)); // T0 sample is now 90 s old

        IReadOnlyList<TimePoint> points = series.Points;
        Assert.Equal(2, points.Count);
        Assert.Equal(2, points[0].Value);
    }

    [Fact]
    public void OneHourAtFiveSeconds_StaysWellUnderTheCap()
    {
        BoundedTimeSeries series = new(TimeSpan.FromHours(1), maxPoints: 1_000);

        for (int i = 0; i < 720; i++) // 3600 s / 5 s
        {
            series.Add(new TimePoint(T0.AddSeconds(i * 5), i));
        }

        Assert.Equal(720, series.Count);
    }

    [Fact]
    public void PointsBetween_ReturnsOnlyTheRequestedSpan()
    {
        BoundedTimeSeries series = new(TimeSpan.FromHours(1), maxPoints: 1_000);
        for (int i = 0; i < 60; i++)
        {
            series.Add(new TimePoint(T0.AddSeconds(i), i));
        }

        IReadOnlyList<TimePoint> window = series.PointsBetween(T0.AddSeconds(10), T0.AddSeconds(20));

        Assert.Equal(11, window.Count);
        Assert.Equal(10, window[0].Value);
        Assert.Equal(20, window[^1].Value);
    }
}
