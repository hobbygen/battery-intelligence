namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// Display power state (<c>GUID_CONSOLE_DISPLAY_STATE</c>, S5). Orthogonal to
/// <see cref="LockState"/> and <see cref="SystemPowerState"/> — screen-off is
/// not sleep, and locked is not screen-off (specification section 50).
/// Persisted (<c>BatterySample.ScreenState</c>); never renumber.
/// </summary>
public enum ScreenState
{
    Unknown = 0,
    On = 1,
    Off = 2,
    Dimmed = 3,
}
