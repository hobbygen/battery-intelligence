using BatteryIntelligence.Core.Alerts;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Notifications;

/// <summary>
/// The application-level alert orchestrator (docs/architecture.md section 5;
/// specification sections 20 and 21). It rides the battery, analytics and process
/// monitors, builds an <see cref="AlertEvaluationInput"/>, runs the pure
/// <see cref="AlertRuleEngine"/>, persists every fired alert, always raises it in
/// the in-app centre, and asks <see cref="INotificationPresenter"/> to show a
/// Windows toast — a toast failure degrades to in-app only, silently.
/// </summary>
/// <remarks>
/// Consumes only Core interfaces, so Notifications stays an independent
/// infrastructure sibling of Battery and Data.
/// </remarks>
public sealed class AlertMonitoringService : IAlertMonitoringService, IHostedService, IDisposable
{
    private const int RecentCap = 100;
    private static readonly TimeSpan MinEvaluationInterval = TimeSpan.FromSeconds(2);

    private readonly IBatteryMonitoringService _battery;
    private readonly IAnalyticsService _analytics;
    private readonly IProcessMonitoringService _processes;
    private readonly IRuntimeEstimationService _runtime;
    private readonly IAlertStore _alertStore;
    private readonly INotificationPresenter _presenter;
    private readonly ISettingsService _settings;
    private readonly ILogger<AlertMonitoringService> _logger;
    private readonly IMonitoringStatusRegistry _status;

    private readonly AlertRuleEngine _engine = new();
    private readonly SemaphoreSlim _evalGate = new(1, 1);
    private readonly Lock _sync = new();

    private volatile bool _started;
    private DateTimeOffset _lastEvaluation = DateTimeOffset.MinValue;
    private List<Alert> _recentAlerts = [];
    private int _unacknowledgedCount;

    public AlertMonitoringService(
        IBatteryMonitoringService battery,
        IAnalyticsService analytics,
        IProcessMonitoringService processes,
        IRuntimeEstimationService runtime,
        IAlertStore alertStore,
        INotificationPresenter presenter,
        ISettingsService settings,
        ILogger<AlertMonitoringService> logger,
        IMonitoringStatusRegistry status)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(analytics);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(alertStore);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _battery = battery;
        _analytics = analytics;
        _processes = processes;
        _runtime = runtime;
        _alertStore = alertStore;
        _presenter = presenter;
        _settings = settings;
        _logger = logger;
        _status = status;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Alert> RecentAlerts
    {
        get
        {
            lock (_sync)
            {
                return _recentAlerts;
            }
        }
    }

    /// <inheritdoc/>
    public int UnacknowledgedCount => _unacknowledgedCount;

    /// <inheritdoc/>
    public string? LastError { get; private set; }

    /// <inheritdoc/>
    public bool NotificationsAvailable
    {
        get
        {
            NotificationSettings n = _settings.Current.Notifications;
            return _presenter.IsAvailable && n.Enabled && n.UseWindowsNotifications;
        }
    }

    /// <inheritdoc/>
    public event EventHandler? Updated;

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _started = true;
        _battery.Updated += OnUpstreamUpdated;
        _analytics.Updated += OnUpstreamUpdated;
        _processes.Updated += OnUpstreamUpdated;

        try
        {
            IReadOnlyList<Alert> recent = await _alertStore.GetRecentAsync(RecentCap, cancellationToken).ConfigureAwait(false);
            int unacked = await _alertStore.GetUnacknowledgedCountAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _recentAlerts = [.. recent];
            }

            _unacknowledgedCount = unacked;
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load the alert history.");
        }

        _ = EvaluateAsync(force: true);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        _battery.Updated -= OnUpstreamUpdated;
        _analytics.Updated -= OnUpstreamUpdated;
        _processes.Updated -= OnUpstreamUpdated;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => EvaluateAsync(force: true);

    /// <inheritdoc/>
    public async Task AcknowledgeAsync(long alertId, CancellationToken cancellationToken = default)
    {
        await _alertStore.AcknowledgeAsync(alertId, cancellationToken).ConfigureAwait(false);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AcknowledgeAllAsync(CancellationToken cancellationToken = default)
    {
        await _alertStore.AcknowledgeAllAsync(cancellationToken).ConfigureAwait(false);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnUpstreamUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_started)
        {
            _ = EvaluateAsync(force: false);
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Alert> recent = await _alertStore.GetRecentAsync(RecentCap, cancellationToken).ConfigureAwait(false);
            int unacked = await _alertStore.GetUnacknowledgedCountAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _recentAlerts = [.. recent];
            }

            _unacknowledgedCount = unacked;
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reload alerts after an acknowledgement.");
        }
    }

    private async Task EvaluateAsync(bool force)
    {
        if (!await _evalGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            DateTimeOffset wallNow = DateTimeOffset.UtcNow;
            if (!force && wallNow - _lastEvaluation < MinEvaluationInterval)
            {
                return;
            }

            _lastEvaluation = wallNow;

            BatterySnapshot? primary = null;
            foreach (BatterySnapshot s in _battery.CurrentSnapshots)
            {
                if (!s.Device.IsAggregate)
                {
                    primary = s;
                    break;
                }
            }

            if (primary is null)
            {
                return;
            }

            // The engine's hysteresis and cooldown are timed off the reading, not
            // wall-clock, so the orchestrator stays deterministic under test —
            // in production the two are the same to the millisecond.
            DateTimeOffset now = primary.Info.TimestampUtc;
            AlertSettings alerts = _settings.Current.Alerts;
            NotificationSettings notifications = _settings.Current.Notifications;

            AlertEvaluationInput input = await BuildInputAsync(primary, now).ConfigureAwait(false);
            IReadOnlyList<Alert> fired = _engine.Evaluate(input, alerts, now);
            LastError = null;
            _status.ReportSuccess(MonitoringComponent.Alerts);

            if (fired.Count == 0)
            {
                return;
            }

            foreach (Alert alert in fired)
            {
                long id;
                try
                {
                    id = await _alertStore.InsertAsync(alert).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not persist a fired alert ({Type}).", alert.Type);
                    continue;
                }

                Alert stored = alert with { Id = id };
                lock (_sync)
                {
                    List<Alert> updated = new(_recentAlerts.Count + 1) { stored };
                    updated.AddRange(_recentAlerts);
                    if (updated.Count > RecentCap)
                    {
                        updated.RemoveRange(RecentCap, updated.Count - RecentCap);
                    }

                    _recentAlerts = updated;
                }

                Interlocked.Increment(ref _unacknowledgedCount);
                Updated?.Invoke(this, EventArgs.Empty);
                _logger.LogInformation("Alert fired: {Type} — {Title}", alert.Type, alert.Title);

                if (notifications.Enabled && notifications.UseWindowsNotifications)
                {
                    try
                    {
                        bool delivered = await _presenter.ShowAsync(stored, notifications.PlaySound).ConfigureAwait(false);
                        if (!delivered)
                        {
                            _logger.LogDebug("OS notification not delivered for {Type}; the in-app alert stands.", alert.Type);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Spec §21: a notification failure degrades to in-app without error.
                        _logger.LogDebug(ex, "OS notification failed for {Type}; the in-app alert stands.", alert.Type);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _status.ReportFailure(MonitoringComponent.Alerts, ex.Message);
            _logger.LogWarning(ex, "Alert evaluation failed.");
        }
        finally
        {
            _evalGate.Release();
        }
    }

    private async Task<AlertEvaluationInput> BuildInputAsync(BatterySnapshot primary, DateTimeOffset now)
    {
        BatteryInfo info = primary.Info;

        double? percentage = info.Percentage is { HasValue: true, Quality: not DataQuality.Suspect } p ? p.Value!.Value : null;
        BatteryState? state = info.State.Value;
        bool? acOnline = info.AcOnline.Value;
        double? temperature = info.TemperatureCelsius is { HasValue: true, Quality: not DataQuality.Suspect } t ? t.Value!.Value : null;

        double? dischargeRate = null;
        double? chargeRate = null;
        if (state == BatteryState.Discharging)
        {
            dischargeRate = _runtime.RecentDischarge.AvgRateMw
                ?? (info.PowerMw is { HasValue: true } dp ? Math.Abs(dp.Value!.Value) : (double?)null);
        }
        else if (state == BatteryState.Charging && info.PowerMw is { HasValue: true } cp)
        {
            chargeRate = Math.Abs(cp.Value!.Value);
        }

        // Personal baselines from the last 7 days, when history exists.
        double? baselineDischarge = null;
        double? baselineCharge = null;
        try
        {
            StatisticsSummary week = await _analytics.GetStatisticsAsync(StatisticsWindow.Last7Days).ConfigureAwait(false);
            baselineDischarge = week.AvgDischargeRateMw;
            baselineCharge = week.AvgChargeRateMw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Baseline statistics read failed; rate alerts will hold.");
        }

        DegradationTrend trend = _analytics.Trend;

        string? topApp = null;
        double? topShare = null;
        AppEnergyAttribution attribution = _processes.CurrentAttribution;
        if (attribution.AbsoluteAvailable)
        {
            AppUsageEntry? top = null;
            foreach (AppUsageEntry entry in attribution.Entries)
            {
                if (!entry.IsBaseline && !entry.IsOther)
                {
                    top = entry;
                    break;
                }
            }

            if (top is not null)
            {
                topApp = top.DisplayName;
                topShare = top.SharePercent;
            }
        }

        return new AlertEvaluationInput(
            PercentagePercent: percentage,
            State: state,
            AcOnline: acOnline,
            TemperatureCelsius: temperature,
            DischargeRateMw: dischargeRate,
            ChargeRateMw: chargeRate,
            BaselineDischargeRateMw: baselineDischarge,
            BaselineChargeRateMw: baselineCharge,
            HealthScore: _analytics.CurrentHealth.Score,
            DegradationSlopePercentPerMonth: trend.IsAvailable ? trend.SlopePercentPerMonth : null,
            TrendConfidence: trend.Confidence,
            TopAppDisplayName: topApp,
            TopAppSharePercent: topShare,
            NowUtc: now);
    }

    public void Dispose()
    {
        _evalGate.Dispose();
    }
}
