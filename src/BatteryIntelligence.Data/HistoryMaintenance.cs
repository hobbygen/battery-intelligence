using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data;

/// <inheritdoc cref="IHistoryMaintenance"/>
public sealed class HistoryMaintenance : IHistoryMaintenance
{
    // Every telemetry, session, health, insight and alert table. NOT listed and
    // therefore kept: BatteryDevice, AppSettings, DataRetentionSettings,
    // SchemaMigration (identity and configuration, not history).
    private static readonly string[] Tables =
    [
        "BatterySample",
        "PowerSample",
        "TemperatureSample",
        "ProcessSample",
        "SampleMinute",
        "SampleHour",
        "DailyStatistics",
        "ApplicationUsage",
        "SessionEvent",
        "BatterySession",
        "SystemEvent",
        "BatteryHealthSnapshot",
        "Insight",
        "Alert",
    ];

    private readonly ISqliteConnectionFactory _connectionFactory;

    public HistoryMaintenance(ISqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                foreach (string table in Tables)
                {
                    await using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = $"DELETE FROM {table};";
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        // Reclaim the freed pages. VACUUM cannot run inside a transaction; this is
        // the one place the app is allowed to VACUUM (docs/database.md section 6).
        await using SqliteCommand vacuum = connection.CreateCommand();
        vacuum.CommandText = "VACUUM;";
        await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
