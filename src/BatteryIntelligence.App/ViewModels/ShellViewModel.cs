using System.Globalization;
using BatteryIntelligence.App.Services;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private readonly DispatcherQueue _dispatcher;

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
        IBatteryMonitoringService battery)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(battery);

        _settings = settings;
        _navigation = navigation;
        _battery = battery;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _isPaneOpen = settings.Current.Appearance.NavigationPaneOpen;

        _navigation.Navigated += OnNavigated;
        _battery.Updated += OnBatteryUpdated;
        ApplyBatteryStatus();
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

    /// <summary>Unread alert count for the title-bar bell. Zero until Phase 9.</summary>
    public int AlertCount => 0;

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
