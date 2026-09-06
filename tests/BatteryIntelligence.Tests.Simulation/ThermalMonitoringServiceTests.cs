using BatteryIntelligence.Battery.Simulation;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Thermal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// The thermal orchestrator over a scripted battery-reading stream
/// (docs/testing.md section 4). None of this can be verified on the reference
/// machine — it exposes no battery temperature sensor (risk R7) — so this
/// simulated path <em>is</em> the acceptance gate for Phase 6: a sensor-present
/// scenario must exercise band classification, the per-band breakdown, threshold
/// dwell detection and persistence.
/// </summary>
public sealed class ThermalMonitoringServiceTests
{
    [Fact]
    public async Task Ingest_NoTemperatureField_LeavesTheSensorUnavailable_AndPersistsNothing()
    {
        FakeBattery battery = new();
        FakeQueue queue = new();
        ThermalMonitoringService service = Create(battery, queue, warnCelsius: 45);
        await service.StartAsync(CancellationToken.None);

        // The reference machine's real configuration: every field present but temperature.
        battery.Push([Frame("battery0", DateTimeOffset.UtcNow, BatteryState.Discharging, temperatureCelsius: null)]);

        Assert.False(service.SensorAvailable);
        Assert.Empty(queue.Enqueued);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Ingest_RisingTemperatureWhileCharging_ClassifiesBands_DetectsAThresholdEvent_AndPersists()
    {
        FakeBattery battery = new();
        FakeQueue queue = new();
        ThermalMonitoringService service = Create(battery, queue, warnCelsius: 45);
        await service.StartAsync(CancellationToken.None);

        SimulatedBatteryProvider provider = new(BatterySimulationScenario.RisingTemperatureWhileCharging);

        // Play the scenario at 90-second spacing so the dwell (60 s) is crossed
        // once the reading holds above 45 °C. Backdated so the whole run is "now-ish".
        DateTimeOffset start = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(20);
        for (int step = 0; step < 8; step++)
        {
            IReadOnlyList<BatterySnapshot> snapshots = await provider.GetSnapshotsAsync();
            DateTimeOffset ts = start + TimeSpan.FromSeconds(step * 90);
            BatteryInfo info = snapshots[0].Info with { TimestampUtc = ts };
            battery.Push([snapshots[0] with { Info = info }]);
        }

        Assert.True(service.SensorAvailable);

        TemperatureReading primary = Assert.Single(service.CurrentReadings);
        Assert.True(primary.TemperatureCelsius.HasValue);
        Assert.Equal(PowerDirection.Charging, primary.ChargeContext);

        // Bands: the run passes through Normal (35–39), Warm (43) and Hot (46–48).
        IReadOnlyList<TemperatureBandDuration> bands = service.GetBandBreakdown(TemperatureWindow.OneHour);
        Assert.Contains(bands, b => b.Band == TemperatureBand.Warm && b.Duration > TimeSpan.Zero);
        Assert.Contains(bands, b => b.Band == TemperatureBand.Hot && b.Duration > TimeSpan.Zero);

        // A confirmed threshold event: > 60 s continuously at or above 45 °C.
        Assert.NotEmpty(service.RecentThresholdEvents);
        ThresholdEvent evt = service.RecentThresholdEvents[0];
        Assert.True(evt.PeakCelsius >= 47.0);
        Assert.Equal(PowerDirection.Charging, evt.Context);

        // Every real reading was handed to the write queue.
        Assert.NotEmpty(queue.Enqueued);
        Assert.All(queue.Enqueued, r => Assert.True(r.TemperatureCelsius.HasValue));

        await service.StopAsync(CancellationToken.None);
    }

    private static ThermalMonitoringService Create(FakeBattery battery, FakeQueue queue, int warnCelsius)
    {
        FakeSettings settings = new();
        settings.Current.Alerts.HighTemperatureCelsius = warnCelsius;
        return new ThermalMonitoringService(
            battery, new FakeSessions(), queue, settings, NullLogger<ThermalMonitoringService>.Instance, new MonitoringStatusRegistry());
    }

    private static BatterySnapshot Frame(string id, DateTimeOffset ts, BatteryState state, double? temperatureCelsius)
    {
        BatteryDevice device = new(
            id, "Test Battery", "Test Mfr", "SN", "LiP",
            Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
            Measurement<int>.Unavailable(), ReportsInMilliamps: false);

        BatteryInfo info = new()
        {
            BatteryId = id,
            TimestampUtc = ts,
            Percentage = Measurement<double>.Measured(60, MeasurementSource.WinRtBattery),
            State = Measurement<BatteryState>.Measured(state, MeasurementSource.WinRtBattery),
            AcOnline = Measurement<bool>.Measured(state == BatteryState.Charging, MeasurementSource.SystemPowerStatus),
            RemainingCapacityMWh = Measurement<int>.Measured(22_000, MeasurementSource.WinRtBattery),
            FullChargeCapacityMWh = Measurement<int>.Measured(38_008, MeasurementSource.WinRtBattery),
            DesignCapacityMWh = Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
            RetentionPercent = Measurement<double>.Calculated(40),
            VoltageMv = Measurement<int>.Measured(11_800, MeasurementSource.Wmi),
            PowerMw = Measurement<int>.Measured(state == BatteryState.Charging ? 9_500 : -6_000, MeasurementSource.WinRtBattery),
            CurrentMa = Measurement<double>.Calculated(0),
            CycleCount = Measurement<int>.Unavailable(),
            TemperatureCelsius = temperatureCelsius is double t
                ? Measurement<double>.Measured(t, MeasurementSource.Wmi)
                : Measurement<double>.Unavailable(),
        };

        return new BatterySnapshot(device, info);
    }

    private sealed class FakeBattery : IBatteryMonitoringService
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

    private sealed class FakeSessions : ISessionMonitoringService
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

    private sealed class FakeQueue : ITemperatureSampleWriteQueue
    {
        public List<TemperatureReading> Enqueued { get; } = [];

        public int PendingCount => 0;

        public DateTimeOffset? LastFlushUtc => null;

        public void Enqueue(BatteryDevice device, TemperatureReading reading) => Enqueued.Add(reading);

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<SettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> mutate, string? category = null, CancellationToken cancellationToken = default)
        {
            mutate(Current);
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
