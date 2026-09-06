using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>Reads and writes <c>Alert</c> rows (docs/database.md; specification section 20).</summary>
internal sealed class AlertRepository
{
    public async Task<long> InsertAsync(SqliteConnection connection, Alert alert, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Alert (TimestampUtc, AlertType, Severity, Title, Message, TriggerValue, ThresholdValue, Acknowledged)
            VALUES ($ts, $type, $severity, $title, $message, $trigger, $threshold, 0)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$ts", alert.TimestampUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$type", (int)alert.Type);
        command.Parameters.AddWithValue("$severity", (int)alert.Severity);
        command.Parameters.AddWithValue("$title", alert.Title);
        command.Parameters.AddWithValue("$message", alert.Message);
        command.Parameters.AddWithValue("$trigger", (object?)alert.TriggerValue ?? DBNull.Value);
        command.Parameters.AddWithValue("$threshold", (object?)alert.ThresholdValue ?? DBNull.Value);

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return (long)result!;
    }

    public async Task<IReadOnlyList<Alert>> GetRecentAsync(SqliteConnection connection, int count, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TimestampUtc, AlertType, Severity, Title, Message, TriggerValue, ThresholdValue, Acknowledged
            FROM Alert
            ORDER BY TimestampUtc DESC, Id DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        List<Alert> alerts = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            alerts.Add(new Alert(
                (AlertType)reader.GetInt32(2),
                (AlertSeverity)reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)))
            {
                Id = reader.GetInt64(0),
                Acknowledged = reader.GetInt32(8) != 0,
            });
        }

        return alerts;
    }

    public async Task<int> GetUnacknowledgedCountAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Alert WHERE Acknowledged = 0;";
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    }

    public async Task AcknowledgeAsync(SqliteConnection connection, long alertId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Alert SET Acknowledged = 1 WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", alertId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AcknowledgeAllAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Alert SET Acknowledged = 1 WHERE Acknowledged = 0;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
