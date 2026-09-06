using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// Migration round-trip correctness (docs/testing.md section 5; specification
/// section 64): the schema applies cleanly, is recorded, re-running is a no-op,
/// and a later migration backs up first and loses no rows.
/// </summary>
public sealed class MigrationTests
{
    /// <summary>The highest embedded migration version. Bump when a new V00N ships.</summary>
    private const int LatestVersion = 2;

    private static readonly string[] ExpectedTables =
    [
        "SchemaMigration", "AppSettings", "DataRetentionSettings", "BatteryDevice",
        "BatterySample", "PowerSample", "TemperatureSample", "ProcessSample",
        "ApplicationUsage", "BatterySession", "SessionEvent", "BatteryHealthSnapshot",
        "SystemEvent", "Alert", "Insight", "SampleMinute", "SampleHour", "DailyStatistics",
    ];

    [Fact]
    public async Task MigrateAsync_OnFreshDatabase_CreatesEveryTable()
    {
        using TempDatabase db = new();

        await db.MigrateAsync();

        foreach (string table in ExpectedTables)
        {
            long count = await db.ScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;",
                ("$name", table));

            Assert.True(count == 1, $"Expected table {table} to exist.");
        }
    }

    [Fact]
    public async Task MigrateAsync_RecordsSchemaVersion()
    {
        using TempDatabase db = new();

        await db.MigrateAsync();

        long version = await db.ScalarAsync<long>("SELECT MAX(Version) FROM SchemaMigration;");
        Assert.Equal(LatestVersion, version);
    }

    [Fact]
    public async Task MigrateAsync_RunTwice_IsIdempotent_NoDuplicateRows()
    {
        using TempDatabase db = new();

        await db.MigrateAsync();
        await db.MigrateAsync();

        long migrationRows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM SchemaMigration;");
        Assert.Equal(LatestVersion, migrationRows);
    }

    [Fact]
    public async Task MigrateAsync_AppliesTheDocumentedPragmas()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        string journalMode = await db.ScalarAsync<string>("PRAGMA journal_mode;");
        long foreignKeys = await db.ScalarAsync<long>("PRAGMA foreign_keys;");

        Assert.Equal("wal", journalMode, ignoreCase: true);
        Assert.Equal(1, foreignKeys);
    }

    [Fact]
    public async Task MigrateAsync_AddsTheV002TimeIndexes()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        foreach (string index in new[] { "IX_SampleMinute_Time", "IX_SampleHour_Time" })
        {
            long count = await db.ScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $name;", ("$name", index));
            Assert.True(count == 1, $"Expected index {index} to exist after V002.");
        }
    }

    [Fact]
    public async Task LaterMigration_BacksUpFirst_AndLosesNoRows()
    {
        // Build a database stuck at V001 (only the V001 script), populate it, then
        // migrate to the latest — R-066: a backup file is written and every row survives.
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bi-mig-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);

            await ApplyV001OnlyAsync(factory);
            long deviceId = await SeedRepresentativeRowsAsync(factory);

            var migrator = new DatabaseMigrator(factory, NullLogger<DatabaseMigrator>.Instance);
            Assert.True(await migrator.MigrateAsync());

            await using SqliteConnection connection = await factory.OpenAsync();
            Assert.Equal(LatestVersion, await ScalarAsync<long>(connection, "SELECT MAX(Version) FROM SchemaMigration;"));
            Assert.Equal(3L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM BatterySample;"));
            Assert.Equal(2L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM SampleMinute;"));
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM BatterySession;"));
            Assert.Equal(deviceId, await ScalarAsync<long>(connection, "SELECT Id FROM BatteryDevice LIMIT 1;"));

            Assert.True(System.IO.File.Exists($"{path}.bak-v002"), "Expected a pre-migration backup for V002.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string p in new[] { path, $"{path}-wal", $"{path}-shm", $"{path}.bak-v002" })
            {
                try { if (System.IO.File.Exists(p)) System.IO.File.Delete(p); } catch (System.IO.IOException) { }
            }
        }
    }

    private static async Task ApplyV001OnlyAsync(SqliteConnectionFactory factory)
    {
        // V001 is the first embedded resource; run it directly, then stamp SchemaMigration.
        System.Reflection.Assembly asm = typeof(DatabaseMigrator).Assembly;
        string resource = asm.GetManifestResourceNames().Single(n => n.Contains("V001"));
        await using Stream stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        string sql = await reader.ReadToEndAsync();

        await using SqliteConnection connection = await factory.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql + "\nINSERT INTO SchemaMigration (Version, Name, AppliedUtc) VALUES (1, 'InitialSchema', 0);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> SeedRepresentativeRowsAsync(SqliteConnectionFactory factory)
    {
        await using SqliteConnection connection = await factory.OpenAsync();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await Exec(connection,
            "INSERT INTO BatteryDevice (HardwareId, DesignCapacityMwh, FirstSeenUtc, LastSeenUtc, IsPresent) VALUES ('battery0', 95008, $n, $n, 1);",
            ("$n", now));
        long deviceId = await ScalarAsync<long>(connection, "SELECT Id FROM BatteryDevice WHERE HardwareId = 'battery0';");

        for (int i = 0; i < 3; i++)
        {
            await Exec(connection,
                "INSERT INTO BatterySample (TimestampUtc, BatteryId, Percentage, Status, DataQuality, MeasurementSource) VALUES ($t, $d, 80, 2, 1, 1);",
                ("$t", now - i * 60_000), ("$d", deviceId));
        }

        for (int i = 0; i < 2; i++)
        {
            await Exec(connection,
                "INSERT INTO SampleMinute (BatteryId, MinuteUtc, AvgPercentage, SampleCount) VALUES ($d, $m, 79, 12);",
                ("$d", deviceId), ("$m", now - i * 60_000));
        }

        await Exec(connection,
            "INSERT INTO BatterySession (BatteryId, SessionType, StartUtc) VALUES ($d, 2, $t);",
            ("$d", deviceId), ("$t", now));

        return deviceId;
    }

    private static async Task Exec(SqliteConnection connection, string sql, params (string, object)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }
}
