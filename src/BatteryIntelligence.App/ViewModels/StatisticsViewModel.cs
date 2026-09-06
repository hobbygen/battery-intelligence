using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>One tile in the statistics summary grid.</summary>
public sealed record StatTile(string Label, string Value, string Sub);

/// <summary>
/// Backs the Statistics page (docs/ui-navigation.md "Statistics"; specification
/// section 16). Today / 7-day / 30-day / lifetime aggregate summaries computed
/// from session history.
/// </summary>
/// <remarks>
/// Subscribes to <see cref="IAnalyticsService.Updated"/> so a closed session
/// refreshes the figures. Reads are coalesced to at most 1 Hz.
/// </remarks>
public sealed partial class StatisticsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly StatisticsWindow[] Windows =
        [StatisticsWindow.Today, StatisticsWindow.Last7Days, StatisticsWindow.Last30Days, StatisticsWindow.Lifetime];

    private readonly IAnalyticsService _analytics;
    private readonly DispatcherQueue _dispatcher;

    private DateTimeOffset _lastApplied = DateTimeOffset.MinValue;
    private bool _refreshQueued;

    private int _selectedWindowIndex;
    private bool _hasData;
    private bool _loaded;
    private string _subtitle = "Aggregate battery usage over the selected period.";
    private IReadOnlyList<StatTile> _tiles = [];
    private double _chargeBarPercent;
    private double _dischargeBarPercent;
    private string _chargeBarLabel = "—";
    private string _dischargeBarLabel = "—";
    private string _screenOnLabel = "—";
    private double _screenOnPercent;

    public StatisticsViewModel(IAnalyticsService analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);

        _analytics = analytics;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _analytics.Updated += OnAnalyticsUpdated;
        _ = ApplyAsync(force: true);
    }

    public IReadOnlyList<string> WindowOptions { get; } = ["Today", "7 days", "30 days", "Lifetime"];

    public int SelectedWindowIndex
    {
        get => _selectedWindowIndex;
        set
        {
            if (value is >= 0 and <= 3 && SetProperty(ref _selectedWindowIndex, value))
            {
                _ = ApplyAsync(force: true);
            }
        }
    }

    public bool HasData
    {
        get => _hasData;
        private set
        {
            if (SetProperty(ref _hasData, value))
            {
                OnPropertyChanged(nameof(ShowContent));
                OnPropertyChanged(nameof(ShowEmpty));
            }
        }
    }

    public bool Loaded
    {
        get => _loaded;
        private set
        {
            if (SetProperty(ref _loaded, value))
            {
                OnPropertyChanged(nameof(ShowContent));
                OnPropertyChanged(nameof(ShowEmpty));
                OnPropertyChanged(nameof(ShowLoading));
            }
        }
    }

    public bool ShowLoading => !Loaded;

    public bool ShowContent => Loaded && HasData;

    public bool ShowEmpty => Loaded && !HasData;

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public IReadOnlyList<StatTile> Tiles
    {
        get => _tiles;
        private set => SetProperty(ref _tiles, value);
    }

    public double ChargeBarPercent
    {
        get => _chargeBarPercent;
        private set => SetProperty(ref _chargeBarPercent, value);
    }

    public double DischargeBarPercent
    {
        get => _dischargeBarPercent;
        private set => SetProperty(ref _dischargeBarPercent, value);
    }

    public string ChargeBarLabel
    {
        get => _chargeBarLabel;
        private set => SetProperty(ref _chargeBarLabel, value);
    }

    public string DischargeBarLabel
    {
        get => _dischargeBarLabel;
        private set => SetProperty(ref _dischargeBarLabel, value);
    }

    public string ScreenOnLabel
    {
        get => _screenOnLabel;
        private set => SetProperty(ref _screenOnLabel, value);
    }

    public double ScreenOnPercent
    {
        get => _screenOnPercent;
        private set => SetProperty(ref _screenOnPercent, value);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await ApplyAsync(force: true).ConfigureAwait(false);

    private void OnAnalyticsUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(() => _ = ApplyAsync());
    }

    private async Task ApplyAsync(bool force = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!force && now - _lastApplied < MinRefreshInterval)
        {
            if (!_refreshQueued)
            {
                _refreshQueued = true;
                _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    _refreshQueued = false;
                    _ = ApplyAsync(force: true);
                });
            }

            return;
        }

        _lastApplied = now;

        StatisticsWindow window = Windows[_selectedWindowIndex];
        StatisticsSummary summary;
        try
        {
            summary = await _analytics.GetStatisticsAsync(window).ConfigureAwait(true);
        }
        catch
        {
            summary = StatisticsSummary.Empty(window, now, now);
        }

        Loaded = true;
        HasData = summary.HasData;

        Subtitle = window switch
        {
            StatisticsWindow.Today => "Since midnight today.",
            StatisticsWindow.Last7Days => "Rolling 7-day totals.",
            StatisticsWindow.Last30Days => "Rolling 30-day totals.",
            _ => "Everything on record.",
        };

        if (!summary.HasData)
        {
            Tiles = [];
            return;
        }

        Tiles =
        [
            new StatTile("Discharging", FormatDuration(summary.DischargingSeconds), $"{summary.DischargeSessions} session(s)"),
            new StatTile("Charging", FormatDuration(summary.ChargingSeconds), $"{summary.ChargeSessions} session(s)"),
            new StatTile("Screen on", FormatDuration(summary.ScreenOnSeconds), $"{summary.ScreenOnFraction * 100:F0}% of active time"),
            new StatTile("Discharged", $"{summary.PercentDischarged:F0}%", RateText(summary.AvgDischargeRateMw, "avg draw")),
            new StatTile("Charged", $"{summary.PercentCharged:F0}%", RateText(summary.AvgChargeRateMw, "avg rate")),
            new StatTile("Through the battery", summary.EnergyThroughputMwh is long e ? $"{e / 1000.0:F1} Wh" : "—", "charge + discharge"),
        ];

        long durationTotal = Math.Max(1, summary.ChargingSeconds + summary.DischargingSeconds);
        ChargeBarPercent = summary.ChargingSeconds / (double)durationTotal * 100.0;
        DischargeBarPercent = summary.DischargingSeconds / (double)durationTotal * 100.0;
        ChargeBarLabel = string.Create(CultureInfo.InvariantCulture, $"Charging {FormatDuration(summary.ChargingSeconds)}");
        DischargeBarLabel = string.Create(CultureInfo.InvariantCulture, $"Discharging {FormatDuration(summary.DischargingSeconds)}");

        ScreenOnPercent = summary.ScreenOnFraction * 100.0;
        ScreenOnLabel = string.Create(CultureInfo.InvariantCulture,
            $"Screen on {FormatDuration(summary.ScreenOnSeconds)} of {FormatDuration(summary.ScreenOnSeconds + summary.ScreenOffSeconds)} awake");
    }

    private static string RateText(double? rateMw, string suffix) =>
        rateMw is double r ? string.Create(CultureInfo.InvariantCulture, $"{r:N0} mW {suffix}") : suffix;

    private static string FormatDuration(long seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes:D2}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m");
    }

    public void Dispose() => _analytics.Updated -= OnAnalyticsUpdated;
}
