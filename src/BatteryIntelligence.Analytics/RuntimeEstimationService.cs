using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Analytics;

/// <summary>
/// The live remaining-runtime estimator (docs/estimation-strategy.md section 3;
/// specification sections 8 and 52). Rides <see cref="IBatteryMonitoringService.Updated"/>
/// — like <c>ThermalMonitoringService</c> — keeps a recency-weighted discharge
/// rate per screen state, and resolves it through <see cref="RuntimeEstimator"/>.
/// </summary>
/// <remarks>
/// Kept separate from <c>PowerMonitoringService</c> so the section 3 model stays
/// isolated and unit-testable and Power keeps one responsibility. Core-only
/// references, so Analytics stays an infrastructure sibling.
/// </remarks>
public sealed class RuntimeEstimationService : IRuntimeEstimationService, IHostedService, IDisposable
{
    private readonly IBatteryMonitoringService _battery;
    private readonly ISessionMonitoringService _sessions;
    private readonly ISettingsService _settings;
    private readonly ILogger<RuntimeEstimationService> _logger;

    private readonly Lock _sync = new();
    private RollingRate _rate;
    private volatile bool _started;

    private RuntimeEstimate _current = RuntimeEstimate.Calculating;
    private DischargeAnalysis _recentDischarge = DischargeAnalysis.Empty;

    public RuntimeEstimationService(
        IBatteryMonitoringService battery,
        ISessionMonitoringService sessions,
        ISettingsService settings,
        ILogger<RuntimeEstimationService> logger)
    {
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _battery = battery;
        _sessions = sessions;
        _settings = settings;
        _logger = logger;
        _rate = new RollingRate(TimeSpan.FromMinutes(15));
    }

    /// <inheritdoc/>
    public RuntimeEstimate Current => _current;

    /// <inheritdoc/>
    public DischargeAnalysis RecentDischarge => _recentDischarge;

    /// <inheritdoc/>
    public event EventHandler? Updated;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _rate = new RollingRate(TimeSpan.FromMinutes(Math.Max(2, _settings.Current.Analytics.RuntimeWindowMinutes)));
        _battery.Updated += OnBatteryUpdated;
        _started = true;
        Ingest(_battery.CurrentSnapshots);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        _battery.Updated -= OnBatteryUpdated;
        return Task.CompletedTask;
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
        BatterySnapshot? primary = null;
        foreach (BatterySnapshot s in snapshots)
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

        BatteryInfo info = primary.Info;
        DateTimeOffset now = info.TimestampUtc;
        ScreenState screen = _sessions.CurrentScreenState;

        lock (_sync)
        {
            if (info.State.Value == BatteryState.Discharging
                && info.PowerMw is { HasValue: true } power
                && power.Quality is DataQuality.Measured or DataQuality.Calculated)
            {
                _rate.Add(now, Math.Abs(power.Value!.Value), screen == ScreenState.Unknown ? ScreenState.On : screen);
            }
            else
            {
                _rate.Prune(now);
            }

            double? remaining = info.RemainingCapacityMWh is { HasValue: true, Quality: not DataQuality.Suspect } cap
                ? cap.Value!.Value
                : (double?)null;

            ScreenState effectiveScreen = screen == ScreenState.Unknown ? ScreenState.On : screen;
            TimeSpan spanCurrent = _rate.SpanFor(effectiveScreen);

            _current = RuntimeEstimator.Estimate(new RuntimeEstimatorInputs(
                RemainingCapacityMwh: remaining,
                BlendedRateMw: _rate.WeightedMeanMw(),
                ScreenOnRateMw: _rate.CountFor(ScreenState.On) > 0 ? _rate.WeightedMeanMw(ScreenState.On) : null,
                ScreenOffRateMw: _rate.CountFor(ScreenState.Off) > 0 ? _rate.WeightedMeanMw(ScreenState.Off) : null,
                DataSpanCurrentState: spanCurrent,
                RateCoefficientOfVariation: _rate.CoefficientOfVariation(effectiveScreen),
                // Roughly how many ~5-sample stretches of steady discharge we have observed.
                ComparablePeriods: Math.Clamp(_rate.Count / 5, 0, 10)));

            _recentDischarge = DischargeAnalyzer.Analyze(_rate);
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        // Nothing unmanaged; the unsubscribe happens in StopAsync.
    }
}
