using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Repositories;

/// <summary>Reads and writes <c>BatterySession</c> rows.</summary>
internal sealed class BatterySessionRepository
{
    public async Task<BatterySessionInfo?> GetOpenAsync(SqliteConnection connection, long batteryDeviceId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.StartUtc, s.SessionType, s.StartPercentage, s.StartCapacityMwh,
                   s.ScreenOnSeconds, s.ScreenOffSeconds, s.SleepSeconds, s.Interruptions, d.HardwareId
            FROM BatterySession s
            JOIN BatteryDevice d ON d.Id = s.BatteryId
            WHERE s.BatteryId = $batteryId AND s.EndUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$batteryId", batteryDeviceId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new BatterySessionInfo
        {
            Id = reader.GetInt64(0),
            BatteryId = reader.GetString(9),
            StartUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
            Type = (SessionType)reader.GetInt32(2),
            StartPercentage = reader.IsDBNull(3) ? null : reader.GetDouble(3),
            StartCapacityMwh = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            ScreenOnSeconds = reader.GetInt64(5),
            ScreenOffSeconds = reader.GetInt64(6),
            SleepSeconds = reader.GetInt64(7),
            Interruptions = reader.GetInt32(8),
        };
    }

    public async Task<long> InsertOpenAsync(
        SqliteConnection connection, BatterySessionInfo session, long batteryDeviceId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO BatterySession
                (BatteryId, SessionType, StartUtc, StartPercentage, StartCapacityMwh, ClosedCleanly)
            VALUES
                ($batteryId, $type, $startUtc, $startPercentage, $startCapacityMwh, 0)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$batteryId", batteryDeviceId);
        command.Parameters.AddWithValue("$type", (int)session.Type);
        command.Parameters.AddWithValue("$startUtc", session.StartUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$startPercentage", (object?)session.StartPercentage ?? DBNull.Value);
        command.Parameters.AddWithValue("$startCapacityMwh", (object?)session.StartCapacityMwh ?? DBNull.Value);

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return (long)result!;
    }

    public async Task UpdateOpenAsync(SqliteConnection connection, long sessionId, BatterySessionInfo state, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE BatterySession
            SET ScreenOnSeconds = $screenOn, ScreenOffSeconds = $screenOff, SleepSeconds = $sleep, Interruptions = $interruptions
            WHERE Id = $id AND EndUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$screenOn", state.ScreenOnSeconds);
        command.Parameters.AddWithValue("$screenOff", state.ScreenOffSeconds);
        command.Parameters.AddWithValue("$sleep", state.SleepSeconds);
        command.Parameters.AddWithValue("$interruptions", state.Interruptions);
        command.Parameters.AddWithValue("$id", sessionId);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseAsync(SqliteConnection connection, long sessionId, BatterySessionInfo closed, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE BatterySession
            SET EndUtc = $endUtc, EndPercentage = $endPercentage, EndCapacityMwh = $endCapacityMwh,
                ScreenOnSeconds = $screenOn, ScreenOffSeconds = $screenOff, SleepSeconds = $sleep,
                Interruptions = $interruptions, ClosedCleanly = $closedCleanly, EndReason = $endReason
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$endUtc", closed.EndUtc!.Value.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$endPercentage", (object?)closed.EndPercentage ?? DBNull.Value);
        command.Parameters.AddWithValue("$endCapacityMwh", (object?)closed.EndCapacityMwh ?? DBNull.Value);
        command.Parameters.AddWithValue("$screenOn", closed.ScreenOnSeconds);
        command.Parameters.AddWithValue("$screenOff", closed.ScreenOffSeconds);
        command.Parameters.AddWithValue("$sleep", closed.SleepSeconds);
        command.Parameters.AddWithValue("$interruptions", closed.Interruptions);
        command.Parameters.AddWithValue("$closedCleanly", closed.ClosedCleanly ? 1 : 0);
        command.Parameters.AddWithValue("$endReason", (object?)(int?)closed.EndReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", sessionId);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BatterySessionInfo>> GetRecentAsync(SqliteConnection connection, int count, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.BatteryId, s.SessionType, s.StartUtc, s.EndUtc, s.StartPercentage, s.EndPercentage,
                   s.StartCapacityMwh, s.EndCapacityMwh, s.ScreenOnSeconds, s.ScreenOffSeconds, s.SleepSeconds,
                   s.Interruptions, s.ClosedCleanly, s.EndReason, d.HardwareId
            FROM BatterySession s
            JOIN BatteryDevice d ON d.Id = s.BatteryId
            ORDER BY s.StartUtc DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

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
}
