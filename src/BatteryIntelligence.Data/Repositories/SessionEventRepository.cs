using BatteryIntelligence.Core.Enums;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>Inserts and reads <c>SessionEvent</c> rows — things that happened within an open session (docs/session-engine.md section 3).</summary>
internal sealed class SessionEventRepository
{
    public async Task InsertAsync(
        SqliteConnection connection, long sessionId, SessionEventKind kind, DateTimeOffset timestampUtc,
        double? percentage, string? detail, bool inferred, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SessionEvent (SessionId, TimestampUtc, EventType, Percentage, Detail, Inferred)
            VALUES ($sessionId, $timestampUtc, $eventType, $percentage, $detail, $inferred);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$timestampUtc", timestampUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$eventType", (int)kind);
        command.Parameters.AddWithValue("$percentage", (object?)percentage ?? DBNull.Value);
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        command.Parameters.AddWithValue("$inferred", inferred ? 1 : 0);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(long SessionId, DateTimeOffset TimestampUtc, SessionEventKind Kind, double? Percentage, string? Detail, bool Inferred)>> GetRangeAsync(
        SqliteConnection connection, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT SessionId, TimestampUtc, EventType, Percentage, Detail, Inferred
            FROM SessionEvent
            WHERE TimestampUtc BETWEEN $from AND $to
            ORDER BY TimestampUtc;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", toUtc.ToUnixTimeMilliseconds());

        List<(long, DateTimeOffset, SessionEventKind, double?, string?, bool)> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((
                reader.GetInt64(0),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
                (SessionEventKind)reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) != 0));
        }

        return results;
    }
}
