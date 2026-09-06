using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Persists and reads <c>BatteryHealthSnapshot</c> rows. Declared in Core,
/// implemented in Data — the same seam as <see cref="ISessionStore"/>, so the
/// Analytics project never references Data directly.
/// </summary>
public interface IHealthSnapshotStore
{
    /// <summary>Appends a snapshot for the given device. A snapshot already present for that exact timestamp is ignored (idempotent).</summary>
    Task AppendAsync(HealthScore score, string batteryHardwareId, double? retentionPercent, int? fullChargeMwh, int? cycleCount,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Retention history for a device from <paramref name="fromUtc"/> onward, oldest first.</summary>
    Task<IReadOnlyList<HealthSnapshotRow>> GetHistoryAsync(string batteryHardwareId, DateTimeOffset fromUtc, CancellationToken cancellationToken = default);

    /// <summary>The single most recent snapshot for a device, or <see langword="null"/>.</summary>
    Task<HealthSnapshotRow?> GetLatestAsync(string batteryHardwareId, CancellationToken cancellationToken = default);
}

/// <summary>Persists and reads <c>Insight</c> rows.</summary>
public interface IInsightStore
{
    /// <summary>Replaces the current non-dismissed insight set with <paramref name="insights"/> in one transaction.</summary>
    Task ReplaceCurrentAsync(IReadOnlyList<AnalyticsInsight> insights, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>The active (non-dismissed) insights, newest first.</summary>
    Task<IReadOnlyList<AnalyticsInsight>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks one insight dismissed so it is not shown again.</summary>
    Task DismissAsync(long insightId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The single read seam the analytics engine uses for historical data — sessions,
/// the aggregate tiers, and temperature exposure. Declared in Core, implemented in
/// Data.
/// </summary>
public interface IAnalyticsReadStore
{
    /// <summary>Sessions that overlap <c>[fromUtc, nowUtc]</c>, newest first.</summary>
    Task<IReadOnlyList<BatterySessionInfo>> GetSessionsAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default);

    /// <summary>Daily aggregate rows from <paramref name="fromUtc"/> onward, oldest first.</summary>
    Task<IReadOnlyList<DailyStatRow>> GetDailyStatisticsAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seconds spent at or above <paramref name="warnDeciKelvin"/> since
    /// <paramref name="fromUtc"/>, from <c>TemperatureSample</c>, or
    /// <see langword="null"/> when the table holds no rows (no sensor).
    /// </summary>
    Task<double?> GetTemperatureExposureSecondsAsync(DateTimeOffset fromUtc, int warnDeciKelvin, CancellationToken cancellationToken = default);
}
