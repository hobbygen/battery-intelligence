namespace BatteryIntelligence.Core.Enums;

/// <summary>Why a session closed. Persisted (<c>BatterySession.EndReason</c>); never renumber.</summary>
public enum SessionEndReason
{
    /// <summary>Not recorded (a session closed before this column existed, or session still open).</summary>
    Unknown = 0,

    /// <summary>Charging reached 100% while AC remained connected.</summary>
    ReachedFull = 1,

    /// <summary>Charging stopped with AC still connected, and did not resume within the interruption grace period (docs/session-engine.md section 3).</summary>
    ChargingStopped = 2,

    /// <summary>AC was disconnected while charging.</summary>
    ChargerDisconnected = 3,

    /// <summary>AC was connected while discharging.</summary>
    ChargerConnected = 4,

    /// <summary>
    /// The application was not running when the session should have continued or
    /// closed (crash, forced shutdown, or a restart outside the adoption grace
    /// window) — see docs/session-engine.md section 6. Real history, marked
    /// incomplete rather than erased.
    /// </summary>
    Interrupted = 5,

    /// <summary>The battery device disappeared while a session was open.</summary>
    BatteryRemoved = 6,
}
