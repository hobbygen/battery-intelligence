using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// The <c>BatteryHealthSnapshot</c> store: append with per-minute dedup, and the
/// history read the degradation trend consumes (docs/database.md).
/// </summary>
public sealed class HealthSnapshotStoreTests
{
    [Fact]
    public async Task Append_ThenGetHistory_RoundTripsRetentionAndScore()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        await CreateDeviceAsync(db);

        HealthSnapshotStore store = new(db.ConnectionFactory);
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(82.0, null, null, "LiP", null, null, null), DateTimeOffset.UtcNow);

        await store.AppendAsync(score, "battery0", 82.0, 38_000, null, DateTimeOffset.UtcNow, CancellationToken.None);

        IReadOnlyList<HealthSnapshotRow> history = await store.GetHistoryAsync("battery0", DateTimeOffset.UtcNow.AddDays(-1));
        HealthSnapshotRow row = Assert.Single(history);
        Assert.Equal(82.0, row.RetentionPercent);
        Assert.Equal(score.Score, row.HealthScore);
        Assert.Equal("HealthScoreV1", row.AlgorithmVersion);
    }

    [Fact]
    public async Task Append_TwiceInTheSameMinute_KeepsOnlyOne()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        await CreateDeviceAsync(db);

        HealthSnapshotStore store = new(db.ConnectionFactory);
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(80.0, null, null, "LiP", null, null, null), DateTimeOffset.UtcNow);

        DateTimeOffset t = new(2026, 9, 6, 12, 0, 30, TimeSpan.Zero);
        await store.AppendAsync(score, "battery0", 80.0, 38_000, null, t, CancellationToken.None);
        await store.AppendAsync(score, "battery0", 79.0, 37_900, null, t.AddSeconds(20), CancellationToken.None);

        long count = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatteryHealthSnapshot;");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Append_BeforeAnyDeviceRow_IsANoOp()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        HealthSnapshotStore store = new(db.ConnectionFactory);
        HealthScore score = HealthScoreCalculator.Compute(
            new HealthScoreInputs(80.0, null, null, "LiP", null, null, null), DateTimeOffset.UtcNow);

        await store.AppendAsync(score, "unknown", 80.0, 38_000, null, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatteryHealthSnapshot;"));
    }

    private static async Task CreateDeviceAsync(TempDatabase db)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.ExecuteAsync(
            "INSERT INTO BatteryDevice (HardwareId, FirstSeenUtc, LastSeenUtc, IsPresent) VALUES ('battery0', $now, $now, 1);",
            ("$now", now));
    }
}
