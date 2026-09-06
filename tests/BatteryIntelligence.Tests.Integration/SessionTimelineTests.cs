using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// The merged session timeline (specification section 12; R-047): session spans,
/// in-session events and system events come back in one chronological list,
/// clipped to the requested range.
/// </summary>
public sealed class SessionTimelineTests
{
    [Fact]
    public async Task GetTimelineAsync_MergesSessionsEventsAndSystemEvents_InChronologicalOrder()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        DateTimeOffset t0 = new(2026, 9, 6, 20, 0, 0, TimeSpan.Zero);

        // A discharging session 20:00–22:00 with an interruption event at 21:00,
        // and a screen-off system event at 20:30.
        long sessionId = await InsertSessionAsync(db, deviceId, type: 2, t0, t0.AddHours(2), startPct: 90, endPct: 55, endReason: 4);
        await InsertSystemEventAsync(db, t0.AddMinutes(30), eventType: 4 /* ScreenOff */);
        await InsertSessionEventAsync(db, sessionId, t0.AddHours(1), eventType: 1 /* Interruption */, pct: 72);

        var store = new SessionStore(db.ConnectionFactory);
        IReadOnlyList<TimelineSegment> timeline = await store.GetTimelineAsync(t0.AddHours(-1), t0.AddHours(3));

        Assert.Equal(3, timeline.Count);
        for (int i = 1; i < timeline.Count; i++)
        {
            Assert.True(timeline[i].StartUtc >= timeline[i - 1].StartUtc);
        }

        Assert.Equal("Discharging", timeline[0].Kind);
        Assert.Equal(90, timeline[0].StartPercentage);
        Assert.Equal(t0.AddMinutes(30), timeline[1].StartUtc);
        Assert.Equal(t0.AddHours(1), timeline[2].StartUtc);
    }

    [Fact]
    public async Task GetTimelineAsync_ClipsToTheRequestedRange()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        DateTimeOffset t0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        await InsertSessionAsync(db, deviceId, type: 2, t0, t0.AddHours(1), startPct: 80, endPct: 60, endReason: 3);          // in range
        await InsertSessionAsync(db, deviceId, type: 1, t0.AddDays(10), t0.AddDays(10).AddHours(1), startPct: 30, endPct: 90, endReason: 1); // out of range
        await InsertSystemEventAsync(db, t0.AddDays(10).AddMinutes(5), eventType: 8 /* AcConnected */);                       // out of range

        var store = new SessionStore(db.ConnectionFactory);
        IReadOnlyList<TimelineSegment> timeline = await store.GetTimelineAsync(t0.AddHours(-1), t0.AddHours(2));

        Assert.Single(timeline);
        Assert.Equal("Discharging", timeline[0].Kind);
    }

    [Fact]
    public async Task GetTimelineAsync_EmptyRange_ReturnsNothing()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        await InsertDeviceAsync(db);

        var store = new SessionStore(db.ConnectionFactory);
        IReadOnlyList<TimelineSegment> timeline = await store.GetTimelineAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Empty(timeline);
    }

    private static async Task<long> InsertDeviceAsync(TempDatabase db)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.ExecuteAsync(
            "INSERT INTO BatteryDevice (HardwareId, DesignCapacityMwh, FirstSeenUtc, LastSeenUtc, IsPresent) VALUES ('battery0', 95008, $n, $n, 1);",
            ("$n", now));
        return await db.ScalarAsync<long>("SELECT Id FROM BatteryDevice WHERE HardwareId = 'battery0';");
    }

    private static async Task<long> InsertSessionAsync(
        TempDatabase db, long deviceId, int type, DateTimeOffset start, DateTimeOffset end, double startPct, double endPct, int endReason)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO BatterySession (BatteryId, SessionType, StartUtc, EndUtc, StartPercentage, EndPercentage, EndReason, ClosedCleanly)
            VALUES ($d, $type, $start, $end, $sp, $ep, $reason, 1);
            """,
            ("$d", deviceId), ("$type", type), ("$start", start.ToUnixTimeMilliseconds()), ("$end", end.ToUnixTimeMilliseconds()),
            ("$sp", startPct), ("$ep", endPct), ("$reason", endReason));
        return await db.ScalarAsync<long>("SELECT Id FROM BatterySession ORDER BY Id DESC LIMIT 1;");
    }

    private static Task InsertSessionEventAsync(TempDatabase db, long sessionId, DateTimeOffset ts, int eventType, double pct) =>
        db.ExecuteAsync(
            "INSERT INTO SessionEvent (SessionId, TimestampUtc, EventType, Percentage, Inferred) VALUES ($s, $t, $e, $p, 0);",
            ("$s", sessionId), ("$t", ts.ToUnixTimeMilliseconds()), ("$e", eventType), ("$p", pct));

    private static Task InsertSystemEventAsync(TempDatabase db, DateTimeOffset ts, int eventType) =>
        db.ExecuteAsync(
            "INSERT INTO SystemEvent (TimestampUtc, EventType, Inferred) VALUES ($t, $e, 0);",
            ("$t", ts.ToUnixTimeMilliseconds()), ("$e", eventType));
}
