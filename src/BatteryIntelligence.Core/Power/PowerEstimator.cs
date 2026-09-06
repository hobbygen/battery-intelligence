using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Power;

/// <summary>
/// Resolves the signed energy rate (milliwatts) down the fallback ladder in
/// docs/estimation-strategy.md section 2, so the Power page always shows an
/// honestly-graded figure — or "Unavailable" — never a fabricated one.
/// </summary>
/// <remarks>
/// Pure and stateless: the caller owns the capacity history. The reference
/// machine satisfies rung 1 (a provider reports mW directly), so rungs 2–4 exist
/// for other hardware. Every rung's grade can only degrade
/// (<see cref="Measurement{T}.DegradedBy"/>) — an estimate combined with a
/// measurement is an estimate.
/// </remarks>
public static class PowerEstimator
{
    /// <summary>The shortest capacity-history span that yields a usable ΔmWh/Δt estimate (rung 3).</summary>
    public static readonly TimeSpan MinimumEstimateWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Resolves the energy rate for one reading.
    /// </summary>
    /// <param name="measuredPowerMw">A provider's directly-reported rate, if any (rung 1).</param>
    /// <param name="voltageMv">Battery voltage, for rung 2.</param>
    /// <param name="measuredCurrentMa">
    /// A <em>separately measured</em> current, if the hardware exposes one (rung 2).
    /// A current that is itself derived from power must not be passed here — that
    /// would make rung 2 circular.
    /// </param>
    /// <param name="capacityHistoryMWh">
    /// Recent remaining-capacity points (milliwatt-hours), oldest first, for
    /// rung 3. The sign of the resulting rate follows the direction of change.
    /// </param>
    /// <param name="now">The timestamp being resolved.</param>
    public static Measurement<int> Estimate(
        Measurement<int> measuredPowerMw,
        Measurement<int> voltageMv,
        Measurement<double> measuredCurrentMa,
        IReadOnlyList<TimePoint> capacityHistoryMWh,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(capacityHistoryMWh);

        // Rung 1 — Measured. A provider reported the rate; nothing to add.
        if (measuredPowerMw is { HasValue: true, Quality: not DataQuality.Suspect })
        {
            return measuredPowerMw;
        }

        // Rung 2 — Calculated. P = V x I, only when both inputs are genuinely
        // measured and independent.
        if (voltageMv is { HasValue: true, Quality: not DataQuality.Suspect } v &&
            measuredCurrentMa is { HasValue: true, Quality: DataQuality.Measured } i)
        {
            double watts = v.Value!.Value / 1000.0 * (i.Value!.Value / 1000.0);
            int milliwatts = (int)Math.Round(watts * 1000.0);
            return Measurement<int>.Calculated(milliwatts, MeasurementSource.Derived)
                .DegradedBy(v.Quality);
        }

        // Rung 3 — Estimated. rate ~= dmWh / dhours over a window of at least a minute.
        if (TryEstimateFromCapacity(capacityHistoryMWh, now, out int estimatedMw))
        {
            return Measurement<int>.Estimated(estimatedMw, MeasurementSource.Model);
        }

        // Rung 4 — Unavailable. Better an honest gap than a plausible-looking guess.
        return Measurement<int>.Unavailable(MeasurementSource.Model);
    }

    private static bool TryEstimateFromCapacity(
        IReadOnlyList<TimePoint> history, DateTimeOffset now, out int rateMw)
    {
        rateMw = 0;

        if (history.Count < 2)
        {
            return false;
        }

        TimePoint oldest = history[0];
        TimePoint newest = history[^1];

        // Guard against an out-of-order or stale tail.
        if (newest.TimestampUtc <= oldest.TimestampUtc || now - newest.TimestampUtc > MinimumEstimateWindow)
        {
            return false;
        }

        TimeSpan span = newest.TimestampUtc - oldest.TimestampUtc;
        if (span < MinimumEstimateWindow)
        {
            return false;
        }

        double deltaMWh = newest.Value - oldest.Value;
        double hours = span.TotalHours;
        if (hours <= 0)
        {
            return false;
        }

        // dmWh / dhours already has the right unit (mW) and the right sign:
        // capacity rising => charging => positive rate.
        double estimate = deltaMWh / hours;
        if (double.IsNaN(estimate) || double.IsInfinity(estimate))
        {
            return false;
        }

        rateMw = (int)Math.Round(estimate);
        return true;
    }
}
