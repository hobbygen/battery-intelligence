using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// The application-level process-attribution orchestrator: <c>AppUsageViewModel →
/// IProcessMonitoringService → (IProcessEnumerator + IBatteryMonitoringService)</c>,
/// mirroring <see cref="IPowerMonitoringService"/> (specification sections 15, 55, 73).
/// </summary>
/// <remarks>
/// A singleton hosted service on its own (slower) cadence — process enumeration is
/// the heaviest sampler (docs/monitoring-dataflow.md section 5). It groups
/// processes into applications, separates the non-attributable baseline, runs
/// <c>AppEnergyV1</c>, and persists per-process rows via
/// <see cref="IProcessSampleWriteQueue"/>.
/// </remarks>
public interface IProcessMonitoringService
{
    /// <summary>The latest attribution over the last hour, or an empty one before the first tick.</summary>
    AppEnergyAttribution CurrentAttribution { get; }

    /// <summary>
    /// Whether absolute milliwatt figures are meaningful right now (on battery) or
    /// only the ranking is (on AC — no battery draw to divide).
    /// </summary>
    bool AbsoluteAvailable { get; }

    /// <summary>The estimation model tag, e.g. "AppEnergyV1".</summary>
    string EstimatorVersion { get; }

    /// <summary>The most recent sampling failure, or <see langword="null"/> when the last tick succeeded.</summary>
    string? LastError { get; }

    /// <summary>Raised on the thread pool after each tick. Consumers marshal to the UI thread themselves.</summary>
    event EventHandler? Updated;

    /// <summary>The ranked attribution averaged over <paramref name="window"/>.</summary>
    AppEnergyAttribution GetRanking(ProcessWindow window);

    /// <summary>Forces an immediate sampling tick, outside the regular cadence.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
