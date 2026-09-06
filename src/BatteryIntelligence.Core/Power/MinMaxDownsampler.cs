using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Power;

/// <summary>
/// Thins a time series to a point budget while preserving local extremes
/// (docs/monitoring-dataflow.md section 7). Plain averaging would erase exactly
/// the transient current spikes the Power page exists to show; this keeps the
/// min and the max of every bucket instead.
/// </summary>
public static class MinMaxDownsampler
{
    /// <summary>
    /// Returns <paramref name="source"/> unchanged when it already fits
    /// <paramref name="pointBudget"/>, otherwise a time-ordered series of at most
    /// <paramref name="pointBudget"/> points: each contiguous bucket contributes
    /// its minimum and maximum value, emitted in timestamp order.
    /// </summary>
    /// <param name="source">Time-ordered points, oldest first.</param>
    /// <param name="pointBudget">Maximum points to return. Must be at least 2.</param>
    public static IReadOnlyList<TimePoint> Downsample(IReadOnlyList<TimePoint> source, int pointBudget)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (pointBudget < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(pointBudget), "The point budget must be at least 2.");
        }

        if (source.Count <= pointBudget)
        {
            return source;
        }

        // Each bucket yields up to two points (its min and its max), so the
        // number of buckets is half the budget.
        int bucketCount = pointBudget / 2;
        List<TimePoint> result = new(pointBudget);

        double step = source.Count / (double)bucketCount;

        for (int bucket = 0; bucket < bucketCount; bucket++)
        {
            int start = (int)(bucket * step);
            int end = bucket == bucketCount - 1 ? source.Count : (int)((bucket + 1) * step);
            if (end <= start)
            {
                end = Math.Min(source.Count, start + 1);
            }

            int minIndex = start;
            int maxIndex = start;
            for (int i = start + 1; i < end; i++)
            {
                if (source[i].Value < source[minIndex].Value)
                {
                    minIndex = i;
                }

                if (source[i].Value > source[maxIndex].Value)
                {
                    maxIndex = i;
                }
            }

            // Emit the earlier of the two extremes first so timestamps stay
            // monotonic; if they coincide, emit a single point.
            if (minIndex == maxIndex)
            {
                result.Add(source[minIndex]);
            }
            else if (minIndex < maxIndex)
            {
                result.Add(source[minIndex]);
                result.Add(source[maxIndex]);
            }
            else
            {
                result.Add(source[maxIndex]);
                result.Add(source[minIndex]);
            }
        }

        return result;
    }
}
