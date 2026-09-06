using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Thermal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>One row of the "time in each band" breakdown. <paramref name="Percent"/> is 0–100 for a ProgressBar.</summary>
public sealed record TemperatureBandRow(string Label, string DurationText, double Percent, Brush Fill);

/// <summary>One row of the threshold-event log.</summary>
public sealed record ThresholdEventRow(string Peak, string Detail, string When, bool IsCritical);

/// <summary>
/// Backs the Temperature page (docs/ui-navigation.md section 2; specification
/// section 14). On the reference machine — no battery temperature sensor — it
/// stays in the unavailable state and never substitutes another sensor (spec §14).
/// </summary>
/// <remarks>
/// Subscribes to the singleton <see cref="ITemperatureMonitoringService"/> for the
/// life of the page; readouts are coalesced to at most 1 Hz, mirroring
/// <see cref="PowerViewModel"/>.
/// </remarks>
public sealed partial class TemperatureViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly IReadOnlyList<string> WindowLabels = ["Last hour", "This session"];

    private readonly ITemperatureMonitoringService _thermal;
    private readonly DispatcherQueue _dispatcher;

    private DateTimeOffset _lastApplied = DateTimeOffset.MinValue;
    private bool _refreshQueued;

    private bool _sensorAvailable;
    private bool _hasData;
    private int _selectedWindowIndex;
    private string _subtitle = "Checking for a battery temperature sensor…";
    private string _currentText = "—";
    private string _statusLabel = "Normal";
    private string _statusGlyph = "";
    private bool _isWarning;
    private bool _isCritical;
    private string _minText = "MIN  —";
    private string _avgText = "AVG  —";
    private string _maxText = "MAX  —";
    private double _warnThreshold = double.NaN;
    private string _thresholdLabel = string.Empty;
    private ChartSeries _series = ChartSeries.Empty("Temperature", "°C");
    private IReadOnlyList<TemperatureBandRow> _bandRows = [];
    private IReadOnlyList<ThresholdEventRow> _thresholdEvents = [];

    public TemperatureViewModel(ITemperatureMonitoringService thermal)
    {
        ArgumentNullException.ThrowIfNull(thermal);

        _thermal = thermal;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _thermal.Updated += OnThermalUpdated;
        Apply();
    }

    public IReadOnlyList<string> WindowOptions => WindowLabels;

    public int SelectedWindowIndex
    {
        get => _selectedWindowIndex;
        set
        {
            if (value is >= 0 and <= 1 && SetProperty(ref _selectedWindowIndex, value))
            {
                Apply(force: true);
            }
        }
    }

    /// <summary>Whether this hardware exposes a battery temperature sensor at all.</summary>
    public bool SensorAvailable
    {
        get => _sensorAvailable;
        private set
        {
            if (SetProperty(ref _sensorAvailable, value))
            {
                OnPropertyChanged(nameof(SensorUnavailable));
                OnPropertyChanged(nameof(ShowLoading));
                OnPropertyChanged(nameof(ShowContent));
            }
        }
    }

    public bool SensorUnavailable => !SensorAvailable;

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

    /// <summary>Sensor present, but no reading yet.</summary>
    public bool ShowLoading => SensorAvailable && !HasData;

    /// <summary>Sensor present and at least one reading.</summary>
    public bool ShowContent => SensorAvailable && HasData;

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string CurrentText
    {
        get => _currentText;
        private set => SetProperty(ref _currentText, value);
    }

    public string StatusLabel
    {
        get => _statusLabel;
        private set => SetProperty(ref _statusLabel, value);
    }

    public string StatusGlyph
    {
        get => _statusGlyph;
        private set => SetProperty(ref _statusGlyph, value);
    }

    public bool IsWarning
    {
        get => _isWarning;
        private set
        {
            if (SetProperty(ref _isWarning, value))
            {
                OnPropertyChanged(nameof(IsNormal));
            }
        }
    }

    public bool IsCritical
    {
        get => _isCritical;
        private set
        {
            if (SetProperty(ref _isCritical, value))
            {
                OnPropertyChanged(nameof(IsNormal));
            }
        }
    }

    public bool IsNormal => !IsWarning && !IsCritical;

    public string MinText
    {
        get => _minText;
        private set => SetProperty(ref _minText, value);
    }

    public string AvgText
    {
        get => _avgText;
        private set => SetProperty(ref _avgText, value);
    }

    public string MaxText
    {
        get => _maxText;
        private set => SetProperty(ref _maxText, value);
    }

    /// <summary>The warning threshold, for the chart band. NaN hides the band.</summary>
    public double WarnThreshold
    {
        get => _warnThreshold;
        private set => SetProperty(ref _warnThreshold, value);
    }

    public string ThresholdLabel
    {
        get => _thresholdLabel;
        private set => SetProperty(ref _thresholdLabel, value);
    }

    public ChartSeries Series
    {
        get => _series;
        private set => SetProperty(ref _series, value);
    }

    public DateTimeOffset SeriesFromUtc { get; private set; }

    public DateTimeOffset SeriesToUtc { get; private set; }

    public IReadOnlyList<TemperatureBandRow> BandRows
    {
        get => _bandRows;
        private set
        {
            if (SetProperty(ref _bandRows, value))
            {
                OnPropertyChanged(nameof(HasBandData));
            }
        }
    }

    public bool HasBandData => _bandRows.Any(r => r.Percent > 0);

    public IReadOnlyList<ThresholdEventRow> ThresholdEvents
    {
        get => _thresholdEvents;
        private set
        {
            if (SetProperty(ref _thresholdEvents, value))
            {
                OnPropertyChanged(nameof(HasThresholdEvents));
                OnPropertyChanged(nameof(NoThresholdEvents));
            }
        }
    }

    public bool HasThresholdEvents => _thresholdEvents.Count > 0;

    public bool NoThresholdEvents => _thresholdEvents.Count == 0;

    [RelayCommand]
    private async Task RefreshAsync() => await _thermal.RefreshAsync().ConfigureAwait(false);

    private void OnThermalUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(() => Apply());
    }

    private void Apply(bool force = false)
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
                    Apply(force: true);
                });
            }

            return;
        }

        _lastApplied = now;

        SensorAvailable = _thermal.SensorAvailable;
        TemperatureThresholds thresholds = _thermal.Thresholds;
        WarnThreshold = thresholds.WarnCelsius;
        ThresholdLabel = string.Create(CultureInfo.InvariantCulture, $"Warning {thresholds.WarnCelsius:F0} °C");

        Subtitle = _thermal.SensorAvailable
            ? string.Create(CultureInfo.InvariantCulture,
                $"Read from the battery driver · warning threshold {thresholds.WarnCelsius:F0} °C")
            : "This hardware does not expose a battery temperature sensor.";

        TemperatureReading? primary = _thermal.Primary;
        HasData = primary is { TemperatureCelsius.HasValue: true };

        if (!HasData)
        {
            return;
        }

        TemperatureWindow window = _selectedWindowIndex == 1 ? TemperatureWindow.Session : TemperatureWindow.OneHour;

        double celsius = primary!.TemperatureCelsius.Value!.Value;
        CurrentText = celsius.ToString("F1", CultureInfo.InvariantCulture);

        TemperatureSeverity severity = primary.Severity;
        IsWarning = severity == TemperatureSeverity.Warning;
        IsCritical = severity == TemperatureSeverity.Critical;
        StatusLabel = severity switch
        {
            TemperatureSeverity.Critical => "Hot",
            TemperatureSeverity.Warning => "Warm",
            _ => "Normal",
        };
        StatusGlyph = severity == TemperatureSeverity.Normal ? "" : "";

        MetricStatistics stats = _thermal.GetStatistics(window);
        MinText = FormatStat("MIN", stats.Min);
        AvgText = FormatStat("AVG", stats.Average);
        MaxText = FormatStat("MAX", stats.Max);

        ChartSeries series = _thermal.GetSeries(window);
        SeriesFromUtc = now - TimeSpan.FromHours(1);
        SeriesToUtc = now;
        Series = series;

        IReadOnlyList<TemperatureBandDuration> bands = _thermal.GetBandBreakdown(window);
        double maxBand = bands.Count == 0 ? 0 : bands.Max(b => b.Duration.TotalSeconds);
        BandRows =
        [
            .. bands.Select(b => new TemperatureBandRow(
                TemperatureBandClassifier.Describe(b.Band),
                FormatDuration(b.Duration),
                maxBand > 0 ? b.Duration.TotalSeconds / maxBand * 100.0 : 0,
                ResolveBrush(BandBrushKey(b.Band)))),
        ];

        ThresholdEvents =
        [
            .. _thermal.RecentThresholdEvents.Select(evt => new ThresholdEventRow(
                string.Create(CultureInfo.InvariantCulture, $"{evt.PeakCelsius:F1} °C"),
                DescribeEvent(evt),
                DescribeWhen(evt.StartUtc, now),
                evt.PeakCelsius >= thresholds.CriticalCelsius)),
        ];
    }

    private static string FormatStat(string label, double? value) =>
        value is double v
            ? string.Create(CultureInfo.InvariantCulture, $"{label}  {v:F1} °C")
            : $"{label}  —";

    private static string BandBrushKey(TemperatureBand band) => band switch
    {
        TemperatureBand.Cool => "AppAccentBrush",
        TemperatureBand.Normal => "AppOkBrush",
        TemperatureBand.Warm => "AppWarnBrush",
        _ => "AppCritBrush",
    };

    private static Brush ResolveBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private static string DescribeEvent(ThresholdEvent evt)
    {
        string context = evt.Context switch
        {
            PowerDirection.Charging => "while charging",
            PowerDirection.Discharging => "while discharging",
            _ => "while idle",
        };

        return evt.IsOpen
            ? string.Create(CultureInfo.InvariantCulture, $"Above threshold {context} · ongoing")
            : string.Create(CultureInfo.InvariantCulture, $"Held above threshold {FormatDuration(evt.Duration)} {context}");
    }

    private static string DescribeWhen(DateTimeOffset timestamp, DateTimeOffset now)
    {
        TimeSpan elapsed = now - timestamp;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "Just now",
            { TotalMinutes: < 60 } => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}m ago"),
            { TotalHours: < 24 } => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}h ago"),
            _ => timestamp.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture),
        };
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes:D2}m");
        }

        if (span.TotalMinutes >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m {span.Seconds:D2}s");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)span.TotalSeconds)}s");
    }

    public void Dispose() => _thermal.Updated -= OnThermalUpdated;
}
