using System.Globalization;
using BatteryIntelligence.App.Services;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>
/// State for the application shell: navigation pane, title bar and the persistent
/// battery status strip (docs/ui-navigation.md section 1).
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IBatteryMonitoringService _battery;
    private readonly IAlertMonitoringService _alerts;
    private readonly DispatcherQueue _dispatcher;

    private int _alertCount;
    private IReadOnlyList<AlertDisplayRow> _recentAlerts = [];

    private bool _isPaneOpen;
    private bool _isBackEnabled;
    private string _currentPageTitle = "Dashboard";

    private bool _hasBatteryStatus;
    private bool _isCharging;
    private bool _isCritical;
    private string _statusText = string.Empty;
    private string _statusGlyph = "";

    public ShellViewModel(
        ISettingsService settings,
        INavigationService navigation,
        IBatteryMonitoringService battery,
        IAlertMonitoringService alerts)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(battery);
        ArgumentNullException.ThrowIfNull(alerts);

        _settings = settings;
        _navigation = navigation;
        _battery = battery;
        _alerts = alerts;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _isPaneOpen = settings.Current.Appearance.NavigationPaneOpen;

        _navigation.Navigated += OnNavigated;
        _battery.Updated += OnBatteryUpdated;
        _alerts.Updated += OnAlertsUpdated;
        ApplyBatteryStatus();
        ApplyAlerts();
    }

    /// <summary>Product name shown in the title bar.</summary>
    public static string AppTitle => "Battery Intelligence";

    /// <summary>Tag of the page that should be selected at startup.</summary>
    public string DefaultPageTag => _settings.Current.General.DefaultPage;

    /// <summary>Whether the navigation pane is expanded. Persisted across sessions.</summary>
    public bool IsPaneOpen
    {
        get => _isPaneOpen;
        set
        {
            if (SetProperty(ref _isPaneOpen, value))
            {
                PersistPaneState(value);
            }
        }
    }

    /// <summary>Whether the back button is enabled.</summary>
    public bool IsBackEnabled
    {
        get => _isBackEnabled;
        private set => SetProperty(ref _isBackEnabled, value);
    }

    /// <summary>Human-readable title of the current page.</summary>
    public string CurrentPageTitle
    {
        get => _currentPageTitle;
        private set => SetProperty(ref _currentPageTitle, value);
    }

    /// <summary>Unacknowledged alert count for the title-bar bell badge.</summary>
    public int AlertCount
    {
        get => _alertCount;
        private set
        {
            if (SetProperty(ref _alertCount, value))
            {
                OnPropertyChanged(nameof(HasAlerts));
                OnPropertyChanged(nameof(AlertCountText));
            }
        }
    }

    /// <summary>Whether the bell should show a badge.</summary>
    public bool HasAlerts => _alertCount > 0;

    /// <summary>Badge text, capped at "9+".</summary>
    public string AlertCountText => _alertCount > 9 ? "9+" : _alertCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Recent alerts for the bell flyout, newest first.</summary>
    public IReadOnlyList<AlertDisplayRow> RecentAlerts
    {
        get => _recentAlerts;
        private set
        {
            if (SetProperty(ref _recentAlerts, value))
            {
                OnPropertyChanged(nameof(HasRecentAlerts));
                OnPropertyChanged(nameof(NoRecentAlerts));
            }
        }
    }

    public bool HasRecentAlerts => _recentAlerts.Count > 0;

    public bool NoRecentAlerts => _recentAlerts.Count == 0;

    /// <summary>Marks every active alert acknowledged (bell flyout button).</summary>
    [RelayCommand]
    private async Task AcknowledgeAllAsync() => await _alerts.AcknowledgeAllAsync().ConfigureAwait(false);

    /// <summary>Opens the Alerts page (bell flyout "View all").</summary>
    [RelayCommand]
    private void OpenAlerts() => _navigation.NavigateTo("Alerts");

    /// <summary>Whether there is a battery reading to show in the status strip.</summary>
    public bool HasBatteryStatus
    {
        get => _hasBatteryStatus;
        private set
        {
            if (SetProperty(ref _hasBatteryStatus, value))
            {
                OnPropertyChanged(nameof(ShowNormalStatus));
            }
        }
    }

    /// <summary>"92% · Charging" / "9% · Critical" / "84%".</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Segoe Fluent glyph paired with the status text (never colour alone).</summary>
    public string StatusGlyph
    {
        get => _statusGlyph;
        private set => SetProperty(ref _statusGlyph, value);
    }

    public bool IsChargingStatus
    {
        get => _isCharging;
        private set
        {
            if (SetProperty(ref _isCharging, value))
            {
                OnPropertyChanged(nameof(ShowNormalStatus));
            }
        }
    }

    public bool IsCriticalStatus
    {
        get => _isCritical;
        private set
        {
            if (SetProperty(ref _isCritical, value))
            {
                OnPropertyChanged(nameof(ShowNormalStatus));
            }
        }
    }

    /// <summary>Neither charging nor critical, but a battery is present.</summary>
    public bool ShowNormalStatus => HasBatteryStatus && !IsChargingStatus && !IsCriticalStatus;

    private void OnBatteryUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(ApplyBatteryStatus);
    }

    private void OnAlertsUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(ApplyAlerts);
    }

    private void ApplyAlerts()
    {
        AlertCount = _alerts.UnacknowledgedCount;
        RecentAlerts =
        [
            .. _alerts.RecentAlerts.Take(6).Select(a => new AlertDisplayRow(
                a.Id ?? 0,
                a.Title,
                a.Message,
                a.Severity switch { AlertSeverity.Critical => "", AlertSeverity.Warning => "", _ => "" },
                a.Severity switch { AlertSeverity.Critical => "AppCritBrush", AlertSeverity.Warning => "AppWarnBrush", _ => "AppAccentBrush" },
                DescribeAlertAge(a.TimestampUtc),
                a.Acknowledged)),
        ];
    }

    private static string DescribeAlertAge(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - timestamp;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "Just now",
            { TotalMinutes: < 60 } => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}m ago"),
            { TotalHours: < 24 } => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}h ago"),
            _ => timestamp.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture),
        };
    }

    private void ApplyBatteryStatus()
    {
        BatteryInfo? info = _battery.Aggregate
            ?? (_battery.CurrentSnapshots.Count > 0 ? _battery.CurrentSnapshots[0].Info : null);

        if (info is null || info.Percentage.Value is not double percent)
        {
            HasBatteryStatus = false;
            IsChargingStatus = false;
            IsCriticalStatus = false;
            StatusText = "No battery";
            StatusGlyph = "";
            return;
        }

        BatteryState state = info.State.Value ?? BatteryState.Unknown;
        bool charging = state is BatteryState.Charging or BatteryState.Full;
        bool critical = !charging && percent <= 10;

        HasBatteryStatus = true;
        IsChargingStatus = charging;
        IsCriticalStatus = critical;

        string label = state switch
        {
            BatteryState.Charging => "Charging",
            BatteryState.Full => "Full",
            BatteryState.Discharging when critical => "Critical",
            BatteryState.Discharging => "Discharging",
            BatteryState.Idle => "Idle",
            _ => "On battery",
        };

        StatusText = string.Create(CultureInfo.InvariantCulture, $"{percent:F0}% · {label}");
        // Segoe Fluent Icons: Warning / Lightning / BatteryCharging.
        StatusGlyph = critical ? "" : charging ? "" : "";
    }

    private void OnNavigated(object? sender, string tag)
    {
        _ = sender;

        CurrentPageTitle = tag switch
        {
            "AppUsage" => "App Usage",
            _ => tag,
        };

        IsBackEnabled = _navigation.CanGoBack;
    }

    private void PersistPaneState(bool isOpen)
    {
        _ = _settings.UpdateAsync(
            settings => settings.Appearance.NavigationPaneOpen = isOpen,
            category: "Appearance");
    }
}
