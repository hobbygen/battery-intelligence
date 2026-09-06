namespace BatteryIntelligence.Core.Enums;

/// <summary>Session lock state (WTS session notifications, S6). Orthogonal to <see cref="ScreenState"/>.</summary>
public enum LockState
{
    Unknown = 0,
    Unlocked = 1,
    Locked = 2,
}
