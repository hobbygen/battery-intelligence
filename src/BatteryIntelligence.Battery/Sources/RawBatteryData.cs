using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Battery.Sources;

/// <summary>
/// One source's view of a battery device's identity. Correlated across sources by
/// <see cref="CorrelationIndex"/> — positional correlation, not identifier
/// matching, because WinRT, WMI and SetupDi each use their own unrelated device
/// identifier scheme for the same physical battery.
/// </summary>
/// <remarks>
/// Positional correlation is exact for the overwhelming common case (one
/// battery) and reasonable for well-behaved multi-battery systems, since every
/// source enumerates ACPI battery devices in the same underlying firmware order.
/// It is a documented simplification (docs/roadmap.md Phase 2), not a claim of
/// perfect correlation on exotic hardware.
/// </remarks>
internal sealed record RawBatteryDevice(
    int CorrelationIndex,
    string? DeviceName,
    string? Manufacturer,
    string? SerialNumber,
    string? Chemistry,
    Measurement<int> DesignCapacityMWh,
    Measurement<int> DesignVoltageMv,
    bool ReportsInMilliamps,
    string? UniqueId = null);

/// <summary>One source's point-in-time reading for one battery device.</summary>
internal sealed record RawBatteryReading(
    Measurement<double> Percentage,
    Measurement<BatteryState> State,
    Measurement<bool> AcOnline,
    Measurement<int> RemainingCapacityMWh,
    Measurement<int> FullChargeCapacityMWh,
    Measurement<int> VoltageMv,
    Measurement<int> PowerMw,
    Measurement<int> CycleCount,
    Measurement<double> TemperatureCelsius)
{
    internal static RawBatteryReading Empty { get; } = new(
        Measurement<double>.Unavailable(),
        Measurement<BatteryState>.Unavailable(),
        Measurement<bool>.Unavailable(),
        Measurement<int>.Unavailable(),
        Measurement<int>.Unavailable(),
        Measurement<int>.Unavailable(),
        Measurement<int>.Unavailable(),
        Measurement<int>.Unavailable(),
        Measurement<double>.Unavailable());
}

/// <summary>One device's identity and reading, as seen by one raw source.</summary>
internal sealed record RawBatteryEntry(RawBatteryDevice Device, RawBatteryReading Reading);

/// <summary>
/// A single hardware/API source in the priority chain
/// (docs/capability-matrix.md section 2). Internal to the Battery project — Core
/// and everything above it see only the merged <see cref="Core.Interfaces.IBatteryProvider"/>.
/// </summary>
internal interface IRawBatterySource
{
    /// <summary>Which <see cref="MeasurementSource"/> this source tags its readings with.</summary>
    MeasurementSource SourceId { get; }

    /// <summary>
    /// Reads every battery device this source can see. Never throws for an
    /// expected "not available" condition — returns an empty list instead, so one
    /// failing source cannot take down the others (specification section 44).
    /// </summary>
    Task<IReadOnlyList<RawBatteryEntry>> ReadAsync(CancellationToken cancellationToken);
}
