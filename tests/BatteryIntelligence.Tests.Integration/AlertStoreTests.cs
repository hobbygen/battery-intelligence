using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// The <c>Alert</c> store: insert, newest-first history, the unacknowledged count
/// for the bell badge, and acknowledge one / all (specification section 20).
/// </summary>
public sealed class AlertStoreTests
{
    [Fact]
    public async Task Insert_ThenGetRecent_ReturnsNewestFirst()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        AlertStore store = new(db.ConnectionFactory);
        await store.InsertAsync(Alert(AlertType.LowBattery, "Low", DateTimeOffset.UtcNow.AddMinutes(-10)));
        await store.InsertAsync(Alert(AlertType.HighTemperature, "Hot", DateTimeOffset.UtcNow.AddMinutes(-2)));

        IReadOnlyList<Alert> recent = await store.GetRecentAsync(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal(AlertType.HighTemperature, recent[0].Type);
        Assert.Equal(AlertType.LowBattery, recent[1].Type);
        Assert.All(recent, a => Assert.NotNull(a.Id));
    }

    [Fact]
    public async Task Acknowledge_ReducesTheUnacknowledgedCount()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        AlertStore store = new(db.ConnectionFactory);
        long a = await store.InsertAsync(Alert(AlertType.LowBattery, "Low", DateTimeOffset.UtcNow));
        await store.InsertAsync(Alert(AlertType.CriticalBattery, "Critical", DateTimeOffset.UtcNow));

        Assert.Equal(2, await store.GetUnacknowledgedCountAsync());

        await store.AcknowledgeAsync(a);
        Assert.Equal(1, await store.GetUnacknowledgedCountAsync());

        await store.AcknowledgeAllAsync();
        Assert.Equal(0, await store.GetUnacknowledgedCountAsync());
    }

    [Fact]
    public async Task GetRecent_RoundTripsEveryField()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        AlertStore store = new(db.ConnectionFactory);
        Alert original = new(
            AlertType.HighTemperature, AlertSeverity.Warning, "Battery running hot (47 °C)",
            "The battery is at 47 °C.", 47.0, 45.0, DateTimeOffset.UtcNow);
        await store.InsertAsync(original);

        Alert stored = Assert.Single(await store.GetRecentAsync(5));
        Assert.Equal(original.Type, stored.Type);
        Assert.Equal(original.Severity, stored.Severity);
        Assert.Equal(original.Title, stored.Title);
        Assert.Equal(47.0, stored.TriggerValue);
        Assert.Equal(45.0, stored.ThresholdValue);
        Assert.False(stored.Acknowledged);
    }

    private static Alert Alert(AlertType type, string title, DateTimeOffset ts) =>
        new(type, AlertSeverity.Warning, title, "Message.", null, null, ts);
}
