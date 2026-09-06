namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// Which API or subsystem supplied a value.
/// </summary>
/// <remarks>
/// Persisted on every sample so the Diagnostics page can always explain
/// provenance. Numeric values must never be renumbered. Identifiers correspond
/// to the source inventory in docs/capability-matrix.md section 2.
/// </remarks>
public enum MeasurementSource
{
    /// <summary>Source not recorded.</summary>
    Unknown = 0,

    /// <summary>S1 — <c>Windows.Devices.Power.Battery</c> (WinRT).</summary>
    WinRtBattery = 1,

    /// <summary>S2 — <c>GetSystemPowerStatus</c> (Win32).</summary>
    SystemPowerStatus = 2,

    /// <summary>S3 — WMI <c>root\wmi</c> battery classes.</summary>
    Wmi = 3,

    /// <summary>S4 — <c>IOCTL_BATTERY_QUERY_INFORMATION</c> via the battery device interface.</summary>
    BatteryIoctl = 4,

    /// <summary>S5 — <c>RegisterPowerSettingNotification</c> power events.</summary>
    PowerSettingNotification = 5,

    /// <summary>S6 — terminal services session notifications (lock/unlock).</summary>
    SessionNotification = 6,

    /// <summary>S7 — <c>WM_POWERBROADCAST</c> suspend/resume.</summary>
    PowerBroadcast = 7,

    /// <summary>S8 — process diagnostics and performance counters.</summary>
    ProcessDiagnostics = 8,

    /// <summary>Derived arithmetically from other measurements rather than read.</summary>
    Derived = 20,

    /// <summary>Produced by an estimation model rather than read.</summary>
    Model = 21,

    /// <summary>Reconstructed from a detected gap rather than observed directly.</summary>
    Inferred = 22,

    /// <summary>A simulated provider. Never present in Release builds.</summary>
    Simulation = 90,
}
