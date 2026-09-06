using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// Export collection (specification section 36): the scope selects the tables,
/// rows are filtered to the range, and enum columns come out as names.
/// </summary>
public sealed class ExportDataSourceTests
{
    [Fact]
    public async Task CollectAsync_ReturnsOnlyTheScopedTables_WithRowsInRange()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await InsertSessionAsync(db, deviceId, now.AddDays(-2), now.AddDays(-2).AddHours(3));
        await InsertSessionAsync(db, deviceId, now.AddDays(-40), now.AddDays(-40).AddHours(1)); // out of range
        await InsertAlertAsync(db, now.AddDays(-1), AlertType.LowBattery, AlertSeverity.Warning);
        await InsertBatterySampleAsync(db, deviceId, now.AddDays(-1));

        ExportDataSource source = new(db.ConnectionFactory);
        IReadOnlyList<ExportTable> tables = await source.CollectAsync(
            new ExportRequest(new DateRange(now.AddDays(-7), now), ExportScope.Sessions | ExportScope.Alerts));

        Assert.Equal(2, tables.Count);
        Assert.Contains(tables, t => t.Name == "BatterySession");
        Assert.Contains(tables, t => t.Name == "Alert");
        Assert.DoesNotContain(tables, t => t.Name == "BatterySample");

        ExportTable sessions = tables.Single(t => t.Name == "BatterySession");
        Assert.Single(sessions.Rows); // the 40-day-old one is outside the range

        int typeColumn = sessions.Columns.ToList().IndexOf("SessionType");
        Assert.Equal("Discharging", sessions.Rows[0][typeColumn]);
    }

    [Fact]
    public async Task CollectAsync_WritesAlertEnumColumnsAsNames()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        await InsertDeviceAsync(db);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await InsertAlertAsync(db, now.AddHours(-2), AlertType.HighTemperature, AlertSeverity.Critical);

        ExportDataSource source = new(db.ConnectionFactory);
        ExportTable alerts = (await source.CollectAsync(
            new ExportRequest(new DateRange(now.AddDays(-1), now), ExportScope.Alerts))).Single();

        var columns = alerts.Columns.ToList();
        Assert.Equal("HighTemperature", alerts.Rows[0][columns.IndexOf("AlertType")]);
        Assert.Equal("Critical", alerts.Rows[0][columns.IndexOf("Severity")]);
    }

    [Fact]
    public async Task CollectAsync_EmptyScope_ReturnsNoTables()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        ExportDataSource source = new(db.ConnectionFactory);
        IReadOnlyList<ExportTable> tables = await source.CollectAsync(
            new ExportRequest(new DateRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow), ExportScope.None));

        Assert.Empty(tables);
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

    private static Task InsertSessionAsync(TempDatabase db, long deviceId, DateTimeOffset start, DateTimeOffset end) =>
        db.ExecuteAsync(
            """
            INSERT INTO BatterySession (BatteryId, SessionType, StartUtc, EndUtc, StartPercentage, EndPercentage, ClosedCleanly, EndReason)
            VALUES ($id, 2, $start, $end, 90, 40, 1, 3);
            """,
            ("$id", deviceId), ("$start", start.ToUnixTimeMilliseconds()), ("$end", end.ToUnixTimeMilliseconds()));

    private static Task InsertAlertAsync(TempDatabase db, DateTimeOffset ts, AlertType type, AlertSeverity severity) =>
        db.ExecuteAsync(
            """
            INSERT INTO Alert (TimestampUtc, AlertType, Severity, Title, Message)
            VALUES ($ts, $type, $sev, 'T', 'M');
            """,
            ("$ts", ts.ToUnixTimeMilliseconds()), ("$type", (int)type), ("$sev", (int)severity));

    private static Task InsertBatterySampleAsync(TempDatabase db, long deviceId, DateTimeOffset ts) =>
        db.ExecuteAsync(
            """
            INSERT INTO BatterySample (TimestampUtc, BatteryId, Percentage, Status, DataQuality, MeasurementSource)
            VALUES ($ts, $id, 55, 2, 1, 1);
            """,
            ("$ts", ts.ToUnixTimeMilliseconds()), ("$id", deviceId));
}
