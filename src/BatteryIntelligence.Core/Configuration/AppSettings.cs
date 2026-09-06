namespace BatteryIntelligence.Core.Configuration;

/// <summary>
/// Root of user-configurable application settings, persisted as JSON.
/// </summary>
/// <remarks>
/// Categories mirror the Settings page layout in the specification (section 35).
/// Every property has a usable default, so a missing or partially corrupt
/// settings file degrades to defaults rather than preventing startup.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>
    /// Version of the settings shape, for future migration.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    public GeneralSettings General { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public MonitoringSettings Monitoring { get; set; } = new();

    public ProcessMonitoringSettings Processes { get; set; } = new();

    public AlertSettings Alerts { get; set; } = new();

    public NotificationSettings Notifications { get; set; } = new();

    public DataSettings Data { get; set; } = new();

    public AdvancedSettings Advanced { get; set; } = new();

    public WindowStateSettings WindowState { get; set; } = new();

    /// <summary>
    /// Clamps every setting into its valid range.
    /// </summary>
    /// <remarks>
    /// Called after loading. A settings file can be hand-edited or corrupted, so
    /// values are treated as untrusted input (specification section 46) and
    /// repaired rather than trusted or rejected outright.
    /// </remarks>
    public void Validate()
    {
        General.Validate();
        Appearance.Validate();
        Monitoring.Validate();
        Processes.Validate();
        Alerts.Validate();
        Notifications.Validate();
        Data.Validate();
        Advanced.Validate();
        WindowState.Validate();
    }
}
