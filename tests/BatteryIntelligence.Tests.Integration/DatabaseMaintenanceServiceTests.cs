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
    public async Task RunRollupAsync_RollsFullyElapsedDays_IntoApplicationUsage_AndIsIdempotent()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        long yesterday = FloorToDay(DateTimeOffset.UtcNow.AddDays(-1));
        // Three ticks of "chrome" that day, one in the foreground.
        await InsertProcessSampleAsync(db, yesterday + 10_000, "chrome", "Google Chrome", cpu: 20, powerMw: 1_000, foreground: true);
        await InsertProcessSampleAsync(db, yesterday + 20_000, "chrome", "Google Chrome", cpu: 30, powerMw: 1_400, foreground: false);
        await InsertProcessSampleAsync(db, yesterday + 30_000, "chrome", "Google Chrome", cpu: 10, powerMw: 600, foreground: false);
        // A sample from today must NOT be rolled — the day is still accumulating.
        await InsertProcessSampleAsync(db, FloorToDay(DateTimeOffset.UtcNow) + 5_000, "chrome", "Google Chrome", cpu: 5, powerMw: 100, foreground: false);

        AppSettings settings = new();
        settings.Monitoring.ProcessSampleSeconds = 10;
        DatabaseMaintenanceService service = CreateService(db, settings);

        await service.RunRollupAsync(CancellationToken.None);
        await service.RunRollupAsync(CancellationToken.None);

        long rows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM ApplicationUsage;");
        long totalSeconds = await db.ScalarAsync<long>("SELECT TotalSeconds FROM ApplicationUsage WHERE ApplicationKey = 'chrome';");
        long foregroundSeconds = await db.ScalarAsync<long>("SELECT ForegroundSeconds FROM ApplicationUsage WHERE ApplicationKey = 'chrome';");
        long energyMwh = await db.ScalarAsync<long>("SELECT EstimatedEnergyMwh FROM ApplicationUsage WHERE ApplicationKey = 'chrome';");

        Assert.Equal(1, rows);
        Assert.Equal(30, totalSeconds);          // 3 ticks × 10 s
        Assert.Equal(10, foregroundSeconds);     // 1 tick × 10 s
        Assert.Equal(8, energyMwh);              // (1000+1400+600) mW × 10 s / 3600 ≈ 8.3 mWh
    }

    [Fact]
    public async Task RunRetentionAsync_DeletesProcessSamples_OnlyAfterTheirDayIsRolledUp()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        long oldDay = FloorToDay(DateTimeOffset.UtcNow.AddDays(-10));
        await InsertProcessSampleAsync(db, oldDay + 10_000, "chrome", "Google Chrome", cpu: 20, powerMw: 1_000, foreground: false);

        AppSettings settings = new();
        settings.Data.RawRetentionDays = 7;
        settings.Monitoring.ProcessSampleSeconds = 10;
        DatabaseMaintenanceService service = CreateService(db, settings);

        // Not rolled up yet: retention must not touch it.
        await service.RunRetentionAsync(CancellationToken.None);
        Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM ProcessSample;"));

        await service.RunRollupAsync(CancellationToken.None);
        await service.RunRetentionAsync(CancellationToken.None);
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM ProcessSample;"));
        Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM ApplicationUsage;"));
    }

    [Fact]
    public async Task RunRollupAsync_RollsSettledHours_IntoSampleHour_AndIsIdempotent()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long hourUtc = (DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeMilliseconds() / 3_600_000) * 3_600_000;
        // Two minute buckets in that hour.
        await InsertMinuteRowAsync(db, deviceId, hourUtc + 60_000, 80.0, -6000);
        await InsertMinuteRowAsync(db, deviceId, hourUtc + 120_000, 78.0, -6400);

        DatabaseMaintenanceService service = CreateService(db);
        await service.RunRollupAsync(CancellationToken.None);
        await service.RunRollupAsync(CancellationToken.None);

        long rows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM SampleHour;");
        long sampleCount = await db.ScalarAsync<long>("SELECT SampleCount FROM SampleHour WHERE HourUtc = $h;", ("$h", hourUtc));
        Assert.Equal(1, rows);
        Assert.Equal(2, sampleCount);
    }

    [Fact]
    public async Task RunRollupAsync_RollsFullyElapsedDays_IntoDailyStatistics_FromSessions()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long dayUtc = FloorToDay(DateTimeOffset.UtcNow.AddDays(-2));
        // A 2-hour discharge (90 -> 60) and a 1-hour charge (60 -> 95) that day.
        await InsertClosedSessionAsync(db, deviceId, type: 2, dayUtc + 3_600_000, dayUtc + 3_600_000 + 7_200_000, 90, 60, screenOn: 3600, screenOff: 3600);
        await InsertClosedSessionAsync(db, deviceId, type: 1, dayUtc + 20_000_000, dayUtc + 20_000_000 + 3_600_000, 60, 95, screenOn: 1800, screenOff: 0);

        DatabaseMaintenanceService service = CreateService(db);
        await service.RunRollupAsync(CancellationToken.None);
        await service.RunRollupAsync(CancellationToken.None);

        long rows = await db.ScalarAsync<long>("SELECT COUNT(*) FROM DailyStatistics;");
        long dischargeSec = await db.ScalarAsync<long>("SELECT DischargingSeconds FROM DailyStatistics WHERE DayUtc = $d;", ("$d", dayUtc));
        double pctDischarged = await db.ScalarAsync<double>("SELECT PercentDischarged FROM DailyStatistics WHERE DayUtc = $d;", ("$d", dayUtc));
        long chargeSessions = await db.ScalarAsync<long>("SELECT ChargeSessions FROM DailyStatistics WHERE DayUtc = $d;", ("$d", dayUtc));

        Assert.Equal(1, rows);
        Assert.Equal(7200, dischargeSec);
        Assert.Equal(30.0, pctDischarged, 1);
        Assert.Equal(1, chargeSessions);
    }

    [Fact]
    public async Task RunRollupAsync_DoesNotRollToday_IntoDailyStatistics()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);

        long today = FloorToDay(DateTimeOffset.UtcNow);
        await InsertClosedSessionAsync(db, deviceId, type: 2, today + 1_000_000, today + 4_000_000, 90, 70, screenOn: 1000, screenOff: 2000);

        DatabaseMaintenanceService service = CreateService(db);
        await service.RunRollupAsync(CancellationToken.None);

        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM DailyStatistics;"));
    }

    [Fact]
    public async Task RunRetentionAsync_DropsAlertsPastTheAlertRetentionWindow()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        long oldTs = DateTimeOffset.UtcNow.AddDays(-120).ToUnixTimeMilliseconds();
        long recentTs = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeMilliseconds();
        await db.ExecuteAsync(
            "INSERT INTO Alert (TimestampUtc, AlertType, Severity, Title, Message, Acknowledged) VALUES ($ts, 1, 1, 'Old', 'm', 1);",
            ("$ts", oldTs));
        await db.ExecuteAsync(
            "INSERT INTO Alert (TimestampUtc, AlertType, Severity, Title, Message, Acknowledged) VALUES ($ts, 1, 1, 'Recent', 'm', 0);",
            ("$ts", recentTs));

        AppSettings settings = new();
        settings.Data.AlertRetentionDays = 90;
        DatabaseMaintenanceService service = CreateService(db, settings);

        await service.RunRetentionAsync(CancellationToken.None);

        long count = await db.ScalarAsync<long>("SELECT COUNT(*) FROM Alert;");
        string kept = await db.ScalarAsync<string>("SELECT Title FROM Alert;");
        Assert.Equal(1, count);
        Assert.Equal("Recent", kept);
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

    private static long FloorToDay(DateTimeOffset timestamp) =>
        (timestamp.ToUnixTimeMilliseconds() / 86_400_000) * 86_400_000;

    private static async Task InsertMinuteRowAsync(TempDatabase db, long deviceId, long minuteUtcMs, double pct, int powerMw)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO SampleMinute (BatteryId, MinuteUtc, AvgPercentage, MinPercentage, MaxPercentage,
                                      AvgPowerMw, MinPowerMw, MaxPowerMw, AvgVoltageMv, SampleCount)
            VALUES ($id, $m, $p, $p, $p, $pw, $pw, $pw, 11800, 1);
            """,
            ("$id", deviceId), ("$m", (minuteUtcMs / 60_000) * 60_000), ("$p", pct), ("$pw", powerMw));
    }

    private static async Task InsertClosedSessionAsync(
        TempDatabase db, long deviceId, int type, long startMs, long endMs, double startPct, double endPct, long screenOn, long screenOff)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO BatterySession
                (BatteryId, SessionType, StartUtc, EndUtc, StartPercentage, EndPercentage,
                 StartCapacityMwh, EndCapacityMwh, ScreenOnSeconds, ScreenOffSeconds, SleepSeconds, ClosedCleanly)
            VALUES ($id, $type, $start, $end, $sp, $ep,
                    $scap, $ecap, $son, $soff, 0, 1);
            """,
            ("$id", deviceId), ("$type", type), ("$start", startMs), ("$end", endMs),
            ("$sp", startPct), ("$ep", endPct),
            ("$scap", (int)(startPct * 380)), ("$ecap", (int)(endPct * 380)),
            ("$son", screenOn), ("$soff", screenOff));
    }

    private static async Task InsertProcessSampleAsync(
        TempDatabase db, long timestampUtcMs, string appKey, string displayName, double cpu, int powerMw, bool foreground)
    {
        await db.ExecuteAsync(
            """
            INSERT INTO ProcessSample
                (TimestampUtc, ProcessId, ProcessName, ApplicationKey, CpuPercent, IsForeground,
                 EstimatedPowerMw, EstimatedSharePercent, EstimatorVersion, DataQuality, MeasurementSource)
            VALUES
                ($ts, 0, $name, $key, $cpu, $fg, $power, 10.0, 'AppEnergyV1', 3, 21);
            """,
            ("$ts", timestampUtcMs),
            ("$name", displayName),
            ("$key", appKey),
            ("$cpu", cpu),
            ("$fg", foreground ? 1 : 0),
            ("$power", powerMw));
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
