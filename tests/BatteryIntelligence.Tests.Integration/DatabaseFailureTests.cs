using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Data;
using BatteryIntelligence.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// The database-failure column of the spec §62 matrix (docs/testing.md §6): a
/// locked, unreachable or corrupt database must not crash the app, must not lose
/// or fabricate data, must surface on Diagnostics, and must recover once the
/// fault clears.
/// </summary>
public sealed class DatabaseFailureTests
{
    [Fact]
    public async Task UnreachableDatabase_FlushDoesNotThrow_RequeuesTheBatch_AndReportsDegraded()
    {
        string badPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"bi-nodir-{Guid.NewGuid():N}", "nested", "battery.db");
        var factory = new SqliteConnectionFactory(badPath);
        var registry = new MonitoringStatusRegistry();
        var queue = new BatterySampleWriteQueue(factory, NullLogger<BatterySampleWriteQueue>.Instance, registry);

        queue.Enqueue(MakeSnapshot("battery0", 70));

        // No throw escapes, the sample is not lost, and Database shows unhealthy.
        await queue.FlushAsync();

        Assert.Equal(1, queue.PendingCount);
        MonitoringStatus dbStatus = registry.Snapshot().Single(s => s.Component == MonitoringComponent.Database);
        Assert.NotEqual(MonitoringHealth.Healthy, dbStatus.Health);
        Assert.NotEqual(MonitoringHealth.Starting, dbStatus.Health);
    }

    [Fact]
    public async Task UnreachableDatabase_PendingRowsAreCappedToBoundMemory()
    {
        string badPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"bi-nodir-{Guid.NewGuid():N}", "battery.db");
        var factory = new SqliteConnectionFactory(badPath);
        var queue = new BatterySampleWriteQueue(factory, NullLogger<BatterySampleWriteQueue>.Instance, new MonitoringStatusRegistry());

        for (int i = 0; i < 1_500; i++)
        {
            queue.Enqueue(MakeSnapshot("battery0", 50));
        }

        await WaitUntilAsync(() => queue.PendingCount <= 1_000, TimeSpan.FromSeconds(5));
        Assert.True(queue.PendingCount <= 1_000, $"Pending count {queue.PendingCount} exceeded the 1000 cap.");
    }

    [Fact]
    public async Task LockedDatabase_RecoversWithoutARestart_OnceTheLockClears()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        var queue = new BatterySampleWriteQueue(db.ConnectionFactory, NullLogger<BatterySampleWriteQueue>.Instance, new MonitoringStatusRegistry());
        queue.Enqueue(MakeSnapshot("battery0", 61));

        await using (SqliteConnection locker = await db.ConnectionFactory.OpenAsync())
        {
            await using SqliteCommand begin = locker.CreateCommand();
            begin.CommandText = "BEGIN EXCLUSIVE;";
            await begin.ExecuteNonQueryAsync();

            // A flush while the database is exclusively locked must not throw and
            // must not lose the batch.
            await queue.FlushAsync();
            Assert.Equal(1, queue.PendingCount);
            Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;"));

            await using SqliteCommand rollback = locker.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            await rollback.ExecuteNonQueryAsync();
        }

        // Recovery without a restart: the next flush lands the requeued batch.
        await queue.FlushAsync();
        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;"));
    }

    [Fact]
    public async Task ReaderDuringWriter_UnderWal_BothSucceed()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();
        long deviceId = await InsertDeviceAsync(db);
        await db.ExecuteAsync(
            "INSERT INTO BatterySample (TimestampUtc, BatteryId, Percentage, Status, DataQuality, MeasurementSource) VALUES ($t, $d, 55, 2, 1, 1);",
            ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ("$d", deviceId));

        var queue = new BatterySampleWriteQueue(db.ConnectionFactory, NullLogger<BatterySampleWriteQueue>.Instance, new MonitoringStatusRegistry());
        queue.Enqueue(MakeSnapshot("battery0", 54));

        // Open a read, hold it, flush a write, then finish the read — WAL lets
        // the writer proceed without blocking the reader.
        await using (SqliteConnection readConn = await db.ConnectionFactory.OpenAsync())
        {
            await using SqliteCommand read = readConn.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM BatterySample;";
            await using SqliteDataReader reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            await queue.FlushAsync();
        }

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(2, await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;"));
    }

    [Fact]
    public async Task CorruptDatabase_MigratorReturnsFalse_AndNeverDeletesTheFile()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bi-corrupt-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            Assert.True(await new DatabaseMigrator(factory, NullLogger<DatabaseMigrator>.Instance).MigrateAsync());

            SqliteConnection.ClearAllPools();

            // Overwrite the header and first pages with garbage.
            byte[] bytes = await System.IO.File.ReadAllBytesAsync(path);
            new Random(1).NextBytes(bytes.AsSpan(0, Math.Min(bytes.Length, 4096)));
            await System.IO.File.WriteAllBytesAsync(path, bytes);

            bool ok = await new DatabaseMigrator(factory, NullLogger<DatabaseMigrator>.Instance).MigrateAsync();

            Assert.False(ok);
            Assert.True(System.IO.File.Exists(path), "A corrupt database must never be deleted.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string p in new[] { path, $"{path}-wal", $"{path}-shm" })
            {
                try { if (System.IO.File.Exists(p)) System.IO.File.Delete(p); } catch (System.IO.IOException) { }
            }
        }
    }

    private static async Task<long> InsertDeviceAsync(TempDatabase db)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.ExecuteAsync(
            "INSERT INTO BatteryDevice (HardwareId, DesignCapacityMwh, FirstSeenUtc, LastSeenUtc, IsPresent) VALUES ('battery0', 95008, $n, $n, 1);",
            ("$n", now));
        return await db.ScalarAsync<long>("SELECT Id FROM BatteryDevice WHERE HardwareId = 'battery0';");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }

    private static BatterySnapshot MakeSnapshot(string hardwareId, double percentage)
    {
        BatteryDevice device = new(
            hardwareId, "Test Battery", "Test Mfr", "SN1", "LiP",
            Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
            Measurement<int>.Unavailable(), ReportsInMilliamps: false);

        BatteryInfo info = new()
        {
            BatteryId = hardwareId,
            TimestampUtc = DateTimeOffset.UtcNow,
            Percentage = Measurement<double>.Measured(percentage, MeasurementSource.WinRtBattery),
            State = Measurement<BatteryState>.Measured(BatteryState.Discharging, MeasurementSource.WinRtBattery),
            AcOnline = Measurement<bool>.Measured(false, MeasurementSource.SystemPowerStatus),
            RemainingCapacityMWh = Measurement<int>.Measured(30_000, MeasurementSource.WinRtBattery),
            FullChargeCapacityMWh = Measurement<int>.Measured(38_008, MeasurementSource.WinRtBattery),
            DesignCapacityMWh = Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
            RetentionPercent = Measurement<double>.Calculated(40.0),
            VoltageMv = Measurement<int>.Measured(11_800, MeasurementSource.Wmi),
            PowerMw = Measurement<int>.Measured(-6000, MeasurementSource.WinRtBattery),
            CurrentMa = Measurement<double>.Calculated(-508.5),
            CycleCount = Measurement<int>.Unavailable(),
            TemperatureCelsius = Measurement<double>.Unavailable(),
        };

        return new BatterySnapshot(device, info);
    }
}
