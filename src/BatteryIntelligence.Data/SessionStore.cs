using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data.Repositories;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data;

/// <inheritdoc cref="ISessionStore"/>
public sealed class SessionStore : ISessionStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly BatteryDeviceRepository _deviceRepository = new();
    private readonly BatterySessionRepository _sessionRepository = new();
    private readonly SessionEventRepository _sessionEventRepository = new();
    private readonly SystemEventRepository _systemEventRepository = new();

    public SessionStore(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<long> GetOrCreateDeviceIdAsync(BatteryDevice device, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        long id = await _deviceRepository.GetOrCreateAsync(connection, transaction, device, nowUtc, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task<LastBatterySample?> GetLastSampleAsync(long batteryDeviceId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT TimestampUtc, Status, Percentage
            FROM BatterySample
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

        return new LastBatterySample(
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
            (BatteryState)reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2));
    }

    public async Task<BatterySessionInfo?> GetOpenSessionAsync(long batteryDeviceId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await _sessionRepository.GetOpenAsync(connection, batteryDeviceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> InsertOpenSessionAsync(BatterySessionInfo session, long batteryDeviceId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await _sessionRepository.InsertOpenAsync(connection, session, batteryDeviceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateOpenSessionAsync(long sessionId, BatterySessionInfo state, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _sessionRepository.UpdateOpenAsync(connection, sessionId, state, cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseSessionAsync(long sessionId, BatterySessionInfo closed, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _sessionRepository.CloseAsync(connection, sessionId, closed, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordSessionEventAsync(
        long sessionId, SessionEventKind kind, DateTimeOffset timestampUtc, double? percentage, string? detail,
        bool inferred, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _sessionEventRepository.InsertAsync(connection, sessionId, kind, timestampUtc, percentage, detail, inferred, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordSystemEventAsync(
        SystemEventKind kind, DateTimeOffset timestampUtc, bool inferred, string? detail, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _systemEventRepository.InsertAsync(connection, kind, timestampUtc, inferred, detail, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BatterySessionInfo>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await _sessionRepository.GetRecentAsync(connection, count, cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// A first, honest version of the timeline: real session spans (with true
    /// start/end and boundary percentages, per docs/session-engine.md section 7)
    /// plus zero-duration markers for session and system events. Reconstructing
    /// contiguous screen-on/off spans (the fuller example in section 7) is left
    /// for a later pass — the underlying <c>SystemEvent</c> rows this method
    /// already reads are exactly what that would be built from, so no new
    /// instrumentation is needed when it is added.
    /// </remarks>
    public async Task<IReadOnlyList<TimelineSegment>> GetTimelineAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        List<TimelineSegment> segments = [];

        await using (SqliteCommand sessions = connection.CreateCommand())
        {
            sessions.CommandText = """
                SELECT SessionType, StartUtc, EndUtc, StartPercentage, EndPercentage, EndReason
                FROM BatterySession
                WHERE StartUtc <= $to AND (EndUtc IS NULL OR EndUtc >= $from)
                ORDER BY StartUtc;
                """;
            sessions.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeMilliseconds());
            sessions.Parameters.AddWithValue("$to", toUtc.ToUnixTimeMilliseconds());

            await using SqliteDataReader reader = await sessions.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                SessionType type = (SessionType)reader.GetInt32(0);
                DateTimeOffset start = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
                DateTimeOffset? end = reader.IsDBNull(2) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));
                double? startPct = reader.IsDBNull(3) ? null : reader.GetDouble(3);
                double? endPct = reader.IsDBNull(4) ? null : reader.GetDouble(4);
                string? reason = reader.IsDBNull(5) ? null : ((SessionEndReason)reader.GetInt32(5)).ToString();

                segments.Add(new TimelineSegment(start, end, type.ToString(), startPct, endPct, reason, Inferred: false));
            }
        }

        foreach ((long _, DateTimeOffset ts, SessionEventKind kind, double? pct, string? detail, bool inferred)
                 in await _sessionEventRepository.GetRangeAsync(connection, fromUtc, toUtc, cancellationToken).ConfigureAwait(false))
        {
            segments.Add(new TimelineSegment(ts, ts, kind.ToString(), pct, pct, detail, inferred));
        }

        foreach ((DateTimeOffset ts, SystemEventKind kind, string? detail, bool inferred)
                 in await _systemEventRepository.GetRangeAsync(connection, fromUtc, toUtc, cancellationToken).ConfigureAwait(false))
        {
            segments.Add(new TimelineSegment(ts, ts, kind.ToString(), null, null, detail, inferred));
        }

        return [.. segments.OrderBy(s => s.StartUtc)];
    }
}
