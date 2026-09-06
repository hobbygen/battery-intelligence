# Session Engine

Status: 1.0.0 — all phases complete. Version 1.0.0.

**Phase 4 confirmation (2026-09-05):** implemented as `Core.Sessions.SessionStateMachine`
(pure, per section 8) driven by `Sessions.SessionMonitoringService` (the
hardware/database-facing orchestrator). All ten scenarios in section 8's table
are unit-tested with a fake clock, and a real AC-disconnect transition was
observed opening a correct Discharging session on the reference machine. Two
places where this document under-specified the exact behaviour were resolved by
interpretation and documented in `docs/roadmap.md` Phase 4 deviations: the
apparent conflict between the transition table's "Charging → Idle: close" and
section 3's interruption requirement (resolved via a grace period), and whether
a debounced session backdates to the start or the confirmation of its sample
streak (resolved: backdates, to avoid understating the session).

Covers spec §11, §12, §24, §49, §50.

---

## 1. What a session is

A **session** is a contiguous period during which the battery is moving in one
direction. Two types:

- **Charging** — energy flowing into the battery.
- **Discharging** — the system running on battery.

A session is *not* ended by the screen turning off, by sleep, or by the user
locking the machine. Those are **events within** a session. Conflating them is the
most common way this kind of feature goes wrong: it fragments an overnight
discharge into dozens of meaningless slivers and destroys the statistics built on
top.

Periods where AC is connected and the battery is full are **idle**, not a session.
Tracking them as charging sessions would flood the history with zero-gain entries
every time the user plugged in an already-full laptop.

---

## 2. State machine

```
                        ┌───────────────┐
              ┌────────▶│   UNKNOWN     │  startup, no battery, provider failure
              │         └───────┬───────┘
              │                 │ first valid sample
              │      ┌──────────┼──────────┐
              │      ▼          ▼          ▼
       ┌──────┴──────┐   ┌───────────┐   ┌──────────────┐
       │ DISCHARGING │   │   IDLE    │   │   CHARGING   │
       │ (session)   │   │ (no sess.)│   │  (session)   │
       └──────┬──────┘   └─────┬─────┘   └──────┬───────┘
              │                │                │
              │  AC connected  │                │  full / AC lost
              └───────────────▶│◀───────────────┘
                      AC lost  │  charging starts
```

Transitions and their session effects:

| From | To | Trigger | Session effect |
|---|---|---|---|
| any | Charging | rate > 0 sustained, AC on | Open charging session |
| any | Discharging | AC off, rate < 0 sustained | Open discharging session |
| Charging | Idle | reached full, or charging stopped with AC still on | Close charging (`ReachedFull` / `ChargingStopped`) |
| Charging | Discharging | AC disconnected | Close charging (`ChargerDisconnected`), open discharging |
| Discharging | Charging | AC connected and charging | Close discharging (`ChargerConnected`), open charging |
| Discharging | Idle | AC connected, not yet charging | Close discharging (`ChargerConnected`) |
| Idle | Charging | charging resumed | Open charging |
| Idle | Discharging | AC disconnected | Open discharging |
| any | Unknown | provider failed repeatedly | **Session stays open**, marked degraded |

The last row matters: a temporarily failing provider must not close a session. A
30-second WMI hiccup should not chop a six-hour discharge in two. Sessions close on
*observed transitions*, not on absence of data.

---

## 3. Debouncing

Raw power readings are noisy. A laptop at 99 % on AC can oscillate between tiny
positive and negative rates as the firmware tops off. Reacting to each flip would
generate hundreds of spurious sessions.

Rules:

- A direction change must **persist for `N` consecutive samples** (default 3, ≈15 s)
  before a transition is committed.
- AC connect/disconnect is an **immediate, trusted** signal — it comes from a power
  event (S5), not from rate noise, and the user unplugging a charger is
  unambiguous.
- A charging session with **duration below a floor** (default 60 s) *and* zero
  percentage gain is discarded rather than persisted, as a charger bounce.

The asymmetry is intentional: rate-derived transitions are debounced because rates
are noisy; event-derived transitions are not, because they are not.

### Interruption, not termination

Charging that pauses and resumes within the same AC connection (thermal throttling,
a smart-charging policy holding at 80 %) records an **interruption** on the existing
session — incrementing `Interruptions` and writing a `SessionEvent` — rather than
closing and reopening. Spec §10 asks for charging interruptions as a *quality
signal*; that only works if they stay attached to the session they interrupted.

---

## 4. Sub-state tracking

Within a session, three independent dimensions are tracked as interval timelines:

| Dimension | States | Source |
|---|---|---|
| Screen | On / Off / Dimmed | `GUID_CONSOLE_DISPLAY_STATE` (S5) |
| Lock | Locked / Unlocked | WTS session notifications (S6) |
| System | Awake / Sleeping / Hibernated | `WM_POWERBROADCAST` (S7) + gap detection |

These are **orthogonal**. Screen-off while awake is not sleep; locked is not
screen-off. Spec §50 is explicit: "Do not confuse screen-off with sleep." Modelling
them as one enum would make that error unavoidable.

Each session accumulates duration and battery delta per screen state, which is what
spec §11's Screen ON / Screen OFF / Sleep breakdown renders from.

---

## 5. Sleep and resume

### On suspend (`PBT_APMSUSPEND`)

1. Take a final sample.
2. Write a `SystemEvent` (`Suspend`).
3. Flush all pending batched writes and commit — nothing may be lost to the suspend.
4. Stop timers; keep the session open.

### On resume (`PBT_APMRESUMEAUTOMATIC` / `PBT_APMRESUMESUSPEND`)

1. Write a `SystemEvent` (`Resume`).
2. **Re-enumerate battery devices** — hardware may have changed while suspended
   (spec §24), and stale device handles are a classic post-resume failure.
3. Re-run capability detection.
4. Take a fresh sample; do **not** interpolate across the sleep gap.
5. Attribute the elapsed time to `SleepSeconds` on the open session.
6. Reconcile: if the percentage changed direction across the gap (unplugged while
   asleep), close the old session and open the correct one, backdated to the
   resume instant.
7. Restart timers.

Step 4 is important. Interpolating a straight line across an eight-hour sleep would
invent samples that were never measured — a direct violation of the core principle,
and it would corrupt every average computed over that window.

### Gap detection

Suspend notifications can be missed entirely — a forced power-off, a crash, a
battery pull. Every sample therefore compares wall-clock delta against monotonic
uptime delta:

- Wall-clock advanced far beyond the sampling interval **and** uptime advanced with
  it ⇒ the machine was awake but the app was not running (app crash / restart).
- Wall-clock advanced but uptime **reset** ⇒ the machine rebooted.
- Wall-clock advanced, uptime advanced by much less ⇒ the machine slept without
  notifying.

Each case writes a `SystemEvent` with `Inferred = 1`. The Diagnostics page and the
session timeline both distinguish inferred events from observed ones, so an
inferred reconstruction is never presented as a measured fact.

---

## 6. Crash recovery

On startup the engine looks for a session with `EndUtc IS NULL`:

1. Find the last `BatterySample` belonging to it.
2. If that sample is **recent** (within a grace window, default 5 minutes) and the
   current direction still matches ⇒ **adopt** the session and continue it. A quick
   app restart should not fragment the user's history.
3. Otherwise ⇒ close it at the last sample's timestamp, with
   `ClosedCleanly = 0` and `EndReason = Interrupted`, then evaluate current state
   fresh.

A session is never silently deleted. An interrupted session is real history; it is
marked as incomplete, not erased.

---

## 7. Timeline construction

The timeline (spec §12) is built by merging, over a time range:

- session start/end boundaries,
- `SessionEvent` rows,
- `SystemEvent` rows,
- screen/lock/system interval changes,

into one chronologically ordered list of segments, each carrying start, end,
kind, and start/end percentage:

```
08:15          Charger disconnected           100%
08:15–10:20    Screen ON        100% → 72%    −28% over 2h05m
10:20–10:55    Screen OFF        72% → 70%     −2% over 35m
10:55–11:20    Sleep             70% → 69%     −1% over 25m
11:20          System resumed
11:20–12:40    Screen ON         69% → 41%    −28% over 1h20m
12:40          Charger connected
12:40–13:45    Charging          41% → 100%   +59% over 1h05m
```

Segments come from the sub-state timelines, so a segment boundary always
corresponds to a real recorded transition. Percentages at boundaries come from the
nearest actual sample, never from interpolation.

---

## 8. Testability

The engine is a **pure state machine over an input event stream**, with no
dependency on real hardware, timers or the database. It consumes
`(timestamp, batteryState, acState, screenState, systemState)` tuples and emits
session commands.

That shape means the full matrix of spec §62 failure cases is ordinary unit
testing — rapid charger toggling, percentage jumps, missed suspend, clock moving
backwards, provider dropout mid-session, battery disappearing — all replayable
deterministically in milliseconds, with no laptop to unplug.

Required scenarios:

| Scenario | Expected |
|---|---|
| Charger connect/disconnect within debounce window | No session churn |
| Rate oscillation at 99 % on AC | Idle, no sessions created |
| Charging pauses at 80 % then resumes | One session, `Interruptions = 1` |
| Suspend and resume mid-discharge | One session, `SleepSeconds` populated, no interpolated samples |
| Missed suspend notification | Inferred `SystemEvent`, session preserved |
| App crash mid-session, restart within grace | Session adopted and continued |
| App crash mid-session, restart after grace | Session closed `Interrupted`, new session opened |
| Percentage jumps 40 % → 90 % instantly | Sample flagged `Suspect`, session not corrupted |
| Battery removed mid-session | Session closed, `IsPresent = 0`, no crash |
| Clock steps backwards (NTP correction) | Rejected, logged, session preserved |
