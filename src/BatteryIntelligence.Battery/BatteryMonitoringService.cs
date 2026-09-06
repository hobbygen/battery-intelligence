using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
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
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private Timer? _timer;
    private CancellationTokenSource? _stopping;
    private volatile IReadOnlyList<BatterySnapshot> _currentSnapshots = [];
    private volatile bool _started;
    private volatile bool _isAwake = true;
    private readonly Dictionary<string, double> _lastPercentageByBattery = [];

    public BatteryMonitoringService(
        IBatteryProvider provider,
        IBatteryCapabilityDetector capabilityDetector,
        ISettingsService settings,
        ILogger<BatteryMonitoringService> logger,
        IMonitoringStatusRegistry status,
        BatteryMessageWindow? messageWindow = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(capabilityDetector);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _provider = provider;
        _capabilityDetector = capabilityDetector;
        _settings = settings;
        _logger = logger;
        _status = status;
        _messageWindow = messageWindow;
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
        }

        Capabilities = await SafeDetectCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

        await RefreshAsync(cancellationToken).ConfigureAwait(false);

        // Phase 5 tightens the verify cadence to the power-sample interval: the
        // Power subsystem reads its electrical quantities off this same pipeline
        // (no second electrical provider), and the session engine — fed from
        // Updated — needs the faster tick for its debounce timing to match
        // docs/session-engine.md's assumptions (docs/roadmap.md Phase 4 deviation
        // "self-corrects when Phase 5 ships").
        MonitoringSettings monitoring = _settings.Current.Monitoring;
        int intervalSeconds = Math.Max(5, Math.Min(monitoring.BatteryVerifySeconds, monitoring.PowerSampleSeconds));
        _timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(intervalSeconds), TimeSpan.FromSeconds(intervalSeconds));
        _started = true;
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
        }

        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
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
        }
        catch (Exception ex)
        {
            // A read failure must not stop monitoring — the next timer tick or
            // notification tries again (specification section 44).
            LastError = ex.Message;
            _status.ReportFailure(MonitoringComponent.Battery, ex.Message);
            _logger.LogWarning(ex, "Battery read failed.");
        }
        finally
        {
            _refreshGate.Release();
        }

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
        if (!_started || _stopping is { IsCancellationRequested: true })
        {
            return;
        }

        _ = RefreshAsync(_stopping?.Token ?? CancellationToken.None);
    }

    private void OnPowerNotification(object? sender, PowerNotificationKind kind)
    {
        if (!_started)
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
