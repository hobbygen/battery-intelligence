using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Power;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Core.Thermal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Thermal;

/// <summary>
/// The application-level temperature monitoring orchestrator (docs/architecture.md
/// section 5; specification section 14). Battery temperature is already resolved
/// (S4 → S3) inside the battery pipeline, so this service does not stand up a
/// provider of its own — like Phase 5's Power service it rides
/// <see cref="IBatteryMonitoringService.Updated"/>. It classifies each reading
/// into a band and a severity, keeps the bounded live series, the per-band time
/// accumulator and the min/max/avg accumulator, detects threshold events (60 s
/// dwell), and persists readings through <see cref="ITemperatureSampleWriteQueue"/>.
/// </summary>
/// <remarks>
/// Consumes only Core interfaces, so the Thermal project stays an independent
/// infrastructure sibling of Battery and Data (docs/architecture.md section 3).
/// On hardware with no battery temperature sensor — the reference machine —
/// <see cref="SensorAvailable"/> stays <see langword="false"/> and nothing is
/// persisted; the page renders the unavailable state.
/// </remarks>
public sealed class ThermalMonitoringService : ITemperatureMonitoringService, IHostedService, IDisposable
{
    private static readonly TimeSpan BufferWindow = TimeSpan.FromHours(1);
    private const int BufferPointCap = 2_000;
    private const int ChartPointBudget = 600;
    private const int MaxRecentEvents = 40;

    /// <summary>A gap longer than this between samples (sleep, a stalled sensor) does not count toward any band.</summary>
    private static readonly TimeSpan MaxBandInterval = TimeSpan.FromMinutes(2);

    private readonly IBatteryMonitoringService _battery;
    private readonly ISessionMonitoringService _sessions;
    private readonly ITemperatureSampleWriteQueue _writeQueue;
    private readonly ISettingsService _settings;
    private readonly ILogger<ThermalMonitoringService> _logger;
    private readonly IMonitoringStatusRegistry _status;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, BatteryThermalContext> _contexts = [];
    private volatile bool _started;

    private IReadOnlyList<TemperatureReading> _currentReadings = [];

    public ThermalMonitoringService(
        IBatteryMonitoringService battery,
        ISessionMonitoringService sessions,
        ITemperatureSampleWriteQueue writeQueue,
        ISettingsService settings,
        ILogger<ThermalMonitoringService> logger,
        IMonitoringStatusRegistry status)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(writeQueue);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _battery = battery;
        _sessions = sessions;
        _writeQueue = writeQueue;
        _settings = settings;
        _logger = logger;
        _status = status;
    }

    public IReadOnlyList<TemperatureReading> CurrentReadings => _currentReadings;

    public TemperatureReading? Primary => _currentReadings.Count > 0 ? _currentReadings[0] : null;

    public bool SensorAvailable { get; private set; }

    public TemperatureThresholds Thresholds =>
        TemperatureThresholds.FromWarnCelsius(_settings.Current.Alerts.HighTemperatureCelsius);

    public string? LastError { get; private set; }

    public event EventHandler? Updated;

    public IReadOnlyList<ThresholdEvent> RecentThresholdEvents
    {
        get
        {
            lock (_sync)
            {
                BatteryThermalContext? context = PrimaryContext();
                if (context is null)
                {
                    return [];
                }

                // The in-progress event (if any) leads, then the closed history.
                return context.EventDetector.OpenEvent is ThresholdEvent open
                    ? [open, .. context.RecentEvents]
                    : [.. context.RecentEvents];
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _battery.Updated += OnBatteryUpdated;
        _started = true;

        // Startup-race guard, mirroring PowerMonitoringService: hosted services
        // start sequentially, so whatever the battery monitor read before this
        // subscription existed must still be picked up.
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

    public MetricStatistics GetStatistics(TemperatureWindow window)
    {
        lock (_sync)
        {
            BatteryThermalContext? context = PrimaryContext();
            if (context is null)
            {
                return MetricStatistics.Empty;
            }

            (DateTimeOffset from, DateTimeOffset to) = ResolveWindow(window);
            return context.Stats.SnapshotBetween(from, to);
        }
    }

    public ChartSeries GetSeries(TemperatureWindow window)
    {
        lock (_sync)
        {
            BatteryThermalContext? context = PrimaryContext();
            (DateTimeOffset from, DateTimeOffset to) = ResolveWindow(window);

            if (context is null)
            {
                return ChartSeries.Empty("Temperature", "°C");
            }

            IReadOnlyList<TimePoint> pts = context.Series.PointsBetween(from, to);
            return new ChartSeries("Temperature", "°C", MinMaxDownsampler.Downsample(pts, ChartPointBudget));
        }
    }

    public IReadOnlyList<TemperatureBandDuration> GetBandBreakdown(TemperatureWindow window)
    {
        lock (_sync)
        {
            BatteryThermalContext? context = PrimaryContext();
            if (context is null)
            {
                return [];
            }

            (DateTimeOffset from, DateTimeOffset to) = ResolveWindow(window);
            return context.BandBreakdown(from, to, MaxBandInterval);
        }
    }

    private (DateTimeOffset From, DateTimeOffset To) ResolveWindow(TemperatureWindow window)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset earliest = now - BufferWindow;

        DateTimeOffset from = window switch
        {
            TemperatureWindow.Session => _sessions.CurrentSession?.StartUtc is DateTimeOffset start && start > earliest
                ? start
                : earliest,
            _ => earliest,
        };

        return (from < earliest ? earliest : from, now);
    }

    private BatteryThermalContext? PrimaryContext()
    {
        if (_currentReadings.Count > 0 && _contexts.TryGetValue(_currentReadings[0].BatteryId, out BatteryThermalContext? primary))
        {
            return primary;
        }

        foreach (BatteryThermalContext context in _contexts.Values)
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
        List<TemperatureReading> readings = [];
        bool sawSensor = false;
        string? tickError = null;

        lock (_sync)
        {
            TemperatureThresholds thresholds = Thresholds;

            foreach (BatterySnapshot snapshot in snapshots)
            {
                if (snapshot.Device.IsAggregate)
                {
                    continue;
                }

                try
                {
                    TemperatureReading reading = ProcessSnapshot(snapshot, thresholds);
                    readings.Add(reading);
                    sawSensor |= reading.TemperatureCelsius.HasValue;
                    LastError = null;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    tickError = ex.Message;
                    _logger.LogWarning(ex, "Temperature sampling failed for battery {BatteryId}.", snapshot.Device.HardwareId);
                }
            }

            if (readings.Count > 0)
            {
                _currentReadings = readings;
            }
        }

        if (sawSensor)
        {
            SensorAvailable = true;
        }
        else if (!SensorAvailable)
        {
            SensorAvailable = _battery.Capabilities?.Find(CapabilityId.Temperature)?.Available ?? false;
        }

        if (tickError is null)
        {
            _status.ReportSuccess(MonitoringComponent.Temperature);
        }
        else
        {
            _status.ReportFailure(MonitoringComponent.Temperature, tickError);
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private TemperatureReading ProcessSnapshot(BatterySnapshot snapshot, TemperatureThresholds thresholds)
    {
        BatteryInfo info = snapshot.Info;
        string batteryId = snapshot.Device.HardwareId;
        DateTimeOffset now = info.TimestampUtc;

        if (!_contexts.TryGetValue(batteryId, out BatteryThermalContext? context))
        {
            context = new BatteryThermalContext(BufferWindow, BufferPointCap);
            _contexts[batteryId] = context;
        }

        PowerDirection chargeContext = info.State.Value switch
        {
            BatteryState.Charging => PowerDirection.Charging,
            BatteryState.Discharging => PowerDirection.Discharging,
            BatteryState.Idle or BatteryState.Full => PowerDirection.Idle,
            _ => PowerDirection.Unknown,
        };

        Measurement<double> temperature = info.TemperatureCelsius;

        if (temperature is not { HasValue: true, Quality: not DataQuality.Suspect })
        {
            context.Series.Prune(now);
            context.Stats.Prune(now);

            return new TemperatureReading
            {
                BatteryId = batteryId,
                TimestampUtc = now,
                TemperatureCelsius = temperature,
                Band = TemperatureBand.Cool,
                Severity = TemperatureSeverity.Normal,
                ChargeContext = chargeContext,
            };
        }

        double celsius = temperature.Value!.Value;
        TemperatureBand band = TemperatureBandClassifier.Classify(celsius);
        TemperatureSeverity severity = thresholds.Classify(celsius);

        context.Series.Add(new TimePoint(now, celsius));
        context.Stats.Add(now, celsius, temperature.Quality);
        context.RecordBand(now, band);

        ThresholdEvent? closed = context.EventDetector.Observe(now, celsius, thresholds.WarnCelsius, chargeContext);
        if (closed is not null)
        {
            context.RecentEvents.Insert(0, closed);
            if (context.RecentEvents.Count > MaxRecentEvents)
            {
                context.RecentEvents.RemoveAt(context.RecentEvents.Count - 1);
            }
        }

        TemperatureReading reading = new()
        {
            BatteryId = batteryId,
            TimestampUtc = now,
            TemperatureCelsius = temperature,
            Band = band,
            Severity = severity,
            ChargeContext = chargeContext,
        };

        _writeQueue.Enqueue(snapshot.Device, reading);
        return reading;
    }

    public void Dispose()
    {
        // Nothing unmanaged; the event unsubscribe happens in StopAsync.
    }

    private sealed class BatteryThermalContext
    {
        private readonly List<(DateTimeOffset Timestamp, TemperatureBand Band)> _bandPoints = [];
        private readonly TimeSpan _window;

        public BatteryThermalContext(TimeSpan window, int pointCap)
        {
            _window = window;
            Series = new BoundedTimeSeries(window, pointCap);
            Stats = new RollingStatistics(window);
        }

        public BoundedTimeSeries Series { get; }

        public RollingStatistics Stats { get; }

        public ThresholdEventDetector EventDetector { get; } = new();

        public List<ThresholdEvent> RecentEvents { get; } = [];

        public void RecordBand(DateTimeOffset now, TemperatureBand band)
        {
            _bandPoints.Add((now, band));
            DateTimeOffset cutoff = now - _window;
            int drop = 0;
            while (drop < _bandPoints.Count - 1 && _bandPoints[drop + 1].Timestamp < cutoff)
            {
                drop++;
            }

            if (drop > 0)
            {
                _bandPoints.RemoveRange(0, drop);
            }
        }

        public IReadOnlyList<TemperatureBandDuration> BandBreakdown(
            DateTimeOffset from, DateTimeOffset to, TimeSpan maxInterval)
        {
            Dictionary<TemperatureBand, TimeSpan> totals = new()
            {
                [TemperatureBand.Cool] = TimeSpan.Zero,
                [TemperatureBand.Normal] = TimeSpan.Zero,
                [TemperatureBand.Warm] = TimeSpan.Zero,
                [TemperatureBand.Hot] = TimeSpan.Zero,
            };

            for (int i = 0; i < _bandPoints.Count; i++)
            {
                DateTimeOffset segmentStart = _bandPoints[i].Timestamp;
                DateTimeOffset segmentEnd = i + 1 < _bandPoints.Count ? _bandPoints[i + 1].Timestamp : to;

                DateTimeOffset clippedStart = segmentStart < from ? from : segmentStart;
                DateTimeOffset clippedEnd = segmentEnd > to ? to : segmentEnd;
                if (clippedEnd <= clippedStart)
                {
                    continue;
                }

                TimeSpan duration = clippedEnd - clippedStart;
                if (duration > maxInterval)
                {
                    duration = maxInterval;
                }

                totals[_bandPoints[i].Band] += duration;
            }

            return
            [
                new TemperatureBandDuration(TemperatureBand.Cool, totals[TemperatureBand.Cool]),
                new TemperatureBandDuration(TemperatureBand.Normal, totals[TemperatureBand.Normal]),
                new TemperatureBandDuration(TemperatureBand.Warm, totals[TemperatureBand.Warm]),
                new TemperatureBandDuration(TemperatureBand.Hot, totals[TemperatureBand.Hot]),
            ];
        }
    }
}
