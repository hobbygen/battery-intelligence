using BatteryIntelligence.Core.Models;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// Reads and writes <c>BatteryHealthSnapshot</c> rows (docs/database.md
/// "Health, events, alerts, insights"). One snapshot per <c>(BatteryId,
/// TimestampUtc)</c> — a repeat at the same rounded timestamp is ignored.
/// </summary>
internal sealed class BatteryHealthSnapshotRepository
{
    public async Task InsertOrIgnoreAsync(
        SqliteConnection connection,
        long batteryDeviceId,
        long timestampUtcMs,
        double? retentionPercent,
        int? fullChargeMwh,
        int? designMwh,
        int? cycleCount,
        double? healthScore,
        int healthCategory,
        string algorithmVersion,
        string? factorsJson,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO BatteryHealthSnapshot
                (TimestampUtc, BatteryId, FullChargeMwh, DesignMwh, RetentionPercent, CycleCount,
                 HealthScore, HealthCategory, AlgorithmVersion, FactorsJson)
            VALUES
                ($ts, $batteryId, $full, $design, $retention, $cycles,
                 $score, $category, $algo, $factors);
            """;
        command.Parameters.AddWithValue("$ts", timestampUtcMs);
        command.Parameters.AddWithValue("$batteryId", batteryDeviceId);
        command.Parameters.AddWithValue("$full", (object?)fullChargeMwh ?? DBNull.Value);
        command.Parameters.AddWithValue("$design", (object?)designMwh ?? DBNull.Value);
        command.Parameters.AddWithValue("$retention", (object?)retentionPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("$cycles", (object?)cycleCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$score", (object?)healthScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$category", healthCategory);
        command.Parameters.AddWithValue("$algo", algorithmVersion);
        command.Parameters.AddWithValue("$factors", (object?)factorsJson ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HealthSnapshotRow>> GetHistoryAsync(
        SqliteConnection connection, long batteryDeviceId, long fromUtcMs, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT TimestampUtc, RetentionPercent, FullChargeMwh, CycleCount, HealthScore, AlgorithmVersion
            FROM BatteryHealthSnapshot
            WHERE BatteryId = $batteryId AND TimestampUtc >= $from
            ORDER BY TimestampUtc;
            """;
        command.Parameters.AddWithValue("$batteryId", batteryDeviceId);
        command.Parameters.AddWithValue("$from", fromUtcMs);

        List<HealthSnapshotRow> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new HealthSnapshotRow(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.GetString(5)));
        }

        return rows;
    }

    public async Task<HealthSnapshotRow?> GetLatestAsync(
        SqliteConnection connection, long batteryDeviceId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT TimestampUtc, RetentionPercent, FullChargeMwh, CycleCount, HealthScore, AlgorithmVersion
            FROM BatteryHealthSnapshot
            WHERE BatteryId = $batteryId
            ORDER BY TimestampUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$batteryId", batteryDeviceId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new HealthSnapshotRow(
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
            reader.IsDBNull(1) ? null : reader.GetDouble(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetDouble(4),
            reader.GetString(5));
    }
}
