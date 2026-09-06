using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace BatteryIntelligence.App.ViewModels;

/// <summary>One rendered alert row for the active list or the history list.</summary>
public sealed record AlertDisplayRow(
    long Id, string Title, string Message, string SeverityGlyph, string SeverityBrushKey, string When, bool Acknowledged)
{
    /// <summary>Whether the "Dismiss" button should show for this row.</summary>
    public bool NotAcknowledged => !Acknowledged;
}

/// <summary>
/// Backs the Alerts page (docs/ui-navigation.md "Alerts"; specification
/// sections 20 and 21): per-alert configuration, the active alert list and the
/// alert history. Rules are edited straight through <see cref="ISettingsService"/>;
/// the lists come from <see cref="IAlertMonitoringService"/>.
/// </summary>
public sealed partial class AlertsViewModel : ObservableObject, IDisposable
{
    private readonly IAlertMonitoringService _alerts;
    private readonly ISettingsService _settings;
    private readonly DispatcherQueue _dispatcher;

    private IReadOnlyList<AlertDisplayRow> _active = [];
    private IReadOnlyList<AlertDisplayRow> _history = [];
    private string _notificationChannel = "Checking…";

    public AlertsViewModel(IAlertMonitoringService alerts, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(settings);

        _alerts = alerts;
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _alerts.Updated += OnAlertsUpdated;
        Apply();
    }

    // --- Per-alert toggles (spec §20) ---------------------------------

    public bool LowBatteryEnabled { get => _settings.Current.Alerts.LowBatteryEnabled; set => SetAlert(s => s.LowBatteryEnabled = value, value != LowBatteryEnabled); }

    public int LowBatteryPercent { get => _settings.Current.Alerts.LowBatteryPercent; set => SetAlert(s => s.LowBatteryPercent = (int)value, value != LowBatteryPercent); }

    public bool CriticalBatteryEnabled { get => _settings.Current.Alerts.CriticalBatteryEnabled; set => SetAlert(s => s.CriticalBatteryEnabled = value, value != CriticalBatteryEnabled); }

    public int CriticalBatteryPercent { get => _settings.Current.Alerts.CriticalBatteryPercent; set => SetAlert(s => s.CriticalBatteryPercent = (int)value, value != CriticalBatteryPercent); }

    public bool FullyChargedEnabled { get => _settings.Current.Alerts.FullyChargedEnabled; set => SetAlert(s => s.FullyChargedEnabled = value, value != FullyChargedEnabled); }

    public bool HighTemperatureEnabled { get => _settings.Current.Alerts.HighTemperatureEnabled; set => SetAlert(s => s.HighTemperatureEnabled = value, value != HighTemperatureEnabled); }

    public int HighTemperatureCelsius { get => _settings.Current.Alerts.HighTemperatureCelsius; set => SetAlert(s => s.HighTemperatureCelsius = (int)value, value != HighTemperatureCelsius); }

    public bool RapidDischargeEnabled { get => _settings.Current.Alerts.RapidDischargeEnabled; set => SetAlert(s => s.RapidDischargeEnabled = value, value != RapidDischargeEnabled); }

    public bool SlowChargingEnabled { get => _settings.Current.Alerts.SlowChargingEnabled; set => SetAlert(s => s.SlowChargingEnabled = value, value != SlowChargingEnabled); }

    public bool ChargerConnectedEnabled { get => _settings.Current.Alerts.ChargerConnectedEnabled; set => SetAlert(s => s.ChargerConnectedEnabled = value, value != ChargerConnectedEnabled); }

    public bool ChargerDisconnectedEnabled { get => _settings.Current.Alerts.ChargerDisconnectedEnabled; set => SetAlert(s => s.ChargerDisconnectedEnabled = value, value != ChargerDisconnectedEnabled); }

    public bool HealthDegradationEnabled { get => _settings.Current.Alerts.HealthDegradationEnabled; set => SetAlert(s => s.HealthDegradationEnabled = value, value != HealthDegradationEnabled); }

    public bool HighApplicationConsumptionEnabled { get => _settings.Current.Alerts.HighApplicationConsumptionEnabled; set => SetAlert(s => s.HighApplicationConsumptionEnabled = value, value != HighApplicationConsumptionEnabled); }

    public int CooldownMinutes { get => _settings.Current.Alerts.CooldownMinutes; set => SetAlert(s => s.CooldownMinutes = (int)value, value != CooldownMinutes); }

    // --- Notification delivery (spec §21) ----------------------------

    public bool NotificationsEnabled
    {
        get => _settings.Current.Notifications.Enabled;
        set
        {
            if (value == NotificationsEnabled) { return; }
            _ = _settings.UpdateAsync(s => s.Notifications.Enabled = value, "Notifications");
            OnPropertyChanged();
        }
    }

    public bool UseWindowsNotifications
    {
        get => _settings.Current.Notifications.UseWindowsNotifications;
        set
        {
            if (value == UseWindowsNotifications) { return; }
            _ = _settings.UpdateAsync(s => s.Notifications.UseWindowsNotifications = value, "Notifications");
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotificationChannel));
        }
    }

    public bool PlaySound
    {
        get => _settings.Current.Notifications.PlaySound;
        set
        {
            if (value == PlaySound) { return; }
            _ = _settings.UpdateAsync(s => s.Notifications.PlaySound = value, "Notifications");
            OnPropertyChanged();
        }
    }

    /// <summary>"Windows notifications + in-app centre" / "In-app centre only (Windows notifications unavailable)".</summary>
    public string NotificationChannel
    {
        get => _notificationChannel;
        private set => SetProperty(ref _notificationChannel, value);
    }

    // --- Alert lists -----------------------------------------------

    public IReadOnlyList<AlertDisplayRow> Active
    {
        get => _active;
        private set
        {
            if (SetProperty(ref _active, value))
            {
                OnPropertyChanged(nameof(HasActive));
                OnPropertyChanged(nameof(NoActive));
            }
        }
    }

    public bool HasActive => _active.Count > 0;

    public bool NoActive => _active.Count == 0;

    public IReadOnlyList<AlertDisplayRow> History
    {
        get => _history;
        private set
        {
            if (SetProperty(ref _history, value))
            {
                OnPropertyChanged(nameof(HasHistory));
                OnPropertyChanged(nameof(NoHistory));
            }
        }
    }

    public bool HasHistory => _history.Count > 0;

    public bool NoHistory => _history.Count == 0;

    [RelayCommand]
    private async Task AcknowledgeAsync(long id) => await _alerts.AcknowledgeAsync(id).ConfigureAwait(false);

    [RelayCommand]
    private async Task AcknowledgeAllAsync() => await _alerts.AcknowledgeAllAsync().ConfigureAwait(false);

    private void SetAlert(Action<Core.Configuration.AlertSettings> mutate, bool changed)
    {
        if (!changed) { return; }
        _ = _settings.UpdateAsync(s => mutate(s.Alerts), "Alerts");
        OnPropertyChanged(string.Empty); // a threshold clamp may have adjusted a neighbouring value
    }

    private void OnAlertsUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _dispatcher.TryEnqueue(Apply);
    }

    private void Apply()
    {
        IReadOnlyList<Alert> recent = _alerts.RecentAlerts;
        Active = [.. recent.Where(a => !a.Acknowledged).Select(ToRow)];
        History = [.. recent.Select(ToRow)];

        NotificationChannel = _alerts.NotificationsAvailable
            ? "Windows notifications + in-app centre"
            : "In-app centre only — Windows notifications are off or unavailable on this device";
    }

    private static AlertDisplayRow ToRow(Alert a) => new(
        a.Id ?? 0,
        a.Title,
        a.Message,
        a.Severity switch { AlertSeverity.Critical => "", AlertSeverity.Warning => "", _ => "" },
        a.Severity switch { AlertSeverity.Critical => "AppCritBrush", AlertSeverity.Warning => "AppWarnBrush", _ => "AppAccentBrush" },
        DescribeWhen(a.TimestampUtc),
        a.Acknowledged);

    private static string DescribeWhen(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - timestamp;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "Just now",
            { TotalMinutes: < 60 } => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}m ago"),
            { TotalHours: < 24 } => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}h ago"),
            { TotalDays: < 7 } => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalDays}d ago"),
            _ => timestamp.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture),
        };
    }

    public void Dispose() => _alerts.Updated -= OnAlertsUpdated;
}
