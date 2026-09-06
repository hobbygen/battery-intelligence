using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// Inserts <c>BatterySample</c> rows. One prepared statement, reused across the
/// whole batch (docs/monitoring-dataflow.md section 6): roughly 200 rows per
/// flush become one prepared statement executed 200 times inside one
/// transaction, not 200 independently-parsed statements.
/// </summary>
internal sealed class BatterySampleRepository
{
    public async Task InsertBatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<BatterySampleRow> rows,
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
            INSERT INTO BatterySample
                (TimestampUtc, BatteryId, SessionId, Percentage, Status, RemainingMwh, FullChargeMwh,
                 DesignMwh, VoltageMv, CurrentMa, PowerMw, ScreenState, DataQuality, MeasurementSource)
            VALUES
                ($timestampUtc, $batteryId, $sessionId, $percentage, $status, $remainingMwh, $fullChargeMwh,
                 $designMwh, $voltageMv, $currentMa, $powerMw, $screenState, $dataQuality, $measurementSource);
            """;

        SqliteParameter timestampUtc = command.Parameters.Add("$timestampUtc", SqliteType.Integer);
        SqliteParameter batteryId = command.Parameters.Add("$batteryId", SqliteType.Integer);
        SqliteParameter sessionId = command.Parameters.Add("$sessionId", SqliteType.Integer);
        SqliteParameter percentage = command.Parameters.Add("$percentage", SqliteType.Real);
        SqliteParameter status = command.Parameters.Add("$status", SqliteType.Integer);
        SqliteParameter remainingMwh = command.Parameters.Add("$remainingMwh", SqliteType.Integer);
        SqliteParameter fullChargeMwh = command.Parameters.Add("$fullChargeMwh", SqliteType.Integer);
        SqliteParameter designMwh = command.Parameters.Add("$designMwh", SqliteType.Integer);
        SqliteParameter voltageMv = command.Parameters.Add("$voltageMv", SqliteType.Integer);
        SqliteParameter currentMa = command.Parameters.Add("$currentMa", SqliteType.Integer);
        SqliteParameter powerMw = command.Parameters.Add("$powerMw", SqliteType.Integer);
        SqliteParameter screenState = command.Parameters.Add("$screenState", SqliteType.Integer);
        SqliteParameter dataQuality = command.Parameters.Add("$dataQuality", SqliteType.Integer);
        SqliteParameter measurementSource = command.Parameters.Add("$measurementSource", SqliteType.Integer);

        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (BatterySampleRow row in rows)
        {
            timestampUtc.Value = row.TimestampUtcMs;
            batteryId.Value = row.BatteryDeviceId;
            sessionId.Value = (object?)row.SessionId ?? DBNull.Value;
            percentage.Value = (object?)row.Percentage ?? DBNull.Value;
            status.Value = row.Status;
            remainingMwh.Value = (object?)row.RemainingMwh ?? DBNull.Value;
            fullChargeMwh.Value = (object?)row.FullChargeMwh ?? DBNull.Value;
            designMwh.Value = (object?)row.DesignMwh ?? DBNull.Value;
            voltageMv.Value = (object?)row.VoltageMv ?? DBNull.Value;
            currentMa.Value = (object?)row.CurrentMa ?? DBNull.Value;
            powerMw.Value = (object?)row.PowerMw ?? DBNull.Value;
            screenState.Value = row.ScreenState;
            dataQuality.Value = row.DataQuality;
            measurementSource.Value = row.MeasurementSource;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
