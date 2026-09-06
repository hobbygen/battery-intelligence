using BatteryIntelligence.Data;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// "Delete all history" (specification section 29): every telemetry, session,
/// health, insight and alert table is emptied, while the device and configuration
/// rows survive.
/// </summary>
public sealed class HistoryMaintenanceTests
{
    [Fact]
    public async Task DeleteAllAsync_EmptiesHistoryTables_ButKeepsTheDeviceAndSchema()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.ExecuteAsync(
            """
            INSERT INTO BatteryDevice (HardwareId, DesignCapacityMwh, FirstSeenUtc, LastSeenUtc, IsPresent)
            VALUES ('battery0', 95008, $now, $now, 1);
            """,
            ("$now", now));
        long deviceId = await db.ScalarAsync<long>("SELECT Id FROM BatteryDevice WHERE HardwareId = 'battery0';");

        await db.ExecuteAsync(
            "INSERT INTO BatterySample (TimestampUtc, BatteryId, Percentage, Status, DataQuality, MeasurementSource) VALUES ($now, $id, 55, 2, 1, 1);",
            ("$now", now), ("$id", deviceId));
        await db.ExecuteAsync(
            "INSERT INTO PowerSample (TimestampUtc, BatteryId, PowerMw, Direction, DataQuality, MeasurementSource) VALUES ($now, $id, -6000, 2, 1, 1);",
            ("$now", now), ("$id", deviceId));
        await db.ExecuteAsync(
            "INSERT INTO TemperatureSample (TimestampUtc, BatteryId, TemperatureDk, DataQuality, MeasurementSource) VALUES ($now, $id, 3031, 1, 1);",
            ("$now", now), ("$id", deviceId));
        await db.ExecuteAsync(
            "INSERT INTO BatterySession (BatteryId, SessionType, StartUtc) VALUES ($id, 2, $now);",
            ("$now", now), ("$id", deviceId));
        await db.ExecuteAsync(
            "INSERT INTO Alert (TimestampUtc, AlertType, Severity, Title, Message) VALUES ($now, 1, 1, 'T', 'M');",
            ("$now", now));
        await db.ExecuteAsync(
            "INSERT INTO DailyStatistics (BatteryId, DayUtc) VALUES ($id, $now);",
            ("$now", now), ("$id", deviceId));
        await db.ExecuteAsync(
            "INSERT INTO ApplicationUsage (DayUtc, ApplicationKey, DisplayName) VALUES ($now, 'app', 'App');",
            ("$now", now));

        HistoryMaintenance maintenance = new(db.ConnectionFactory);
        await maintenance.DeleteAllAsync();

        foreach (string table in new[]
        {
            "BatterySample", "PowerSample", "TemperatureSample", "BatterySession",
            "Alert", "DailyStatistics", "ApplicationUsage",
        })
        {
            long count = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table};");
            Assert.Equal(0, count);
        }

        Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatteryDevice;"));
        Assert.True(await db.ScalarAsync<long>("SELECT COUNT(*) FROM SchemaMigration;") > 0);
    }
}
