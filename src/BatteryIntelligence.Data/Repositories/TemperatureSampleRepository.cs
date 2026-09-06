using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// Inserts <c>TemperatureSample</c> rows. One prepared statement reused across the
/// whole batch, exactly like <see cref="PowerSampleRepository"/>
/// (docs/monitoring-dataflow.md section 6).
/// </summary>
internal sealed class TemperatureSampleRepository
{
    public async Task InsertBatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<TemperatureSampleRow> rows,
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
            INSERT INTO TemperatureSample
                (TimestampUtc, BatteryId, TemperatureDk, ChargeState, DataQuality, MeasurementSource)
            VALUES
                ($timestampUtc, $batteryId, $temperatureDk, $chargeState, $dataQuality, $measurementSource);
            """;

        SqliteParameter timestampUtc = command.Parameters.Add("$timestampUtc", SqliteType.Integer);
        SqliteParameter batteryId = command.Parameters.Add("$batteryId", SqliteType.Integer);
        SqliteParameter temperatureDk = command.Parameters.Add("$temperatureDk", SqliteType.Integer);
        SqliteParameter chargeState = command.Parameters.Add("$chargeState", SqliteType.Integer);
        SqliteParameter dataQuality = command.Parameters.Add("$dataQuality", SqliteType.Integer);
        SqliteParameter measurementSource = command.Parameters.Add("$measurementSource", SqliteType.Integer);

        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (TemperatureSampleRow row in rows)
        {
            timestampUtc.Value = row.TimestampUtcMs;
            batteryId.Value = row.BatteryDeviceId;
            temperatureDk.Value = row.TemperatureDk;
            chargeState.Value = (object?)row.ChargeState ?? DBNull.Value;
            dataQuality.Value = row.DataQuality;
            measurementSource.Value = row.MeasurementSource;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
