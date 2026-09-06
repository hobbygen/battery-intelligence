using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Core.Processes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.ProcessMonitoring;

/// <summary>
/// The application-level process-attribution orchestrator (docs/architecture.md
/// section 5; specification sections 15 and 55). It runs on its own timer — the
/// process sampler is the heaviest one (docs/monitoring-dataflow.md section 5) —
/// enumerating processes, computing delta CPU, grouping into applications,
/// separating the non-attributable baseline, running <c>AppEnergyV1</c>, and
/// persisting per-application rows through <see cref="IProcessSampleWriteQueue"/>.
/// </summary>
/// <remarks>
/// Consumes only Core interfaces, so ProcessMonitoring stays an independent
/// infrastructure sibling of Battery and Data. Attribution is system-wide (one
/// context), unlike the per-battery contexts in Power and Thermal — there is one
/// process tree regardless of battery count.
/// </remarks>
public sealed class ProcessMonitoringService : IProcessMonitoringService, IHostedService, IDisposable
{
    private static readonly TimeSpan BufferWindow = TimeSpan.FromHours(1);
    private const int MaxTicks = 800;

    /// <summary>A per-process CPU delta over a gap longer than this (a stall, a resume) is discarded rather than shown as a spike.</summary>
    private static readonly TimeSpan MaxDeltaInterval = TimeSpan.FromMinutes(5);

    private readonly IProcessEnumerator _enumerator;
    private readonly IBatteryMonitoringService _battery;
    private readonly ISessionMonitoringService _sessions;
    private readonly IProcessSampleWriteQueue _writeQueue;
    private readonly ISettingsService _settings;
    private readonly ILogger<ProcessMonitoringService> _logger;
    private readonly IMonitoringStatusRegistry _status;

    private readonly Lock _sync = new();
    private readonly Dictionary<(int Pid, long StartTicks), (TimeSpan Cpu, DateTimeOffset At)> _previousCpu = [];
    private readonly List<WindowTick> _ticks = [];

    private ProcessGroupingTable _grouping = ProcessGroupingTable.Default;
    private BaselineEstimator _baseline = new(4_000);
    private Timer? _timer;
    private volatile bool _started;

    private AppEnergyAttribution _current = AppEnergyAttribution.Empty(AppEnergyEstimator.Version, DateTimeOffset.UtcNow);

    private sealed record WindowTick(
        DateTimeOffset TimestampUtc,
        IReadOnlyList<AppActivity> Activities,
        int? TotalBudgetMw,
        double BaselineMw,
        AppEnergyConfidence Confidence);

    public ProcessMonitoringService(
        IProcessEnumerator enumerator,
        IBatteryMonitoringService battery,
        ISessionMonitoringService sessions,
        IProcessSampleWriteQueue writeQueue,
        ISettingsService settings,
        ILogger<ProcessMonitoringService> logger,
        IMonitoringStatusRegistry status)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(writeQueue);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _enumerator = enumerator;
        _battery = battery;
        _sessions = sessions;
        _writeQueue = writeQueue;
        _settings = settings;
        _logger = logger;
        _status = status;
    }

    /// <inheritdoc/>
    public AppEnergyAttribution CurrentAttribution => _current;

    /// <inheritdoc/>
    public bool AbsoluteAvailable => _current.AbsoluteAvailable;

    /// <inheritdoc/>
    public string EstimatorVersion => AppEnergyEstimator.Version;

    /// <inheritdoc/>
    public string? LastError { get; private set; }

    /// <inheritdoc/>
    public event EventHandler? Updated;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _grouping = ProcessGroupingTableLoader.Load(_logger);
        _baseline = new BaselineEstimator(_settings.Current.Processes.DefaultBaselineMw);

        int seconds = Math.Max(5, _settings.Current.Monitoring.ProcessSampleSeconds);
        TimeSpan interval = TimeSpan.FromSeconds(seconds);
        _started = true;
        _timer = new Timer(_ => SafeTick(), null, TimeSpan.FromSeconds(2), interval);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        SafeTick();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public AppEnergyAttribution GetRanking(ProcessWindow window)
    {
        lock (_sync)
        {
            // Anchored on the last sample, not wall-clock now: the ranking is
            // "as of the most recent tick", and the live buffer never reaches
            // further back than one hour anyway.
            DateTimeOffset now = _ticks.Count > 0 ? _ticks[^1].TimestampUtc : DateTimeOffset.UtcNow;
            DateTimeOffset earliest = now - BufferWindow;
            DateTimeOffset from = window == ProcessWindow.ThisSession
                && _sessions.CurrentSession?.StartUtc is DateTimeOffset start
                && start > earliest
                ? start
                : earliest;

            return BuildAttribution(from, now);
        }
    }

    /// <summary>Runs one sampling tick at <paramref name="now"/>. Exposed for simulation tests, which drive their own clock.</summary>
    public void Tick(DateTimeOffset now)
    {
        try
        {
            IReadOnlyList<ProcessRawSample> raw = _enumerator.Enumerate();
            bool published = Ingest(raw, now);
            LastError = null;
            _status.ReportSuccess(MonitoringComponent.ApplicationUsage);
            if (published)
            {
                Updated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _status.ReportFailure(MonitoringComponent.ApplicationUsage, ex.Message);
            _logger.LogWarning(ex, "Process sampling tick failed.");
        }
    }

    private void SafeTick()
    {
        if (_started)
        {
            Tick(DateTimeOffset.UtcNow);
        }
    }

    private bool Ingest(IReadOnlyList<ProcessRawSample> raw, DateTimeOffset now)
    {
        ProcessMonitoringSettings config = _settings.Current.Processes;
        int coreCount = Math.Max(1, _enumerator.CoreCount);

        List<(ProcessRawSample Sample, double CpuPercent)> perProcess = new(raw.Count);
        HashSet<(int, long)> live = [];
        double totalCpu = 0;
        bool coldStart;

        lock (_sync)
        {
            // The first tick after a (re)start has no previous cumulative times to
            // diff against, so every CPU delta would read zero. Record the
            // baseline and publish nothing — a fabricated all-idle ranking would
            // only dilute the next real one.
            coldStart = _previousCpu.Count == 0;

            foreach (ProcessRawSample sample in raw)
            {
                (int, long) key = sample.Key;
                live.Add(key);

                double cpuPercent = 0;
                if (_previousCpu.TryGetValue(key, out (TimeSpan Cpu, DateTimeOffset At) prev))
                {
                    TimeSpan wall = now - prev.At;
                    if (wall > TimeSpan.Zero && wall <= MaxDeltaInterval)
                    {
                        cpuPercent = ProcessCpuCalculator.Percent(sample.ProcessorTime - prev.Cpu, wall, coreCount);
                    }
                }

                _previousCpu[key] = (sample.ProcessorTime, now);
                perProcess.Add((sample, cpuPercent));
                totalCpu += cpuPercent;
            }

            PrunePrevious(live);
        }

        bool screenOn = _sessions.CurrentScreenState != ScreenState.Off;
        bool systemIdle = totalCpu < config.IdleCpuFloorPercent;

        int? totalBudgetMw = ResolveBudgetMw(out double batteryMagnitudeMw);
        if (batteryMagnitudeMw > 0)
        {
            _baseline.Observe(batteryMagnitudeMw, systemIdle, screenOn);
        }

        if (coldStart)
        {
            return false;
        }

        // Skip-when-idle: screen off and nothing working — the delta baseline is
        // already refreshed above; do not attribute or persist this cycle
        // (docs/monitoring-dataflow.md section 5, item 5).
        if (systemIdle && !screenOn)
        {
            return false;
        }

        (double baselineMw, AppEnergyConfidence confidence) = _baseline.Estimate(screenOn);

        IReadOnlyList<AppActivity> activities = GroupIntoApplications(perProcess);

        WindowTick tick = new(now, activities, totalBudgetMw, baselineMw, confidence);
        lock (_sync)
        {
            _ticks.Add(tick);
            TrimTicks(now);
            _current = BuildAttribution(now - BufferWindow, now);
        }

        Persist(now, _current, config);
        return true;
    }

    private int? ResolveBudgetMw(out double magnitudeMw)
    {
        magnitudeMw = 0;

        foreach (BatterySnapshot snapshot in _battery.CurrentSnapshots)
        {
            if (snapshot.Device.IsAggregate)
            {
                continue;
            }

            BatteryInfo info = snapshot.Info;
            bool discharging = info.State.Value == BatteryState.Discharging;
            if (discharging
                && info.PowerMw is { HasValue: true } power
                && power.Quality is DataQuality.Measured or DataQuality.Calculated)
            {
                magnitudeMw = Math.Abs(power.Value!.Value);
                return magnitudeMw > 0 ? (int)Math.Round(magnitudeMw) : null;
            }

            // Present but on AC / charging / idle — no battery draw to divide.
            return null;
        }

        return null;
    }

    private IReadOnlyList<AppActivity> GroupIntoApplications(
        IReadOnlyList<(ProcessRawSample Sample, double CpuPercent)> perProcess)
    {
        Dictionary<string, AppAccumulator> byKey = [];

        foreach ((ProcessRawSample sample, double cpuPercent) in perProcess)
        {
            (string key, string display) = ProcessGrouping.Resolve(sample, _grouping);
            if (!byKey.TryGetValue(key, out AppAccumulator? acc))
            {
                acc = new AppAccumulator(key, display);
                byKey[key] = acc;
            }

            acc.Add(sample, cpuPercent);
        }

        return [.. byKey.Values.Select(a => a.ToActivity())];
    }

    private AppEnergyAttribution BuildAttribution(DateTimeOffset from, DateTimeOffset to)
    {
        List<WindowTick> window = _ticks.Where(t => t.TimestampUtc >= from && t.TimestampUtc <= to).ToList();
        if (window.Count == 0)
        {
            return AppEnergyAttribution.Empty(AppEnergyEstimator.Version, DateTimeOffset.UtcNow);
        }

        WindowTick latest = window[^1];

        // CPU is averaged over the window; everything else reflects the latest
        // tick (current draw, current foreground, current process counts).
        Dictionary<string, (double CpuSum, int Count)> cpuByKey = [];
        foreach (WindowTick tick in window)
        {
            foreach (AppActivity activity in tick.Activities)
            {
                (double sum, int count) = cpuByKey.GetValueOrDefault(activity.ApplicationKey);
                cpuByKey[activity.ApplicationKey] = (sum + activity.CpuPercent, count + 1);
            }
        }

        List<AppActivity> averaged = [];
        foreach (AppActivity latestActivity in latest.Activities)
        {
            (double cpuSum, int count) = cpuByKey.GetValueOrDefault(latestActivity.ApplicationKey, (latestActivity.CpuPercent, 1));
            double avgCpu = count > 0 ? cpuSum / count : latestActivity.CpuPercent;
            averaged.Add(latestActivity with { CpuPercent = avgCpu });
        }

        AppEnergyWeights weights = AppEnergyWeights.FromSettings(_settings.Current.Processes);
        return AppEnergyEstimator.Estimate(
            weights,
            averaged,
            latest.TotalBudgetMw,
            latest.BaselineMw,
            latest.Confidence,
            _settings.Current.Processes.TopApplicationCount,
            latest.TimestampUtc);
    }

    private void Persist(DateTimeOffset now, AppEnergyAttribution attribution, ProcessMonitoringSettings config)
    {
        _ = config;

        long? sessionId = FirstOpenSessionId();

        List<ProcessSampleRecord> rows = new(attribution.Entries.Count + 1);
        foreach (AppUsageEntry entry in attribution.Entries)
        {
            rows.Add(new ProcessSampleRecord(
                ProcessId: 0,
                ProcessName: entry.DisplayName,
                ApplicationKey: entry.ApplicationKey,
                CpuPercent: entry.CpuPercent,
                MemoryBytes: entry.MemoryBytes == 0 ? null : entry.MemoryBytes,
                IsForeground: entry.IsForeground,
                EstimatedPowerMw: entry.EstimatedPowerMw,
                EstimatedSharePercent: entry.SharePercent));
        }

        if (attribution.Baseline.EstimatedPowerMw is int || attribution.AbsoluteAvailable)
        {
            AppUsageEntry baseline = attribution.Baseline;
            rows.Add(new ProcessSampleRecord(
                ProcessId: 0,
                ProcessName: baseline.DisplayName,
                ApplicationKey: baseline.ApplicationKey,
                CpuPercent: null,
                MemoryBytes: null,
                IsForeground: false,
                EstimatedPowerMw: baseline.EstimatedPowerMw,
                EstimatedSharePercent: baseline.SharePercent));
        }

        if (rows.Count == 0)
        {
            return;
        }

        _writeQueue.Enqueue(new ProcessSampleBatch(now, sessionId, attribution.EstimatorVersion, rows));
    }

    private long? FirstOpenSessionId()
    {
        foreach (BatterySnapshot snapshot in _battery.CurrentSnapshots)
        {
            if (snapshot.Device.IsAggregate)
            {
                continue;
            }

            long? id = _sessions.GetOpenSessionId(snapshot.Device.HardwareId);
            if (id is not null)
            {
                return id;
            }
        }

        return null;
    }

    private void PrunePrevious(HashSet<(int, long)> live)
    {
        if (_previousCpu.Count <= live.Count)
        {
            return;
        }

        List<(int, long)> dead = [.. _previousCpu.Keys.Where(k => !live.Contains(k))];
        foreach ((int, long) key in dead)
        {
            _previousCpu.Remove(key);
        }
    }

    private void TrimTicks(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - BufferWindow;
        _ticks.RemoveAll(t => t.TimestampUtc < cutoff);
        if (_ticks.Count > MaxTicks)
        {
            _ticks.RemoveRange(0, _ticks.Count - MaxTicks);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private sealed class AppAccumulator(string key, string displayName)
    {
        private double _cpu;
        private long _memory;
        private bool _foreground;
        private int _count;

        public void Add(ProcessRawSample sample, double cpuPercent)
        {
            _cpu += cpuPercent;
            _memory += Math.Max(0, sample.WorkingSetBytes);
            _foreground |= sample.IsForeground;
            _count++;
        }

        public AppActivity ToActivity() => new(
            key, displayName, _cpu, GpuPercent: 0, IoRate: 0, _foreground, _memory, _count);
    }
}
