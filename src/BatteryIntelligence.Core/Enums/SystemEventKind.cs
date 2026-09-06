namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// What a system-wide <c>SystemEvent</c> row records (docs/session-engine.md
/// sections 4-5). Persisted; never renumber.
/// </summary>
public enum SystemEventKind
{
    Unknown = 0,
    Suspend = 1,
    Resume = 2,
    ScreenOn = 3,
    ScreenOff = 4,
    ScreenDimmed = 5,
    Locked = 6,
    Unlocked = 7,
    AcConnected = 8,
    AcDisconnected = 9,

    /// <summary>
    /// Gap detection inferred an unnotified suspend/resume from a wall-clock vs
    /// monotonic-uptime discrepancy between two consecutive samples — no
    /// <c>PBT_APMSUSPEND</c>/<c>PBT_APMRESUMEAUTOMATIC</c> was ever delivered
    /// (docs/session-engine.md section 5, "gap detection").
    /// </summary>
    InferredSleepGap = 10,

    BatteryRemoved = 11,
    BatteryArrived = 12,
}
