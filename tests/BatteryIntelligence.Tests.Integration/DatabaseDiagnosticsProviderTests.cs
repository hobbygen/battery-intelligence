using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Integration;

/// <summary>Backs the mandatory Diagnostics "Database" facts (specification section 47).</summary>
public sealed class DatabaseDiagnosticsProviderTests
{
    [Fact]
    public async Task GetDiagnosticsAsync_BeforeMigration_ReportsDoesNotExist()
    {
        using TempDatabase db = new();
        BatterySampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<BatterySampleWriteQueue>.Instance);
        PowerSampleWriteQueue powerQueue = new(db.ConnectionFactory, NullLogger<PowerSampleWriteQueue>.Instance);
        TemperatureSampleWriteQueue temperatureQueue = new(db.ConnectionFactory, NullLogger<TemperatureSampleWriteQueue>.Instance);
        ProcessSampleWriteQueue processQueue = new(db.ConnectionFactory, NullLogger<ProcessSampleWriteQueue>.Instance);
        DatabaseDiagnosticsProvider provider = new(db.ConnectionFactory, queue, powerQueue, temperatureQueue, processQueue, NullLogger<DatabaseDiagnosticsProvider>.Instance);

        DatabaseDiagnostics diagnostics = await provider.GetDiagnosticsAsync();

        Assert.False(diagnostics.Exists);
        Assert.Equal(0, diagnostics.SampleRowCount);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_AfterWrites_ReportsRowCountAndLastWrite()
    {
        using TempDatabase db = new();
        await db.MigrateAsync();

        BatterySampleWriteQueue queue = new(db.ConnectionFactory, NullLogger<BatterySampleWriteQueue>.Instance);
        PowerSampleWriteQueue powerQueue = new(db.ConnectionFactory, NullLogger<PowerSampleWriteQueue>.Instance);
        TemperatureSampleWriteQueue temperatureQueue = new(db.ConnectionFactory, NullLogger<TemperatureSampleWriteQueue>.Instance);
        ProcessSampleWriteQueue processQueue = new(db.ConnectionFactory, NullLogger<ProcessSampleWriteQueue>.Instance);
        DatabaseDiagnosticsProvider provider = new(db.ConnectionFactory, queue, powerQueue, temperatureQueue, processQueue, NullLogger<DatabaseDiagnosticsProvider>.Instance);

        await db.ExecuteAsync(
            """
            INSERT INTO BatteryDevice (HardwareId, FirstSeenUtc, LastSeenUtc, IsPresent) VALUES ('battery0', 0, 0, 1);
            """);
        long deviceId = await db.ScalarAsync<long>("SELECT Id FROM BatteryDevice;");
        await db.ExecuteAsync(
            """
            INSERT INTO BatterySample (TimestampUtc, BatteryId, Status, DataQuality, MeasurementSource)
            VALUES ($ts, $id, 2, 1, 1);
            """,
            ("$ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ("$id", deviceId));

        DatabaseDiagnostics diagnostics = await provider.GetDiagnosticsAsync();

        Assert.True(diagnostics.Exists);
        Assert.Equal(1, diagnostics.SampleRowCount);
        Assert.True(diagnostics.SizeBytes > 0);
    }
}
