using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BatteryIntelligence.App.Services;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.History;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>One selectable table in the export scope list.</summary>
public sealed partial class ExportScopeOption : ObservableObject
{
    private bool _isSelected;

    public ExportScopeOption(string label, ExportScope flag, bool isSelected)
    {
        Label = label;
        Flag = flag;
        _isSelected = isSelected;
    }

    /// <summary>Checkbox label.</summary>
    public string Label { get; }

    /// <summary>The scope bit this option toggles.</summary>
    public ExportScope Flag { get; }

    /// <summary>Whether this table is included in the next export.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// Backs the History page (specification section 17): a tier-aware metric chart
/// over 24 hours to a year, and a CSV / JSON export of the selected tables.
/// </summary>
/// <remarks>
/// History is user-driven, not live, so there is no 1 Hz coalescing — instead a
/// selection change cancels any in-flight query and starts a fresh one.
/// </remarks>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private static readonly HistoryRange[] Ranges =
        [HistoryRange.Last24Hours, HistoryRange.Last7Days, HistoryRange.Last30Days, HistoryRange.Last90Days, HistoryRange.LastYear];

    private static readonly HistoryMetric[] Metrics =
        [HistoryMetric.ChargePercent, HistoryMetric.PowerMw, HistoryMetric.VoltageMv, HistoryMetric.TemperatureCelsius];

    private readonly IHistoryReadStore _store;
    private readonly ExportService _export;
    private readonly IReportExporter _csv;
    private readonly IReportExporter _json;
    private readonly ILogger<HistoryViewModel> _logger;
    private readonly DispatcherQueue _dispatcher;

    private CancellationTokenSource? _queryCts;
    private bool _disposed;

    private int _selectedRangeIndex;
    private int _selectedMetricIndex;
    private bool _isLoading = true;
    private bool _hasData;
    private bool _temperatureAvailable = true;
    private ChartSeries _series = ChartSeries.Empty("Charge", "%");
    private DateTimeOffset _rangeStartUtc;
    private DateTimeOffset _rangeEndUtc;
    private string _tierCaption = string.Empty;
    private string _extentCaption = string.Empty;
    private string _emptyMessage = "No samples recorded in this range yet.";
    private InfoBarState _exportResult = InfoBarState.None;

    public HistoryViewModel(
        IHistoryReadStore store,
        ExportService export,
        IEnumerable<IReportExporter> exporters,
        ILogger<HistoryViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(exporters);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _export = export;
        _logger = logger;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        IReportExporter[] all = [.. exporters];
        _csv = all.First(e => e.FileExtension.Equals(".csv", StringComparison.OrdinalIgnoreCase));
        _json = all.First(e => e.FileExtension.Equals(".json", StringComparison.OrdinalIgnoreCase));

        ExportScopes =
        [
            new("Battery samples (charge, voltage, power)", ExportScope.BatterySamples, true),
            new("Power samples", ExportScope.PowerSamples, false),
            new("Temperature samples", ExportScope.TemperatureSamples, false),
            new("Sessions", ExportScope.Sessions, true),
            new("Alerts", ExportScope.Alerts, true),
            new("Health snapshots", ExportScope.HealthSnapshots, false),
            new("Daily statistics", ExportScope.DailyStatistics, false),
            new("Application usage", ExportScope.ApplicationUsage, false),
        ];

        _ = InitializeAsync();
    }

    public IReadOnlyList<string> RangeOptions { get; } = ["24 hours", "7 days", "30 days", "90 days", "1 year"];

    public IReadOnlyList<string> MetricOptions { get; } = ["Charge", "Power", "Voltage", "Temperature"];

    public ObservableCollection<ExportScopeOption> ExportScopes { get; }

    public int SelectedRangeIndex
    {
        get => _selectedRangeIndex;
        set
        {
            if (value is >= 0 && value < Ranges.Length && SetProperty(ref _selectedRangeIndex, value))
            {
                QueueLoad();
            }
        }
    }

    public int SelectedMetricIndex
    {
        get => _selectedMetricIndex;
        set
        {
            if (value is >= 0 && value < Metrics.Length && SetProperty(ref _selectedMetricIndex, value))
            {
                OnPropertyChanged(nameof(ShowTemperatureUnavailable));
                QueueLoad();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowChart));
                OnPropertyChanged(nameof(ShowEmpty));
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
                OnPropertyChanged(nameof(ShowChart));
                OnPropertyChanged(nameof(ShowEmpty));
            }
        }
    }

    /// <summary>The chart is drawn only once a query has returned points.</summary>
    public bool ShowChart => !IsLoading && HasData;

    /// <summary>The range holds no data for this metric.</summary>
    public bool ShowEmpty => !IsLoading && !HasData && !ShowTemperatureUnavailable;

    /// <summary>Temperature was picked but this device has no sensor history.</summary>
    public bool ShowTemperatureUnavailable =>
        Metrics[_selectedMetricIndex] == HistoryMetric.TemperatureCelsius && !_temperatureAvailable;

    public ChartSeries Series
    {
        get => _series;
        private set => SetProperty(ref _series, value);
    }

    public DateTimeOffset RangeStartUtc
    {
        get => _rangeStartUtc;
        private set => SetProperty(ref _rangeStartUtc, value);
    }

    public DateTimeOffset RangeEndUtc
    {
        get => _rangeEndUtc;
        private set => SetProperty(ref _rangeEndUtc, value);
    }

    /// <summary>e.g. "Minute averages · last 7 days".</summary>
    public string TierCaption
    {
        get => _tierCaption;
        private set => SetProperty(ref _tierCaption, value);
    }

    /// <summary>e.g. "Recorded data spans 12 Aug 2026 – 6 Sep 2026".</summary>
    public string ExtentCaption
    {
        get => _extentCaption;
        private set => SetProperty(ref _extentCaption, value);
    }

    public string EmptyMessage
    {
        get => _emptyMessage;
        private set => SetProperty(ref _emptyMessage, value);
    }

    public bool HasExportResult => _exportResult.Kind != InfoBarKind.None;

    public bool ExportResultIsError => _exportResult.Kind == InfoBarKind.Error;

    public string ExportResultMessage => _exportResult.Message;

    [RelayCommand]
    private Task ExportCsvAsync() => RunExportAsync(_csv);

    [RelayCommand]
    private Task ExportJsonAsync() => RunExportAsync(_json);

    [RelayCommand]
    private void DismissExportResult() => SetExportResult(InfoBarState.None);

    private async Task InitializeAsync()
    {
        try
        {
            _temperatureAvailable = await _store.HasTemperatureDataAsync().ConfigureAwait(true);
            (DateTimeOffset? earliest, DateTimeOffset? latest) = await _store.GetExtentAsync().ConfigureAwait(true);
            ExtentCaption = earliest is { } e && latest is { } l
                ? string.Create(CultureInfo.CurrentCulture, $"Recorded data spans {e.LocalDateTime:d MMM yyyy} – {l.LocalDateTime:d MMM yyyy}")
                : "No history recorded yet.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "History initialisation failed.");
        }

        OnPropertyChanged(nameof(ShowTemperatureUnavailable));
        await LoadAsync().ConfigureAwait(true);
    }

    private void QueueLoad()
    {
        _dispatcher.TryEnqueue(() => _ = LoadAsync());
    }

    private async Task LoadAsync()
    {
        if (_disposed)
        {
            return;
        }

        _queryCts?.Cancel();
        _queryCts?.Dispose();
        var cts = new CancellationTokenSource();
        _queryCts = cts;
        CancellationToken token = cts.Token;

        HistoryMetric metric = Metrics[_selectedMetricIndex];
        HistoryRange range = Ranges[_selectedRangeIndex];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateRange window = HistoryTierSelector.ResolveRange(range, now);

        IsLoading = true;

        if (ShowTemperatureUnavailable)
        {
            HasData = false;
            IsLoading = false;
            return;
        }

        try
        {
            // A small debounce so dragging across the segments does not fire a
            // query per intermediate selection.
            await Task.Delay(120, token).ConfigureAwait(true);

            ChartSeries series = await _store.GetSeriesAsync(new HistoryRequest(metric, window), token).ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }

            Series = series;
            RangeStartUtc = window.FromUtc;
            RangeEndUtc = window.ToUtc;
            HasData = series.HasPoints;

            HistoryTier tier = HistoryTierSelector.TierForSpan(window.Duration);
            string desc = HistoryTierSelector.Describe(tier);
            string tierText = desc[..1].ToUpperInvariant() + desc[1..];
            TierCaption = string.Create(CultureInfo.CurrentCulture, $"{tierText} · {RangeOptions[_selectedRangeIndex]}");

            if (!series.HasPoints)
            {
                EmptyMessage = $"No {MetricOptions[_selectedMetricIndex].ToLower(CultureInfo.CurrentCulture)} data recorded in this range yet.";
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection — leave the newer query to finish.
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "History query failed.");
            HasData = false;
            EmptyMessage = "The history query failed. See the log for details.";
        }
        finally
        {
            if (ReferenceEquals(_queryCts, cts) && !_disposed)
            {
                IsLoading = false;
            }
        }
    }

    private async Task RunExportAsync(IReportExporter exporter)
    {
        ExportScope scope = ExportScopes
            .Where(o => o.IsSelected)
            .Aggregate(ExportScope.None, (acc, o) => acc | o.Flag);

        if (scope == ExportScope.None)
        {
            SetExportResult(new InfoBarState(InfoBarKind.Error, "Select at least one table to export."));
            return;
        }

        // Export covers the whole recorded history, not just the charted window —
        // the range selector is for the chart, the scope checkboxes are for the file.
        DateRange full = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow.AddDays(1));
        ExportResult result = await _export.ExportAsync(new ExportRequest(full, scope), exporter).ConfigureAwait(true);

        SetExportResult(result.Status switch
        {
            ExportStatus.Saved => new InfoBarState(InfoBarKind.Success, $"Saved to {result.Path}"),
            ExportStatus.Cancelled => InfoBarState.None,
            _ => new InfoBarState(InfoBarKind.Error, $"Export failed: {result.Error}"),
        });
    }

    private void SetExportResult(InfoBarState state)
    {
        _exportResult = state;
        OnPropertyChanged(nameof(HasExportResult));
        OnPropertyChanged(nameof(ExportResultIsError));
        OnPropertyChanged(nameof(ExportResultMessage));
    }

    public void Dispose()
    {
        _disposed = true;
        _queryCts?.Cancel();
        _queryCts?.Dispose();
        _queryCts = null;
    }

    private enum InfoBarKind
    {
        None,
        Success,
        Error,
    }

    private readonly record struct InfoBarState(InfoBarKind Kind, string Message)
    {
        public static InfoBarState None => new(InfoBarKind.None, string.Empty);
    }
}
