using Microsoft.Data.Sqlite;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// Hot-path query plans (R-062): the time-range reads the History and Statistics
/// pages issue must use an index, never a full table scan of a growing table.
/// </summary>
public sealed class QueryPlanTests
{
    [Theory]
    [InlineData(
        "raw battery samples",
        "SELECT TimestampUtc, Percentage FROM BatterySample WHERE TimestampUtc >= 0 AND TimestampUtc < 9 AND Percentage IS NOT NULL ORDER BY TimestampUtc;")]
    [InlineData(
        "minute tier",
        "SELECT MinuteUtc, AvgPercentage FROM SampleMinute WHERE MinuteUtc >= 0 AND MinuteUtc < 9 AND AvgPercentage IS NOT NULL ORDER BY MinuteUtc;")]
    [InlineData(
        "hour tier",
        "SELECT HourUtc, AvgVoltageMv FROM SampleHour WHERE HourUtc >= 0 AND HourUtc < 9 AND AvgVoltageMv IS NOT NULL ORDER BY HourUtc;")]
    [InlineData(
        "session list",
        "SELECT Id, StartUtc FROM BatterySession WHERE EndUtc IS NULL OR EndUtc >= 0 ORDER BY StartUtc DESC;")]
    public async Task HotPathQuery_UsesAnIndex_NeverAFullTableScan(string label, string sql)
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        IReadOnlyList<string> steps = await ExplainAsync(db, sql);

        // A full table scan ("SCAN <table>") with no index is the failure. A
        // "SCAN … USING [COVERING] INDEX" (an index-only scan) is fine.
        foreach (string step in steps)
        {
            bool isScan = step.Contains("SCAN", StringComparison.OrdinalIgnoreCase);
            bool usesIndex = step.Contains("USING INDEX", StringComparison.OrdinalIgnoreCase)
                || step.Contains("USING COVERING INDEX", StringComparison.OrdinalIgnoreCase)
                || step.Contains("USING INTEGER PRIMARY KEY", StringComparison.OrdinalIgnoreCase);

            Assert.False(isScan && !usesIndex, $"[{label}] full table scan in plan step: {step}");
        }

        Assert.Contains(steps, s => s.Contains("INDEX", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<string>> ExplainAsync(TempDatabase db, string sql)
    {
        await using SqliteConnection connection = await db.ConnectionFactory.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;

        var lines = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return lines;
    }
}
