using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Power;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// The Power orchestrator over a scripted battery-reading stream (docs/testing.md
/// section 4): readings are produced, the live series stays bounded, the
/// estimator ladder engages when a provider stops reporting power, and multiple
/// batteries are tracked independently.
/// </summary>
public sealed class PowerMonitoringServiceTests
{
    [Fact]
    public async Task Ingest_DischargingReading_ProducesAMeasuredRate_AndACalculatedCurrent()
    {
        FakeBatteryMonitoringService battery = new();
        FakePowerSampleWriteQueue queue = new();
        PowerMonitoringService service = Create(battery, queue);
        await service.StartAsync(CancellationToken.None);

        battery.Push([Discharging("battery0", DateTimeOffset.UtcNow, powerMw: -6_332, voltageMv: 11_791)]);

        PowerReading reading = Assert.Single(service.CurrentReadings);
        Assert.Equal(DataQuality.Measured, reading.PowerMw.Quality);
        Assert.Equal(-6_332, reading.PowerMw.Value);
        Assert.Equal(DataQuality.Calculated, reading.CurrentMa.Quality);
        Assert.True(reading.CurrentMa.Value < 0);
        Assert.Equal(PowerDirection.Discharging, reading.Direction);
        Assert.Single(queue.Enqueued);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Ingest_AnHourOfSamples_KeepsTheChartSeriesBounded()
    {
        FakeBatteryMonitoringService battery = new();
        PowerMonitoringService service = Create(battery, new FakePowerSampleWriteQueue());
        await service.StartAsync(CancellationToken.None);

        const int count = 2_000;
        DateTimeOffset start = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
        for (int i = 0; i < count; i++)
        {
            DateTimeOffset ts = start + TimeSpan.FromSeconds(i * 3_600.0 / count);
            int power = -6_000 + (i % 7 == 0 ? -40_000 : 0); // periodic spikes
            battery.Push([Discharging("battery0", ts, power, 11_800)]);
        }

        PowerSeriesSet series = service.GetSeries(PowerWindow.OneHour);
        Assert.InRange(series.Power.Points.Count, 2, 600);
        Assert.True(service.GetStatistics(PowerWindow.OneHour).Power.HasData);

        // A spike far below the baseline must survive decimation.
        Assert.Contains(series.Power.Points, p => p.Value <= -40_000);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Ingest_ProviderStopsReportingPower_EstimatorFallsBackToCapacitySlope()
    {
        FakeBatteryMonitoringService battery = new();
        PowerMonitoringService service = Create(battery, new FakePowerSampleWriteQueue());
        await service.StartAsync(CancellationToken.None);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int[] remaining = [30_000, 29_950, 29_900, 29_850, 29_800];
        for (int i = 0; i < remaining.Length; i++)
        {
            battery.Push([NoMeteredPower("battery0", now.AddMinutes(-(remaining.Length - 1 - i)), remaining[i])]);
        }

        PowerReading reading = Assert.Single(service.CurrentReadings);
        Assert.Equal(DataQuality.Estimated, reading.PowerMw.Quality);
        Assert.True(reading.PowerMw.Value < 0, "a draining capacity must estimate a negative rate");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Ingest_TwoBatteries_AreTrackedIndependently()
    {
        FakeBatteryMonitoringService battery = new();
        PowerMonitoringService service = Create(battery, new FakePowerSampleWriteQueue());
        await service.StartAsync(CancellationToken.None);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        battery.Push(
        [
            Discharging("battery0", now, -6_000, 11_800),
            Charging("battery1", now, 9_500, 12_400),
        ]);

        Assert.Equal(2, service.CurrentReadings.Count);
        Assert.Contains(service.CurrentReadings, r => r.Direction == PowerDirection.Discharging);
        Assert.Contains(service.CurrentReadings, r => r.Direction == PowerDirection.Charging);

        await service.StopAsync(CancellationToken.None);
    }

    private static PowerMonitoringService Create(FakeBatteryMonitoringService battery, FakePowerSampleWriteQueue queue) =>
        new(battery, new FakeSessionMonitoringService(), queue, NullLogger<PowerMonitoringService>.Instance, new MonitoringStatusRegistry());

    private static BatterySnapshot Discharging(string id, DateTimeOffset ts, int powerMw, int voltageMv) =>
        Build(id, ts, BatteryState.Discharging, powerMw, voltageMv, remainingMWh: 30_000);

    private static BatterySnapshot Charging(string id, DateTimeOffset ts, int powerMw, int voltageMv) =>
        Build(id, ts, BatteryState.Charging, powerMw, voltageMv, remainingMWh: 20_000);

    private static BatterySnapshot NoMeteredPower(string id, DateTimeOffset ts, int remainingMWh)
    {
        BatterySnapshot baseline = Build(id, ts, BatteryState.Discharging, 0, 11_800, remainingMWh);
        return baseline with { Info = baseline.Info with { PowerMw = Measurement<int>.Unavailable() } };
    }

    private static BatterySnapshot Build(
        string id, DateTimeOffset ts, BatteryState state, int powerMw, int voltageMv, int remainingMWh)
    {
        BatteryDevice device = new(
            id, "Test Battery", "Test Mfr", "SN", "LiP",
            Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
            Measurement<int>.Unavailable(), ReportsInMilliamps: false);

        BatteryInfo info = new()
        {
            BatteryId = id,
            TimestampUtc = ts,
            Percentage = Measurement<double>.Measured(50, MeasurementSource.WinRtBattery),
            State = Measurement<BatteryState>.Measured(state, MeasurementSource.WinRtBattery),
            AcOnline = Measurement<bool>.Measured(state == BatteryState.Charging, MeasurementSource.SystemPowerStatus),
            RemainingCapacityMWh = Measurement<int>.Measured(remainingMWh, MeasurementSource.WinRtBattery),
            FullChargeCapacityMWh = Measurement<int>.Measured(38_008, MeasurementSource.WinRtBattery),
            DesignCapacityMWh = Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
            RetentionPercent = Measurement<double>.Calculated(40),
            VoltageMv = Measurement<int>.Measured(voltageMv, MeasurementSource.Wmi),
            PowerMw = Measurement<int>.Measured(powerMw, MeasurementSource.WinRtBattery),
            CurrentMa = Measurement<double>.Calculated(powerMw / (double)voltageMv * 1000.0),
            CycleCount = Measurement<int>.Unavailable(),
            TemperatureCelsius = Measurement<double>.Unavailable(),
        };

        return new BatterySnapshot(device, info);
    }

    private sealed class FakeBatteryMonitoringService : IBatteryMonitoringService
    {
        public IReadOnlyList<BatterySnapshot> CurrentSnapshots { get; private set; } = [];

        public BatteryInfo? Aggregate => null;

        public CapabilitySnapshot? Capabilities => null;

        public string? LastError => null;

        public event EventHandler? Updated;

        public void Push(IReadOnlyList<BatterySnapshot> snapshots)
        {
            CurrentSnapshots = snapshots;
            Updated?.Invoke(this, EventArgs.Empty);
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSessionMonitoringService : ISessionMonitoringService
    {
        public BatterySessionInfo? CurrentSession => null;

        public ScreenState CurrentScreenState => ScreenState.On;

        public LockState CurrentLockState => LockState.Unlocked;

        public event EventHandler? Updated
        {
            add { }
            remove { }
        }

        public long? GetOpenSessionId(string batteryId) => null;

        public Task<IReadOnlyList<BatterySessionInfo>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BatterySessionInfo>>([]);

        public Task<IReadOnlyList<TimelineSegment>> GetTimelineAsync(
            DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TimelineSegment>>([]);
    }

    private sealed class FakePowerSampleWriteQueue : IPowerSampleWriteQueue
    {
        public List<PowerReading> Enqueued { get; } = [];

        public int PendingCount => 0;

        public DateTimeOffset? LastFlushUtc => null;

        public void Enqueue(BatteryDevice device, PowerReading reading) => Enqueued.Add(reading);

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
