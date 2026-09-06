using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>Shared fakes for the analytics simulation tests (docs/testing.md section 4).</summary>
internal sealed class AnalyticsFakeBattery : IBatteryMonitoringService
{
    public IReadOnlyList<BatterySnapshot> CurrentSnapshots { get; private set; } = [];

    public BatteryInfo? Aggregate => null;

    public CapabilitySnapshot? Capabilities => null;

    public string? LastError => null;

    public event EventHandler? Updated;

    public void Push(BatteryInfo info)
    {
        BatteryDevice device = new(
            "battery0", "Test Battery", "SMP", "SN", "LiP",
            Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
            Measurement<int>.Unavailable(), ReportsInMilliamps: false);
        CurrentSnapshots = [new BatterySnapshot(device, info)];
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public static BatteryInfo Discharging(DateTimeOffset ts, int powerMw, int remainingMwh, int? cycleCount = null, double retention = 82.0) => new()
    {
        BatteryId = "battery0",
        TimestampUtc = ts,
        Percentage = Measurement<double>.Measured(remainingMwh / 950.08, MeasurementSource.WinRtBattery),
        State = Measurement<BatteryState>.Measured(BatteryState.Discharging, MeasurementSource.WinRtBattery),
        AcOnline = Measurement<bool>.Measured(false, MeasurementSource.SystemPowerStatus),
        RemainingCapacityMWh = Measurement<int>.Measured(remainingMwh, MeasurementSource.WinRtBattery),
        FullChargeCapacityMWh = Measurement<int>.Measured(38_008, MeasurementSource.WinRtBattery),
        DesignCapacityMWh = Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
        RetentionPercent = Measurement<double>.Calculated(retention),
        VoltageMv = Measurement<int>.Measured(11_800, MeasurementSource.Wmi),
        PowerMw = Measurement<int>.Measured(-Math.Abs(powerMw), MeasurementSource.WinRtBattery),
        CurrentMa = Measurement<double>.Calculated(0),
        CycleCount = cycleCount is int c ? Measurement<int>.Measured(c, MeasurementSource.Wmi) : Measurement<int>.Unavailable(),
        TemperatureCelsius = Measurement<double>.Unavailable(),
    };

    public static BatteryInfo Charging(DateTimeOffset ts, int remainingMwh) => Discharging(ts, 0, remainingMwh) with
    {
        State = Measurement<BatteryState>.Measured(BatteryState.Charging, MeasurementSource.WinRtBattery),
        AcOnline = Measurement<bool>.Measured(true, MeasurementSource.SystemPowerStatus),
        PowerMw = Measurement<int>.Measured(12_000, MeasurementSource.WinRtBattery),
    };
}

internal sealed class AnalyticsFakeSessions : ISessionMonitoringService
{
    public BatterySessionInfo? CurrentSession { get; set; }

    public ScreenState CurrentScreenState { get; set; } = ScreenState.On;

    public LockState CurrentLockState => LockState.Unlocked;

    public event EventHandler? Updated;

    public void RaiseUpdated() => Updated?.Invoke(this, EventArgs.Empty);

    public long? GetOpenSessionId(string batteryId) => CurrentSession?.Id;

    public Task<IReadOnlyList<BatterySessionInfo>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BatterySessionInfo>>([]);

    public Task<IReadOnlyList<TimelineSegment>> GetTimelineAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimelineSegment>>([]);
}

internal sealed class AnalyticsFakeSettings : ISettingsService
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

internal sealed class FakeAnalyticsReadStore : IAnalyticsReadStore
{
    public List<BatterySessionInfo> Sessions { get; } = [];

    public double? TemperatureExposureSeconds { get; set; }

    public Task<IReadOnlyList<BatterySessionInfo>> GetSessionsAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BatterySessionInfo>>(
            [.. Sessions.Where(s => s.EndUtc is null || s.EndUtc >= fromUtc).OrderByDescending(s => s.StartUtc)]);

    public Task<IReadOnlyList<DailyStatRow>> GetDailyStatisticsAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DailyStatRow>>([]);

    public Task<double?> GetTemperatureExposureSecondsAsync(DateTimeOffset fromUtc, int warnDeciKelvin, CancellationToken cancellationToken = default) =>
        Task.FromResult(TemperatureExposureSeconds);
}

internal sealed class FakeHealthSnapshotStore : IHealthSnapshotStore
{
    public List<HealthSnapshotRow> Rows { get; } = [];

    public Task AppendAsync(HealthScore score, string batteryHardwareId, double? retentionPercent, int? fullChargeMwh, int? cycleCount,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        Rows.Add(new HealthSnapshotRow(nowUtc, retentionPercent, fullChargeMwh, cycleCount, score.Score, score.AlgorithmVersion));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HealthSnapshotRow>> GetHistoryAsync(string batteryHardwareId, DateTimeOffset fromUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HealthSnapshotRow>>([.. Rows.Where(r => r.TimestampUtc >= fromUtc).OrderBy(r => r.TimestampUtc)]);

    public Task<HealthSnapshotRow?> GetLatestAsync(string batteryHardwareId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Count > 0 ? Rows[^1] : null);
}

internal sealed class FakeInsightStore : IInsightStore
{
    public List<AnalyticsInsight> Active { get; private set; } = [];

    public Task ReplaceCurrentAsync(IReadOnlyList<AnalyticsInsight> insights, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        Active = [.. insights];
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AnalyticsInsight>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AnalyticsInsight>>(Active);

    public Task DismissAsync(long insightId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeRuntimeEstimationService : IRuntimeEstimationService
{
    public RuntimeEstimate Current { get; set; } = RuntimeEstimate.Calculating;

    public DischargeAnalysis RecentDischarge { get; set; } = DischargeAnalysis.Empty;

    public event EventHandler? Updated
    {
        add { }
        remove { }
    }
}
