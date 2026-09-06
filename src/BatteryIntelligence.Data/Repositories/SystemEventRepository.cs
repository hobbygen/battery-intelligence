using BatteryIntelligence.Core.Enums;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>Inserts and reads system-wide <c>SystemEvent</c> rows (docs/session-engine.md sections 4-5).</summary>
internal sealed class SystemEventRepository
{
    public async Task InsertAsync(
        SqliteConnection connection, SystemEventKind kind, DateTimeOffset timestampUtc, bool inferred, string? detail,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SystemEvent (TimestampUtc, EventType, Detail, Inferred)
            VALUES ($timestampUtc, $eventType, $detail, $inferred);
            """;
        command.Parameters.AddWithValue("$timestampUtc", timestampUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$eventType", (int)kind);
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        command.Parameters.AddWithValue("$inferred", inferred ? 1 : 0);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(DateTimeOffset TimestampUtc, SystemEventKind Kind, string? Detail, bool Inferred)>> GetRangeAsync(
        SqliteConnection connection, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT TimestampUtc, EventType, Detail, Inferred
            FROM SystemEvent
            WHERE TimestampUtc BETWEEN $from AND $to
            ORDER BY TimestampUtc;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", toUtc.ToUnixTimeMilliseconds());

        List<(DateTimeOffset, SystemEventKind, string?, bool)> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                (SystemEventKind)reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3) != 0));
        }

        return results;
    }
}
