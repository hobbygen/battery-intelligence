using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// One point-in-time electrical reading for a battery device: the Power page's
/// unit of live data and the shape persisted as a <c>PowerSample</c> row
/// (docs/database.md section 4; specification section 13).
/// </summary>
/// <remarks>
/// Distinct from <see cref="BatteryInfo"/>: this carries only the three electrical
/// quantities the Power subsystem owns, sampled on its own (faster) cadence and
/// resolved through <c>PowerEstimator</c>'s fallback ladder
/// (docs/estimation-strategy.md section 2). <see cref="CurrentMa"/> is always
/// Calculated, never Measured — the battery reports power and voltage, not current.
/// </remarks>
public sealed record PowerReading
{
    /// <summary>The <see cref="BatteryDevice.HardwareId"/> this reading belongs to.</summary>
    public required string BatteryId { get; init; }

    /// <summary>When this reading was captured, in UTC.</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    /// Signed energy rate, milliwatts: positive charging, negative discharging.
    /// Measured when a provider reported it, otherwise resolved down
    /// <c>PowerEstimator</c>'s ladder (Calculated from V×I, Estimated from
    /// capacity change, or Unavailable).
    /// </summary>
    public required Measurement<int> PowerMw { get; init; }

    /// <summary>Voltage, millivolts (C09).</summary>
    public required Measurement<int> VoltageMv { get; init; }

    /// <summary>Signed current, milliamps. Always Calculated, never Measured (C11).</summary>
    public required Measurement<double> CurrentMa { get; init; }

    /// <summary>Coarse energy-flow direction, for the persisted row and the charts.</summary>
    public required PowerDirection Direction { get; init; }

    /// <summary>A reading whose every electrical field is unavailable.</summary>
    public static PowerReading Empty(string batteryId, DateTimeOffset timestampUtc) => new()
    {
        BatteryId = batteryId,
        TimestampUtc = timestampUtc,
        PowerMw = Measurement<int>.Unavailable(),
        VoltageMv = Measurement<int>.Unavailable(),
        CurrentMa = Measurement<double>.Unavailable(),
        Direction = PowerDirection.Unknown,
    };
}
