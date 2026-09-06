using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Power;
using BatteryIntelligence.Core.Primitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Power;

/// <summary>
/// The application-level power monitoring orchestrator (docs/architecture.md
/// section 5; specification section 13). It rides the battery monitor's readings
/// — tightened to the power cadence in Phase 5 — resolves the energy rate down
/// <see cref="PowerEstimator"/>'s ladder, keeps the bounded live series and
/// min/max/avg accumulators the Power page binds to, and persists every reading
/// through <see cref="IPowerSampleWriteQueue"/>.
/// </summary>
/// <remarks>
/// Consumes only Core interfaces, so the Power project stays an independent
/// infrastructure sibling of Battery and Data (docs/architecture.md section 3).
/// </remarks>
public sealed class PowerMonitoringService : IPowerMonitoringService, IHostedService, IDisposable
{
    /// <summary>How far back the live buffer and the accumulators reach — the widest selectable window.</summary>
    private static readonly TimeSpan BufferWindow = TimeSpan.FromHours(1);

    /// <summary>Hard point cap per metric series. One hour at 5 s is ~720 points; this leaves headroom without unbounded growth.</summary>
    private const int BufferPointCap = 1_000;

    /// <summary>Chart point budget after min/max-preserving downsampling (docs/monitoring-dataflow.md section 7).</summary>
    private const int ChartPointBudget = 600;

    /// <summary>How much remaining-capacity history to retain for <see cref="PowerEstimator"/>'s rung 3.</summary>
    private static readonly TimeSpan CapacityHistoryWindow = TimeSpan.FromMinutes(5);

    private readonly IBatteryMonitoringService _battery;
    private readonly ISessionMonitoringService _sessions;
    private readonly IPowerSampleWriteQueue _writeQueue;
    private readonly ILogger<PowerMonitoringService> _logger;
    private readonly IMonitoringStatusRegistry _status;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, BatteryPowerContext> _contexts = [];
    private volatile bool _started;

    private IReadOnlyList<PowerReading> _currentReadings = [];

    public PowerMonitoringService(
        IBatteryMonitoringService battery,
        ISessionMonitoringService sessions,
        IPowerSampleWriteQueue writeQueue,
        ILogger<PowerMonitoringService> logger,
        IMonitoringStatusRegistry status)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(writeQueue);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _battery = battery;
        _sessions = sessions;
        _writeQueue = writeQueue;
        _logger = logger;
        _status = status;
    }

    public IReadOnlyList<PowerReading> CurrentReadings => _currentReadings;

    public PowerReading? Primary => _currentReadings.Count > 0 ? _currentReadings[0] : null;

    public string? LastError { get; private set; }

    public event EventHandler? Updated;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _battery.Updated += OnBatteryUpdated;
        _started = true;

        // Startup-race guard, mirroring BatteryPersistenceBridge / SessionMonitoringService:
        // hosted services start sequentially, so whatever the battery monitor read
        // before this subscription existed must still be picked up.
        Ingest(_battery.CurrentSnapshots);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        _battery.Updated -= OnBatteryUpdated;
        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        _battery.RefreshAsync(cancellationToken);

    public PowerWindowStatistics GetStatistics(PowerWindow window)
    {
        lock (_sync)
        {
            BatteryPowerContext? context = PrimaryContext();
            if (context is null)
            {
                return PowerWindowStatistics.Empty;
            }

            (DateTimeOffset from, DateTimeOffset to) = ResolveWindow(window);
            return new PowerWindowStatistics(
                context.PowerStats.SnapshotBetween(from, to),
                context.VoltageStats.SnapshotBetween(from, to),
                context.CurrentStats.SnapshotBetween(from, to));
        }
    }

    public PowerSeriesSet GetSeries(PowerWindow window)
    {
        lock (_sync)
        {
            BatteryPowerContext? context = PrimaryContext();
            (DateTimeOffset from, DateTimeOffset to) = ResolveWindow(window);

            if (context is null)
            {
                return PowerSeriesSet.Empty with { FromUtc = from, ToUtc = to };
            }

            return new PowerSeriesSet(
                BuildSeries("Power", "mW", context.PowerSeries, from, to),
                BuildSeries("Voltage", "mV", context.VoltageSeries, from, to),
                BuildSeries("Current", "mA", context.CurrentSeries, from, to),
                from,
                to);
        }
    }

    private static ChartSeries BuildSeries(
        string label, string unit, BoundedTimeSeries series, DateTimeOffset from, DateTimeOffset to)
    {
        IReadOnlyList<TimePoint> window = series.PointsBetween(from, to);
        return new ChartSeries(label, unit, MinMaxDownsampler.Downsample(window, ChartPointBudget));
    }

    private (DateTimeOffset From, DateTimeOffset To) ResolveWindow(PowerWindow window)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset earliest = now - BufferWindow;

        DateTimeOffset from = window switch
        {
            PowerWindow.OneMinute => now - TimeSpan.FromMinutes(1),
            PowerWindow.FiveMinutes => now - TimeSpan.FromMinutes(5),
            PowerWindow.FifteenMinutes => now - TimeSpan.FromMinutes(15),
            PowerWindow.OneHour => earliest,
            PowerWindow.Session => _sessions.CurrentSession?.StartUtc is DateTimeOffset start && start > earliest
                ? start
                : earliest,
            _ => earliest,
        };

        return (from < earliest ? earliest : from, now);
    }

    private BatteryPowerContext? PrimaryContext()
    {
        if (_currentReadings.Count > 0 && _contexts.TryGetValue(_currentReadings[0].BatteryId, out BatteryPowerContext? primary))
        {
            return primary;
        }

        foreach (BatteryPowerContext context in _contexts.Values)
        {
            return context;
        }

        return null;
    }

    private void OnBatteryUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (_started)
        {
            Ingest(_battery.CurrentSnapshots);
        }
    }

    private void Ingest(IReadOnlyList<BatterySnapshot> snapshots)
    {
        List<PowerReading> readings = [];
        string? tickError = null;

        lock (_sync)
        {
            foreach (BatterySnapshot snapshot in snapshots)
            {
                if (snapshot.Device.IsAggregate)
                {
                    continue;
                }

                try
                {
                    readings.Add(ProcessSnapshot(snapshot));
                    LastError = null;
                }
                catch (Exception ex)
                {
                    // One battery failing must not stop the others (specification section 44).
                    LastError = ex.Message;
                    tickError = ex.Message;
                    _logger.LogWarning(ex, "Power sampling failed for battery {BatteryId}.", snapshot.Device.HardwareId);
                }
            }

            if (readings.Count > 0)
            {
                _currentReadings = readings;
            }
        }

        if (tickError is null)
        {
            _status.ReportSuccess(MonitoringComponent.Power);
        }
        else
        {
            _status.ReportFailure(MonitoringComponent.Power, tickError);
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private PowerReading ProcessSnapshot(BatterySnapshot snapshot)
    {
        BatteryInfo info = snapshot.Info;
        string batteryId = snapshot.Device.HardwareId;
        DateTimeOffset now = info.TimestampUtc;

        if (!_contexts.TryGetValue(batteryId, out BatteryPowerContext? context))
        {
            context = new BatteryPowerContext(BufferWindow, BufferPointCap);
            _contexts[batteryId] = context;
        }

        // Remaining-capacity history feeds PowerEstimator rung 3.
        if (info.RemainingCapacityMWh is { HasValue: true, Quality: not DataQuality.Suspect } capacity)
        {
            context.CapacityHistory.Add(new TimePoint(now, capacity.Value!.Value));
            DateTimeOffset capacityCutoff = now - CapacityHistoryWindow;
            context.CapacityHistory.RemoveAll(p => p.TimestampUtc < capacityCutoff);
        }

        // The battery's CurrentMa is itself derived from power (C11), so it is not
        // an independent measurement and must not be offered to rung 2.
        Measurement<int> powerMw = PowerEstimator.Estimate(
            info.PowerMw, info.VoltageMv, Measurement<double>.Unavailable(), context.CapacityHistory, now);

        Measurement<int> voltageMv = info.VoltageMv;
        Measurement<double> currentMa = BatteryCalculations.CalculateCurrentMa(powerMw, voltageMv);
        PowerDirection direction = ClassifyDirection(info.State.Value, powerMw);

        PowerReading reading = new()
        {
            BatteryId = batteryId,
            TimestampUtc = now,
            PowerMw = powerMw,
            VoltageMv = voltageMv,
            CurrentMa = currentMa,
            Direction = direction,
        };

        context.Latest = reading;
        RecordMetric(context.PowerSeries, context.PowerStats, now, powerMw);
        RecordMetric(context.VoltageSeries, context.VoltageStats, now, voltageMv);
        RecordMetric(context.CurrentSeries, context.CurrentStats, now, currentMa);

        _writeQueue.Enqueue(snapshot.Device, reading);

        return reading;
    }

    private static void RecordMetric(
        BoundedTimeSeries series, RollingStatistics stats, DateTimeOffset now, Measurement<int> value)
    {
        if (value is { HasValue: true, Quality: not DataQuality.Suspect })
        {
            series.Add(new TimePoint(now, value.Value!.Value));
            stats.Add(now, value.Value!.Value, value.Quality);
        }
        else
        {
            series.Prune(now);
            stats.Prune(now);
        }
    }

    private static void RecordMetric(
        BoundedTimeSeries series, RollingStatistics stats, DateTimeOffset now, Measurement<double> value)
    {
        if (value is { HasValue: true, Quality: not DataQuality.Suspect })
        {
            series.Add(new TimePoint(now, value.Value!.Value));
            stats.Add(now, value.Value!.Value, value.Quality);
        }
        else
        {
            series.Prune(now);
            stats.Prune(now);
        }
    }

    private static PowerDirection ClassifyDirection(BatteryState? state, Measurement<int> powerMw)
    {
        if (powerMw.Value is int mw && powerMw.Quality != DataQuality.Suspect)
        {
            // A small dead-band keeps a near-zero trickle from flip-flopping.
            if (mw > 250)
            {
                return PowerDirection.Charging;
            }

            if (mw < -250)
            {
                return PowerDirection.Discharging;
            }

            return PowerDirection.Idle;
        }

        return state switch
        {
            BatteryState.Charging => PowerDirection.Charging,
            BatteryState.Discharging => PowerDirection.Discharging,
            BatteryState.Idle or BatteryState.Full => PowerDirection.Idle,
            _ => PowerDirection.Unknown,
        };
    }

    public void Dispose()
    {
        // Nothing unmanaged; the event unsubscribe happens in StopAsync.
    }

    private sealed class BatteryPowerContext
    {
        public BatteryPowerContext(TimeSpan window, int pointCap)
        {
            PowerSeries = new BoundedTimeSeries(window, pointCap);
            VoltageSeries = new BoundedTimeSeries(window, pointCap);
            CurrentSeries = new BoundedTimeSeries(window, pointCap);
            PowerStats = new RollingStatistics(window);
            VoltageStats = new RollingStatistics(window);
            CurrentStats = new RollingStatistics(window);
        }

        public BoundedTimeSeries PowerSeries { get; }

        public BoundedTimeSeries VoltageSeries { get; }

        public BoundedTimeSeries CurrentSeries { get; }

        public RollingStatistics PowerStats { get; }

        public RollingStatistics VoltageStats { get; }

        public RollingStatistics CurrentStats { get; }

        public List<TimePoint> CapacityHistory { get; } = [];

        public PowerReading? Latest { get; set; }
    }
}
