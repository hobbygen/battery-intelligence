using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Diagnostics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Analytics;

/// <summary>
/// The application-level analytics orchestrator (docs/architecture.md section 5;
/// specification sections 16, 19, 53). On a slow cadence — plus promptly when a
/// session closes — it computes a Battery Health Score, appends a snapshot,
/// recomputes the degradation trend, and refreshes the qualifying insight set.
/// </summary>
/// <remarks>
/// Consumes only Core interfaces (`IAnalyticsReadStore`, `IHealthSnapshotStore`,
/// `IInsightStore`, `IInsightProvider`, `IBatteryMonitoringService`,
/// `ISessionMonitoringService`, `IRuntimeEstimationService`), so Analytics stays
/// an independent infrastructure sibling of Battery and Data.
/// </remarks>
public sealed class AnalyticsService : IAnalyticsService, IHostedService, IDisposable
{
    private static readonly TimeSpan RecentSessionWindow = TimeSpan.FromDays(60);
    private static readonly TimeSpan RetentionHistoryWindow = TimeSpan.FromDays(420);

    private readonly IBatteryMonitoringService _battery;
    private readonly ISessionMonitoringService _sessions;
    private readonly IRuntimeEstimationService _runtime;
    private readonly IAnalyticsReadStore _readStore;
    private readonly IHealthSnapshotStore _healthStore;
    private readonly IInsightStore _insightStore;
    private readonly IInsightProvider _insightProvider;
    private readonly ISettingsService _settings;
    private readonly ILogger<AnalyticsService> _logger;
    private readonly IMonitoringStatusRegistry _status;

    private readonly SemaphoreSlim _recomputeGate = new(1, 1);
    private Timer? _timer;
    private volatile bool _started;

    private HealthScore _currentHealth;
    private DegradationTrend _trend;
    private IReadOnlyList<AnalyticsInsight> _insights = [];

    public AnalyticsService(
        IBatteryMonitoringService battery,
        ISessionMonitoringService sessions,
        IRuntimeEstimationService runtime,
        IAnalyticsReadStore readStore,
        IHealthSnapshotStore healthStore,
        IInsightStore insightStore,
        IInsightProvider insightProvider,
        ISettingsService settings,
        ILogger<AnalyticsService> logger,
        IMonitoringStatusRegistry status)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(readStore);
        ArgumentNullException.ThrowIfNull(healthStore);
        ArgumentNullException.ThrowIfNull(insightStore);
        ArgumentNullException.ThrowIfNull(insightProvider);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);

        _battery = battery;
        _sessions = sessions;
        _runtime = runtime;
        _readStore = readStore;
        _healthStore = healthStore;
        _insightStore = insightStore;
        _insightProvider = insightProvider;
        _settings = settings;
        _logger = logger;
        _status = status;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _currentHealth = HealthScore.Unavailable(HealthScoreCalculator.Version, now, []);
        _trend = DegradationTrend.NotEnoughData(0, now, now);
    }

    /// <inheritdoc/>
    public HealthScore CurrentHealth => _currentHealth;

    /// <inheritdoc/>
    public DegradationTrend Trend => _trend;

    /// <inheritdoc/>
    public IReadOnlyList<AnalyticsInsight> Insights => _insights;

    /// <inheritdoc/>
    public string? LastError { get; private set; }

    /// <inheritdoc/>
    public event EventHandler? Updated;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _started = true;
        _sessions.Updated += OnSessionsUpdated;

        int minutes = Math.Max(5, _settings.Current.Analytics.HealthSnapshotIntervalMinutes);
        TimeSpan interval = TimeSpan.FromMinutes(minutes);
        _timer = new Timer(_ => _ = RecomputeAsync(CancellationToken.None), null, TimeSpan.FromSeconds(8), interval);

        // Load any persisted active insights so the UI is not empty until the first pass.
        _ = LoadActiveInsightsAsync();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        _sessions.Updated -= OnSessionsUpdated;
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => RecomputeAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<StatisticsSummary> GetStatisticsAsync(
        StatisticsWindow window, DateRange? range = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateRange resolved = StatisticsEngine.ResolveRange(window, now, TimeZoneInfo.Local, range);

        try
        {
            IReadOnlyList<BatterySessionInfo> sessions =
                await _readStore.GetSessionsAsync(resolved.FromUtc, cancellationToken).ConfigureAwait(false);
            return StatisticsEngine.Summarize(window, resolved, sessions, now);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Statistics summary failed for {Window}.", window);
            return StatisticsSummary.Empty(window, resolved.FromUtc, resolved.ToUtc);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RetentionPoint>> GetRetentionHistoryAsync(CancellationToken cancellationToken = default)
    {
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
            return [];
        }

        try
        {
            IReadOnlyList<HealthSnapshotRow> history = await _healthStore
                .GetHistoryAsync(primary.Device.HardwareId, DateTimeOffset.UtcNow - RetentionHistoryWindow, cancellationToken)
                .ConfigureAwait(false);

            return [.. history
                .Where(h => h.RetentionPercent is not null)
                .Select(h => new RetentionPoint(h.TimestampUtc, h.RetentionPercent!.Value))];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Retention history read failed.");
            return [];
        }
    }

    private void OnSessionsUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_started)
        {
            _ = RecomputeAsync(CancellationToken.None);
        }
    }

    private async Task LoadActiveInsightsAsync()
    {
        try
        {
            _insights = await _insightStore.GetActiveAsync().ConfigureAwait(false);
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load persisted insights.");
        }
    }

    private async Task RecomputeAsync(CancellationToken cancellationToken)
    {
        if (!await _recomputeGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
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

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string hardwareId = primary.Device.HardwareId;
            BatteryInfo info = primary.Info;

            IReadOnlyList<BatterySessionInfo> recentSessions =
                await _readStore.GetSessionsAsync(now - RecentSessionWindow, cancellationToken).ConfigureAwait(false);

            IReadOnlyList<HealthSnapshotRow> history =
                await _healthStore.GetHistoryAsync(hardwareId, now - RetentionHistoryWindow, cancellationToken).ConfigureAwait(false);

            AnalyticsSettingsView cfg = ReadConfig();

            // --- Degradation trend -----------------------------------------
            List<RetentionPoint> retentionPoints = [.. history
                .Where(h => h.RetentionPercent is not null)
                .Select(h => new RetentionPoint(h.TimestampUtc, h.RetentionPercent!.Value))];
            if (info.RetentionPercent is { HasValue: true } liveRetention)
            {
                retentionPoints.Add(new RetentionPoint(now, liveRetention.Value!.Value));
            }

            DegradationTrend trend = DegradationTrendCalculator.Compute(
                retentionPoints, cfg.DegradationMinSpanDays, minSamples: 8, now);

            // --- Health score --------------------------------------------
            double? thermalExposure = null;
            double? thermalFraction = null;
            try
            {
                int warnDeciKelvin = (int)Math.Round((_settings.Current.Alerts.HighTemperatureCelsius + 273.15) * 10.0);
                thermalExposure = await _readStore
                    .GetTemperatureExposureSecondsAsync(now - TimeSpan.FromDays(7), warnDeciKelvin, cancellationToken)
                    .ConfigureAwait(false);
                if (thermalExposure is double secs)
                {
                    thermalFraction = Math.Clamp(secs / TimeSpan.FromDays(7).TotalSeconds, 0, 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Temperature-exposure read failed; thermal factor omitted.");
            }

            (double? avgDod, double? chargeCov) = BehaviouralInputs(recentSessions);

            HealthScore health = HealthScoreCalculator.Compute(
                new HealthScoreInputs(
                    RetentionPercent: info.RetentionPercent.Value,
                    DegradationSlopePercentPerMonth: trend.IsAvailable ? trend.SlopePercentPerMonth : null,
                    CycleCount: info.CycleCount.HasValue ? info.CycleCount.Value : null,
                    Chemistry: primary.Device.Chemistry,
                    ThermalExposureFraction: thermalFraction,
                    AvgDepthOfDischargePercent: avgDod,
                    ChargeRateCoefficientOfVariation: chargeCov),
                now);

            try
            {
                await _healthStore.AppendAsync(
                    health,
                    hardwareId,
                    info.RetentionPercent.Value,
                    info.FullChargeCapacityMWh.HasValue ? info.FullChargeCapacityMWh.Value : null,
                    info.CycleCount.HasValue ? info.CycleCount.Value : null,
                    now,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist the health snapshot.");
            }

            // --- Insights -------------------------------------------------
            AnalyticsContext context = new(
                RecentSessions: recentSessions,
                Trend: trend,
                RecentDischarge: _runtime.RecentDischarge,
                TemperatureExposureSecondsAboveWarn: thermalExposure,
                WarnTemperatureCelsius: _settings.Current.Alerts.HighTemperatureCelsius,
                ConfidenceThreshold: cfg.InsightConfidenceThreshold,
                NowUtc: now);

            IReadOnlyList<AnalyticsInsight> insights;
            try
            {
                insights = await _insightProvider.GenerateAsync(context, cancellationToken).ConfigureAwait(false);
                await _insightStore.ReplaceCurrentAsync(insights, now, cancellationToken).ConfigureAwait(false);
                insights = await _insightStore.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Insight generation failed.");
                insights = _insights;
            }

            _currentHealth = health;
            _trend = trend;
            _insights = insights;
            LastError = null;
            _status.ReportSuccess(MonitoringComponent.Analytics);
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _status.ReportFailure(MonitoringComponent.Analytics, ex.Message);
            _logger.LogWarning(ex, "Analytics recompute failed.");
        }
        finally
        {
            _recomputeGate.Release();
        }
    }

    private static (double? AvgDepthOfDischargePercent, double? ChargeRateCoefficientOfVariation) BehaviouralInputs(
        IReadOnlyList<BatterySessionInfo> sessions)
    {
        List<double> dods = [];
        List<double> chargeRates = [];

        foreach (BatterySessionInfo s in sessions)
        {
            if (s.EndUtc is not DateTimeOffset end)
            {
                continue;
            }

            if (s.Type == SessionType.Discharging && s.EndPercentage is double ep)
            {
                dods.Add(Math.Clamp(100.0 - ep, 0, 100));
            }
            else if (s.Type == SessionType.Charging
                     && s.StartCapacityMwh is int sc && s.EndCapacityMwh is int ec && ec > sc)
            {
                double hours = (end - s.StartUtc).TotalHours;
                if (hours > 0.05)
                {
                    chargeRates.Add((ec - sc) / hours);
                }
            }
        }

        double? avgDod = dods.Count >= 3 ? Math.Round(LinearFit.Median(dods), 1) : null;
        double? cov = null;
        if (chargeRates.Count >= 3)
        {
            double mean = chargeRates.Average();
            cov = mean > 0 ? Math.Round(LinearFit.StandardDeviation(chargeRates) / mean, 3) : null;
        }

        return (avgDod, cov);
    }

    private AnalyticsSettingsView ReadConfig()
    {
        var a = _settings.Current.Analytics;
        return new AnalyticsSettingsView(a.InsightConfidenceThreshold, a.DegradationMinSpanDays, a.ChargingQualityMinSessions);
    }

    private readonly record struct AnalyticsSettingsView(double InsightConfidenceThreshold, int DegradationMinSpanDays, int ChargingQualityMinSessions);

    public void Dispose()
    {
        _timer?.Dispose();
        _recomputeGate.Dispose();
    }
}
