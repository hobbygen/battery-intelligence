using BatteryIntelligence.Core.Power;
using BatteryIntelligence.Core.Primitives;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Power;

/// <summary>
/// Min/max-preserving decimation (docs/monitoring-dataflow.md section 7): plain
/// averaging would erase the transient spikes the Power page exists to show, so
/// every bucket keeps its extremes.
/// </summary>
public sealed class MinMaxDownsamplerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<TimePoint> Ramp(int count)
    {
        List<TimePoint> points = new(count);
        for (int i = 0; i < count; i++)
        {
            points.Add(new TimePoint(T0.AddSeconds(i), i));
        }

        return points;
    }

    [Fact]
    public void Downsample_ShorterThanBudget_ReturnsInputUnchanged()
    {
        IReadOnlyList<TimePoint> input = Ramp(50);

        IReadOnlyList<TimePoint> result = MinMaxDownsampler.Downsample(input, 600);

        Assert.Same(input, result);
    }

    [Fact]
    public void Downsample_RespectsThePointBudget()
    {
        IReadOnlyList<TimePoint> result = MinMaxDownsampler.Downsample(Ramp(10_000), 600);

        Assert.True(result.Count <= 600, $"expected <= 600, got {result.Count}");
    }

    [Fact]
    public void Downsample_KeepsTimestampsInOrder()
    {
        IReadOnlyList<TimePoint> result = MinMaxDownsampler.Downsample(Ramp(5_000), 400);

        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i].TimestampUtc >= result[i - 1].TimestampUtc);
        }
    }

    [Fact]
    public void Downsample_LoneSpikeSurvivesDecimation()
    {
        List<TimePoint> points = [.. Ramp(2_000).Select(p => p with { Value = 100 })];
        points[973] = points[973] with { Value = 99_999 }; // a single outlier

        IReadOnlyList<TimePoint> result = MinMaxDownsampler.Downsample(points, 200);

        Assert.Contains(result, p => p.Value == 99_999);
    }

    [Fact]
    public void Downsample_RejectsBudgetBelowTwo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MinMaxDownsampler.Downsample(Ramp(100), 1));
    }
}
