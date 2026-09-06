namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// What a <c>SessionEvent</c> row records — something that happened
/// <em>within</em> an open session, as opposed to the session's own start/end
/// (docs/session-engine.md section 7). Persisted; never renumber.
/// </summary>
public enum SessionEventKind
{
    Unknown = 0,

    /// <summary>
    /// Charging paused (AC still connected) and resumed before the interruption
    /// grace period elapsed — the session stayed open throughout
    /// (docs/session-engine.md section 3).
    /// </summary>
    Interruption = 1,

    /// <summary>
    /// A restarted application adopted this session rather than closing it
    /// (docs/session-engine.md section 6).
    /// </summary>
    Adopted = 2,

    /// <summary>
    /// A percentage jump was observed while the system was awake — flagged
    /// Suspect rather than corrupting the session's rate maths
    /// (docs/monitoring-dataflow.md section 4).
    /// </summary>
    PercentageJumpSuspect = 3,
}
