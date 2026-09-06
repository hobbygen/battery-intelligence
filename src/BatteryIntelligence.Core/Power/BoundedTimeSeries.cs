using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Power;

/// <summary>
/// A raw point buffer with two independent bounds: a maximum age and a hard
/// point cap. Whichever binds first, memory stays bounded — an hour of 5-second
/// samples cannot grow without limit (docs/architecture.md driver 3;
/// docs/monitoring-dataflow.md section 7).
/// </summary>
/// <remarks>
/// Not thread-safe — the owner serialises access. This holds the full-fidelity
/// samples; thinning to a chart's point budget is a read-time concern
/// (<see cref="MinMaxDownsampler"/>), so a spike is never lost before the user
/// has had a chance to see it.
/// </remarks>
public sealed class BoundedTimeSeries
{
    private readonly TimeSpan _maxAge;
    private readonly int _maxPoints;
    private readonly LinkedList<TimePoint> _points = [];

    /// <param name="maxAge">Points older than this (relative to the newest add) are dropped.</param>
    /// <param name="maxPoints">Absolute ceiling on retained points; the oldest are evicted first.</param>
    public BoundedTimeSeries(TimeSpan maxAge, int maxPoints)
    {
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), "The maximum age must be positive.");
        }

        if (maxPoints < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPoints), "The point cap must be at least 2.");
        }

        _maxAge = maxAge;
        _maxPoints = maxPoints;
    }

    /// <summary>The retained points, oldest first.</summary>
    public IReadOnlyList<TimePoint> Points => [.. _points];

    /// <summary>Current retained point count.</summary>
    public int Count => _points.Count;

    /// <summary>Appends a point and enforces both bounds.</summary>
    public void Add(TimePoint point)
    {
        _points.AddLast(point);
        PruneOlderThan(point.TimestampUtc - _maxAge);

        while (_points.Count > _maxPoints)
        {
            _points.RemoveFirst();
        }
    }

    /// <summary>Drops points older than <paramref name="asOfUtc"/> minus the max age.</summary>
    public void Prune(DateTimeOffset asOfUtc) => PruneOlderThan(asOfUtc - _maxAge);

    /// <summary>The retained points whose timestamp falls within <c>[fromUtc, toUtc]</c>, oldest first.</summary>
    public IReadOnlyList<TimePoint> PointsBetween(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        List<TimePoint> window = [];
        foreach (TimePoint point in _points)
        {
            if (point.TimestampUtc >= fromUtc && point.TimestampUtc <= toUtc)
            {
                window.Add(point);
            }
        }

        return window;
    }

    /// <summary>Timestamp of the oldest retained point, or null when empty.</summary>
    public DateTimeOffset? OldestTimestampUtc => _points.First?.Value.TimestampUtc;

    private void PruneOlderThan(DateTimeOffset cutoff)
    {
        while (_points.First is { Value.TimestampUtc: var ts } && ts < cutoff)
        {
            _points.RemoveFirst();
        }
    }
}
