using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// The application-level power monitoring orchestrator: <c>PowerViewModel →
/// IPowerMonitoringService → IBatteryMonitoringService</c>, mirroring
/// <see cref="IBatteryMonitoringService"/>'s role (specification section 73).
/// </summary>
/// <remarks>
/// A singleton hosted service. It samples the electrical quantities on the
/// power cadence (docs/monitoring-dataflow.md section 3), resolves the energy
/// rate down <c>PowerEstimator</c>'s ladder, maintains the bounded live series
/// and the min/max/avg accumulators, and persists <see cref="PowerReading"/>s via
/// <see cref="IPowerSampleWriteQueue"/>.
/// </remarks>
public interface IPowerMonitoringService
{
    /// <summary>The most recent reading for every present battery device.</summary>
    IReadOnlyList<PowerReading> CurrentReadings { get; }

    /// <summary>
    /// The reading the Power page's headline shows — the single battery, or the
    /// first present one — or null before the first successful sample.
    /// </summary>
    PowerReading? Primary { get; }

    /// <summary>The most recent sampling failure, or null when the last sample succeeded.</summary>
    string? LastError { get; }

    /// <summary>Raised on the thread pool after a new sample. Consumers marshal to the UI thread themselves.</summary>
    event EventHandler? Updated;

    /// <summary>Min / max / average for each electrical metric over <paramref name="window"/>, for the primary battery.</summary>
    PowerWindowStatistics GetStatistics(PowerWindow window);

    /// <summary>The three synchronised chart series over <paramref name="window"/>, for the primary battery.</summary>
    PowerSeriesSet GetSeries(PowerWindow window);

    /// <summary>Forces an immediate battery read and power sample, outside the regular cadence.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
