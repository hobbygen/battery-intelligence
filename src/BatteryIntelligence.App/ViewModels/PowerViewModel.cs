using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>A selectable chart/statistics window on the Power page.</summary>
/// <param name="Label">What the selector shows.</param>
/// <param name="Value">The underlying window.</param>
public sealed record PowerWindowOption(string Label, PowerWindow Value);

/// <summary>Min / max / average of one metric, formatted for the stats strip.</summary>
/// <param name="Title">Metric name.</param>
/// <param name="Live">The current live value.</param>
/// <param name="Min">Smallest over the window, or the "not enough data" text.</param>
/// <param name="Max">Largest over the window.</param>
/// <param name="Average">Mean over the window.</param>
/// <param name="RequiresBadge">Whether the live value carries a grade badge.</param>
/// <param name="BadgeText">The badge label.</param>
public sealed record PowerMetricDisplay(
    string Title,
    string Live,
    string Min,
    string Max,
    string Average,
    bool RequiresBadge,
    string BadgeText);

/// <summary>
/// Backs the Power page: live current / voltage / power, min/max/avg over a
/// selectable window, and three synchronised chart series
/// (docs/ui-navigation.md section 2; specification section 13).
/// </summary>
/// <remarks>
/// Subscribes to the singleton <see cref="IPowerMonitoringService"/> for the life
/// of the page — see <see cref="BatteryViewModel"/> for why this is
/// <see cref="IDisposable"/>. Numeric readouts are coalesced to at most 1 Hz
/// regardless of the 5-second sampling rate (docs/monitoring-dataflow.md
/// section 7).
/// </remarks>
public sealed partial class PowerViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(1);

    private static readonly IReadOnlyList<PowerWindowOption> Windows =
    [
        new("1 minute", PowerWindow.OneMinute),
        new("5 minutes", PowerWindow.FiveMinutes),
        new("15 minutes", PowerWindow.FifteenMinutes),
        new("1 hour", PowerWindow.OneHour),
        new("This session", PowerWindow.Session),
    ];

    private readonly IPowerMonitoringService _power;
    private readonly IBatteryMonitoringService _battery;
    private readonly DispatcherQueue _dispatcher;

    private DateTimeOffset _lastApplied = DateTimeOffset.MinValue;
    private bool _refreshQueued;

    private bool _hasBattery;
    private bool _hasData;
    private string? _lastError;
    private PowerWindowOption _selectedWindow = Windows[1];
    private PowerMetricDisplay _powerMetric = EmptyMetric("Power");
    private PowerMetricDisplay _voltageMetric = EmptyMetric("Voltage");
    private PowerMetricDisplay _currentMetric = EmptyMetric("Current");
    private string _direction = "—";
    private PowerSeriesSet _series = PowerSeriesSet.Empty;
    private int _selectedMetricTab;

    public PowerViewModel(IPowerMonitoringService power, IBatteryMonitoringService battery)
    {
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(battery);

        _power = power;
        _battery = battery;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _power.Updated += OnPowerUpdated;
        ApplySnapshot();
    }

    public IReadOnlyList<PowerWindowOption> WindowOptions => Windows;

    public PowerWindowOption SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (value is not null && SetProperty(ref _selectedWindow, value))
            {
                ApplySnapshot(force: true);
            }
        }
    }

    /// <summary>Whether a battery is present. Drives the "no battery" unavailable state.</summary>
    public bool HasBattery
    {
        get => _hasBattery;
        private set
        {
            if (SetProperty(ref _hasBattery, value))
            {
                OnPropertyChanged(nameof(NoBattery));
                OnPropertyChanged(nameof(ShowLoading));
                OnPropertyChanged(nameof(ShowContent));
            }
        }
    }

    public bool NoBattery => !HasBattery;

    /// <summary>Whether at least one reading has been produced.</summary>
    public bool HasData
    {
        get => _hasData;
        private set
        {
            if (SetProperty(ref _hasData, value))
            {
                OnPropertyChanged(nameof(ShowLoading));
                OnPropertyChanged(nameof(ShowContent));
            }
        }
    }

    /// <summary>A battery is present but no reading has arrived yet.</summary>
    public bool ShowLoading => HasBattery && !HasData;

    /// <summary>A battery is present and there is at least one reading to show.</summary>
    public bool ShowContent => HasBattery && HasData;

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

    public bool HasError => _lastError is not null;

    public string Direction
    {
        get => _direction;
        private set => SetProperty(ref _direction, value);
    }

    public PowerMetricDisplay PowerMetric
    {
        get => _powerMetric;
        private set => SetProperty(ref _powerMetric, value);
    }

    public PowerMetricDisplay VoltageMetric
    {
        get => _voltageMetric;
        private set => SetProperty(ref _voltageMetric, value);
    }

    public PowerMetricDisplay CurrentMetric
    {
        get => _currentMetric;
        private set => SetProperty(ref _currentMetric, value);
    }

    /// <summary>The three synchronised chart series for the selected window.</summary>
    public PowerSeriesSet Series
    {
        get => _series;
        private set
        {
            if (SetProperty(ref _series, value))
            {
                OnPropertyChanged(nameof(ActiveSeries));
                OnPropertyChanged(nameof(PowerSpark));
            }
        }
    }

    /// <summary>Chart metric tabs, in display order.</summary>
    public IReadOnlyList<string> MetricTabs { get; } = ["Power", "Current", "Voltage"];

    /// <summary>Which metric the single chart shows (0 Power, 1 Current, 2 Voltage).</summary>
    public int SelectedMetricTab
    {
        get => _selectedMetricTab;
        set
        {
            if (value is >= 0 and <= 2 && SetProperty(ref _selectedMetricTab, value))
            {
                OnPropertyChanged(nameof(ActiveSeries));
                OnPropertyChanged(nameof(ActiveMetric));
            }
        }
    }

    /// <summary>The chart series for the selected metric tab.</summary>
    public Core.Models.ChartSeries ActiveSeries => _selectedMetricTab switch
    {
        1 => Series.Current,
        2 => Series.Voltage,
        _ => Series.Power,
    };

    /// <summary>The min/max/avg readout for the selected metric tab.</summary>
    public PowerMetricDisplay ActiveMetric => _selectedMetricTab switch
    {
        1 => CurrentMetric,
        2 => VoltageMetric,
        _ => PowerMetric,
    };

    /// <summary>Bare power values for a dashboard sparkline, oldest first (empty until there is data).</summary>
    public IReadOnlyList<double> PowerSpark => [.. _series.Power.Points.Select(p => p.Value)];

    [RelayCommand]
    private async Task RefreshAsync() => await _power.RefreshAsync().ConfigureAwait(false);

    private void OnPowerUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(() => ApplySnapshot());
    }

    private void ApplySnapshot(bool force = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!force && now - _lastApplied < MinRefreshInterval)
        {
            // Coalesce: schedule one trailing refresh so the last value in a
            // burst is never dropped, but do not repaint per 5-second sample.
            if (!_refreshQueued)
            {
                _refreshQueued = true;
                _dispatcher.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        _refreshQueued = false;
                        ApplySnapshot(force: true);
                    });
            }

            return;
        }

        _lastApplied = now;

        HasBattery = _battery.CurrentSnapshots.Count > 0 || _power.CurrentReadings.Count > 0;
        LastError = _power.LastError;

        PowerReading? primary = _power.Primary;
        HasData = primary is not null;

        PowerWindow window = _selectedWindow.Value;
        PowerWindowStatistics stats = _power.GetStatistics(window);
        Series = _power.GetSeries(window);

        PowerMetric = BuildMetric("Power", primary?.PowerMw, stats.Power, FormatMilliwatts);
        VoltageMetric = BuildMetric("Voltage", primary?.VoltageMv, stats.Voltage, FormatMillivolts);
        CurrentMetric = BuildCurrentMetric(primary?.CurrentMa, stats.Current);
        Direction = primary is null ? "—" : DescribeDirection(primary.Direction);

        OnPropertyChanged(nameof(ActiveMetric));
        OnPropertyChanged(nameof(ActiveSeries));
    }

    private static PowerMetricDisplay BuildMetric(
        string title,
        Core.Primitives.Measurement<int>? live,
        MetricStatistics stats,
        Func<double, string> format)
    {
        string liveText = live is { HasValue: true } lm ? format(lm.Value!.Value) : "Not available";
        bool requiresBadge = live is { RequiresBadge: true };
        string badge = requiresBadge ? BadgeLabel(live!.Value.Quality) : string.Empty;

        return new PowerMetricDisplay(
            title,
            liveText,
            StatLine("Min", stats.Min, stats.HasData, format),
            StatLine("Max", stats.Max, stats.HasData, format),
            StatLine("Avg", stats.Average, stats.HasData, format),
            requiresBadge,
            badge);
    }

    private static PowerMetricDisplay BuildCurrentMetric(Core.Primitives.Measurement<double>? live, MetricStatistics stats)
    {
        string liveText = live is { HasValue: true } lm ? FormatMilliamps(lm.Value!.Value) : "Not available";
        // Current is always Calculated — it always carries a badge when present.
        bool requiresBadge = live is { RequiresBadge: true };
        string badge = requiresBadge ? BadgeLabel(live!.Value.Quality) : string.Empty;

        return new PowerMetricDisplay(
            "Current",
            liveText,
            StatLine("Min", stats.Min, stats.HasData, FormatMilliamps),
            StatLine("Max", stats.Max, stats.HasData, FormatMilliamps),
            StatLine("Avg", stats.Average, stats.HasData, FormatMilliamps),
            requiresBadge,
            badge);
    }

    private static string StatLine(string label, double? value, bool hasData, Func<double, string> format) =>
        hasData && value is double v ? $"{label}  {format(v)}" : $"{label}  —";

    private static PowerMetricDisplay EmptyMetric(string title) =>
        new(title, "Not available", "Min  —", "Max  —", "Avg  —", false, string.Empty);

    private static string FormatMilliwatts(double value) =>
        $"{(value >= 0 ? "+" : string.Empty)}{value.ToString("N0", CultureInfo.InvariantCulture)} mW";

    private static string FormatMillivolts(double value) =>
        $"{value.ToString("N0", CultureInfo.InvariantCulture)} mV";

    private static string FormatMilliamps(double value) =>
        $"{(value >= 0 ? "+" : string.Empty)}{value.ToString("F0", CultureInfo.InvariantCulture)} mA";

    private static string BadgeLabel(DataQuality quality) => quality switch
    {
        DataQuality.Calculated => "Calculated",
        DataQuality.Estimated => "Estimated",
        DataQuality.Suspect => "Suspect",
        _ => string.Empty,
    };

    private static string DescribeDirection(PowerDirection direction) => direction switch
    {
        PowerDirection.Charging => "Charging",
        PowerDirection.Discharging => "Discharging",
        PowerDirection.Idle => "Idle",
        _ => "Unknown",
    };

    public void Dispose() => _power.Updated -= OnPowerUpdated;
}
