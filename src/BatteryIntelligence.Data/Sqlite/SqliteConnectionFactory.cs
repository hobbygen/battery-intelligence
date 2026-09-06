using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Data.Sqlite;

/// <summary>
/// Opens connections to the application database with the pragmas
/// docs/database.md section 5 requires, applied consistently everywhere a
/// connection is created.
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>Full path to the database file this factory opens.</summary>
    string DatabasePath { get; }

    /// <summary>Opens a new, pragma-configured, open connection. The caller owns disposal.</summary>
    Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISqliteConnectionFactory"/>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
        };

        SqliteConnection connection = new(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // docs/database.md section 5. WAL enables concurrent readers during a
        // write; NORMAL synchronous is the standard trade-off under WAL (risks
        // only the last transaction on an OS crash, in exchange for far fewer
        // fsyncs — appropriate for a process that writes continuously for
        // months, per docs/architecture.md driver 3).
        const string pragmaSql = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous  = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA temp_store   = MEMORY;
            PRAGMA cache_size   = -8000;
            """;

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = pragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
