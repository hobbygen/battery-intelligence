using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// One point-in-time battery-temperature reading: the Temperature page's unit of
/// live data and the shape persisted as a <c>TemperatureSample</c> row
/// (docs/database.md section 4; specification section 14).
/// </summary>
/// <remarks>
/// The temperature itself is a <see cref="Measurement{T}"/> so "no sensor on this
/// hardware" is representable — the reference-machine outcome. Battery Intelligence
/// never substitutes a CPU thermal-zone reading for a missing battery sensor
/// (spec §14; docs/capability-matrix.md S10).
/// </remarks>
public sealed record TemperatureReading
{
    /// <summary>The <see cref="BatteryDevice.HardwareId"/> this reading belongs to.</summary>
    public required string BatteryId { get; init; }

    /// <summary>When this reading was captured, in UTC.</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Battery temperature in degrees Celsius (C13). Measured (S4 → S3) or Unavailable.</summary>
    public required Measurement<double> TemperatureCelsius { get; init; }

    /// <summary>The fixed-boundary band this reading falls in (undefined when unavailable).</summary>
    public required TemperatureBand Band { get; init; }

    /// <summary>Severity relative to the configured thresholds.</summary>
    public required TemperatureSeverity Severity { get; init; }

    /// <summary>The charge context recorded alongside the sample (charging / discharging / idle).</summary>
    public required PowerDirection ChargeContext { get; init; }
}

/// <summary>How long a battery spent in one <see cref="TemperatureBand"/> over a window.</summary>
/// <param name="Band">The band.</param>
/// <param name="Duration">Accumulated time in the band.</param>
public sealed record TemperatureBandDuration(TemperatureBand Band, TimeSpan Duration);

/// <summary>
/// A recorded period the battery temperature held above the warning threshold for
/// longer than the dwell time (docs/ui-navigation.md — "recorded when the reading
/// stays above a threshold for more than 60 seconds, so brief spikes do not flood
/// the log").
/// </summary>
/// <param name="StartUtc">When the reading first crossed the threshold.</param>
/// <param name="EndUtc">When it dropped back below, or null while ongoing.</param>
/// <param name="PeakCelsius">The highest reading during the event.</param>
/// <param name="Context">The charge context at the peak.</param>
public sealed record ThresholdEvent(
    DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc,
    double PeakCelsius,
    PowerDirection Context)
{
    /// <summary>How long the event has lasted (so far).</summary>
    public TimeSpan Duration => (EndUtc ?? StartUtc) - StartUtc;

    /// <summary>Whether the temperature is still above the threshold.</summary>
    public bool IsOpen => EndUtc is null;
}
