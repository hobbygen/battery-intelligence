using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// The application-level analytics orchestrator: <c>StatisticsViewModel /
/// BatteryViewModel → IAnalyticsService → (analytics engine + stores)</c>,
/// mirroring <see cref="IPowerMonitoringService"/> (specification sections 16, 19,
/// 53; section 73).
/// </summary>
/// <remarks>
/// A singleton hosted service. It computes and caches the health score, the
/// degradation trend and the qualifying insight set on a slow cadence (plus
/// promptly when a session closes), and persists each health snapshot.
/// </remarks>
public interface IAnalyticsService
{
    /// <summary>The most recent Battery Health Score, or an Unavailable one before the first pass.</summary>
    HealthScore CurrentHealth { get; }

    /// <summary>The current smoothed degradation trend.</summary>
    DegradationTrend Trend { get; }

    /// <summary>The insights that currently qualify — empty when nothing clears the gates.</summary>
    IReadOnlyList<AnalyticsInsight> Insights { get; }

    /// <summary>The most recent analytics failure, or <see langword="null"/>.</summary>
    string? LastError { get; }

    /// <summary>Raised on the thread pool after a recompute. Consumers marshal to the UI thread themselves.</summary>
    event EventHandler? Updated;

    /// <summary>A usage summary for a window. <paramref name="range"/> is required only for <see cref="StatisticsWindow.Custom"/>.</summary>
    Task<StatisticsSummary> GetStatisticsAsync(StatisticsWindow window, DateRange? range = null, CancellationToken cancellationToken = default);

    /// <summary>The retention-over-time history for the primary battery, oldest first — the degradation-trend chart source.</summary>
    Task<IReadOnlyList<RetentionPoint>> GetRetentionHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Forces an immediate recompute, outside the regular cadence.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The live remaining-runtime estimate (specification sections 8 and 52;
/// docs/estimation-strategy.md section 3).
/// </summary>
public interface IRuntimeEstimationService
{
    /// <summary>The current estimate. <see cref="RuntimeEstimate.Calculating"/> until enough discharge history exists.</summary>
    RuntimeEstimate Current { get; }

    /// <summary>The discharge breakdown over the recent rolling window — mean rate and the screen-on/off split.</summary>
    DischargeAnalysis RecentDischarge { get; }

    /// <summary>Raised on the thread pool after each update.</summary>
    event EventHandler? Updated;
}
