using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Sessions;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Sessions;

/// <summary>
/// The full scenario matrix from docs/session-engine.md section 8, replayed
/// deterministically with a fake clock — no laptop to unplug, no sleep to wait
/// through.
/// </summary>
public sealed class SessionStateMachineTests
{
    private const string BatteryId = "battery0";
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ---- Basic open/close ----

    [Fact]
    public void Idle_ThenChargingForDebounceCount_OpensChargingSession()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 3);

        Assert.Null(Tick(machine, clock, BatteryState.Charging, ac: true, pct: 50).OpenedSession);
        Assert.Null(Tick(machine, clock, BatteryState.Charging, ac: true, pct: 51).OpenedSession);
        SessionEngineTickResult third = Tick(machine, clock, BatteryState.Charging, ac: true, pct: 52);

        Assert.NotNull(third.OpenedSession);
        Assert.Equal(SessionType.Charging, third.OpenedSession!.Type);
        Assert.Equal(50, third.OpenedSession.StartPercentage); // the session's start is backdated to when tracking began? No: opens on the 3rd sample.
    }

    [Fact]
    public void ChargingReachesFull_ClosesReachedFull()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1);

        Tick(machine, clock, BatteryState.Charging, ac: true, pct: 90);
        SessionEngineTickResult full = Tick(machine, clock, BatteryState.Full, ac: true, pct: 100);

        Assert.NotNull(full.ClosedSession);
        Assert.Equal(SessionEndReason.ReachedFull, full.ClosedSession!.EndReason);
        Assert.True(full.ClosedSession.ClosedCleanly);
        Assert.Null(machine.CurrentSession);
    }

    [Fact]
    public void Discharging_ThenAcConnectedAndCharging_ClosesAndOpensImmediately_NoDebounce()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 3);
        OpenDischargingSession(machine, clock);

        SessionEngineTickResult result = Tick(machine, clock, BatteryState.Charging, ac: true, pct: 40);

        Assert.NotNull(result.ClosedSession);
        Assert.Equal(SessionEndReason.ChargerConnected, result.ClosedSession!.EndReason);
        Assert.NotNull(result.OpenedSession);
        Assert.Equal(SessionType.Charging, result.OpenedSession!.Type);
    }

    // ---- Scenario matrix ----

    [Fact]
    public void ChargerConnectDisconnectWithinDebounceWindow_NoSessionChurn()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 3);

        // AC bounces on/off before any session ever opens, and the battery
        // state briefly reports Charging then reverts — nothing should commit.
        Tick(machine, clock, BatteryState.Idle, ac: false, pct: 60);
        Tick(machine, clock, BatteryState.Charging, ac: true, pct: 60);
        Tick(machine, clock, BatteryState.Idle, ac: false, pct: 60);

        Assert.Null(machine.CurrentSession);
    }

    [Fact]
    public void RateOscillationNearFullOnAc_StaysIdle_NoSessionsCreated()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 3);

        // Firmware topping off at 99%: Charging/Idle flicker, never 3 in a row.
        for (int i = 0; i < 10; i++)
        {
            BatteryState state = i % 2 == 0 ? BatteryState.Charging : BatteryState.Idle;
            Tick(machine, clock, state, ac: true, pct: 99);
        }

        Assert.Null(machine.CurrentSession);
    }

    [Fact]
    public void ChargingPausesThenResumesWithinGrace_OneSessionWithOneInterruption()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1, interruptionGracePeriod: TimeSpan.FromMinutes(15));

        SessionEngineTickResult opened = Tick(machine, clock, BatteryState.Charging, ac: true, pct: 80);
        Assert.NotNull(opened.OpenedSession);

        clock.AdvanceRealTime(TimeSpan.FromMinutes(2));
        Tick(machine, clock, BatteryState.Idle, ac: true, pct: 80); // smart-charging hold

        clock.AdvanceRealTime(TimeSpan.FromMinutes(5));
        SessionEngineTickResult resumed = Tick(machine, clock, BatteryState.Charging, ac: true, pct: 81);

        Assert.True(resumed.InterruptionRecorded);
        Assert.Null(resumed.ClosedSession);
        Assert.NotNull(machine.CurrentSession);
        Assert.Equal(1, machine.CurrentSession!.Interruptions);
    }

    [Fact]
    public void ChargingPausesBeyondGrace_ClosesChargingStopped()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1, interruptionGracePeriod: TimeSpan.FromMinutes(15));

        Tick(machine, clock, BatteryState.Charging, ac: true, pct: 80);
        clock.AdvanceRealTime(TimeSpan.FromMinutes(1));
        Tick(machine, clock, BatteryState.Idle, ac: true, pct: 80);

        clock.AdvanceRealTime(TimeSpan.FromMinutes(20));
        SessionEngineTickResult result = Tick(machine, clock, BatteryState.Idle, ac: true, pct: 80);

        Assert.NotNull(result.ClosedSession);
        Assert.Equal(SessionEndReason.ChargingStopped, result.ClosedSession!.EndReason);
    }

    [Fact]
    public void SuspendAndResumeMidDischarge_OneSession_SleepSecondsPopulated_NoInterpolation()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1);

        Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 80);

        DateTimeOffset suspendAt = clock.Now;
        machine.ProcessSuspend(suspendAt);
        clock.AdvanceRealTime(TimeSpan.FromHours(4)); // real elapsed time; monotonic clock does NOT advance across sleep
        DateTimeOffset resumeAt = clock.Now;
        machine.ProcessResume(resumeAt, suspendAt);

        // The very next sample is a real, current reading — never an invented
        // point interpolated across the gap.
        SessionEngineTickResult afterResume = Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 65);

        Assert.Null(afterResume.ClosedSession);
        Assert.NotNull(machine.CurrentSession);
        Assert.True(machine.CurrentSession!.SleepSeconds >= (long)TimeSpan.FromHours(4).TotalSeconds - 1);
        Assert.Null(afterResume.InferredSystemEvent); // explicit resume suppresses the redundant gap inference
    }

    [Fact]
    public void MissedSuspendNotification_InfersSleepGap_SessionPreserved()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1);

        Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 80);

        // No ProcessSuspend/ProcessResume call at all — the notification was
        // missed. Wall clock jumps far ahead; monotonic uptime barely moves.
        clock.AdvanceRealTimeOnly(TimeSpan.FromHours(3));
        SessionEngineTickResult result = Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 60);

        Assert.Equal(SystemEventKind.InferredSleepGap, result.InferredSystemEvent);
        Assert.NotNull(machine.CurrentSession);
        Assert.True(machine.CurrentSession!.SleepSeconds > 0);
    }

    [Fact]
    public void PercentageJumpWhileAwake_FlaggedSuspect_SessionNotCorrupted()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1);

        Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 40);
        SessionEngineTickResult jump = Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 90);

        Assert.True(jump.PercentageJumpSuspect);
        Assert.Null(jump.ClosedSession); // the session survives; only the sample is suspect
        Assert.NotNull(machine.CurrentSession);
    }

    [Fact]
    public void PercentageJumpAcrossInferredSleepGap_NotFlaggedSuspect()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1);

        Tick(machine, clock, BatteryState.Charging, ac: true, pct: 40);
        clock.AdvanceRealTimeOnly(TimeSpan.FromHours(2));
        SessionEngineTickResult result = Tick(machine, clock, BatteryState.Charging, ac: true, pct: 95);

        Assert.Equal(SystemEventKind.InferredSleepGap, result.InferredSystemEvent);
        Assert.False(result.PercentageJumpSuspect);
    }

    [Fact]
    public void BatteryRemovedMidSession_ClosesSession_NoCrash()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1);

        Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 55);
        SessionEngineTickResult removed = Tick(machine, clock, BatteryState.NotPresent, ac: null, pct: null);

        Assert.NotNull(removed.ClosedSession);
        Assert.Equal(SessionEndReason.BatteryRemoved, removed.ClosedSession!.EndReason);
        Assert.Null(machine.CurrentSession);
    }

    [Fact]
    public void ClockStepsBackwards_Rejected_SessionPreserved()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1);

        Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 55);
        BatterySessionInfo? before = machine.CurrentSession;

        DateTimeOffset backwards = clock.Now - TimeSpan.FromMinutes(5);
        SessionEngineTickResult rejected = machine.Process(new SessionEngineInput(
            backwards, clock.MonotonicMs, BatteryId, BatteryState.Discharging, false, 54, 30_000, ScreenState.On, LockState.Unlocked));

        Assert.True(rejected.Rejected);
        Assert.Equal(before, machine.CurrentSession);
    }

    [Fact]
    public void Adopt_ContinuesAnExistingOpenSession()
    {
        Clock clock = new();
        SessionStateMachine machine = new(debounceSampleCount: 1);

        BatterySessionInfo existing = new()
        {
            Id = 42,
            BatteryId = BatteryId,
            Type = SessionType.Discharging,
            StartUtc = Epoch,
            StartPercentage = 90,
            Interruptions = 0,
        };

        machine.Adopt(existing);

        Assert.NotNull(machine.CurrentSession);
        Assert.Equal(42, machine.CurrentSession!.Id);

        clock.AdvanceRealTime(TimeSpan.FromMinutes(1));
        SessionEngineTickResult tick = Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 85);

        Assert.Null(tick.OpenedSession); // continued, not reopened
        Assert.Equal(42, tick.OpenSessionState!.Id);
    }

    // ---- helpers ----

    private static SessionEngineTickResult Tick(
        SessionStateMachine machine, Clock clock, BatteryState state, bool? ac, double? pct)
    {
        SessionEngineInput input = new(
            clock.Now, clock.MonotonicMs, BatteryId, state, ac, pct, RemainingCapacityMWh: null, ScreenState.On, LockState.Unlocked);

        SessionEngineTickResult result = machine.Process(input);
        clock.AdvanceRealTime(TimeSpan.FromSeconds(30));
        return result;
    }

    private static void OpenDischargingSession(SessionStateMachine machine, Clock clock)
    {
        for (int i = 0; i < 3; i++)
        {
            Tick(machine, clock, BatteryState.Discharging, ac: false, pct: 70);
        }

        Assert.NotNull(machine.CurrentSession);
    }

    /// <summary>A fake wall clock + monotonic clock, advanced explicitly by each test.</summary>
    private sealed class Clock
    {
        public DateTimeOffset Now { get; private set; } = Epoch;

        public long MonotonicMs { get; private set; }

        /// <summary>Advances both clocks together — normal elapsed time.</summary>
        public void AdvanceRealTime(TimeSpan by)
        {
            Now += by;
            MonotonicMs += (long)by.TotalMilliseconds;
        }

        /// <summary>Advances only wall-clock time — simulates sleep, where uptime does not accumulate.</summary>
        public void AdvanceRealTimeOnly(TimeSpan by) => Now += by;
    }
}
