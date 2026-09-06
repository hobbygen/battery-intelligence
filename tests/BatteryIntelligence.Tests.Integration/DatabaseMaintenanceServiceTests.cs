using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// Rollup idempotency and retention safety (docs/testing.md sections 3 and 5;
/// docs/database.md section 6): retention never deletes a row until it has been
/// rolled up, and never deletes a row belonging to an open session.
/// </summary>
public sealed class DatabaseMaintenanceServiceTests
{
    private const long OneMinuteMs = 60_000;

    [Fact]
    public async Task RunRollupAsync_RollsOldEnoughSamples_IntoSampleMinute()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long minuteUtc = FloorToMinute(DateTimeOffset.UtcNow.AddMinutes(-10));
        await InsertSampleAsync(db, deviceId, minuteUtc + 5_000, percentage: 80.0, powerMw: -6000, dataQuality: 1);
        await InsertSampleAsync(db, deviceId, minuteUtc + 35_000, percentage: 79.0, powerMw: -6200, dataQuality: 1);

        DatabaseMaintenanceService service = CreateService(db);
        await service.RunRollupAsync(CancellationToken.None);

        long rolledCount = await db.ScalarAsync<long>(
            "SELECT SampleCount FROM SampleMinute WHERE BatteryId = $id AND MinuteUtc = $minute;",
            ("$id", deviceId), ("$minute", minuteUtc));
        double avgPercentage = await db.ScalarAsync<double>(
            "SELECT AvgPercentage FROM SampleMinute WHERE BatteryId = $id AND MinuteUtc = $minute;",
            ("$id", deviceId), ("$minute", minuteUtc));

        Assert.Equal(2, rolledCount);
        Assert.Equal(79.5, avgPercentage, 1);
    }

    [Fact]
    public async Task RunRollupAsync_LeavesRecentSamples_ForTheNextPass()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        // Inside the 2-minute safety margin: a batched write for this minute
        // could still arrive.
        long recentMinute = FloorToMinute(DateTimeOffset.UtcNow.AddSeconds(-30));
        await InsertSampleAsync(db, deviceId, recentMinute, percentage: 80.0, powerMw: -6000, dataQuality: 1);

        DatabaseMaintenanceService service = CreateService(db);
        await service.RunRollupAsync(CancellationToken.None);

        long rolledRows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM SampleMinute;");
        Assert.Equal(0, rolledRows);
    }

    [Fact]
    public async Task RunRollupAsync_ExcludesSuspectSamples()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long minuteUtc = FloorToMinute(DateTimeOffset.UtcNow.AddMinutes(-10));
        await InsertSampleAsync(db, deviceId, minuteUtc, percentage: 200.0, powerMw: -6000, dataQuality: 4); // Suspect

        DatabaseMaintenanceService service = CreateService(db);
        await service.RunRollupAsync(CancellationToken.None);

        long rolledRows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM SampleMinute;");
        Assert.Equal(0, rolledRows);
    }

    [Fact]
    public async Task RunRollupAsync_RunTwice_IsIdempotent()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long minuteUtc = FloorToMinute(DateTimeOffset.UtcNow.AddMinutes(-10));
        await InsertSampleAsync(db, deviceId, minuteUtc, percentage: 80.0, powerMw: -6000, dataQuality: 1);

        DatabaseMaintenanceService service = CreateService(db);
        await service.RunRollupAsync(CancellationToken.None);
        await service.RunRollupAsync(CancellationToken.None);

        long rolledRows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM SampleMinute;");
        Assert.Equal(1, rolledRows);
    }

    [Fact]
    public async Task RunRetentionAsync_DeletesOldSamples_OnlyAfterTheyAreRolledUp()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long oldMinute = FloorToMinute(DateTimeOffset.UtcNow.AddDays(-10));
        await InsertSampleAsync(db, deviceId, oldMinute, percentage: 50.0, powerMw: -1000, dataQuality: 1);

        AppSettings settings = new();
        settings.Data.RawRetentionDays = 7;
        DatabaseMaintenanceService service = CreateService(db, settings);

        // Not rolled up yet: retention must not touch it.
        await service.RunRetentionAsync(CancellationToken.None);
        Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;"));

        // Now roll it up, then retention should remove the raw row.
        await service.RunRollupAsync(CancellationToken.None);
        await service.RunRetentionAsync(CancellationToken.None);

        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;"));
        Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM SampleMinute;"));
    }

    [Fact]
    public async Task RunRetentionAsync_NeverDeletesARowInAnOpenSession()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long openSessionId = await InsertOpenSessionAsync(db, deviceId);

        long oldMinute = FloorToMinute(DateTimeOffset.UtcNow.AddDays(-10));
        await InsertSampleAsync(db, deviceId, oldMinute, percentage: 50.0, powerMw: -1000, dataQuality: 1, sessionId: openSessionId);

        AppSettings settings = new();
        settings.Data.RawRetentionDays = 7;
        DatabaseMaintenanceService service = CreateService(db, settings);

        await service.RunRollupAsync(CancellationToken.None);
        await service.RunRetentionAsync(CancellationToken.None);

        // Even though the sample is old and rolled up, it belongs to a session
        // that has not closed, so it must survive (docs/database.md section 6).
        Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;"));
    }

    [Fact]
    public async Task RunRetentionAsync_DeletesPowerSamplesPastTheRawWindow()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long oldTs = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        long recentTs = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        await InsertPowerSampleAsync(db, deviceId, oldTs);
        await InsertPowerSampleAsync(db, deviceId, recentTs);

        AppSettings settings = new();
        settings.Data.RawRetentionDays = 7;
        DatabaseMaintenanceService service = CreateService(db, settings);

        await service.RunRetentionAsync(CancellationToken.None);

        // PowerSample is raw-only: no rollup gate, so the age cutoff alone applies.
        Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM PowerSample;"));
        Assert.Equal(recentTs, await db.ScalarAsync<long>("SELECT TimestampUtc FROM PowerSample;"));
    }

    [Fact]
    public async Task RunRetentionAsync_RecordsLastCleanupUtc()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        DatabaseMaintenanceService service = CreateService(db);
        await service.RunRetentionAsync(CancellationToken.None);

        long lastCleanup = await db.ScalarAsync<long>("SELECT LastCleanupUtc FROM DataRetentionSettings WHERE Id = 1;");
        Assert.True(DateTimeOffset.FromUnixTimeMilliseconds(lastCleanup) > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private static DatabaseMaintenanceService CreateService(TempDatabase db, AppSettings? settings = null) =>
        new(db.ConnectionFactory, new FakeSettingsService(settings), NullLogger<DatabaseMaintenanceService>.Instance);

    private static long FloorToMinute(DateTimeOffset timestamp) =>
        (timestamp.ToUnixTimeMilliseconds() / OneMinuteMs) * OneMinuteMs;

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

    private static async Task InsertSampleAsync(
        TempDatabase db, long deviceId, long timestampUtcMs, double percentage, int powerMw, int dataQuality, long? sessionId = null)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO BatterySample
                (TimestampUtc, BatteryId, SessionId, Percentage, Status, PowerMw, DataQuality, MeasurementSource)
            VALUES
                ($ts, $batteryId, $sessionId, $pct, 2, $power, $quality, 1);
            """,
            ("$ts", timestampUtcMs),
            ("$batteryId", deviceId),
            ("$sessionId", (object?)sessionId ?? DBNull.Value),
            ("$pct", percentage),
            ("$power", powerMw),
            ("$quality", dataQuality));
    }

    private static async Task InsertPowerSampleAsync(TempDatabase db, long deviceId, long timestampUtcMs)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO PowerSample (TimestampUtc, BatteryId, PowerMw, VoltageMv, Direction, DataQuality, MeasurementSource)
            VALUES ($ts, $batteryId, -6000, 11800, 2, 1, 1);
            """,
            ("$ts", timestampUtcMs), ("$batteryId", deviceId));
    }

    private static async Task<long> InsertOpenSessionAsync(TempDatabase db, long deviceId)
    {
        long start = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        await db.ExecuteAsync(
            """
            INSERT INTO BatterySession (BatteryId, SessionType, StartUtc, EndUtc)
            VALUES ($batteryId, 2, $start, NULL);
            """,
            ("$batteryId", deviceId), ("$start", start));

        return await db.ScalarAsync<long>("SELECT Id FROM BatterySession WHERE BatteryId = $id AND EndUtc IS NULL;", ("$id", deviceId));
    }
}
