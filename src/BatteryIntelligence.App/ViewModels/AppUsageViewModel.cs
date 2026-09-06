using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>One row of the App Usage ranking.</summary>
/// <param name="Name">Application display name.</param>
/// <param name="Monogram">One- or two-letter tile stand-in for an icon (icons are deferred — docs/roadmap.md Phase 7).</param>
/// <param name="BarPercent">Width of the share bar, 0–100, relative to the largest row.</param>
/// <param name="ShareText">The share of the attributable budget, e.g. "34%".</param>
/// <param name="CpuText">CPU line, e.g. "CPU 12.4%".</param>
/// <param name="MemoryText">Memory line, e.g. "480 MB".</param>
/// <param name="PowerText">Estimated draw, e.g. "1,240 mW", or "—" on AC.</param>
/// <param name="IsForeground">Whether the app currently owns the foreground window.</param>
/// <param name="IsBaseline">Whether this is the non-attributable baseline slice.</param>
/// <param name="IsOther">Whether this is the collapsed "Other" row.</param>
public sealed record AppUsageRow(
    string Name,
    string Monogram,
    double BarPercent,
    string ShareText,
    string CpuText,
    string MemoryText,
    string PowerText,
    bool IsForeground,
    bool IsBaseline,
    bool IsOther);

/// <summary>
/// Backs the App Usage page (docs/ui-navigation.md section 2; specification
/// sections 15 and 55). Every figure is an <c>AppEnergyV1</c> estimate — the page
/// carries a permanent <strong>Estimated</strong> badge and a link to the
/// methodology, and states plainly while on AC that absolute power is unavailable.
/// </summary>
/// <remarks>
/// Subscribes to the singleton <see cref="IProcessMonitoringService"/> for the
/// life of the page; readouts are coalesced to at most 1 Hz, mirroring
/// <see cref="TemperatureViewModel"/> and <see cref="PowerViewModel"/>.
/// </remarks>
public sealed partial class AppUsageViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IProcessMonitoringService _service;
    private readonly DispatcherQueue _dispatcher;

    private DateTimeOffset _lastApplied = DateTimeOffset.MinValue;
    private bool _refreshQueued;

    private bool _hasData;
    private bool _absoluteAvailable;
    private int _selectedWindowIndex;
    private int _selectedSortIndex;
    private string _subtitle = "Estimated battery impact by application. Ranked from Windows process activity — never a measurement.";
    private string _confidenceText = string.Empty;
    private string _totalText = "—";
    private IReadOnlyList<AppUsageRow> _rows = [];
    private AppUsageRow? _baselineRow;

    public AppUsageViewModel(IProcessMonitoringService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _service.Updated += OnUpdated;
        Apply(force: true);
    }

    /// <summary>Window selector labels.</summary>
    public IReadOnlyList<string> WindowOptions { get; } = ["Last hour", "This session"];

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

    /// <summary>Sort selector labels.</summary>
    public IReadOnlyList<string> SortOptions { get; } = ["Impact", "CPU", "Name"];

    public int SelectedSortIndex
    {
        get => _selectedSortIndex;
        set
        {
            if (value is >= 0 and <= 2 && SetProperty(ref _selectedSortIndex, value))
            {
                Apply(force: true);
            }
        }
    }

    /// <summary>Whether at least one application has been ranked yet.</summary>
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

    /// <summary>No ranking produced yet — the first sampling tick is ~10 s out.</summary>
    public bool ShowLoading => !HasData;

    /// <summary>A ranking is available.</summary>
    public bool ShowContent => HasData;

    /// <summary>Whether absolute milliwatt figures are meaningful (on battery) or only the ranking is (on AC).</summary>
    public bool AbsoluteAvailable
    {
        get => _absoluteAvailable;
        private set
        {
            if (SetProperty(ref _absoluteAvailable, value))
            {
                OnPropertyChanged(nameof(OnAcPower));
            }
        }
    }

    /// <summary>On AC: absolute per-app power cannot be divided from a battery draw that does not exist.</summary>
    public bool OnAcPower => !AbsoluteAvailable;

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string ConfidenceText
    {
        get => _confidenceText;
        private set => SetProperty(ref _confidenceText, value);
    }

    /// <summary>The measured system draw the estimate divides, or "—" on AC.</summary>
    public string TotalText
    {
        get => _totalText;
        private set => SetProperty(ref _totalText, value);
    }

    public IReadOnlyList<AppUsageRow> Rows
    {
        get => _rows;
        private set
        {
            if (SetProperty(ref _rows, value))
            {
                OnPropertyChanged(nameof(TopRow));
                OnPropertyChanged(nameof(HasTopRow));
            }
        }
    }

    /// <summary>The highest-impact application, for the Dashboard card. <see langword="null"/> before the first ranking.</summary>
    public AppUsageRow? TopRow => _rows.FirstOrDefault(r => !r.IsOther) ?? _rows.FirstOrDefault();

    public bool HasTopRow => TopRow is not null;

    /// <summary>The non-attributable baseline, shown as a distinct slice so nothing is double-counted.</summary>
    public AppUsageRow? BaselineRow
    {
        get => _baselineRow;
        private set
        {
            if (SetProperty(ref _baselineRow, value))
            {
                OnPropertyChanged(nameof(HasBaseline));
            }
        }
    }

    public bool HasBaseline => _baselineRow is not null;

    [RelayCommand]
    private async Task RefreshAsync() => await _service.RefreshAsync().ConfigureAwait(false);

    private void OnUpdated(object? sender, EventArgs e)
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

        ProcessWindow window = _selectedWindowIndex == 1 ? ProcessWindow.ThisSession : ProcessWindow.LastHour;
        AppEnergyAttribution attribution = _service.GetRanking(window);

        HasData = attribution.HasEntries;
        AbsoluteAvailable = attribution.AbsoluteAvailable;

        TotalText = attribution.TotalBudgetMw is int total
            ? string.Create(CultureInfo.InvariantCulture, $"{total:N0} mW")
            : "—";

        ConfidenceText = attribution.Confidence switch
        {
            AppEnergyConfidence.High => "Confidence: high — the baseline is modelled from this device's idle history.",
            AppEnergyConfidence.Medium => "Confidence: medium — the baseline model is still learning this device.",
            _ => "Confidence: low — the baseline is a conservative default until enough idle history accrues.",
        };

        Subtitle = attribution.AbsoluteAvailable
            ? "Estimated battery impact by application, apportioned from the measured system draw."
            : "Applications ranked by relative CPU activity. Absolute power needs a battery draw to divide — unavailable on AC.";

        if (!attribution.HasEntries)
        {
            Rows = [];
            BaselineRow = null;
            return;
        }

        IEnumerable<AppUsageEntry> ordered = _selectedSortIndex switch
        {
            1 => attribution.Entries.OrderByDescending(e => e.IsOther ? double.MinValue : e.CpuPercent),
            2 => attribution.Entries.OrderBy(e => e.IsOther).ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => attribution.Entries.OrderBy(e => e.IsOther).ThenByDescending(e => e.EstimatedPowerMw ?? 0).ThenByDescending(e => e.SharePercent),
        };

        List<AppUsageEntry> list = [.. ordered];
        double maxShare = list.Count == 0 ? 0 : list.Max(e => e.SharePercent);

        Rows = [.. list.Select(e => ToRow(e, maxShare))];
        BaselineRow = ToRow(attribution.Baseline, Math.Max(maxShare, attribution.Baseline.SharePercent));
    }

    private static AppUsageRow ToRow(AppUsageEntry entry, double maxShare)
    {
        string power = entry.EstimatedPowerMw is int mw
            ? string.Create(CultureInfo.InvariantCulture, $"{mw:N0} mW")
            : "—";

        return new AppUsageRow(
            Name: entry.DisplayName,
            Monogram: Monogram(entry.DisplayName),
            BarPercent: maxShare > 0 ? Math.Clamp(entry.SharePercent / maxShare * 100.0, 0, 100) : 0,
            ShareText: string.Create(CultureInfo.InvariantCulture, $"{entry.SharePercent:0.#}%"),
            CpuText: entry.IsBaseline
                ? "System overhead"
                : string.Create(CultureInfo.InvariantCulture, $"CPU {entry.CpuPercent:0.#}%"),
            MemoryText: entry.MemoryBytes > 0 ? FormatBytes(entry.MemoryBytes) : string.Empty,
            PowerText: power,
            IsForeground: entry.IsForeground,
            IsBaseline: entry.IsBaseline,
            IsOther: entry.IsOther);
    }

    private static string Monogram(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "?";
        }

        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]))
            : char.ToUpperInvariant(trimmed[0]).ToString();
    }

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{mb / 1024.0:0.0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{mb:0} MB");
    }

    public void Dispose() => _service.Updated -= OnUpdated;
}
