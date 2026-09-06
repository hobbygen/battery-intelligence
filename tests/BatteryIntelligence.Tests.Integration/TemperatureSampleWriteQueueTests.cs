using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>
/// The dedicated <c>TemperatureSample</c> pipeline (docs/database.md section 4;
/// docs/roadmap.md Phase 6): batched writes, deci-Kelvin storage, and — the
/// reference-machine outcome — an unavailable reading persisting nothing.
/// </summary>
public sealed class TemperatureSampleWriteQueueTests
{
    [Fact]
    public async Task FlushAsync_PersistsReading_InDeciKelvin_AndCreatesTheDeviceRow()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        TemperatureSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<TemperatureSampleWriteQueue>.Instance);

        queue.Enqueue(Device("battery0"), Reading("battery0", celsius: 34.2, PowerDirection.Charging));
        await queue.FlushAsync();

        long deviceCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM BatteryDevice WHERE HardwareId = 'battery0';");
        long rowCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM TemperatureSample;");
        long deciKelvin = await db.ScalarAsync<long>("SELECT TemperatureDk FROM TemperatureSample LIMIT 1;");
        long chargeState = await db.ScalarAsync<long>("SELECT ChargeState FROM TemperatureSample LIMIT 1;");

        Assert.Equal(1, deviceCount);
        Assert.Equal(1, rowCount);
        Assert.Equal(3073, deciKelvin); // round((34.2 + 273.15) * 10)
        Assert.Equal((long)PowerDirection.Charging, chargeState);
        Assert.NotNull(queue.LastFlushUtc);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task Enqueue_UnavailableReading_PersistsNothing()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        TemperatureSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<TemperatureSampleWriteQueue>.Instance);

        TemperatureReading unavailable = new()
        {
            BatteryId = "battery0",
            TimestampUtc = DateTimeOffset.UtcNow,
            TemperatureCelsius = Measurement<double>.Unavailable(),
            Band = TemperatureBand.Cool,
            Severity = TemperatureSeverity.Normal,
            ChargeContext = PowerDirection.Discharging,
        };

        queue.Enqueue(Device("battery0"), unavailable);
        await queue.FlushAsync();

        long rowCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM TemperatureSample;");
        Assert.Equal(0, rowCount);
    }

    [Fact]
    public async Task Enqueue_ReachingCountThreshold_FlushesWithoutAnExplicitCall()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        TemperatureSampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<TemperatureSampleWriteQueue>.Instance);

        for (int i = 0; i < 200; i++)
        {
            queue.Enqueue(Device("battery0"), Reading("battery0", 30 + (i % 10), PowerDirection.Idle));
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (queue.PendingCount != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        long rowCount = await db.ScalarAsync<long>("SELECT COUNT(*) FROM TemperatureSample;");
        Assert.Equal(200, rowCount);
    }

    private static BatteryDevice Device(string hardwareId) => new(
        hardwareId, "Test Battery", "Test Mfr", "SN1", "LiP",
        Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
        Measurement<int>.Unavailable(), ReportsInMilliamps: false);

    private static TemperatureReading Reading(string hardwareId, double celsius, PowerDirection context) => new()
    {
        BatteryId = hardwareId,
        TimestampUtc = DateTimeOffset.UtcNow,
        TemperatureCelsius = Measurement<double>.Measured(celsius, MeasurementSource.Wmi),
        Band = TemperatureBand.Normal,
        Severity = TemperatureSeverity.Normal,
        ChargeContext = context,
    };
}
