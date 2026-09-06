using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// A single point-in-time reading for one battery device (or the system-wide
/// aggregate).
/// </summary>
/// <remarks>
/// Every field is a <see cref="Measurement{T}"/> so that "not available on this
/// hardware" is representable everywhere the specification's core engineering
/// principle applies (specification section 3). There is no bare
/// <see langword="double"/> or <see langword="int"/> field on this type — that is
/// deliberate, not an oversight.
/// </remarks>
public sealed record BatteryInfo
{
    /// <summary>The <see cref="BatteryDevice.HardwareId"/> this reading belongs to.</summary>
    public required string BatteryId { get; init; }

    /// <summary>When this reading was captured, in UTC.</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Charge percentage, 0-100 (C02).</summary>
    public required Measurement<double> Percentage { get; init; }

    /// <summary>Charging / discharging / idle / full / not present (C03).</summary>
    public required Measurement<BatteryState> State { get; init; }

    /// <summary>Whether AC power is connected (C04).</summary>
    public required Measurement<bool> AcOnline { get; init; }

    /// <summary>Remaining capacity, milliwatt-hours (C05).</summary>
    public required Measurement<int> RemainingCapacityMWh { get; init; }

    /// <summary>Full-charge capacity, milliwatt-hours (C06).</summary>
    public required Measurement<int> FullChargeCapacityMWh { get; init; }

    /// <summary>Design capacity, milliwatt-hours (C07).</summary>
    public required Measurement<int> DesignCapacityMWh { get; init; }

    /// <summary>Capacity retention, 0-100%. Always Calculated (C08).</summary>
    public required Measurement<double> RetentionPercent { get; init; }

    /// <summary>Voltage, millivolts (C09).</summary>
    public required Measurement<int> VoltageMv { get; init; }

    /// <summary>Signed power rate, milliwatts: positive charging, negative discharging (C10).</summary>
    public required Measurement<int> PowerMw { get; init; }

    /// <summary>Signed current, milliamps. Always Calculated, never Measured (C11).</summary>
    public required Measurement<double> CurrentMa { get; init; }

    /// <summary>Charge cycle count. A firmware-reported zero is treated as Unavailable (quirk Q5) (C12).</summary>
    public required Measurement<int> CycleCount { get; init; }

    /// <summary>Battery temperature in degrees Celsius (C13).</summary>
    public required Measurement<double> TemperatureCelsius { get; init; }

    /// <summary>
    /// An unavailable reading for a device that could not be read at all — every
    /// field Unavailable, at the given timestamp. Used when a provider fails
    /// outright rather than reporting individual fields.
    /// </summary>
    public static BatteryInfo Empty(string batteryId, DateTimeOffset timestampUtc) => new()
    {
        BatteryId = batteryId,
        TimestampUtc = timestampUtc,
        Percentage = Measurement<double>.Unavailable(),
        State = Measurement<BatteryState>.Unavailable(),
        AcOnline = Measurement<bool>.Unavailable(),
        RemainingCapacityMWh = Measurement<int>.Unavailable(),
        FullChargeCapacityMWh = Measurement<int>.Unavailable(),
        DesignCapacityMWh = Measurement<int>.Unavailable(),
        RetentionPercent = Measurement<double>.Unavailable(),
        VoltageMv = Measurement<int>.Unavailable(),
        PowerMw = Measurement<int>.Unavailable(),
        CurrentMa = Measurement<double>.Unavailable(),
        CycleCount = Measurement<int>.Unavailable(),
        TemperatureCelsius = Measurement<double>.Unavailable(),
    };
}

/// <summary>A device's identity paired with its current reading.</summary>
/// <param name="Device">Identity — manufacturer, model, design capacity.</param>
/// <param name="Info">The current point-in-time reading.</param>
public sealed record BatterySnapshot(BatteryDevice Device, BatteryInfo Info);
