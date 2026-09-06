using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>An immutable, display-ready projection of one <see cref="BatterySnapshot"/>.</summary>
/// <remarks>
/// Rebuilt in full on every monitoring update rather than mutated in place — at
/// the default 30-second verify interval this costs nothing, and it keeps the
/// formatting logic in one pure function instead of spread across property
/// setters (specification section 75's "throttle, don't refresh everything"
/// concern does not apply at this update rate; it matters once Phase 5 adds
/// 5-second power sampling to the same pipeline).
/// </remarks>
public sealed class BatteryCardDisplay
{
    public required string Title { get; init; }

    public required DisplayValue Percentage { get; init; }

    /// <summary>Raw percentage, 0-100, for progress-bar binding. Zero when unavailable (the bar simply reads empty; the text alongside it always states "Not available").</summary>
    public required double PercentageValue { get; init; }

    public required DisplayValue State { get; init; }

    public required DisplayValue AcOnline { get; init; }

    public required DisplayValue Remaining { get; init; }

    public required DisplayValue Full { get; init; }

    public required DisplayValue Design { get; init; }

    public required DisplayValue Retention { get; init; }

    public required DisplayValue Voltage { get; init; }

    public required DisplayValue Power { get; init; }

    public required DisplayValue Current { get; init; }

    public required DisplayValue CycleCount { get; init; }

    public required DisplayValue Temperature { get; init; }

    public required string Identity { get; init; }

    /// <summary>Design capacity as a fraction of itself (always 1.0) — the reference bar in the capacity comparison.</summary>
    public double DesignFraction => 1.0;

    /// <summary>Full-charge capacity as a fraction of design capacity, for the capacity comparison bar (spec section 9). 0 when unavailable.</summary>
    public double FullFraction { get; init; }

    public static BatteryCardDisplay From(BatterySnapshot snapshot)
    {
        BatteryInfo info = snapshot.Info;
        BatteryDevice device = snapshot.Device;

        double fullFraction = 0.0;
        if (info.FullChargeCapacityMWh.Value is int full && info.DesignCapacityMWh.Value is int design && design > 0)
        {
            fullFraction = Math.Clamp(full / (double)design, 0.0, 1.0);
        }

        return new BatteryCardDisplay
        {
            Title = device.IsAggregate
                ? "All batteries"
                : device.DeviceName ?? device.Manufacturer ?? "Battery",
            Percentage = MeasurementFormatting.Percentage(info.Percentage),
            PercentageValue = Math.Clamp(info.Percentage.Value ?? 0.0, 0.0, 100.0),
            State = MeasurementFormatting.State(info.State),
            AcOnline = MeasurementFormatting.AcOnline(info.AcOnline),
            Remaining = MeasurementFormatting.MilliwattHours(info.RemainingCapacityMWh),
            Full = MeasurementFormatting.MilliwattHours(info.FullChargeCapacityMWh),
            Design = MeasurementFormatting.MilliwattHours(info.DesignCapacityMWh),
            Retention = MeasurementFormatting.RetentionPercentage(info.RetentionPercent),
            Voltage = MeasurementFormatting.Millivolts(info.VoltageMv),
            Power = MeasurementFormatting.Milliwatts(info.PowerMw),
            Current = MeasurementFormatting.Milliamps(info.CurrentMa),
            CycleCount = MeasurementFormatting.Count(info.CycleCount),
            Temperature = MeasurementFormatting.Celsius(info.TemperatureCelsius),
            Identity = DescribeIdentity(device),
            FullFraction = fullFraction,
        };
    }

    private static string DescribeIdentity(BatteryDevice device)
    {
        List<string> parts = [];
        if (device.Manufacturer is not null)
        {
            parts.Add(device.Manufacturer);
        }

        if (device.DeviceName is not null)
        {
            parts.Add(device.DeviceName);
        }

        if (device.Chemistry is not null)
        {
            parts.Add(device.Chemistry);
        }

        if (device.SerialNumber is not null)
        {
            parts.Add($"S/N {device.SerialNumber}");
        }

        return parts.Count == 0 ? "Not reported by any available source" : string.Join(" • ", parts);
    }
}
