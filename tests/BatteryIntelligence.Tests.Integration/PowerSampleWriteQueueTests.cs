using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// The dedicated <c>PowerSample</c> pipeline (docs/database.md section 4;
/// docs/roadmap.md Phase 5): batched writes, the count trigger, and the
/// aggregate never persisted as a device.
/// </summary>
public sealed class PowerSampleWriteQueueTests
{
    [Fact]
    public async Task FlushAsync_PersistsQueuedReadings_AndCreatesTheDeviceRow()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        PowerSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<PowerSampleWriteQueue>.Instance);

        queue.Enqueue(Device("battery0"), Reading("battery0", powerMw: -6_332, voltageMv: 11_791));
        await queue.FlushAsync();

        long deviceCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatteryDevice WHERE HardwareId = 'battery0';");
        long rowCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM PowerSample;");
        long power = await db.ScalarAsync<long>("SELECT PowerMw FROM PowerSample LIMIT 1;");
        long direction = await db.ScalarAsync<long>("SELECT Direction FROM PowerSample LIMIT 1;");

        Assert.Equal(1, deviceCount);
        Assert.Equal(1, rowCount);
        Assert.Equal(-6_332, power);
        Assert.Equal((long)PowerDirection.Discharging, direction);
        Assert.NotNull(queue.LastFlushUtc);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task Enqueue_ReachingCountThreshold_FlushesWithoutAnExplicitCall()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        PowerSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<PowerSampleWriteQueue>.Instance);

        for (int i = 0; i < 200; i++)
        {
            queue.Enqueue(Device("battery0"), Reading("battery0", -5_000, 11_800));
        }

        await WaitUntilAsync(() => queue.PendingCount == 0, TimeSpan.FromSeconds(5));

        long rowCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM PowerSample;");
        Assert.Equal(200, rowCount);
    }

    [Fact]
    public async Task Enqueue_AggregateDevice_IsNeverPersisted()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        PowerSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<PowerSampleWriteQueue>.Instance);

        queue.Enqueue(Device("battery0"), Reading("battery0", -6_000, 11_800));
        queue.Enqueue(Device(BatteryDevice.AggregateHardwareId), Reading(BatteryDevice.AggregateHardwareId, -6_000, 11_800));
        await queue.FlushAsync();

        long rowCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM PowerSample;");
        long deviceCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatteryDevice;");

        Assert.Equal(1, rowCount);
        Assert.Equal(1, deviceCount);
    }

    [Fact]
    public async Task Flush_EstimatedRate_StoresTheEstimatedGrade()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        PowerSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<PowerSampleWriteQueue>.Instance);

        PowerReading estimated = new()
        {
            BatteryId = "battery0",
            TimestampUtc = DateTimeOffset.UtcNow,
            PowerMw = Measurement<int>.Estimated(-4_200, MeasurementSource.Model),
            VoltageMv = Measurement<int>.Unavailable(),
            CurrentMa = Measurement<double>.Unavailable(),
            Direction = PowerDirection.Discharging,
        };

        queue.Enqueue(Device("battery0"), estimated);
        await queue.FlushAsync();

        long quality = await db.ScalarAsync<long>("SELECT DataQuality FROM PowerSample LIMIT 1;");
        Assert.Equal((long)DataQuality.Estimated, quality);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }

    private static BatteryDevice Device(string hardwareId) => new(
        hardwareId, "Test Battery", "Test Mfr", "SN1", "LiP",
        Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
        Measurement<int>.Unavailable(), ReportsInMilliamps: false);

    private static PowerReading Reading(string hardwareId, int powerMw, int voltageMv) => new()
    {
        BatteryId = hardwareId,
        TimestampUtc = DateTimeOffset.UtcNow,
        PowerMw = Measurement<int>.Measured(powerMw, MeasurementSource.WinRtBattery),
        VoltageMv = Measurement<int>.Measured(voltageMv, MeasurementSource.Wmi),
        CurrentMa = Measurement<double>.Calculated(powerMw / (double)voltageMv * 1000.0),
        Direction = powerMw >= 0 ? PowerDirection.Charging : PowerDirection.Discharging,
    };
}
