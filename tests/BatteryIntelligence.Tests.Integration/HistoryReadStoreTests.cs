using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// Tier-aware history reads (docs/database.md section 6; specification section 17):
/// the span picks the table, the series is time-ordered and within the point budget.
/// </summary>
public sealed class HistoryReadStoreTests
{
    [Fact]
    public async Task GetSeriesAsync_ShortSpan_ReadsRawSamples()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            await InsertRawAsync(db, deviceId, now.AddMinutes(-i * 5), 90 - i);
        }

        HistoryReadStore store = new(db.ConnectionFactory);
        ChartSeries series = await store.GetSeriesAsync(
            new HistoryRequest(HistoryMetric.ChargePercent, new DateRange(now.AddHours(-2), now.AddMinutes(1))));

        Assert.True(series.HasPoints);
        Assert.Equal("%", series.Unit);
        AssertTimeOrdered(series);
        // Raw values are the exact percentages seeded, so the max is 90.
        Assert.Equal(90, series.Points.Max(p => p.Value));
    }

    [Fact]
    public async Task GetSeriesAsync_WeekSpan_ReadsMinuteAverages()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        // No raw rows in this window — only minute rows. A Raw read would be empty.
        for (int i = 0; i < 200; i++)
        {
            await InsertMinuteAsync(db, deviceId, now.AddHours(-i), avgPercentage: 50 + (i % 10));
        }

        HistoryReadStore store = new(db.ConnectionFactory);
        ChartSeries series = await store.GetSeriesAsync(
            new HistoryRequest(HistoryMetric.ChargePercent, new DateRange(now.AddDays(-3), now), PointBudget: 100));

        Assert.True(series.HasPoints);
        Assert.True(series.Points.Count <= 100);
        AssertTimeOrdered(series);
    }

    [Fact]
    public async Task GetSeriesAsync_TwoMonthSpan_ReadsHourAverages()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 500; i++)
        {
            await InsertHourAsync(db, deviceId, now.AddHours(-i * 2), avgVoltageMv: 11000 + i);
        }

        HistoryReadStore store = new(db.ConnectionFactory);
        ChartSeries series = await store.GetSeriesAsync(
            new HistoryRequest(HistoryMetric.VoltageMv, new DateRange(now.AddDays(-60), now), PointBudget: 200));

        Assert.True(series.HasPoints);
        Assert.True(series.Points.Count <= 200);
        Assert.Equal("mV", series.Unit);
        AssertTimeOrdered(series);
    }

    [Fact]
    public async Task GetSeriesAsync_EmptyRange_ReturnsEmptySeries()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        await InsertDeviceAsync(db);

        HistoryReadStore store = new(db.ConnectionFactory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ChartSeries series = await store.GetSeriesAsync(
            new HistoryRequest(HistoryMetric.ChargePercent, new DateRange(now.AddHours(-1), now)));

        Assert.False(series.HasPoints);
    }

    [Fact]
    public async Task GetExtentAsync_ReportsEarliestAndLatestSample()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await InsertRawAsync(db, deviceId, now.AddDays(-5), 70);
        await InsertRawAsync(db, deviceId, now.AddHours(-1), 60);

        HistoryReadStore store = new(db.ConnectionFactory);
        (DateTimeOffset? earliest, DateTimeOffset? latest) = await store.GetExtentAsync();

        Assert.NotNull(earliest);
        Assert.NotNull(latest);
        Assert.True(latest > earliest);
    }

    [Fact]
    public async Task HasTemperatureDataAsync_IsFalseWithNoRowsAndTrueWithOne()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        HistoryReadStore store = new(db.ConnectionFactory);
        Assert.False(await store.HasTemperatureDataAsync());

        await db.ExecuteAsync(
            """
            INSERT INTO TemperatureSample (TimestampUtc, BatteryId, TemperatureDk, DataQuality, MeasurementSource)
            VALUES ($ts, $id, 3031, 1, 1);
            """,
            ("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ("$id", deviceId));

        Assert.True(await store.HasTemperatureDataAsync());
    }

    private static void AssertTimeOrdered(ChartSeries series)
    {
        for (int i = 1; i < series.Points.Count; i++)
        {
            Assert.True(series.Points[i].TimestampUtc >= series.Points[i - 1].TimestampUtc);
        }
    }

    private static async Task<long> InsertDeviceAsync(TempDatabase db)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.ExecuteAsync(
            """
            INSERT INTO BatteryDevice (HardwareId, DesignCapacityMwh, FirstSeenUtc, LastSeenUtc, IsPresent)
            VALUES ('battery0', 95008, $now, $now, 1);
            """,
            ("$now", now));
        return await db.ScalarAsync<long>("SELECT Id FROM BatteryDevice WHERE HardwareId = 'battery0';");
    }

    private static Task InsertRawAsync(TempDatabase db, long deviceId, DateTimeOffset ts, double percentage) =>
        db.ExecuteAsync(
            """
            INSERT INTO BatterySample (TimestampUtc, BatteryId, Percentage, Status, VoltageMv, PowerMw, DataQuality, MeasurementSource)
            VALUES ($ts, $id, $pct, 2, 11800, -6000, 1, 1);
            """,
            ("$ts", ts.ToUnixTimeMilliseconds()), ("$id", deviceId), ("$pct", percentage));

    private static Task InsertMinuteAsync(TempDatabase db, long deviceId, DateTimeOffset minute, double avgPercentage) =>
        db.ExecuteAsync(
            """
            INSERT INTO SampleMinute (BatteryId, MinuteUtc, AvgPercentage, AvgPowerMw, AvgVoltageMv, SampleCount)
            VALUES ($id, $minute, $pct, -6000, 11800, 12);
            """,
            ("$id", deviceId), ("$minute", minute.ToUnixTimeMilliseconds()), ("$pct", avgPercentage));

    private static Task InsertHourAsync(TempDatabase db, long deviceId, DateTimeOffset hour, int avgVoltageMv) =>
        db.ExecuteAsync(
            """
            INSERT INTO SampleHour (BatteryId, HourUtc, AvgPercentage, AvgPowerMw, AvgVoltageMv, SampleCount)
            VALUES ($id, $hour, 55, -6000, $mv, 720);
            """,
            ("$id", deviceId), ("$hour", hour.ToUnixTimeMilliseconds()), ("$mv", avgVoltageMv));
}
