using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Sessions;

/// <summary>
/// The charge/discharge session state machine (docs/session-engine.md).
/// </summary>
/// <remarks>
/// <para>
/// A pure state machine over an input event stream, with no dependency on real
/// hardware, timers or the database (docs/session-engine.md section 8) — the
/// caller supplies both wall-clock time and a monotonic tick count on every
/// input, which is what makes the full failure-scenario matrix ordinary,
/// millisecond-fast unit testing instead of something requiring a laptop to
/// suspend and resume repeatedly.
/// </para>
/// <para>
/// Two kinds of transition are treated asymmetrically, deliberately
/// (docs/session-engine.md section 3): an AC line change is immediate and
/// trusted (it comes from a power event, not from rate noise), while a
/// Charging/Discharging state change with AC unchanged must persist for a
/// configurable number of consecutive samples (default 3) before a new session
/// opens.
/// </para>
/// </remarks>
public sealed class SessionStateMachine
{
    private const double GapDetectionFactor = 2.0;

    private readonly int _debounceSampleCount;
    private readonly TimeSpan _interruptionGracePeriod;
    private readonly double _percentageJumpThreshold;
    private readonly TimeSpan _gapDetectionThreshold;
    private readonly double _fullPercentageThreshold;

    private OpenSession? _openSession;
    private BatteryState _pendingState = BatteryState.Unknown;
    private int _pendingCount;
    private SessionEngineInput? _pendingStartInput;
    private bool? _lastAcOnline;
    private double? _lastPercentage;
    private DateTimeOffset? _lastTimestamp;
    private long? _lastMonotonicTicksMs;
    private bool _suppressNextGapCheck;

    public SessionStateMachine(
        int debounceSampleCount = 3,
        TimeSpan? interruptionGracePeriod = null,
        double percentageJumpThreshold = 25.0,
        TimeSpan? gapDetectionThreshold = null,
        double fullPercentageThreshold = 99.5)
    {
        _debounceSampleCount = Math.Max(1, debounceSampleCount);
        _interruptionGracePeriod = interruptionGracePeriod ?? TimeSpan.FromMinutes(15);
        _percentageJumpThreshold = percentageJumpThreshold;
        _gapDetectionThreshold = gapDetectionThreshold ?? TimeSpan.FromSeconds(90);
        _fullPercentageThreshold = fullPercentageThreshold;
    }

    /// <summary>The currently open session, or <see langword="null"/> when idle.</summary>
    public BatterySessionInfo? CurrentSession => _openSession?.ToInfo();

    /// <summary>
    /// Resumes tracking an already-open session found in the database at startup
    /// (docs/session-engine.md section 6, "adopt"). The next <see cref="Process"/>
    /// call continues it rather than treating the machine as freshly idle.
    /// </summary>
    public void Adopt(BatterySessionInfo existingOpenSession)
    {
        ArgumentNullException.ThrowIfNull(existingOpenSession);
        if (!existingOpenSession.IsOpen)
        {
            throw new ArgumentException("Only an open session (EndUtc null) can be adopted.", nameof(existingOpenSession));
        }

        _openSession = OpenSession.FromInfo(existingOpenSession);
    }

    /// <summary>
    /// Records the database id assigned to the currently open session, once the
    /// orchestrator has actually persisted the row a <see cref="Process"/> call
    /// just opened (its <c>OpenedSession</c> carries <see langword="null"/>
    /// until this is called).
    /// </summary>
    public void AssignSessionId(long id)
    {
        if (_openSession is OpenSession session)
        {
            session.Id = id;
        }
    }

    /// <summary>Processes one reading. See the type's remarks for the transition rules.</summary>
    public SessionEngineTickResult Process(SessionEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_lastTimestamp is DateTimeOffset last && input.TimestampUtc <= last)
        {
            // Clock moved backwards (NTP correction): reject, keep prior state
            // (docs/session-engine.md section 8, "clock steps backwards").
            return SessionEngineTickResult.RejectedResult;
        }

        SystemEventKind? inferredEvent = DetectGap(input);
        bool jumpSuspect = DetectPercentageJump(input, inferredEvent);

        AccumulateElapsedTime(input, inferredEvent);

        BatterySessionInfo? closed = null;
        BatterySessionInfo? opened = null;
        bool interruptionRecorded = false;

        bool? acTransitioned = _lastAcOnline is bool previousAc && input.AcOnline is bool currentAc && previousAc != currentAc
            ? currentAc
            : null;

        if (input.BatteryState == BatteryState.NotPresent && _openSession is not null)
        {
            closed = CloseSession(input.TimestampUtc, SessionEndReason.BatteryRemoved, closedCleanly: true);
        }
        else if (_openSession is { Type: SessionType.Charging })
        {
            (closed, opened, interruptionRecorded) = ProcessOpenCharging(input, acTransitioned);
        }
        else if (_openSession is { Type: SessionType.Discharging })
        {
            (closed, opened) = ProcessOpenDischarging(input, acTransitioned);
        }
        else
        {
            opened = ProcessIdle(input);
        }

        _lastAcOnline = input.AcOnline ?? _lastAcOnline;
        _lastPercentage = input.Percentage ?? _lastPercentage;
        _lastTimestamp = input.TimestampUtc;
        _lastMonotonicTicksMs = input.MonotonicTicksMs;
        _suppressNextGapCheck = false;

        return new SessionEngineTickResult(
            Rejected: false,
            ClosedSession: closed,
            OpenedSession: opened,
            OpenSessionState: _openSession?.ToInfo(),
            InterruptionRecorded: interruptionRecorded,
            InferredSystemEvent: inferredEvent,
            PercentageJumpSuspect: jumpSuspect);
    }

    /// <summary>
    /// An explicit, notified suspend (<c>PBT_APMSUSPEND</c>) — trusted, not
    /// inferred. The next real sample's gap check is suppressed so the same gap
    /// is not counted twice.
    /// </summary>
    public SessionEngineTickResult ProcessSuspend(DateTimeOffset timestampUtc)
    {
        _lastTimestamp = timestampUtc;
        return SessionEngineTickResult.Empty(_openSession?.ToInfo());
    }

    /// <summary>
    /// An explicit, notified resume. Attributes the elapsed wall-clock time to
    /// the open session's <c>SleepSeconds</c> and suppresses the next gap check
    /// (docs/session-engine.md section 5, "on resume", steps 4-5).
    /// </summary>
    public SessionEngineTickResult ProcessResume(DateTimeOffset resumeTimestampUtc, DateTimeOffset suspendedAtUtc)
    {
        if (_openSession is not null && resumeTimestampUtc > suspendedAtUtc)
        {
            _openSession.SleepSeconds += (long)Math.Round((resumeTimestampUtc - suspendedAtUtc).TotalSeconds);
        }

        _lastTimestamp = resumeTimestampUtc;
        _suppressNextGapCheck = true;

        return SessionEngineTickResult.Empty(_openSession?.ToInfo());
    }

    private SystemEventKind? DetectGap(SessionEngineInput input)
    {
        if (_suppressNextGapCheck || _lastTimestamp is not DateTimeOffset previous || _lastMonotonicTicksMs is not long previousTicks)
        {
            return null;
        }

        double wallClockDeltaMs = (input.TimestampUtc - previous).TotalMilliseconds;
        double tickDeltaMs = input.MonotonicTicksMs - previousTicks;

        if (wallClockDeltaMs <= _gapDetectionThreshold.TotalMilliseconds || wallClockDeltaMs <= tickDeltaMs * GapDetectionFactor)
        {
            return null;
        }

        // The wall clock advanced far more than the monotonic clock: the
        // machine was asleep and no suspend/resume notification ever arrived.
        double inferredSleepSeconds = Math.Max(0, (wallClockDeltaMs - Math.Max(tickDeltaMs, 0)) / 1000.0);
        if (_openSession is not null)
        {
            _openSession.SleepSeconds += (long)Math.Round(inferredSleepSeconds);
        }

        return SystemEventKind.InferredSleepGap;
    }

    private bool DetectPercentageJump(SessionEngineInput input, SystemEventKind? inferredEvent)
    {
        // A jump across a detected sleep gap is normal (the machine charged or
        // drained while suspended) and must not be flagged
        // (docs/monitoring-dataflow.md section 4).
        if (inferredEvent == SystemEventKind.InferredSleepGap)
        {
            return false;
        }

        return PercentageJumpDetector.IsSuspiciousJump(_lastPercentage, input.Percentage, _percentageJumpThreshold);
    }

    private void AccumulateElapsedTime(SessionEngineInput input, SystemEventKind? inferredEvent)
    {
        if (_openSession is not OpenSession session || _lastTimestamp is not DateTimeOffset previous)
        {
            return;
        }

        // Time already attributed to sleep by gap detection must not also be
        // counted as screen-on/off time.
        if (inferredEvent == SystemEventKind.InferredSleepGap)
        {
            return;
        }

        long elapsedSeconds = (long)Math.Round((input.TimestampUtc - previous).TotalSeconds);
        if (elapsedSeconds <= 0)
        {
            return;
        }

        if (input.Screen is ScreenState.Off or ScreenState.Dimmed)
        {
            session.ScreenOffSeconds += elapsedSeconds;
        }
        else
        {
            session.ScreenOnSeconds += elapsedSeconds;
        }
    }

    private (BatterySessionInfo? Closed, BatterySessionInfo? Opened, bool Interrupted) ProcessOpenCharging(
        SessionEngineInput input, bool? acTransitioned)
    {
        UpdateLastKnownValues(input);

        if (acTransitioned == false)
        {
            // AC disconnected: immediate, trusted close, and discharging begins
            // at once — there is no ambiguity to debounce (docs/session-engine.md's transition table).
            BatterySessionInfo closed = CloseSession(input.TimestampUtc, SessionEndReason.ChargerDisconnected, closedCleanly: true)!;
            BatterySessionInfo opened = OpenSessionInternal(SessionType.Discharging, input);
            return (closed, opened, false);
        }

        if (input.BatteryState == BatteryState.Full
            || (input.Percentage is double pct && pct >= _fullPercentageThreshold))
        {
            return (CloseSession(input.TimestampUtc, SessionEndReason.ReachedFull, closedCleanly: true), null, false);
        }

        if (input.BatteryState == BatteryState.Idle)
        {
            if (_openSession!.IdleSince is null)
            {
                _openSession.IdleSince = input.TimestampUtc;
                return (null, null, false);
            }

            if (input.TimestampUtc - _openSession.IdleSince.Value >= _interruptionGracePeriod)
            {
                return (CloseSession(input.TimestampUtc, SessionEndReason.ChargingStopped, closedCleanly: true), null, false);
            }

            return (null, null, false);
        }

        if (input.BatteryState == BatteryState.Charging && _openSession!.IdleSince is not null)
        {
            // Resumed before the grace period elapsed: an interruption on the
            // same session, not a new one (docs/session-engine.md section 3).
            _openSession.IdleSince = null;
            _openSession.Interruptions++;
            return (null, null, true);
        }

        return (null, null, false);
    }

    private (BatterySessionInfo? Closed, BatterySessionInfo? Opened) ProcessOpenDischarging(
        SessionEngineInput input, bool? acTransitioned)
    {
        UpdateLastKnownValues(input);

        if (acTransitioned != true)
        {
            // Discharging only ends on AC reconnecting — never on a debounced
            // rate reading (docs/session-engine.md's transition table).
            return (null, null);
        }

        SessionEndReason reason = SessionEndReason.ChargerConnected;
        BatterySessionInfo closed = CloseSession(input.TimestampUtc, reason, closedCleanly: true)!;

        BatterySessionInfo? opened = input.BatteryState == BatteryState.Charging
            ? OpenSessionInternal(SessionType.Charging, input)
            : null; // AC connected but not yet charging: Idle, no new session (transition table).

        return (closed, opened);
    }

    private BatterySessionInfo? ProcessIdle(SessionEngineInput input)
    {
        if (input.BatteryState is not (BatteryState.Charging or BatteryState.Discharging))
        {
            _pendingState = BatteryState.Unknown;
            _pendingCount = 0;
            _pendingStartInput = null;
            return null;
        }

        if (input.BatteryState == _pendingState)
        {
            _pendingCount++;
        }
        else
        {
            _pendingState = input.BatteryState;
            _pendingCount = 1;
            _pendingStartInput = input;
        }

        if (_pendingCount < _debounceSampleCount)
        {
            return null;
        }

        // Backdated to the first sample of the confirmed streak, not the one
        // that confirmed it — the session genuinely began there, and losing a
        // debounce window's worth of it from the record would understate the
        // session's true duration and starting percentage.
        SessionEngineInput startInput = _pendingStartInput ?? input;
        _pendingCount = 0;
        _pendingState = BatteryState.Unknown;
        _pendingStartInput = null;

        SessionType type = input.BatteryState == BatteryState.Charging ? SessionType.Charging : SessionType.Discharging;
        return OpenSessionInternal(type, startInput);
    }

    private BatterySessionInfo OpenSessionInternal(SessionType type, SessionEngineInput input)
    {
        _openSession = new OpenSession
        {
            BatteryId = input.BatteryId,
            Type = type,
            StartUtc = input.TimestampUtc,
            StartPercentage = input.Percentage,
            StartCapacityMwh = input.RemainingCapacityMWh,
            LastPercentage = input.Percentage,
            LastCapacityMwh = input.RemainingCapacityMWh,
        };

        return _openSession.ToInfo();
    }

    private BatterySessionInfo? CloseSession(DateTimeOffset endUtc, SessionEndReason reason, bool closedCleanly)
    {
        if (_openSession is not OpenSession session)
        {
            return null;
        }

        BatterySessionInfo info = session.ToInfo(endUtc, reason, closedCleanly);
        _openSession = null;
        return info;
    }

    private void UpdateLastKnownValues(SessionEngineInput input)
    {
        if (_openSession is not OpenSession session)
        {
            return;
        }

        session.LastPercentage = input.Percentage ?? session.LastPercentage;
        session.LastCapacityMwh = input.RemainingCapacityMWh ?? session.LastCapacityMwh;
    }
}
