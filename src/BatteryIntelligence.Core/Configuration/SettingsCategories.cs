using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Configuration;

/// <summary>General behaviour. Specification section 35, "General".</summary>
public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }

    public bool StartMinimized { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool StartMonitoringImmediately { get; set; } = true;

    /// <summary>Navigation tag of the page shown at startup.</summary>
    public string DefaultPage { get; set; } = "Dashboard";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DefaultPage))
        {
            DefaultPage = "Dashboard";
        }
    }
}

/// <summary>Theme and density. Specification section 35, "Appearance".</summary>
public sealed class AppearanceSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public DisplayDensity Density { get; set; } = DisplayDensity.Comfortable;

    /// <summary>Whether the navigation pane is expanded.</summary>
    public bool NavigationPaneOpen { get; set; } = true;

    public void Validate()
    {
        if (!Enum.IsDefined(Theme))
        {
            Theme = ThemePreference.System;
        }

        if (!Enum.IsDefined(Density))
        {
            Density = DisplayDensity.Comfortable;
        }
    }
}

/// <summary>
/// Sampling intervals. Specification sections 30 and 35, "Monitoring".
/// </summary>
/// <remarks>
/// Defaults come from docs/monitoring-dataflow.md section 3. Lower bounds exist
/// because the application must not be configurable into consuming more power
/// than it measures.
/// </remarks>
public sealed class MonitoringSettings
{
    public int BatteryVerifySeconds { get; set; } = 30;

    public int PowerSampleSeconds { get; set; } = 5;

    public int TemperatureSampleSeconds { get; set; } = 10;

    public int ProcessSampleSeconds { get; set; } = 10;

    /// <summary>
    /// Whether sampling rates back off when detail cannot matter — for example
    /// while the screen is off. See docs/monitoring-dataflow.md section 3.
    /// </summary>
    public bool AdaptiveSampling { get; set; } = true;

    public bool Paused { get; set; }

    public void Validate()
    {
        BatteryVerifySeconds = Math.Clamp(BatteryVerifySeconds, 5, 600);
        PowerSampleSeconds = Math.Clamp(PowerSampleSeconds, 1, 300);
        TemperatureSampleSeconds = Math.Clamp(TemperatureSampleSeconds, 2, 600);
        ProcessSampleSeconds = Math.Clamp(ProcessSampleSeconds, 5, 600);
    }
}

/// <summary>Alert thresholds. Specification sections 20 and 35, "Alerts".</summary>
public sealed class AlertSettings
{
    public bool LowBatteryEnabled { get; set; } = true;

    public int LowBatteryPercent { get; set; } = 20;

    public bool CriticalBatteryEnabled { get; set; } = true;

    public int CriticalBatteryPercent { get; set; } = 10;

    public bool FullyChargedEnabled { get; set; } = true;

    public int FullyChargedPercent { get; set; } = 100;

    public bool HighTemperatureEnabled { get; set; } = true;

    /// <summary>
    /// Warning threshold in degrees Celsius. Conservative by default, and only
    /// meaningful where the hardware exposes a battery temperature sensor.
    /// </summary>
    public int HighTemperatureCelsius { get; set; } = 45;

    public bool RapidDischargeEnabled { get; set; }

    public bool SlowChargingEnabled { get; set; }

    public bool ChargerConnectedEnabled { get; set; }

    public bool ChargerDisconnectedEnabled { get; set; }

    public bool HealthDegradationEnabled { get; set; } = true;

    public bool HighApplicationConsumptionEnabled { get; set; }

    /// <summary>
    /// Minimum interval between repeats of the same alert, preventing
    /// notification spam (specification section 20).
    /// </summary>
    public int CooldownMinutes { get; set; } = 15;

    public void Validate()
    {
        LowBatteryPercent = Math.Clamp(LowBatteryPercent, 1, 99);
        CriticalBatteryPercent = Math.Clamp(CriticalBatteryPercent, 1, 99);
        FullyChargedPercent = Math.Clamp(FullyChargedPercent, 50, 100);
        HighTemperatureCelsius = Math.Clamp(HighTemperatureCelsius, 30, 80);
        CooldownMinutes = Math.Clamp(CooldownMinutes, 1, 1440);

        // Critical must be strictly below low, or the two alerts fight.
        if (CriticalBatteryPercent >= LowBatteryPercent)
        {
            CriticalBatteryPercent = Math.Max(1, LowBatteryPercent - 1);
        }
    }
}

/// <summary>Notification delivery. Specification section 35, "Notifications".</summary>
public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    public bool UseWindowsNotifications { get; set; } = true;

    public bool UseInAppAlerts { get; set; } = true;

    public bool PlaySound { get; set; }

    public void Validate()
    {
        // If every delivery channel is disabled, alerts would fire silently into
        // nothing. Keep the in-app centre as the floor.
        if (Enabled && !UseWindowsNotifications && !UseInAppAlerts)
        {
            UseInAppAlerts = true;
        }
    }
}

/// <summary>Retention and storage. Specification sections 29 and 35, "Data".</summary>
public sealed class DataSettings
{
    public int RawRetentionDays { get; set; } = 7;

    public int MinuteRetentionDays { get; set; } = 90;

    public int HourRetentionDays { get; set; } = 365;

    /// <summary>Daily aggregate retention in days; zero means keep indefinitely.</summary>
    public int DailyRetentionDays { get; set; }

    /// <summary>
    /// Override for the database directory. Empty means the default location
    /// under <c>%LocalAppData%</c>.
    /// </summary>
    public string DatabaseDirectory { get; set; } = string.Empty;

    public void Validate()
    {
        RawRetentionDays = Math.Clamp(RawRetentionDays, 1, 3650);
        MinuteRetentionDays = Math.Clamp(MinuteRetentionDays, 1, 3650);
        HourRetentionDays = Math.Clamp(HourRetentionDays, 1, 3650);
        DailyRetentionDays = Math.Clamp(DailyRetentionDays, 0, 3650);

        // Each tier must retain at least as long as the one below it, or data
        // would be discarded before it has been rolled up. See docs/database.md.
        MinuteRetentionDays = Math.Max(MinuteRetentionDays, RawRetentionDays);
        HourRetentionDays = Math.Max(HourRetentionDays, MinuteRetentionDays);

        if (DailyRetentionDays != 0)
        {
            DailyRetentionDays = Math.Max(DailyRetentionDays, HourRetentionDays);
        }
    }
}

/// <summary>Diagnostics and logging. Specification section 35, "Advanced".</summary>
public sealed class AdvancedSettings
{
    public LogVerbosity LogLevel { get; set; } = LogVerbosity.Information;

    public bool ExperimentalFeatures { get; set; }

    public void Validate()
    {
        if (!Enum.IsDefined(LogLevel))
        {
            LogLevel = LogVerbosity.Information;
        }
    }
}

/// <summary>
/// Persisted main-window geometry.
/// </summary>
/// <remarks>
/// Restored geometry is validated against the current display arrangement before
/// use, so a window saved on a monitor that is no longer attached cannot restore
/// off-screen. See <c>WindowStateService</c>.
/// </remarks>
public sealed class WindowStateSettings
{
    public int Left { get; set; } = -1;

    public int Top { get; set; } = -1;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 820;

    public bool IsMaximized { get; set; }

    public void Validate()
    {
        Width = Math.Clamp(Width, MinimumWidth, 10000);
        Height = Math.Clamp(Height, MinimumHeight, 10000);
    }

    /// <summary>Minimum usable window width. See docs/ui-navigation.md section 3.</summary>
    public const int MinimumWidth = 960;

    /// <summary>Minimum usable window height. See docs/ui-navigation.md section 3.</summary>
    public const int MinimumHeight = 640;
}
