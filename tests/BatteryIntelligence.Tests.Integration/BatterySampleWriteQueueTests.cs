using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// Batched write correctness (docs/testing.md section 5; docs/monitoring-dataflow.md
/// section 6): samples persist in batches, the count trigger fires without
/// waiting for the timer, and the synthesised multi-battery aggregate is never
/// written as though it were a physical device.
/// </summary>
public sealed class BatterySampleWriteQueueTests
{
    [Fact]
    public async Task FlushAsync_PersistsQueuedSamples_AndCreatesTheDeviceRow()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        BatterySampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<BatterySampleWriteQueue>.Instance, new MonitoringStatusRegistry());

        queue.Enqueue(MakeSnapshot("battery0", 84.0));
        await queue.FlushAsync();

        long deviceCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatteryDevice WHERE HardwareId = 'battery0';");
        long sampleCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;");
        double percentage = await db.ScalarAsync<double>("SELECT Percentage FROM BatterySample LIMIT 1;");

        Assert.Equal(1, deviceCount);
        Assert.Equal(1, sampleCount);
        Assert.Equal(84.0, percentage, 1);
        Assert.NotNull(queue.LastFlushUtc);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task Enqueue_ReachingCountThreshold_FlushesWithoutAnExplicitCall()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        BatterySampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<BatterySampleWriteQueue>.Instance, new MonitoringStatusRegistry());

        for (int i = 0; i < 200; i++)
        {
            queue.Enqueue(MakeSnapshot("battery0", 50.0));
        }

        // The 200th Enqueue call triggers an async flush; give it a moment to land.
        await WaitUntilAsync(() => queue.PendingCount == 0, TimeSpan.FromSeconds(5));

        long sampleCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;");
        Assert.Equal(200, sampleCount);
    }

    [Fact]
    public async Task Enqueue_AggregateSnapshot_IsNeverPersisted()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        BatterySampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<BatterySampleWriteQueue>.Instance, new MonitoringStatusRegistry());

        BatterySnapshot real = MakeSnapshot("battery0", 70.0);
        BatterySnapshot aggregate = real with
        {
            Device = real.Device with { HardwareId = BatteryDevice.AggregateHardwareId },
        };

        queue.Enqueue(real);
        queue.Enqueue(aggregate);
        await queue.FlushAsync();

        long sampleCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;");
        long deviceCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatteryDevice;");

        Assert.Equal(1, sampleCount);
        Assert.Equal(1, deviceCount);
    }

    [Fact]
    public async Task FlushAsync_TwiceWithNoNewData_IsHarmless()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        BatterySampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<BatterySampleWriteQueue>.Instance, new MonitoringStatusRegistry());
        queue.Enqueue(MakeSnapshot("battery0", 60.0));

        await queue.FlushAsync();
        await queue.FlushAsync();

        long sampleCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatterySample;");
        Assert.Equal(1, sampleCount);
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
