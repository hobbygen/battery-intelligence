using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Thermal;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// The application-level temperature monitoring orchestrator: <c>TemperatureViewModel
/// → ITemperatureMonitoringService → IBatteryMonitoringService</c>, mirroring
/// <see cref="IPowerMonitoringService"/>'s role (specification section 73).
/// </summary>
/// <remarks>
/// A singleton hosted service. It rides the battery monitor's readings, reads
/// their <c>TemperatureCelsius</c> field (already resolved S4 → S3 by the battery
/// pipeline), classifies each into a band and a severity, keeps the bounded live
/// series, the per-band accumulators and the min/max/avg accumulator the
/// Temperature page binds to, detects threshold events, and persists readings via
/// <see cref="ITemperatureSampleWriteQueue"/>. On hardware with no battery
/// temperature sensor — the reference machine — <see cref="SensorAvailable"/> is
/// <see langword="false"/> and the page renders the unavailable state.
/// </remarks>
public interface ITemperatureMonitoringService
{
    /// <summary>The most recent reading for every present battery device.</summary>
    IReadOnlyList<TemperatureReading> CurrentReadings { get; }

    /// <summary>The reading the Temperature page's headline shows, or null before the first sample.</summary>
    TemperatureReading? Primary { get; }

    /// <summary>
    /// Whether this hardware exposes a battery temperature sensor at all. False on
    /// the reference machine — the page shows "sensor not exposed" and does not
    /// substitute another sensor (spec §14).
    /// </summary>
    bool SensorAvailable { get; }

    /// <summary>The active thresholds (from the configured alert warning value).</summary>
    TemperatureThresholds Thresholds { get; }

    /// <summary>The most recent sampling failure, or null when the last sample succeeded.</summary>
    string? LastError { get; }

    /// <summary>Raised on the thread pool after a new sample. Consumers marshal to the UI thread themselves.</summary>
    event EventHandler? Updated;

    /// <summary>Min / max / average over <paramref name="window"/>, for the primary battery.</summary>
    MetricStatistics GetStatistics(TemperatureWindow window);

    /// <summary>The temperature chart series over <paramref name="window"/>, for the primary battery.</summary>
    ChartSeries GetSeries(TemperatureWindow window);

    /// <summary>Time spent in each band over <paramref name="window"/>, for the primary battery.</summary>
    IReadOnlyList<TemperatureBandDuration> GetBandBreakdown(TemperatureWindow window);

    /// <summary>Recent threshold events for the primary battery, newest first.</summary>
    IReadOnlyList<ThresholdEvent> RecentThresholdEvents { get; }

    /// <summary>Forces an immediate battery read and temperature sample, outside the regular cadence.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
