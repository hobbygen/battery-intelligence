using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Sessions;

/// <summary>
/// One tick's worth of input to <see cref="SessionStateMachine"/> — exactly the
/// tuple docs/session-engine.md section 8 specifies:
/// <c>(timestamp, batteryState, acState, screenState, systemState)</c>, plus a
/// caller-supplied monotonic clock reading for gap detection.
/// </summary>
/// <param name="TimestampUtc">Wall-clock time of this reading.</param>
/// <param name="MonotonicTicksMs">
/// A monotonic uptime counter (e.g. <c>Environment.TickCount64</c>), supplied by
/// the caller rather than read internally — this is what keeps the engine
/// testable with a fake clock (docs/session-engine.md section 8).
/// </param>
/// <param name="BatteryId">Which battery this reading belongs to.</param>
/// <param name="BatteryState">Charging / Discharging / Idle / Full / Unknown / NotPresent.</param>
/// <param name="AcOnline">AC line state; the trusted, immediate signal for Charging/Discharging boundary transitions.</param>
/// <param name="Percentage">Current charge percentage, if known.</param>
/// <param name="RemainingCapacityMWh">Current remaining capacity, if known.</param>
/// <param name="Screen">Display power state.</param>
/// <param name="Lock">Session lock state.</param>
public sealed record SessionEngineInput(
    DateTimeOffset TimestampUtc,
    long MonotonicTicksMs,
    string BatteryId,
    BatteryState BatteryState,
    bool? AcOnline,
    double? Percentage,
    int? RemainingCapacityMWh,
    ScreenState Screen,
    LockState Lock);

/// <summary>
/// Mutable working state for the currently open session. Distinct from the
/// immutable, persisted-shape <see cref="Models.BatterySessionInfo"/> that the
/// orchestrator writes to the database and exposes to the UI.
/// </summary>
public sealed class OpenSession
{
    public long? Id { get; set; }

    public required string BatteryId { get; init; }

    public required SessionType Type { get; init; }

    public required DateTimeOffset StartUtc { get; init; }

    public double? StartPercentage { get; init; }

    public int? StartCapacityMwh { get; init; }

    public double? LastPercentage { get; set; }

    public int? LastCapacityMwh { get; set; }

    public long ScreenOnSeconds { get; set; }

    public long ScreenOffSeconds { get; set; }

    public long SleepSeconds { get; set; }

    public int Interruptions { get; set; }

    /// <summary>
    /// Set when a Charging session observes Idle with AC still connected — the
    /// possible start of an interruption (docs/session-engine.md section 3).
    /// <see langword="null"/> when not currently in a pause.
    /// </summary>
    public DateTimeOffset? IdleSince { get; set; }

    public Models.BatterySessionInfo ToInfo(
        DateTimeOffset? endUtc = null, SessionEndReason? endReason = null, bool closedCleanly = false) => new()
    {
        Id = Id,
        BatteryId = BatteryId,
        Type = Type,
        StartUtc = StartUtc,
        EndUtc = endUtc,
        StartPercentage = StartPercentage,
        EndPercentage = endUtc is not null ? LastPercentage : null,
        StartCapacityMwh = StartCapacityMwh,
        EndCapacityMwh = endUtc is not null ? LastCapacityMwh : null,
        ScreenOnSeconds = ScreenOnSeconds,
        ScreenOffSeconds = ScreenOffSeconds,
        SleepSeconds = SleepSeconds,
        Interruptions = Interruptions,
        ClosedCleanly = closedCleanly,
        EndReason = endReason,
    };

    public static OpenSession FromInfo(Models.BatterySessionInfo info) => new()
    {
        Id = info.Id,
        BatteryId = info.BatteryId,
        Type = info.Type,
        StartUtc = info.StartUtc,
        StartPercentage = info.StartPercentage,
        StartCapacityMwh = info.StartCapacityMwh,
        LastPercentage = info.StartPercentage,
        LastCapacityMwh = info.StartCapacityMwh,
        ScreenOnSeconds = info.ScreenOnSeconds,
        ScreenOffSeconds = info.ScreenOffSeconds,
        SleepSeconds = info.SleepSeconds,
        Interruptions = info.Interruptions,
    };
}

/// <summary>Everything that happened as a result of one <see cref="SessionStateMachine"/> tick.</summary>
/// <param name="Rejected">
/// <see langword="true"/> when the sample's timestamp moved backwards relative
/// to the last accepted one — the engine ignored it entirely and kept its prior
/// state (docs/session-engine.md section 5, "clock steps backwards").
/// </param>
/// <param name="ClosedSession">A session that closed this tick, ready to persist with its final <c>EndUtc</c>/<c>EndReason</c>.</param>
/// <param name="OpenedSession">A session that opened this tick.</param>
/// <param name="OpenSessionState">
/// The live accumulators of whichever session is open after this tick (which
/// may be <paramref name="OpenedSession"/>, a pre-existing one, or
/// <see langword="null"/> if none) — for periodic persistence of running
/// totals without waiting for the session to close.
/// </param>
/// <param name="InterruptionRecorded">Whether a charging interruption was recorded on the currently open session this tick.</param>
/// <param name="InferredSystemEvent">A system event gap detection inferred this tick, if any.</param>
/// <param name="PercentageJumpSuspect">
/// Whether this sample's percentage jumped implausibly while the system was
/// awake — the caller should re-grade the sample Suspect
/// (docs/monitoring-dataflow.md section 4).
/// </param>
public sealed record SessionEngineTickResult(
    bool Rejected,
    Models.BatterySessionInfo? ClosedSession,
    Models.BatterySessionInfo? OpenedSession,
    Models.BatterySessionInfo? OpenSessionState,
    bool InterruptionRecorded,
    SystemEventKind? InferredSystemEvent,
    bool PercentageJumpSuspect)
{
    public static SessionEngineTickResult Empty(Models.BatterySessionInfo? openSessionState = null) =>
        new(false, null, null, openSessionState, false, null, false);

    public static SessionEngineTickResult RejectedResult { get; } = new(true, null, null, null, false, null, false);
}
