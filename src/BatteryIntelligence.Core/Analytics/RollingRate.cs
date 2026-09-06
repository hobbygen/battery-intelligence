using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Analytics;

/// <summary>
/// A recency-weighted rolling mean of discharge rate over a time window,
/// filterable by screen state (docs/estimation-strategy.md section 3). Feeds both
/// <see cref="RuntimeEstimator"/> and <see cref="DischargeAnalyzer"/>.
/// </summary>
/// <remarks>
/// Not thread-safe — the owning service serialises access. Rates are stored as
/// positive magnitudes; the caller takes the absolute value of a signed reading
/// before adding it.
/// </remarks>
public sealed class RollingRate
{
    private readonly TimeSpan _window;
    private readonly List<Sample> _samples = [];

    private readonly record struct Sample(DateTimeOffset At, double RateMw, ScreenState Screen);

    /// <param name="window">How far back samples are retained and weighted.</param>
    public RollingRate(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "The window must be positive.");
        }

        _window = window;
    }

    /// <summary>Samples currently retained.</summary>
    public int Count => _samples.Count;

    /// <summary>Records a rate magnitude and drops anything aged out of the window.</summary>
    public void Add(DateTimeOffset at, double rateMw, ScreenState screen)
    {
        if (double.IsNaN(rateMw) || double.IsInfinity(rateMw) || rateMw < 0)
        {
            return;
        }

        _samples.Add(new Sample(at, rateMw, screen));
        Prune(at);
    }

    /// <summary>Drops every sample older than <paramref name="asOfUtc"/> minus the window.</summary>
    public void Prune(DateTimeOffset asOfUtc)
    {
        DateTimeOffset cutoff = asOfUtc - _window;
        int drop = 0;
        while (drop < _samples.Count && _samples[drop].At < cutoff)
        {
            drop++;
        }

        if (drop > 0)
        {
            _samples.RemoveRange(0, drop);
        }
    }

    /// <summary>Sample count for one screen state.</summary>
    public int CountFor(ScreenState screen)
    {
        int n = 0;
        foreach (Sample s in _samples)
        {
            if (s.Screen == screen)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The span from the oldest to the newest retained sample (optionally for one screen state).</summary>
    public TimeSpan SpanFor(ScreenState? screen = null)
    {
        DateTimeOffset? min = null;
        DateTimeOffset? max = null;
        foreach (Sample s in _samples)
        {
            if (screen is not null && s.Screen != screen)
            {
                continue;
            }

            min = min is null || s.At < min ? s.At : min;
            max = max is null || s.At > max ? s.At : max;
        }

        return min is null ? TimeSpan.Zero : max!.Value - min.Value;
    }

    /// <summary>
    /// The recency-weighted mean rate magnitude (optionally for one screen state),
    /// or <see langword="null"/> when no matching samples exist.
    /// </summary>
    public double? WeightedMeanMw(ScreenState? screen = null)
    {
        if (_samples.Count == 0)
        {
            return null;
        }

        DateTimeOffset now = _samples[^1].At;
        double halfLifeSeconds = _window.TotalSeconds / 2.0;

        double weightSum = 0;
        double weighted = 0;
        foreach (Sample s in _samples)
        {
            if (screen is not null && s.Screen != screen)
            {
                continue;
            }

            double ageSeconds = (now - s.At).TotalSeconds;
            double w = Math.Exp(-ageSeconds / Math.Max(1.0, halfLifeSeconds));
            weightSum += w;
            weighted += w * s.RateMw;
        }

        return weightSum > 0 ? weighted / weightSum : null;
    }

    /// <summary>
    /// Coefficient of variation (std ÷ mean) of the retained rates for one screen
    /// state, or a large sentinel when there is too little data — used to grade
    /// confidence.
    /// </summary>
    public double CoefficientOfVariation(ScreenState? screen = null)
    {
        List<double> values = [];
        foreach (Sample s in _samples)
        {
            if (screen is null || s.Screen == screen)
            {
                values.Add(s.RateMw);
            }
        }

        if (values.Count < 2)
        {
            return double.PositiveInfinity;
        }

        double mean = values.Average();
        if (mean <= 0)
        {
            return double.PositiveInfinity;
        }

        return LinearFit.StandardDeviation(values) / mean;
    }
}
