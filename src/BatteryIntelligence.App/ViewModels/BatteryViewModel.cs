using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>One row of the "How this score is calculated" breakdown.</summary>
public sealed record HealthFactorRow(string Label, string WeightText, string ScoreText, string Basis, bool Contributed);

/// <summary>
/// Backs the Battery page and the Dashboard's battery card: identity, capacity,
/// health and live state for every present battery (specification section 8-9).
/// </summary>
/// <remarks>
/// Subscribes to the singleton <see cref="IBatteryMonitoringService"/> for the
/// life of the page. Because <see cref="Services.NavigationService"/> creates a
/// new page (and therefore a new transient ViewModel) on every navigation, this
/// type is <see cref="IDisposable"/> and must be disposed when its page unloads,
/// or the subscription would accumulate across repeated navigation.
/// </remarks>
public sealed partial class BatteryViewModel : ObservableObject, IDisposable
{
    private readonly IBatteryMonitoringService _monitoring;
    private readonly IAnalyticsService _analytics;
    private readonly IRuntimeEstimationService _runtime;
    private readonly DispatcherQueue _dispatcher;

    private bool _hasBattery;
    private string? _lastError;
    private IReadOnlyList<BatteryCardDisplay> _batteries = [];
    private BatteryCardDisplay? _aggregate;

    private bool _healthAvailable;
    private string _healthScoreText = "—";
    private string _healthCategoryText = "Calculating";
    private string _healthCategoryKey = "unknown";
    private IReadOnlyList<HealthFactorRow> _healthFactors = [];
    private string _degradationLine = "The 90-day degradation trend needs about a month of history.";
    private bool _degradationAvailable;
    private IReadOnlyList<double> _retentionSpark = [];
    private string _runtimeAtCurrent = "Calculating…";
    private string _runtimeScreenOn = "—";
    private string _runtimeScreenOff = "Not enough screen-off history yet";
    private string _runtimeConfidence = string.Empty;
    private bool _runtimeAvailable;

    public BatteryViewModel(
        IBatteryMonitoringService monitoring,
        IAnalyticsService analytics,
        IRuntimeEstimationService runtime)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(analytics);
        ArgumentNullException.ThrowIfNull(runtime);

        _monitoring = monitoring;
        _analytics = analytics;
        _runtime = runtime;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _monitoring.Updated += OnMonitoringUpdated;
        _analytics.Updated += OnAnalyticsUpdated;
        _runtime.Updated += OnRuntimeUpdated;
        ApplySnapshot();
        ApplyAnalytics();
        ApplyRuntime();
    }

    /// <summary>Whether a Battery Health Score is available (retention was computable).</summary>
    public bool HealthAvailable
    {
        get => _healthAvailable;
        private set
        {
            if (SetProperty(ref _healthAvailable, value))
            {
                OnPropertyChanged(nameof(HealthUnavailable));
            }
        }
    }

    public bool HealthUnavailable => !HealthAvailable;

    public string HealthScoreText
    {
        get => _healthScoreText;
        private set => SetProperty(ref _healthScoreText, value);
    }

    public string HealthCategoryText
    {
        get => _healthCategoryText;
        private set => SetProperty(ref _healthCategoryText, value);
    }

    /// <summary>"excellent" / "good" / "fair" / "poor" / "critical" — for a status-chip style selector.</summary>
    public string HealthCategoryKey
    {
        get => _healthCategoryKey;
        private set => SetProperty(ref _healthCategoryKey, value);
    }

    public IReadOnlyList<HealthFactorRow> HealthFactors
    {
        get => _healthFactors;
        private set => SetProperty(ref _healthFactors, value);
    }

    public string DegradationLine
    {
        get => _degradationLine;
        private set => SetProperty(ref _degradationLine, value);
    }

    public bool DegradationAvailable
    {
        get => _degradationAvailable;
        private set => SetProperty(ref _degradationAvailable, value);
    }

    public IReadOnlyList<double> RetentionSpark
    {
        get => _retentionSpark;
        private set
        {
            if (SetProperty(ref _retentionSpark, value))
            {
                OnPropertyChanged(nameof(HasRetentionSpark));
            }
        }
    }

    public bool HasRetentionSpark => _retentionSpark.Count >= 3;

    public string RuntimeAtCurrent
    {
        get => _runtimeAtCurrent;
        private set => SetProperty(ref _runtimeAtCurrent, value);
    }

    public string RuntimeScreenOn
    {
        get => _runtimeScreenOn;
        private set => SetProperty(ref _runtimeScreenOn, value);
    }

    public string RuntimeScreenOff
    {
        get => _runtimeScreenOff;
        private set => SetProperty(ref _runtimeScreenOff, value);
    }

    public string RuntimeConfidence
    {
        get => _runtimeConfidence;
        private set => SetProperty(ref _runtimeConfidence, value);
    }

    /// <summary>Whether a real runtime figure exists (not "Calculating…").</summary>
    public bool RuntimeAvailable
    {
        get => _runtimeAvailable;
        private set
        {
            if (SetProperty(ref _runtimeAvailable, value))
            {
                OnPropertyChanged(nameof(RuntimeCalculating));
            }
        }
    }

    public bool RuntimeCalculating => !RuntimeAvailable;

    /// <summary>Whether at least one battery is present. Drives the empty state.</summary>
    public bool HasBattery
    {
        get => _hasBattery;
        private set
        {
            if (SetProperty(ref _hasBattery, value))
            {
                OnPropertyChanged(nameof(NoBattery));
            }
        }
    }

    /// <summary>Inverse of <see cref="HasBattery"/>, for binding the empty state's visibility directly.</summary>
    public bool NoBattery => !HasBattery;

    /// <summary>The most recent monitoring failure, or <see langword="null"/> when the last read succeeded.</summary>
    public string? LastError
    {
        get => _lastError;
        private set
        {
            if (SetProperty(ref _lastError, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>Whether <see cref="LastError"/> is set, for binding a warning banner's visibility directly.</summary>
    public bool HasError => _lastError is not null;

    /// <summary>One display card per present battery device.</summary>
    public IReadOnlyList<BatteryCardDisplay> Batteries
    {
        get => _batteries;
        private set => SetProperty(ref _batteries, value);
    }

    /// <summary>Whether more than one battery is present, so the aggregate card is worth showing.</summary>
    public bool HasMultipleBatteries => Batteries.Count > 1;

    /// <summary>The system-wide aggregate, or <see langword="null"/> when zero or one battery is present.</summary>
    public BatteryCardDisplay? Aggregate
    {
        get => _aggregate;
        private set => SetProperty(ref _aggregate, value);
    }

    /// <summary>
    /// The single card the Dashboard's condensed battery summary shows: the
    /// aggregate when multiple batteries are present, otherwise the one battery.
    /// </summary>
    public BatteryCardDisplay? Primary => Aggregate ?? Batteries.FirstOrDefault();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _monitoring.RefreshAsync().ConfigureAwait(false);
        await _analytics.RefreshAsync().ConfigureAwait(false);
    }

    private void OnMonitoringUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        // Updated fires on the thread pool (IBatteryMonitoringService contract);
        // every bound property must change on the UI thread.
        _dispatcher.TryEnqueue(ApplySnapshot);
    }

    private void OnAnalyticsUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(ApplyAnalytics);
    }

    private void OnRuntimeUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(ApplyRuntime);
    }

    private void ApplyAnalytics()
    {
        HealthScore health = _analytics.CurrentHealth;
        HealthAvailable = health.IsAvailable;

        if (health.Score is double score)
        {
            HealthScoreText = score.ToString("F0", CultureInfo.InvariantCulture);
            HealthCategoryText = health.Category.ToString();
            HealthCategoryKey = health.Category.ToString().ToLowerInvariant();
        }
        else
        {
            HealthScoreText = "—";
            HealthCategoryText = "Unavailable";
            HealthCategoryKey = "unknown";
        }

        HealthFactors =
        [
            .. health.Factors.Select(f => new HealthFactorRow(
                f.Label,
                f.Contributed ? $"{f.NormalisedWeight * 100:F0}% weight" : "weight redistributed",
                f.Score01 is double s ? $"{s * 100:F0}/100" : "—",
                f.Basis,
                f.Contributed)),
        ];

        DegradationTrend trend = _analytics.Trend;
        DegradationAvailable = trend.IsAvailable;
        DegradationLine = trend.IsAvailable
            ? string.Create(CultureInfo.InvariantCulture,
                $"About {-(trend.SlopePercentPerMonth ?? 0):+0.0;-0.0;0.0} points/month over {(trend.ToUtc - trend.FromUtc).TotalDays:F0} days · projected {trend.ProjectedRetentionPercentIn90Days:F0}% in 90 days ({trend.Confidence} confidence).")
            : "The 90-day degradation trend needs about a month of health history — collecting it now.";

        _ = LoadRetentionSparkAsync();
    }

    private async Task LoadRetentionSparkAsync()
    {
        try
        {
            IReadOnlyList<RetentionPoint> points = await _analytics.GetRetentionHistoryAsync().ConfigureAwait(true);
            RetentionSpark = [.. points.Select(p => p.RetentionPercent)];
        }
        catch
        {
            RetentionSpark = [];
        }
    }

    private void ApplyRuntime()
    {
        RuntimeEstimate estimate = _runtime.Current;
        RuntimeAvailable = estimate.IsAvailable;

        if (!estimate.IsAvailable)
        {
            RuntimeAtCurrent = "Calculating…";
            RuntimeScreenOn = "—";
            RuntimeScreenOff = "—";
            RuntimeConfidence = "Not enough discharge history yet.";
            return;
        }

        RuntimeAtCurrent = FormatRuntime(estimate.AtCurrentUsage);
        RuntimeScreenOn = FormatRuntime(estimate.ScreenOn);
        RuntimeScreenOff = estimate.ScreenOff is TimeSpan off
            ? FormatRuntime(off)
            : "Not enough screen-off history yet";
        RuntimeConfidence = string.Create(CultureInfo.InvariantCulture, $"{estimate.Confidence} confidence · {estimate.Basis}");
    }

    private static string FormatRuntime(TimeSpan? span)
    {
        if (span is not TimeSpan t)
        {
            return "—";
        }

        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours} h {t.Minutes:D2} min")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes} min");
    }

    private void ApplySnapshot()
    {
        IReadOnlyList<BatterySnapshot> snapshots = _monitoring.CurrentSnapshots;

        HasBattery = snapshots.Count > 0;
        LastError = _monitoring.LastError;
        Batteries = [.. snapshots.Select(BatteryCardDisplay.From)];
        Aggregate = snapshots.Count > 1 && _monitoring.Aggregate is BatteryInfo aggregate
            ? BatteryCardDisplay.From(new BatterySnapshot(
                new Core.Models.BatteryDevice(
                    Core.Models.BatteryDevice.AggregateHardwareId, null, null, null, null,
                    aggregate.DesignCapacityMWh, Core.Primitives.Measurement<int>.Unavailable(), false),
                aggregate))
            : null;

        OnPropertyChanged(nameof(HasMultipleBatteries));
        OnPropertyChanged(nameof(Primary));
    }

    public void Dispose()
    {
        _monitoring.Updated -= OnMonitoringUpdated;
        _analytics.Updated -= OnAnalyticsUpdated;
        _runtime.Updated -= OnRuntimeUpdated;
    }
}
