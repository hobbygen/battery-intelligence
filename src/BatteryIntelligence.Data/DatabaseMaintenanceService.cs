using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Data;

/// <summary>
/// Rolls raw samples up into <c>SampleMinute</c> and enforces retention
/// (docs/database.md sections 6-7; specification sections 29 and 31).
/// </summary>
/// <remarks>
/// <para>
/// Rollup runs every 60 seconds and is idempotent: a minute already present in
/// <c>SampleMinute</c> is never recomputed, so re-running (including after a
/// crash mid-rollup) is harmless. Only minutes at least two minutes old are
/// considered, which comfortably outlives the write queue's 30-second flush
/// lag — a minute is never rolled while it could still receive a late sample.
/// </para>
/// <para>
/// Retention deletes raw <c>BatterySample</c> rows only once they are both older
/// than the configured window <em>and</em> already rolled up, and never deletes a
/// row attached to an open session (docs/database.md section 6 cleanup rules).
/// <c>PowerSample</c> rows are raw-only (no rollup tier, no session link), so
/// they are dropped on a plain age cutoff.
/// </para>
/// </remarks>
public sealed class DatabaseMaintenanceService : IHostedService, IDisposable
{
    private static readonly TimeSpan RollupInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RollupSafetyMargin = TimeSpan.FromMinutes(2);
    private const long DayMs = 86_400_000;

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ISettingsService _settings;
    private readonly ILogger<DatabaseMaintenanceService> _logger;
    private readonly IMonitoringStatusRegistry _status;
    private readonly SemaphoreSlim _rollupGate = new(1, 1);
    private readonly SemaphoreSlim _retentionGate = new(1, 1);

    private Timer? _rollupTimer;
    private Timer? _retentionTimer;

    public DatabaseMaintenanceService(
        ISqliteConnectionFactory connectionFactory,
        ISettingsService settings,
        ILogger<DatabaseMaintenanceService> logger,
        IMonitoringStatusRegistry status)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _connectionFactory = connectionFactory;
        _settings = settings;
        _logger = logger;
        _status = status;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _rollupTimer = new Timer(_ => _ = RunRollupAsync(CancellationToken.None), null, RollupInterval, RollupInterval);
        _retentionTimer = new Timer(_ => _ = RunRetentionAsync(CancellationToken.None), null, TimeSpan.FromMinutes(1), RetentionInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _rollupTimer?.Dispose();
        _retentionTimer?.Dispose();
        _rollupTimer = null;
        _retentionTimer = null;
        return Task.CompletedTask;
    }

    /// <summary>Runs one rollup pass now. Exposed for integration tests, which cannot wait 60 seconds for the timer.</summary>
    public async Task RunRollupAsync(CancellationToken cancellationToken)
    {
        if (!await _rollupGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            long cutoffMs = DateTimeOffset.UtcNow.Subtract(RollupSafetyMargin).ToUnixTimeMilliseconds();

            await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO SampleMinute
                    (BatteryId, MinuteUtc, AvgPercentage, MinPercentage, MaxPercentage,
                     AvgPowerMw, MinPowerMw, MaxPowerMw, AvgVoltageMv, SampleCount)
                SELECT
                    bs.BatteryId,
                    (bs.TimestampUtc / 60000) * 60000 AS MinuteUtc,
                    AVG(bs.Percentage), MIN(bs.Percentage), MAX(bs.Percentage),
                    CAST(ROUND(AVG(bs.PowerMw)) AS INTEGER), MIN(bs.PowerMw), MAX(bs.PowerMw),
                    CAST(ROUND(AVG(bs.VoltageMv)) AS INTEGER),
                    COUNT(*)
                FROM BatterySample bs
                WHERE bs.DataQuality != 4
                  AND bs.TimestampUtc < $cutoff
                  AND NOT EXISTS (
                      SELECT 1 FROM SampleMinute sm
                      WHERE sm.BatteryId = bs.BatteryId AND sm.MinuteUtc = (bs.TimestampUtc / 60000) * 60000
                  )
                GROUP BY bs.BatteryId, MinuteUtc;
                """;
            command.Parameters.AddWithValue("$cutoff", cutoffMs);

            int rolled = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (rolled > 0)
            {
                _logger.LogDebug("Rolled {Count} minute bucket(s) into SampleMinute.", rolled);
            }

            await RollUpApplicationUsageAsync(connection, cancellationToken).ConfigureAwait(false);
            await RollUpSampleHourAsync(connection, cancellationToken).ConfigureAwait(false);
            await RollUpDailyStatisticsAsync(connection, cancellationToken).ConfigureAwait(false);
            _status.ReportSuccess(MonitoringComponent.Database);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _status.ReportFailure(MonitoringComponent.Database, ex.Message);
            _logger.LogWarning(ex, "Rollup pass failed; will retry on the next interval.");
        }
        finally
        {
            _rollupGate.Release();
        }
    }

    /// <summary>
    /// Idempotently rolls fully-elapsed days of <c>ProcessSample</c> rows into the
    /// daily <c>ApplicationUsage</c> aggregate (docs/database.md section 4;
    /// docs/roadmap.md Phase 7). A day already present for an application key is
    /// never recomputed, and only days strictly before today are considered, so a
    /// still-accumulating day is never summarised early.
    /// </summary>
    private async Task RollUpApplicationUsageAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        long todayStartMs = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / DayMs) * DayMs;
        int cadenceSeconds = Math.Max(1, _settings.Current.Monitoring.ProcessSampleSeconds);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ApplicationUsage
                (DayUtc, ApplicationKey, DisplayName, TotalSeconds, ForegroundSeconds,
                 AvgCpuPercent, EstimatedEnergyMwh, EstimatorVersion)
            SELECT
                (ps.TimestampUtc / 86400000) * 86400000 AS DayUtc,
                ps.ApplicationKey,
                (SELECT ps2.ProcessName FROM ProcessSample ps2
                 WHERE ps2.ApplicationKey = ps.ApplicationKey
                   AND (ps2.TimestampUtc / 86400000) = (ps.TimestampUtc / 86400000)
                 ORDER BY ps2.TimestampUtc DESC LIMIT 1),
                COUNT(*) * $cadence,
                SUM(CASE WHEN ps.IsForeground = 1 THEN 1 ELSE 0 END) * $cadence,
                AVG(ps.CpuPercent),
                CAST(ROUND(SUM(COALESCE(ps.EstimatedPowerMw, 0)) * $cadence / 3600.0) AS INTEGER),
                MAX(ps.EstimatorVersion)
            FROM ProcessSample ps
            WHERE (ps.TimestampUtc / 86400000) * 86400000 < $todayStart
              AND ps.ApplicationKey <> '__baseline__'
              AND NOT EXISTS (
                  SELECT 1 FROM ApplicationUsage au
                  WHERE au.DayUtc = (ps.TimestampUtc / 86400000) * 86400000
                    AND au.ApplicationKey = ps.ApplicationKey
              )
            GROUP BY DayUtc, ps.ApplicationKey;
            """;
        command.Parameters.AddWithValue("$cadence", cadenceSeconds);
        command.Parameters.AddWithValue("$todayStart", todayStartMs);

        int rolled = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rolled > 0)
        {
            _logger.LogDebug("Rolled {Count} application/day row(s) into ApplicationUsage.", rolled);
        }
    }

    /// <summary>
    /// Idempotently rolls fully-elapsed hours of <c>SampleMinute</c> into
    /// <c>SampleHour</c> (closes docs/traceability.md R-067). Only hours strictly
    /// before the current hour are considered.
    /// </summary>
    private async Task RollUpSampleHourAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const long hourMs = 3_600_000;
        long hourCutoff = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / hourMs) * hourMs;

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SampleHour
                (BatteryId, HourUtc, AvgPercentage, MinPercentage, MaxPercentage,
                 AvgPowerMw, MinPowerMw, MaxPowerMw, AvgVoltageMv, AvgTemperatureDk,
                 ScreenOnSeconds, SampleCount)
            SELECT
                sm.BatteryId,
                (sm.MinuteUtc / 3600000) * 3600000 AS HourUtc,
                AVG(sm.AvgPercentage), MIN(sm.MinPercentage), MAX(sm.MaxPercentage),
                CAST(ROUND(AVG(sm.AvgPowerMw)) AS INTEGER), MIN(sm.MinPowerMw), MAX(sm.MaxPowerMw),
                CAST(ROUND(AVG(sm.AvgVoltageMv)) AS INTEGER),
                CAST(ROUND(AVG(sm.AvgTemperatureDk)) AS INTEGER),
                SUM(sm.ScreenOnSeconds),
                SUM(sm.SampleCount)
            FROM SampleMinute sm
            WHERE (sm.MinuteUtc / 3600000) * 3600000 < $hourCutoff
              AND NOT EXISTS (
                  SELECT 1 FROM SampleHour sh
                  WHERE sh.BatteryId = sm.BatteryId AND sh.HourUtc = (sm.MinuteUtc / 3600000) * 3600000
              )
            GROUP BY sm.BatteryId, HourUtc;
            """;
        command.Parameters.AddWithValue("$hourCutoff", hourCutoff);

        int rolled = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rolled > 0)
        {
            _logger.LogDebug("Rolled {Count} hour bucket(s) into SampleHour.", rolled);
        }
    }

    /// <summary>
    /// Idempotently rolls fully-elapsed days of closed <c>BatterySession</c> rows
    /// into <c>DailyStatistics</c>, attributing each session to its start day.
    /// Temperature and end-of-day health columns are left null for now — the
    /// statistics engine computes from sessions directly; this tier is for Phase 11.
    /// </summary>
    private async Task RollUpDailyStatisticsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        long todayStartMs = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / DayMs) * DayMs;

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DailyStatistics
                (BatteryId, DayUtc, ChargeSessions, DischargeSessions, ChargingSeconds, DischargingSeconds,
                 ScreenOnSeconds, ScreenOffSeconds, SleepSeconds, PercentCharged, PercentDischarged,
                 AvgChargeRateMw, AvgDischargeRateMw)
            SELECT
                s.BatteryId,
                (s.StartUtc / 86400000) * 86400000 AS DayUtc,
                SUM(CASE WHEN s.SessionType = 1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN s.SessionType = 2 THEN 1 ELSE 0 END),
                CAST(SUM(CASE WHEN s.SessionType = 1 THEN (s.EndUtc - s.StartUtc) / 1000 ELSE 0 END) AS INTEGER),
                CAST(SUM(CASE WHEN s.SessionType = 2 THEN (s.EndUtc - s.StartUtc) / 1000 ELSE 0 END) AS INTEGER),
                SUM(s.ScreenOnSeconds), SUM(s.ScreenOffSeconds), SUM(s.SleepSeconds),
                SUM(CASE WHEN s.SessionType = 1 AND s.EndPercentage > s.StartPercentage
                         THEN s.EndPercentage - s.StartPercentage ELSE 0 END),
                SUM(CASE WHEN s.SessionType = 2 AND s.StartPercentage > s.EndPercentage
                         THEN s.StartPercentage - s.EndPercentage ELSE 0 END),
                CAST(ROUND(AVG(CASE WHEN s.SessionType = 1 AND s.EndCapacityMwh > s.StartCapacityMwh
                         THEN (s.EndCapacityMwh - s.StartCapacityMwh) * 3600000.0 / (s.EndUtc - s.StartUtc) END)) AS INTEGER),
                CAST(ROUND(AVG(CASE WHEN s.SessionType = 2 AND s.StartCapacityMwh > s.EndCapacityMwh
                         THEN (s.StartCapacityMwh - s.EndCapacityMwh) * 3600000.0 / (s.EndUtc - s.StartUtc) END)) AS INTEGER)
            FROM BatterySession s
            WHERE s.EndUtc IS NOT NULL
              AND (s.StartUtc / 86400000) * 86400000 < $todayStart
              AND NOT EXISTS (
                  SELECT 1 FROM DailyStatistics ds
                  WHERE ds.BatteryId = s.BatteryId AND ds.DayUtc = (s.StartUtc / 86400000) * 86400000
              )
            GROUP BY s.BatteryId, DayUtc;
            """;
        command.Parameters.AddWithValue("$todayStart", todayStartMs);

        int rolled = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rolled > 0)
        {
            _logger.LogDebug("Rolled {Count} day(s) into DailyStatistics.", rolled);
        }
    }

    /// <summary>Runs one retention pass now. Exposed for integration tests.</summary>
    public async Task RunRetentionAsync(CancellationToken cancellationToken)
    {
        if (!await _retentionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            DataSettings data = _settings.Current.Data;
            long rawCutoffMs = DateTimeOffset.UtcNow.AddDays(-data.RawRetentionDays).ToUnixTimeMilliseconds();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                int deleted;
                await using (SqliteCommand delete = connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = """
                        DELETE FROM BatterySample
                        WHERE TimestampUtc < $cutoff
                          AND (SessionId IS NULL OR SessionId NOT IN (SELECT Id FROM BatterySession WHERE EndUtc IS NULL))
                          AND EXISTS (
                              SELECT 1 FROM SampleMinute sm
                              WHERE sm.BatteryId = BatterySample.BatteryId
                                AND sm.MinuteUtc = (BatterySample.TimestampUtc / 60000) * 60000
                          );
                        """;
                    delete.Parameters.AddWithValue("$cutoff", rawCutoffMs);
                    deleted = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                int powerDeleted;
                await using (SqliteCommand deletePower = connection.CreateCommand())
                {
                    // PowerSample is raw-only — it feeds the Power page's live
                    // charts, not a rollup tier (docs/database.md; docs/roadmap.md
                    // Phase 5 deviations), and carries no SessionId, so retention
                    // is a plain age cutoff with no open-session or rolled-up guard.
                    deletePower.Transaction = transaction;
                    deletePower.CommandText = "DELETE FROM PowerSample WHERE TimestampUtc < $cutoff;";
                    deletePower.Parameters.AddWithValue("$cutoff", rawCutoffMs);
                    powerDeleted = await deletePower.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                int temperatureDeleted;
                await using (SqliteCommand deleteTemperature = connection.CreateCommand())
                {
                    // TemperatureSample is raw-only for the same reasons as PowerSample
                    // (docs/roadmap.md Phase 6): live charts and the per-band breakdown,
                    // no rollup tier, no session link — a plain age cutoff.
                    deleteTemperature.Transaction = transaction;
                    deleteTemperature.CommandText = "DELETE FROM TemperatureSample WHERE TimestampUtc < $cutoff;";
                    deleteTemperature.Parameters.AddWithValue("$cutoff", rawCutoffMs);
                    temperatureDeleted = await deleteTemperature.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                int processDeleted;
                await using (SqliteCommand deleteProcess = connection.CreateCommand())
                {
                    // ProcessSample feeds the daily ApplicationUsage rollup, so a
                    // raw row is only dropped once its day is summarised for that
                    // application key — and never while it belongs to an open
                    // session (docs/roadmap.md Phase 7; docs/database.md section 6).
                    deleteProcess.Transaction = transaction;
                    deleteProcess.CommandText = """
                        DELETE FROM ProcessSample
                        WHERE TimestampUtc < $cutoff
                          AND (SessionId IS NULL OR SessionId NOT IN (SELECT Id FROM BatterySession WHERE EndUtc IS NULL))
                          AND (
                              ApplicationKey = '__baseline__'
                              OR EXISTS (
                                  SELECT 1 FROM ApplicationUsage au
                                  WHERE au.DayUtc = (ProcessSample.TimestampUtc / 86400000) * 86400000
                                    AND au.ApplicationKey = ProcessSample.ApplicationKey
                              )
                          );
                        """;
                    deleteProcess.Parameters.AddWithValue("$cutoff", rawCutoffMs);
                    processDeleted = await deleteProcess.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (SqliteCommand deleteHour = connection.CreateCommand())
                {
                    // The minute and hour aggregate tiers age out on their own
                    // configured windows (docs/database.md section 6, retention tiers).
                    long minuteCutoff = DateTimeOffset.UtcNow.AddDays(-data.MinuteRetentionDays).ToUnixTimeMilliseconds();
                    long hourCutoff = DateTimeOffset.UtcNow.AddDays(-data.HourRetentionDays).ToUnixTimeMilliseconds();
                    deleteHour.Transaction = transaction;
                    deleteHour.CommandText = """
                        DELETE FROM SampleMinute WHERE MinuteUtc < $minuteCutoff;
                        DELETE FROM SampleHour WHERE HourUtc < $hourCutoff;
                        """;
                    deleteHour.Parameters.AddWithValue("$minuteCutoff", minuteCutoff);
                    deleteHour.Parameters.AddWithValue("$hourCutoff", hourCutoff);
                    await deleteHour.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (data.DailyRetentionDays > 0)
                {
                    await using SqliteCommand deleteDaily = connection.CreateCommand();
                    long dailyCutoff = DateTimeOffset.UtcNow.AddDays(-data.DailyRetentionDays).ToUnixTimeMilliseconds();
                    deleteDaily.Transaction = transaction;
                    deleteDaily.CommandText = "DELETE FROM DailyStatistics WHERE DayUtc < $cutoff;";
                    deleteDaily.Parameters.AddWithValue("$cutoff", dailyCutoff);
                    await deleteDaily.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                int alertsDeleted;
                await using (SqliteCommand deleteAlerts = connection.CreateCommand())
                {
                    // Alert history has a bound too (docs/roadmap.md Phase 9): rows
                    // older than AlertRetentionDays are dropped, acknowledged or not.
                    long alertCutoff = DateTimeOffset.UtcNow.AddDays(-data.AlertRetentionDays).ToUnixTimeMilliseconds();
                    deleteAlerts.Transaction = transaction;
                    deleteAlerts.CommandText = "DELETE FROM Alert WHERE TimestampUtc < $cutoff;";
                    deleteAlerts.Parameters.AddWithValue("$cutoff", alertCutoff);
                    alertsDeleted = await deleteAlerts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (SqliteCommand upsertSettings = connection.CreateCommand())
                {
                    upsertSettings.Transaction = transaction;
                    upsertSettings.CommandText = """
                        INSERT INTO DataRetentionSettings (Id, RawRetentionDays, MinuteRetentionDays, HourRetentionDays, DailyRetentionDays, LastCleanupUtc)
                        VALUES (1, $raw, $minute, $hour, $daily, $now)
                        ON CONFLICT(Id) DO UPDATE SET
                            RawRetentionDays    = excluded.RawRetentionDays,
                            MinuteRetentionDays = excluded.MinuteRetentionDays,
                            HourRetentionDays   = excluded.HourRetentionDays,
                            DailyRetentionDays  = excluded.DailyRetentionDays,
                            LastCleanupUtc      = excluded.LastCleanupUtc;
                        """;
                    upsertSettings.Parameters.AddWithValue("$raw", data.RawRetentionDays);
                    upsertSettings.Parameters.AddWithValue("$minute", data.MinuteRetentionDays);
                    upsertSettings.Parameters.AddWithValue("$hour", data.HourRetentionDays);
                    upsertSettings.Parameters.AddWithValue("$daily", data.DailyRetentionDays);
                    upsertSettings.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
                    await upsertSettings.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                if (deleted > 0 || powerDeleted > 0 || temperatureDeleted > 0 || processDeleted > 0 || alertsDeleted > 0)
                {
                    _logger.LogInformation(
                        "Retention removed {BatteryCount} battery, {PowerCount} power, {TempCount} temperature, {ProcessCount} process and {AlertCount} alert row(s).",
                        deleted, powerDeleted, temperatureDeleted, processDeleted, alertsDeleted);
                }
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }

            _status.ReportSuccess(MonitoringComponent.Database);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _status.ReportFailure(MonitoringComponent.Database, ex.Message);
            _logger.LogWarning(ex, "Retention pass failed; will retry on the next interval.");
        }
        finally
        {
            _retentionGate.Release();
        }
    }

    public void Dispose()
    {
        _rollupTimer?.Dispose();
        _retentionTimer?.Dispose();
        _rollupGate.Dispose();
        _retentionGate.Dispose();
    }
}
