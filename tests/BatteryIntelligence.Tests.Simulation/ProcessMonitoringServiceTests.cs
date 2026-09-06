using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.ProcessMonitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// The process-attribution orchestrator over a scripted process/battery stream
/// (docs/testing.md section 4). This simulated path is the acceptance gate for
/// R-050…R-054: top-N bounding, shares summing to the attributable budget with
/// the baseline separate, the permanent Estimated grade, the on-AC "ranking only"
/// state, skip-when-idle, and delta-CPU across ticks.
/// </summary>
public sealed class ProcessMonitoringServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnBattery_RanksApplications_SeparatesTheBaseline_AndPersistsEveryTick()
    {
        FakeEnumerator processes = new(coreCount: 8);
        FakeBattery battery = new();
        FakeQueue queue = new();
        ProcessMonitoringService service = Create(processes, battery, queue, new FakeSessions());

        battery.SetDischarging(powerMw: -18_000);

        // Tick 1 establishes the CPU baseline; tick 2 produces the first delta.
        processes.Set(
            Proc(100, "chrome", cpu: TimeSpan.Zero),
            Proc(200, "Code", cpu: TimeSpan.Zero),
            Proc(300, "spotify", cpu: TimeSpan.Zero));
        service.Tick(Start);

        processes.Set(
            Proc(100, "chrome", cpu: TimeSpan.FromSeconds(6), foreground: true),
            Proc(200, "Code", cpu: TimeSpan.FromSeconds(2)),
            Proc(300, "spotify", cpu: TimeSpan.FromSeconds(0.5)));
        service.Tick(Start.AddSeconds(10));

        AppEnergyAttribution attribution = service.CurrentAttribution;

        Assert.True(attribution.AbsoluteAvailable);
        Assert.Equal("AppEnergyV1", attribution.EstimatorVersion);
        Assert.NotEmpty(attribution.Entries);
        Assert.Equal("chrome", attribution.Entries[0].ApplicationKey);
        Assert.All(attribution.Entries, e => Assert.NotNull(e.EstimatedPowerMw));

        // Shares of the attributable budget sum to 100 %; baseline is a separate slice.
        Assert.Equal(100.0, attribution.Entries.Sum(e => e.SharePercent), 1);
        Assert.True(attribution.Baseline.IsBaseline);
        Assert.NotNull(attribution.Baseline.EstimatedPowerMw);

        // Every tick that produced a ranking was handed to the write queue.
        Assert.NotEmpty(queue.Batches);
        Assert.Contains(queue.Batches, b => b.Rows.Any(r => r.ApplicationKey == "chrome"));
        Assert.Contains(queue.Batches, b => b.Rows.Any(r => r.ApplicationKey == "__baseline__"));
    }

    [Fact]
    public void OnAc_RanksApplications_ButReportsAbsolutePowerAsUnavailable()
    {
        FakeEnumerator processes = new(coreCount: 8);
        FakeBattery battery = new();
        FakeQueue queue = new();
        ProcessMonitoringService service = Create(processes, battery, queue, new FakeSessions());

        battery.SetCharging();

        processes.Set(Proc(100, "chrome", TimeSpan.Zero), Proc(200, "Code", TimeSpan.Zero));
        service.Tick(Start);
        processes.Set(
            Proc(100, "chrome", TimeSpan.FromSeconds(5)),
            Proc(200, "Code", TimeSpan.FromSeconds(1)));
        service.Tick(Start.AddSeconds(10));

        AppEnergyAttribution attribution = service.CurrentAttribution;

        Assert.False(attribution.AbsoluteAvailable);
        Assert.Null(attribution.TotalBudgetMw);
        Assert.NotEmpty(attribution.Entries);
        Assert.Equal("chrome", attribution.Entries[0].ApplicationKey);
        Assert.All(attribution.Entries, e => Assert.Null(e.EstimatedPowerMw));
    }

    [Fact]
    public void ScreenOffAndIdle_SkipsTheCycle_PersistingNothing()
    {
        FakeEnumerator processes = new(coreCount: 8);
        FakeBattery battery = new();
        FakeQueue queue = new();
        FakeSessions sessions = new() { CurrentScreenState = ScreenState.Off };
        ProcessMonitoringService service = Create(processes, battery, queue, sessions);

        battery.SetDischarging(-9_000);

        // No CPU movement at all, screen off → below the idle floor.
        processes.Set(Proc(100, "chrome", TimeSpan.Zero), Proc(200, "Code", TimeSpan.Zero));
        service.Tick(Start);
        service.Tick(Start.AddSeconds(10));

        Assert.Empty(queue.Batches);
        Assert.False(service.CurrentAttribution.HasEntries);
    }

    [Fact]
    public void BeyondTheTopN_ApplicationsCollapseIntoOne_OtherRow()
    {
        FakeEnumerator processes = new(coreCount: 16);
        FakeBattery battery = new();
        FakeQueue queue = new();
        ProcessMonitoringService service = Create(processes, battery, queue, new FakeSessions(), topApplicationCount: 3);

        battery.SetDischarging(-20_000);

        ProcessRawSample[] zero = Enumerable.Range(0, 12).Select(i => Proc(1000 + i, $"app{i}", TimeSpan.Zero)).ToArray();
        processes.Set(zero);
        service.Tick(Start);

        ProcessRawSample[] busy = Enumerable.Range(0, 12)
            .Select(i => Proc(1000 + i, $"app{i}", TimeSpan.FromSeconds(12 - i)))
            .ToArray();
        processes.Set(busy);
        service.Tick(Start.AddSeconds(10));

        AppEnergyAttribution attribution = service.CurrentAttribution;
        Assert.Equal(4, attribution.Entries.Count);        // 3 ranked + Other
        Assert.Equal(1, attribution.Entries.Count(e => e.IsOther));
        Assert.True(attribution.Entries[^1].IsOther);
        Assert.Equal(100.0, attribution.Entries.Sum(e => e.SharePercent), 1);
    }

    [Fact]
    public void CpuPercent_RisesAcrossTicks_AsCumulativeProcessorTimeAdvances()
    {
        FakeEnumerator processes = new(coreCount: 4);
        FakeBattery battery = new();
        ProcessMonitoringService service = Create(processes, battery, new FakeQueue(), new FakeSessions());
        battery.SetDischarging(-10_000);

        processes.Set(Proc(100, "chrome", TimeSpan.Zero));
        service.Tick(Start);

        // 2 s of CPU over a 10 s wall interval → 20 % of one core.
        processes.Set(Proc(100, "chrome", TimeSpan.FromSeconds(2)));
        service.Tick(Start.AddSeconds(10));
        double first = service.CurrentAttribution.Entries.Single(e => e.ApplicationKey == "chrome").CpuPercent;

        // 8 s of CPU over the next 10 s → 80 %; the windowed average then rises.
        processes.Set(Proc(100, "chrome", TimeSpan.FromSeconds(10)));
        service.Tick(Start.AddSeconds(20));
        double second = service.GetRanking(ProcessWindow.LastHour).Entries.Single(e => e.ApplicationKey == "chrome").CpuPercent;

        Assert.InRange(first, 18.0, 22.0);
        Assert.True(second > first);
    }

    [Fact]
    public void Tick_WhenTheEnumeratorFailsRepeatedly_DrivesTheComponentDegraded_ThenRecovers()
    {
        FakeEnumerator processes = new(coreCount: 4) { ThrowOnEnumerate = true };
        MonitoringStatusRegistry registry = new();
        ProcessMonitoringService service = Create(processes, new FakeBattery(), new FakeQueue(), new FakeSessions(), registry);

        for (int i = 0; i < MonitoringStatusRegistry.DegradedThreshold; i++)
        {
            service.Tick(Start.AddSeconds(10 * (i + 1)));
        }

        MonitoringStatus appUsage = registry.Snapshot().Single(s => s.Component == MonitoringComponent.ApplicationUsage);
        Assert.Equal(MonitoringHealth.Degraded, appUsage.Health);
        Assert.NotNull(appUsage.LastError);

        // Failure isolation: no other subsystem is affected (specification section 44).
        Assert.All(
            registry.Snapshot().Where(s => s.Component != MonitoringComponent.ApplicationUsage),
            s => Assert.Equal(MonitoringHealth.Starting, s.Health));

        processes.ThrowOnEnumerate = false;
        processes.Set(Proc(100, "chrome", TimeSpan.FromSeconds(1)));
        service.Tick(Start.AddSeconds(100));

        Assert.Equal(
            MonitoringHealth.Healthy,
            registry.Snapshot().Single(s => s.Component == MonitoringComponent.ApplicationUsage).Health);
    }

    private static ProcessMonitoringService Create(
        FakeEnumerator processes, FakeBattery battery, FakeQueue queue, FakeSessions sessions, int topApplicationCount = 40) =>
        Create(processes, battery, queue, sessions, new MonitoringStatusRegistry(), topApplicationCount);

    private static ProcessMonitoringService Create(
        FakeEnumerator processes, FakeBattery battery, FakeQueue queue, FakeSessions sessions, MonitoringStatusRegistry registry, int topApplicationCount = 40)
    {
        FakeSettings settings = new();
        settings.Current.Processes.TopApplicationCount = topApplicationCount;
        settings.Current.Processes.IdleCpuFloorPercent = 3.0;
        return new ProcessMonitoringService(
            processes, battery, sessions, queue, settings, NullLogger<ProcessMonitoringService>.Instance, registry);
    }

    private static ProcessRawSample Proc(int pid, string name, TimeSpan cpu, bool foreground = false) => new(
        ProcessId: pid,
        StartTimeUtc: DateTimeOffset.UnixEpoch,
        ProcessName: name,
        ExecutablePath: null,
        ProcessorTime: cpu,
        WorkingSetBytes: 128 * 1024 * 1024,
        IsForeground: foreground);

    private sealed class FakeEnumerator(int coreCount) : IProcessEnumerator
    {
        private IReadOnlyList<ProcessRawSample> _current = [];

        public int CoreCount { get; } = coreCount;

        public bool ThrowOnEnumerate { get; set; }

        public void Set(params ProcessRawSample[] samples) => _current = samples;

        public IReadOnlyList<ProcessRawSample> Enumerate() =>
            ThrowOnEnumerate ? throw new InvalidOperationException("enumeration failed") : _current;
    }

    private sealed class FakeQueue : IProcessSampleWriteQueue
    {
        public List<ProcessSampleBatch> Batches { get; } = [];

        public int PendingCount => 0;

        public DateTimeOffset? LastFlushUtc => null;

        public void Enqueue(ProcessSampleBatch batch) => Batches.Add(batch);

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBattery : IBatteryMonitoringService
    {
        public IReadOnlyList<BatterySnapshot> CurrentSnapshots { get; private set; } = [];

        public BatteryInfo? Aggregate => null;

        public CapabilitySnapshot? Capabilities => null;

        public string? LastError => null;

        public event EventHandler? Updated;

        public void SetDischarging(int powerMw) => Set(BatteryState.Discharging, powerMw, acOnline: false);

        public void SetCharging() => Set(BatteryState.Charging, 12_000, acOnline: true);

        private void Set(BatteryState state, int powerMw, bool acOnline)
        {
            BatteryDevice device = new(
                "battery0", "Test Battery", "Test Mfr", "SN", "LiP",
                Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
                Measurement<int>.Unavailable(), ReportsInMilliamps: false);

            BatteryInfo info = new()
            {
                BatteryId = "battery0",
                TimestampUtc = DateTimeOffset.UtcNow,
                Percentage = Measurement<double>.Measured(60, MeasurementSource.WinRtBattery),
                State = Measurement<BatteryState>.Measured(state, MeasurementSource.WinRtBattery),
                AcOnline = Measurement<bool>.Measured(acOnline, MeasurementSource.SystemPowerStatus),
                RemainingCapacityMWh = Measurement<int>.Measured(22_000, MeasurementSource.WinRtBattery),
                FullChargeCapacityMWh = Measurement<int>.Measured(38_008, MeasurementSource.WinRtBattery),
                DesignCapacityMWh = Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
                RetentionPercent = Measurement<double>.Calculated(40),
                VoltageMv = Measurement<int>.Measured(11_800, MeasurementSource.Wmi),
                PowerMw = Measurement<int>.Measured(powerMw, MeasurementSource.WinRtBattery),
                CurrentMa = Measurement<double>.Calculated(0),
                CycleCount = Measurement<int>.Unavailable(),
                TemperatureCelsius = Measurement<double>.Unavailable(),
            };

            CurrentSnapshots = [new BatterySnapshot(device, info)];
            Updated?.Invoke(this, EventArgs.Empty);
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSessions : ISessionMonitoringService
    {
        public BatterySessionInfo? CurrentSession => null;

        public ScreenState CurrentScreenState { get; set; } = ScreenState.On;

        public LockState CurrentLockState => LockState.Unlocked;

        public event EventHandler? Updated
        {
            add { }
            remove { }
        }

        public long? GetOpenSessionId(string batteryId) => 42;

        public Task<IReadOnlyList<BatterySessionInfo>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BatterySessionInfo>>([]);

        public Task<IReadOnlyList<TimelineSegment>> GetTimelineAsync(
            DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TimelineSegment>>([]);
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
