using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data.Repositories;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data;

/// <inheritdoc cref="IAlertStore"/>
public sealed class AlertStore : IAlertStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly AlertRepository _repository = new();

    public AlertStore(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task<long> InsertAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await _repository.InsertAsync(connection, alert, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Alert>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await _repository.GetRecentAsync(connection, count, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetUnacknowledgedCountAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await _repository.GetUnacknowledgedCountAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task AcknowledgeAsync(long alertId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _repository.AcknowledgeAsync(connection, alertId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AcknowledgeAllAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _repository.AcknowledgeAllAsync(connection, cancellationToken).ConfigureAwait(false);
    }
}
