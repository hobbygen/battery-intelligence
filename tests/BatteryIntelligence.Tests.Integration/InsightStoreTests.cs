using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// The <c>Insight</c> store: the active set is replaced wholesale each pass, and
/// dismissed rows survive so a suppressed insight does not reappear
/// (specification section 18).
/// </summary>
public sealed class InsightStoreTests
{
    [Fact]
    public async Task ReplaceCurrent_ThenGetActive_ReturnsTheNewSet()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        InsightStore store = new(db.ConnectionFactory);
        await store.ReplaceCurrentAsync([Insight(InsightType.FastDrain, "Draining faster"), Insight(InsightType.ChargingSlow, "Charging slower")], DateTimeOffset.UtcNow);

        IReadOnlyList<AnalyticsInsight> active = await store.GetActiveAsync();
        Assert.Equal(2, active.Count);
        Assert.Contains(active, i => i.Type == InsightType.FastDrain);
        Assert.All(active, i => Assert.Equal("InsightRulesV1", i.RuleVersion));
    }

    [Fact]
    public async Task ReplaceCurrent_ReplacesTheOldActiveSet_ButKeepsDismissedRows()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        InsightStore store = new(db.ConnectionFactory);
        await store.ReplaceCurrentAsync([Insight(InsightType.FastDrain, "One")], DateTimeOffset.UtcNow);

        long id = await db.ScalarAsync<long>("SELECT Id FROM Insight LIMIT 1;");
        await store.DismissAsync(id);

        await store.ReplaceCurrentAsync([Insight(InsightType.HealthDegrading, "Two")], DateTimeOffset.UtcNow);

        IReadOnlyList<AnalyticsInsight> active = await store.GetActiveAsync();
        Assert.Single(active);
        Assert.Equal(InsightType.HealthDegrading, active[0].Type);

        long total = await db.ScalarAsync<long>("SELECT COUNT(*) FROM Insight;");
        Assert.Equal(2, total); // the dismissed one is still there
    }

    [Fact]
    public async Task ReplaceCurrent_WithNothing_ClearsTheActiveSet()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        InsightStore store = new(db.ConnectionFactory);
        await store.ReplaceCurrentAsync([Insight(InsightType.FastDrain, "One")], DateTimeOffset.UtcNow);
        await store.ReplaceCurrentAsync([], DateTimeOffset.UtcNow);

        Assert.Empty(await store.GetActiveAsync());
    }

    private static AnalyticsInsight Insight(InsightType type, string title) => new(
        type, InsightSeverity.Advice, title, "Explanation.",
        new Dictionary<string, double> { ["x"] = 1.0 }, 0.8,
        DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, "InsightRulesV1");
}
