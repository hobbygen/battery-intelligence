using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Power;

/// <summary>
/// A time-windowed min / max / average accumulator over one metric
/// (specification section 13). Holds only the points still inside its window, so
/// its memory is bounded by the window length divided by the sample interval.
/// </summary>
/// <remarks>
/// Not thread-safe — the owner (<c>PowerMonitoringService</c>) serialises access.
/// Only trustworthy samples should be added; Suspect readings are excluded by the
/// caller before they reach here (docs/monitoring-dataflow.md section 4).
/// </remarks>
public sealed class RollingStatistics
{
    private readonly TimeSpan _window;
    private readonly List<Entry> _entries = [];

    private readonly record struct Entry(DateTimeOffset TimestampUtc, double Value, DataQuality Grade);

    /// <param name="window">How far back the statistics look.</param>
    public RollingStatistics(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "The window must be positive.");
        }

        _window = window;
    }

    /// <summary>Records a sample and drops anything that has aged out of the window.</summary>
    public void Add(DateTimeOffset timestampUtc, double value, DataQuality grade)
    {
        _entries.Add(new Entry(timestampUtc, value, grade));
        Prune(timestampUtc);
    }

    /// <summary>Drops every sample older than <paramref name="asOfUtc"/> minus the window.</summary>
    public void Prune(DateTimeOffset asOfUtc)
    {
        DateTimeOffset cutoff = asOfUtc - _window;

        int removeUpTo = 0;
        while (removeUpTo < _entries.Count && _entries[removeUpTo].TimestampUtc < cutoff)
        {
            removeUpTo++;
        }

        if (removeUpTo > 0)
        {
            _entries.RemoveRange(0, removeUpTo);
        }
    }

    /// <summary>The current min / max / average over the retained samples.</summary>
    public MetricStatistics Snapshot()
    {
        if (_entries.Count == 0)
        {
            return MetricStatistics.Empty;
        }

        double min = double.MaxValue;
        double max = double.MinValue;
        double sum = 0;
        DataQuality grade = DataQuality.Measured;

        foreach (Entry entry in _entries)
        {
            min = Math.Min(min, entry.Value);
            max = Math.Max(max, entry.Value);
            sum += entry.Value;
            grade = grade.Worst(entry.Grade);
        }

        return new MetricStatistics(min, max, sum / _entries.Count, _entries.Count, grade);
    }

    /// <summary>
    /// Computes statistics over only the samples inside an explicit sub-range —
    /// used when the UI window (for example one minute) is shorter than this
    /// accumulator's own window.
    /// </summary>
    public MetricStatistics SnapshotBetween(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        double sum = 0;
        int count = 0;
        DataQuality grade = DataQuality.Measured;

        foreach (Entry entry in _entries)
        {
            if (entry.TimestampUtc < fromUtc || entry.TimestampUtc > toUtc)
            {
                continue;
            }

            min = Math.Min(min, entry.Value);
            max = Math.Max(max, entry.Value);
            sum += entry.Value;
            grade = grade.Worst(entry.Grade);
            count++;
        }

        return count == 0
            ? MetricStatistics.Empty
            : new MetricStatistics(min, max, sum / count, count, grade);
    }
}
