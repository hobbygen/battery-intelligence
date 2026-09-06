namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// Migration round-trip correctness (docs/testing.md section 5; specification
/// section 64): the schema applies cleanly, is recorded, and re-running is a
/// no-op rather than a failure or a duplicate application.
/// </summary>
public sealed class MigrationTests
{
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
        Assert.Equal(1, version);
    }

    [Fact]
    public async Task MigrateAsync_RunTwice_IsIdempotent_NoDuplicateRows()
    {
        using TempDatabase db = new();

        await db.MigrateAsync();
        await db.MigrateAsync();

        long migrationRows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM SchemaMigration;");
        Assert.Equal(1, migrationRows);
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
}
