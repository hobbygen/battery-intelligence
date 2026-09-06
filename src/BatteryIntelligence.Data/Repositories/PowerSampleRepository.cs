using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// Inserts <c>PowerSample</c> rows. One prepared statement reused across the
/// whole batch, exactly like <see cref="BatterySampleRepository"/>
/// (docs/monitoring-dataflow.md section 6).
/// </summary>
internal sealed class PowerSampleRepository
{
    public async Task InsertBatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<PowerSampleRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PowerSample
                (TimestampUtc, BatteryId, CurrentMa, VoltageMv, PowerMw, Direction, DataQuality, MeasurementSource)
            VALUES
                ($timestampUtc, $batteryId, $currentMa, $voltageMv, $powerMw, $direction, $dataQuality, $measurementSource);
            """;

        SqliteParameter timestampUtc = command.Parameters.Add("$timestampUtc", SqliteType.Integer);
        SqliteParameter batteryId = command.Parameters.Add("$batteryId", SqliteType.Integer);
        SqliteParameter currentMa = command.Parameters.Add("$currentMa", SqliteType.Integer);
        SqliteParameter voltageMv = command.Parameters.Add("$voltageMv", SqliteType.Integer);
        SqliteParameter powerMw = command.Parameters.Add("$powerMw", SqliteType.Integer);
        SqliteParameter direction = command.Parameters.Add("$direction", SqliteType.Integer);
        SqliteParameter dataQuality = command.Parameters.Add("$dataQuality", SqliteType.Integer);
        SqliteParameter measurementSource = command.Parameters.Add("$measurementSource", SqliteType.Integer);

        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (PowerSampleRow row in rows)
        {
            timestampUtc.Value = row.TimestampUtcMs;
            batteryId.Value = (object?)row.BatteryDeviceId ?? DBNull.Value;
            currentMa.Value = (object?)row.CurrentMa ?? DBNull.Value;
            voltageMv.Value = (object?)row.VoltageMv ?? DBNull.Value;
            powerMw.Value = (object?)row.PowerMw ?? DBNull.Value;
            direction.Value = row.Direction;
            dataQuality.Value = row.DataQuality;
            measurementSource.Value = row.MeasurementSource;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
