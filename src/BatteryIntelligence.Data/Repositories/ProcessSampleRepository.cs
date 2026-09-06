using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>
/// Inserts <c>ProcessSample</c> rows. One prepared statement reused across the
/// whole batch, exactly like <see cref="PowerSampleRepository"/>
/// (docs/monitoring-dataflow.md section 6).
/// </summary>
internal sealed class ProcessSampleRepository
{
    public async Task InsertBatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ProcessSampleRow> rows,
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
            INSERT INTO ProcessSample
                (TimestampUtc, SessionId, ProcessId, ProcessName, ApplicationKey, CpuPercent,
                 MemoryBytes, IsForeground, EstimatedPowerMw, EstimatedSharePercent, EstimatorVersion,
                 DataQuality, MeasurementSource)
            VALUES
                ($timestampUtc, $sessionId, $processId, $processName, $applicationKey, $cpuPercent,
                 $memoryBytes, $isForeground, $estimatedPowerMw, $estimatedSharePercent, $estimatorVersion,
                 $dataQuality, $measurementSource);
            """;

        SqliteParameter timestampUtc = command.Parameters.Add("$timestampUtc", SqliteType.Integer);
        SqliteParameter sessionId = command.Parameters.Add("$sessionId", SqliteType.Integer);
        SqliteParameter processId = command.Parameters.Add("$processId", SqliteType.Integer);
        SqliteParameter processName = command.Parameters.Add("$processName", SqliteType.Text);
        SqliteParameter applicationKey = command.Parameters.Add("$applicationKey", SqliteType.Text);
        SqliteParameter cpuPercent = command.Parameters.Add("$cpuPercent", SqliteType.Real);
        SqliteParameter memoryBytes = command.Parameters.Add("$memoryBytes", SqliteType.Integer);
        SqliteParameter isForeground = command.Parameters.Add("$isForeground", SqliteType.Integer);
        SqliteParameter estimatedPowerMw = command.Parameters.Add("$estimatedPowerMw", SqliteType.Integer);
        SqliteParameter estimatedSharePercent = command.Parameters.Add("$estimatedSharePercent", SqliteType.Real);
        SqliteParameter estimatorVersion = command.Parameters.Add("$estimatorVersion", SqliteType.Text);
        SqliteParameter dataQuality = command.Parameters.Add("$dataQuality", SqliteType.Integer);
        SqliteParameter measurementSource = command.Parameters.Add("$measurementSource", SqliteType.Integer);

        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (ProcessSampleRow row in rows)
        {
            timestampUtc.Value = row.TimestampUtcMs;
            sessionId.Value = (object?)row.SessionId ?? DBNull.Value;
            processId.Value = row.ProcessId;
            processName.Value = row.ProcessName;
            applicationKey.Value = row.ApplicationKey;
            cpuPercent.Value = (object?)row.CpuPercent ?? DBNull.Value;
            memoryBytes.Value = (object?)row.MemoryBytes ?? DBNull.Value;
            isForeground.Value = row.IsForeground;
            estimatedPowerMw.Value = (object?)row.EstimatedPowerMw ?? DBNull.Value;
            estimatedSharePercent.Value = (object?)row.EstimatedSharePercent ?? DBNull.Value;
            estimatorVersion.Value = row.EstimatorVersion;
            dataQuality.Value = row.DataQuality;
            measurementSource.Value = row.MeasurementSource;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
