namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// Whether the system is running or suspended (<c>WM_POWERBROADCAST</c>, S7).
/// </summary>
/// <remarks>
/// Windows' suspend/resume broadcast does not distinguish standby (S3) from
/// hibernate (S4) — both deliver the same <c>PBT_APMSUSPEND</c> — so this
/// deliberately has one "suspended" value rather than inventing a distinction
/// the platform does not expose (docs/session-engine.md section 4 lists
/// Awake/Sleeping/Hibernated conceptually, but only two states are actually
/// observable from user mode without additional privilege).
/// </remarks>
public enum SystemPowerState
{
    Unknown = 0,
    Awake = 1,
    Suspended = 2,
}
