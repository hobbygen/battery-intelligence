using System.Text.Json;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data.Repositories;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data;

/// <inheritdoc cref="IHealthSnapshotStore"/>
public sealed class HealthSnapshotStore : IHealthSnapshotStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly BatteryHealthSnapshotRepository _repository = new();

    public HealthSnapshotStore(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task AppendAsync(
        HealthScore score, string batteryHardwareId, double? retentionPercent, int? fullChargeMwh, int? cycleCount,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);

        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        long? deviceId = await ResolveDeviceIdAsync(connection, batteryHardwareId, cancellationToken).ConfigureAwait(false);
        if (deviceId is null)
        {
            return; // no device row yet — a sample write will create it shortly
        }

        // Round to the minute so a burst of recomputes does not fill the table.
        long tsMs = nowUtc.ToUnixTimeMilliseconds() / 60_000 * 60_000;
        string factorsJson = JsonSerializer.Serialize(score.Factors, Json);

        await _repository.InsertOrIgnoreAsync(
            connection, deviceId.Value, tsMs, retentionPercent, fullChargeMwh, designMwh: null, cycleCount,
            score.Score, (int)score.Category, score.AlgorithmVersion, factorsJson, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HealthSnapshotRow>> GetHistoryAsync(
        string batteryHardwareId, DateTimeOffset fromUtc, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        long? deviceId = await ResolveDeviceIdAsync(connection, batteryHardwareId, cancellationToken).ConfigureAwait(false);
        return deviceId is null
            ? []
            : await _repository.GetHistoryAsync(connection, deviceId.Value, fromUtc.ToUnixTimeMilliseconds(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<HealthSnapshotRow?> GetLatestAsync(string batteryHardwareId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        long? deviceId = await ResolveDeviceIdAsync(connection, batteryHardwareId, cancellationToken).ConfigureAwait(false);
        return deviceId is null ? null : await _repository.GetLatestAsync(connection, deviceId.Value, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long?> ResolveDeviceIdAsync(SqliteConnection connection, string hardwareId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM BatteryDevice WHERE HardwareId = $hw;";
        command.Parameters.AddWithValue("$hw", hardwareId);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long id ? id : null;
    }
}

/// <inheritdoc cref="IInsightStore"/>
public sealed class InsightStore : IInsightStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly InsightRepository _repository = new();

    public InsightStore(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task ReplaceCurrentAsync(
        IReadOnlyList<AnalyticsInsight> insights, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(insights);

        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _repository.ReplaceActiveAsync(connection, transaction, insights, nowUtc.ToUnixTimeMilliseconds(), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<AnalyticsInsight>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await _repository.GetActiveAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task DismissAsync(long insightId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _repository.DismissAsync(connection, insightId, cancellationToken).ConfigureAwait(false);
    }
}

/// <inheritdoc cref="IAnalyticsReadStore"/>
public sealed class AnalyticsReadStore : IAnalyticsReadStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public AnalyticsReadStore(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<BatterySessionInfo>> GetSessionsAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.BatteryId, s.SessionType, s.StartUtc, s.EndUtc, s.StartPercentage, s.EndPercentage,
                   s.StartCapacityMwh, s.EndCapacityMwh, s.ScreenOnSeconds, s.ScreenOffSeconds, s.SleepSeconds,
                   s.Interruptions, s.ClosedCleanly, s.EndReason, d.HardwareId
            FROM BatterySession s
            JOIN BatteryDevice d ON d.Id = s.BatteryId
            WHERE s.EndUtc IS NULL OR s.EndUtc >= $from
            ORDER BY s.StartUtc DESC;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());

        List<BatterySessionInfo> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new BatterySessionInfo
            {
                Id = reader.GetInt64(0),
                BatteryId = reader.GetString(15),
                Type = (SessionType)reader.GetInt32(2),
                StartUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                EndUtc = reader.IsDBNull(4) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
                StartPercentage = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                EndPercentage = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                StartCapacityMwh = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                EndCapacityMwh = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                ScreenOnSeconds = reader.GetInt64(9),
                ScreenOffSeconds = reader.GetInt64(10),
                SleepSeconds = reader.GetInt64(11),
                Interruptions = reader.GetInt32(12),
                ClosedCleanly = reader.GetInt32(13) != 0,
                EndReason = reader.IsDBNull(14) ? null : (SessionEndReason)reader.GetInt32(14),
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<DailyStatRow>> GetDailyStatisticsAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT DayUtc, ChargeSessions, DischargeSessions, ChargingSeconds, DischargingSeconds,
                   ScreenOnSeconds, ScreenOffSeconds, SleepSeconds, PercentCharged, PercentDischarged,
                   AvgChargeRateMw, AvgDischargeRateMw
            FROM DailyStatistics
            WHERE DayUtc >= $from
            ORDER BY DayUtc;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());

        List<DailyStatRow> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new DailyStatRow(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                reader.IsDBNull(9) ? 0 : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11)));
        }

        return rows;
    }

    public async Task<double?> GetTemperatureExposureSecondsAsync(
        DateTimeOffset fromUtc, int warnDeciKelvin, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand any = connection.CreateCommand())
        {
            any.CommandText = "SELECT COUNT(*) FROM TemperatureSample WHERE TimestampUtc >= $from;";
            any.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());
            long count = (long)(await any.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
            if (count == 0)
            {
                return null; // no sensor / no data
            }
        }

        // Approximate: each above-threshold sample represents the median inter-sample
        // gap. With ~10 s temperature sampling, 10 s per hot sample is a fair estimate.
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TemperatureSample WHERE TimestampUtc >= $from AND TemperatureDk >= $warn;";
        command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$warn", warnDeciKelvin);
        long hot = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        return hot * 10.0;
    }
}
