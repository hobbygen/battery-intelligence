using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// A real, uniquely-named SQLite file for one test, migrated to the latest
/// schema on construction and deleted (including <c>-wal</c>/<c>-shm</c>
/// siblings) on dispose.
/// </summary>
/// <remarks>
/// Integration tests use a real file rather than an in-memory database
/// deliberately: WAL mode, file locking and <c>busy_timeout</c> are part of what
/// is under test, and an in-memory database exercises none of them
/// (docs/testing.md section 2).
/// </remarks>
public sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"bi-test-{Guid.NewGuid():N}.db");

        ConnectionFactory = new SqliteConnectionFactory(Path);
        Migrator = new DatabaseMigrator(ConnectionFactory, NullLogger<DatabaseMigrator>.Instance);
    }

    public string Path { get; }

    public ISqliteConnectionFactory ConnectionFactory { get; }

    public DatabaseMigrator Migrator { get; }

    public async Task MigrateAsync()
    {
        bool ok = await Migrator.MigrateAsync();
        if (!ok)
        {
            throw new InvalidOperationException("Test migration failed.");
        }
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using SqliteConnection connection = await ConnectionFactory.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        object? result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result ?? 0L, typeof(T));
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using SqliteConnection connection = await ConnectionFactory.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        // SQLite's connection pool can keep a handle briefly after disposal;
        // clearing pools first prevents an intermittent "file in use" failure
        // when the temp file is deleted immediately afterward.
        SqliteConnection.ClearAllPools();

        TryDelete(Path);
        TryDelete(Path + "-wal");
        TryDelete(Path + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp file does not fail the test.
        }
    }
}
