using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Monitoring;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Battery;

/// <summary>
/// The application-level battery monitoring orchestrator: polls
/// <see cref="IBatteryProvider"/> on a timer, refreshes immediately on a Windows
/// power notification, and holds the latest state for every consumer
/// (specification section 73, docs/architecture.md section 5).
/// </summary>
/// <remarks>
/// A singleton hosted service (docs/architecture.md section 4) — one polling
/// pipeline feeding every page, not one per ViewModel. This phase implements the
/// "Provider → Sampler" portion of the monitoring pipeline; validation beyond
/// what providers already apply, the ring buffer and the database sink arrive
/// with Phase 3.
/// </remarks>
public sealed class BatteryMonitoringService : IBatteryMonitoringService, IHostedService, IDisposable
{
    private readonly IBatteryProvider _provider;
    private readonly IBatteryCapabilityDetector _capabilityDetector;
    private readonly ISettingsService _settings;
    private readonly BatteryMessageWindow? _messageWindow;
    private readonly ILogger<BatteryMonitoringService> _logger;
    private readonly IMonitoringStatusRegistry _status;
    private readonly IAppVisibilityState _visibility;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _intervalLock = new();

    private Timer? _timer;
    private CancellationTokenSource? _stopping;
    private volatile IReadOnlyList<BatterySnapshot> _currentSnapshots = [];
    private volatile bool _started;
    private volatile bool _isAwake = true;
    private readonly Dictionary<string, double> _lastPercentageByBattery = [];

    private ScreenState _screenState = ScreenState.Unknown;
    private int _consecutiveFailures;
    private TimeSpan _currentInterval = TimeSpan.FromSeconds(5);

    public BatteryMonitoringService(
        IBatteryProvider provider,
        IBatteryCapabilityDetector capabilityDetector,
        ISettingsService settings,
        ILogger<BatteryMonitoringService> logger,
        IMonitoringStatusRegistry status,
        IAppVisibilityState visibility,
        BatteryMessageWindow? messageWindow = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(capabilityDetector);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(visibility);

        _provider = provider;
        _capabilityDetector = capabilityDetector;
        _settings = settings;
        _logger = logger;
        _status = status;
        _visibility = visibility;
        _messageWindow = messageWindow;
    }

    /// <summary>The battery/power sampling interval currently in effect (adaptive or backed-off). For tests and diagnostics.</summary>
    public TimeSpan CurrentInterval
    {
        get
        {
            lock (_intervalLock)
            {
                return _currentInterval;
            }
        }
    }

    public IReadOnlyList<BatterySnapshot> CurrentSnapshots => _currentSnapshots;

    public BatteryInfo? Aggregate { get; private set; }

    public CapabilitySnapshot? Capabilities { get; private set; }

    public string? LastError { get; private set; }

    public event EventHandler? Updated;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();

        if (_messageWindow is not null)
        {
            _messageWindow.NotificationReceived += OnPowerNotification;
            _messageWindow.Suspended += OnSuspended;
            _messageWindow.Resumed += OnResumed;
            _messageWindow.ScreenStateChanged += OnScreenStateChanged;
        }

        _visibility.Changed += OnAdaptiveSignalChanged;
        _settings.Changed += OnSettingsChanged;

        Capabilities = await SafeDetectCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

        await RefreshAsync(cancellationToken).ConfigureAwait(false);

        // Phase 5 tightened the verify cadence to the power-sample interval; Phase
        // 13 makes it adaptive (docs/monitoring-dataflow.md section 3). The timer
        // starts at whatever the current conditions resolve to and is re-armed by
        // RecomputeInterval whenever a signal changes.
        _timer = new Timer(OnTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        lock (_intervalLock)
        {
            // Force the first RecomputeInterval to arm the timer.
            _currentInterval = Timeout.InfiniteTimeSpan;
        }

        _started = true;
        RecomputeInterval();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        _stopping?.Cancel();

        if (_messageWindow is not null)
        {
            _messageWindow.NotificationReceived -= OnPowerNotification;
            _messageWindow.Suspended -= OnSuspended;
            _messageWindow.Resumed -= OnResumed;
            _messageWindow.ScreenStateChanged -= OnScreenStateChanged;
        }

        _visibility.Changed -= OnAdaptiveSignalChanged;
        _settings.Changed -= OnSettingsChanged;

        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    private void OnScreenStateChanged(object? sender, ScreenState state)
    {
        _screenState = state;
        RecomputeInterval();
    }

    private void OnAdaptiveSignalChanged(object? sender, EventArgs e) => RecomputeInterval();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => RecomputeInterval();

    /// <summary>
    /// Re-arms the timer from the adaptive-sampling policy plus any active retry
    /// back-off (docs/monitoring-dataflow.md sections 3 and 8). Called from every
    /// signal handler and after every read.
    /// </summary>
    private void RecomputeInterval()
    {
        if (!_started || _timer is null)
        {
            return;
        }

        MonitoringSettings monitoring = _settings.Current.Monitoring;
        TimeSpan baseInterval = TimeSpan.FromSeconds(
            Math.Max(5, Math.Min(monitoring.BatteryVerifySeconds, monitoring.PowerSampleSeconds)));
        TimeSpan baseProcess = TimeSpan.FromSeconds(Math.Max(5, monitoring.ProcessSampleSeconds));

        var conditions = new SamplingConditions(
            Screen: _screenState,
            PowerState: Aggregate?.State.Value ?? BatteryState.Unknown,
            BatteryPercent: Aggregate?.Percentage.Value,
            WindowVisible: _visibility.IsWindowVisible,
            ChargingSessionActive: (Aggregate?.State.Value ?? BatteryState.Unknown) == BatteryState.Charging,
            MonitoringPaused: monitoring.Paused,
            AdaptiveEnabled: monitoring.AdaptiveSampling);

        SamplingPlan plan = AdaptiveSamplingPolicy.Resolve(baseInterval, baseProcess, conditions);

        TimeSpan target = plan.AllStopped
            ? Timeout.InfiniteTimeSpan
            : MonitoringBackoff.NextInterval(plan.BatteryInterval, _consecutiveFailures);

        lock (_intervalLock)
        {
            if (target == _currentInterval)
            {
                return;
            }

            _currentInterval = target;
        }

        _timer?.Change(target == Timeout.InfiniteTimeSpan ? Timeout.InfiniteTimeSpan : target, target);
        if (target == Timeout.InfiniteTimeSpan)
        {
            _logger.LogInformation("Battery/power sampling stopped (monitoring paused).");
        }
        else
        {
            _logger.LogDebug("Battery/power sampling interval set to {Seconds:F1} s.", target.TotalSeconds);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // A refresh is already in flight (timer tick overlapping a manual
            // refresh, or two notifications close together) — not an error, and
            // starting a second concurrent read would only waste I/O.
            return;
        }

        try
        {
            IReadOnlyList<BatterySnapshot> raw = await _provider.GetSnapshotsAsync(cancellationToken).ConfigureAwait(false);

            // Validator stage (docs/monitoring-dataflow.md section 2): every
            // reading is plausibility-checked before it reaches the UI, the
            // aggregate, or (via IBatteryMonitoringService.Updated) the database
            // write queue. Implausible fields are re-graded Suspect, not dropped.
            IReadOnlyList<BatterySnapshot> snapshots = [.. raw.Select(s => s with
            {
                Info = ApplyPercentageJumpCheck(BatterySampleValidation.ApplyPlausibilityChecks(s.Info)),
            })];

            _currentSnapshots = snapshots;
            Aggregate = BatteryAggregation.Aggregate(snapshots, DateTimeOffset.UtcNow);
            LastError = null;
            _status.ReportSuccess(MonitoringComponent.Battery);
            _consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            // A read failure must not stop monitoring — the next timer tick or
            // notification tries again (specification section 44). Each further
            // failure widens the retry interval (docs/monitoring-dataflow.md §8).
            LastError = ex.Message;
            _status.ReportFailure(MonitoringComponent.Battery, ex.Message);
            _consecutiveFailures++;
            _logger.LogWarning(ex, "Battery read failed (attempt {Count}).", _consecutiveFailures);
        }
        finally
        {
            _refreshGate.Release();
        }

        // The reading may have crossed 20 % or reached Full, or a failure run may
        // have started or ended — re-arm the timer accordingly.
        RecomputeInterval();

        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-grades <see cref="BatteryInfo.Percentage"/> Suspect when it jumped
    /// implausibly while the system was awake (docs/monitoring-dataflow.md
    /// section 4). A jump while suspended is a normal outcome of however long
    /// the machine charged or drained, not a sensor fault, so it is never
    /// flagged — this is exactly why the check needs awake/suspended state and
    /// could not be implemented back in Phase 3.
    /// </summary>
    private BatteryInfo ApplyPercentageJumpCheck(BatteryInfo info)
    {
        double? previous = _lastPercentageByBattery.TryGetValue(info.BatteryId, out double p) ? p : null;
        double? current = info.Percentage.Value;

        if (current is double c)
        {
            _lastPercentageByBattery[info.BatteryId] = c;
        }

        if (!_isAwake || !PercentageJumpDetector.IsSuspiciousJump(previous, current))
        {
            return info;
        }

        return info with { Percentage = Measurement<double>.Suspect(current!.Value, info.Percentage.Source) };
    }

    private void OnSuspended(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _isAwake = false;
    }

    private void OnResumed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _isAwake = true;

        if (!_started)
        {
            return;
        }

        _logger.LogInformation("System resumed: re-enumerating battery devices and re-detecting capabilities.");

        // docs/session-engine.md section 5, "on resume": hardware may have
        // changed while suspended, and stale device handles are a classic
        // post-resume failure — re-detect rather than trust cached state.
        _ = SafeDetectCapabilitiesAsync(_stopping?.Token ?? CancellationToken.None)
            .ContinueWith(t => Capabilities = t.Result, TaskScheduler.Default);

        _ = RefreshAsync(_stopping?.Token ?? CancellationToken.None);
    }

    private async Task<CapabilitySnapshot?> SafeDetectCapabilitiesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _capabilityDetector.DetectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Capability detection failed at startup.");
            return null;
        }
    }

    private void OnTimerTick(object? state)
    {
        if (!_started || _stopping is { IsCancellationRequested: true } || _settings.Current.Monitoring.Paused)
        {
            return;
        }

        _ = RefreshAsync(_stopping?.Token ?? CancellationToken.None);
    }

    private void OnPowerNotification(object? sender, PowerNotificationKind kind)
    {
        if (!_started || _settings.Current.Monitoring.Paused)
        {
            return;
        }

        _logger.LogDebug("Power notification {Kind} triggered an immediate battery refresh.", kind);

        if (kind == PowerNotificationKind.DeviceChange)
        {
            // A device arrived or was removed: capabilities may have changed
            // (specification section 26 requires re-detection after such a
            // change), not just the reading.
            _ = SafeDetectCapabilitiesAsync(_stopping?.Token ?? CancellationToken.None)
                .ContinueWith(t => Capabilities = t.Result, TaskScheduler.Default);
        }

        _ = RefreshAsync(_stopping?.Token ?? CancellationToken.None);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _stopping?.Dispose();
        _refreshGate.Dispose();
    }
}
