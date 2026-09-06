using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Battery;

/// <summary>
/// Combines per-device readings into the system-wide figure specification
/// section 25 requires when multiple batteries are present.
/// </summary>
/// <remarks>
/// Only mathematically meaningful quantities are combined. Voltage is
/// deliberately <em>not</em> summed or averaged across devices with different
/// chemistries/configurations — spec section 25 explicitly forbids incorrectly
/// combining incompatible measurements — so the aggregate reports it Unavailable
/// unless exactly one battery is present.
/// </remarks>
public static class BatteryAggregation
{
    /// <summary>
    /// Produces the aggregate reading across every supplied snapshot.
    /// </summary>
    /// <param name="snapshots">One snapshot per present battery device.</param>
    /// <param name="timestampUtc">Timestamp to stamp on the aggregate.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="snapshots"/> is empty — no
    /// battery present, so there is nothing to aggregate. The caller decides how
    /// that renders ("No battery detected").
    /// </returns>
    public static BatteryInfo? Aggregate(IReadOnlyList<BatterySnapshot> snapshots, DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        if (snapshots.Count == 0)
        {
            return null;
        }

        if (snapshots.Count == 1)
        {
            // A single-battery system's aggregate is simply that battery's reading,
            // re-tagged with the aggregate identity so the UI can bind uniformly
            // whether one battery or several are present.
            return snapshots[0].Info with { BatteryId = BatteryDevice.AggregateHardwareId };
        }

        Measurement<int> remaining = SumMWh(snapshots, s => s.Info.RemainingCapacityMWh);
        Measurement<int> full = SumMWh(snapshots, s => s.Info.FullChargeCapacityMWh);
        Measurement<int> design = SumMWh(snapshots, s => s.Info.DesignCapacityMWh);
        Measurement<int> power = SumSignedMw(snapshots, s => s.Info.PowerMw);
        Measurement<double> percentage = ComputeAggregatePercentage(remaining, full);
        Measurement<double> retention = BatteryCalculations.CalculateRetentionPercent(full, design);
        Measurement<bool> acOnline = ComputeAggregateAcOnline(snapshots);
        BatteryState state = DeriveAggregateState(snapshots);

        return new BatteryInfo
        {
            BatteryId = BatteryDevice.AggregateHardwareId,
            TimestampUtc = timestampUtc,
            Percentage = percentage,
            State = Measurement<BatteryState>.Calculated(state, MeasurementSource.Derived),
            AcOnline = acOnline,
            RemainingCapacityMWh = remaining,
            FullChargeCapacityMWh = full,
            DesignCapacityMWh = design,
            RetentionPercent = retention,
            VoltageMv = Measurement<int>.Unavailable(MeasurementSource.Derived),
            PowerMw = power,
            CurrentMa = Measurement<double>.Unavailable(MeasurementSource.Derived),
            CycleCount = Measurement<int>.Unavailable(MeasurementSource.Derived),
            TemperatureCelsius = Measurement<double>.Unavailable(MeasurementSource.Derived),
        };
    }

    private static Measurement<double> ComputeAggregatePercentage(Measurement<int> remaining, Measurement<int> full)
    {
        if (remaining.Value is not int r || full.Value is not int f || f <= 0)
        {
            return Measurement<double>.Unavailable(MeasurementSource.Derived);
        }

        double pct = Math.Clamp(r / (double)f * 100.0, 0.0, 100.0);
        DataQuality quality = remaining.Quality.Worst(full.Quality);
        return quality == DataQuality.Measured
            ? Measurement<double>.Calculated(pct)
            : Measurement<double>.Calculated(pct).DegradedBy(quality);
    }

    private static Measurement<bool> ComputeAggregateAcOnline(IReadOnlyList<BatterySnapshot> snapshots)
    {
        List<Measurement<bool>> values = [.. snapshots.Select(s => s.Info.AcOnline).Where(v => v.HasValue)];

        if (values.Count == 0)
        {
            return Measurement<bool>.Unavailable();
        }

        bool anyOnline = values.Any(v => v.Value == true);
        DataQuality worst = DataQualityExtensions.Worst(values.Select(v => v.Quality));
        MeasurementSource source = values[0].Source;

        return worst == DataQuality.Measured
            ? Measurement<bool>.Measured(anyOnline, source)
            : Measurement<bool>.Calculated(anyOnline, source);
    }

    private static Measurement<int> SumMWh(
        IReadOnlyList<BatterySnapshot> snapshots,
        Func<BatterySnapshot, Measurement<int>> selector)
    {
        List<Measurement<int>> values = [.. snapshots.Select(selector)];

        if (values.Any(v => !v.HasValue))
        {
            return Measurement<int>.Unavailable(MeasurementSource.Derived);
        }

        int sum = values.Sum(v => v.Value!.Value);
        DataQuality worst = DataQualityExtensions.Worst(values.Select(v => v.Quality));

        return worst == DataQuality.Measured
            ? Measurement<int>.Calculated(sum)
            : Measurement<int>.Calculated(sum).DegradedBy(worst);
    }

    private static Measurement<int> SumSignedMw(
        IReadOnlyList<BatterySnapshot> snapshots,
        Func<BatterySnapshot, Measurement<int>> selector)
    {
        List<Measurement<int>> values = [.. snapshots.Select(selector).Where(v => v.HasValue)];

        if (values.Count == 0)
        {
            return Measurement<int>.Unavailable(MeasurementSource.Derived);
        }

        int sum = values.Sum(v => v.Value!.Value);
        DataQuality worst = DataQualityExtensions.Worst(values.Select(v => v.Quality));

        return worst == DataQuality.Measured
            ? Measurement<int>.Calculated(sum)
            : Measurement<int>.Calculated(sum).DegradedBy(worst);
    }

    private static BatteryState DeriveAggregateState(IReadOnlyList<BatterySnapshot> snapshots)
    {
        List<BatteryState> states = [.. snapshots
            .Select(s => s.Info.State.Value)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)];

        if (states.Count == 0)
        {
            return BatteryState.Unknown;
        }

        // Priority: any battery charging or discharging dominates the summary;
        // only when every present battery is full/idle does the aggregate read
        // that way. This matches what a user expects "system status" to mean.
        if (states.Contains(BatteryState.Charging))
        {
            return BatteryState.Charging;
        }

        if (states.Contains(BatteryState.Discharging))
        {
            return BatteryState.Discharging;
        }

        if (states.All(s => s == BatteryState.Full))
        {
            return BatteryState.Full;
        }

        return BatteryState.Idle;
    }
}
