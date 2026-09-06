# Monitoring Data Flow

Status: Phase 5. Version 1.0.0.

Covers spec §30, §31, §32, §45, §74, §75.

**Phase 5 confirmation (2026-09-05):** the power/voltage/current cadence in
section 3 is live — `BatteryMonitoringService`'s verify interval is now
`min(BatteryVerifySeconds, PowerSampleSeconds)` (5 s by default), feeding both the
Power subsystem and the session engine. Section 7's bounded chart buffers and
min/max-preserving downsampling are implemented in `Core.Power`
(`BoundedTimeSeries`, `MinMaxDownsampler`) and consumed by the Power page.
Section 3's **adaptive sampling** table is **deferred to Phase 13** (Optimisation)
— it is not a Phase 5 exit criterion; fixed-rate sampling ships now.

---

## 1. The governing constraint

This application runs continuously on a laptop, on battery, for months. Its own
consumption must be negligible relative to what it measures. Every decision here
follows from that.

Targets:

| Resource | Budget (idle, monitoring active) |
|---|---|
| CPU | < 0.5 % average on a modern mobile CPU |
| Working set | < 150 MB with UI visible; < 80 MB tray-only |
| Disk writes | < 1 MB/min sustained |
| UI frame time | No frame > 16 ms attributable to monitoring |

A monitor that costs 3 % CPU to tell you your battery is draining has become part
of the problem it reports on.

---

## 2. End-to-end flow

```
   HARDWARE / OS
        │
   ┌────┴─────────────────────────────────────────────┐
   │  Providers  (S1–S8)   async, cancellable         │
   └────┬─────────────────────────────────────────────┘
        │  raw readings
   ┌────┴─────────────────────────────────────────────┐
   │  Samplers   timer-driven + event-driven          │
   │             per-subsystem fault isolation        │
   └────┬─────────────────────────────────────────────┘
        │  candidate samples
   ┌────┴─────────────────────────────────────────────┐
   │  Validator  range · sentinel · monotonic-time    │
   │             → Measured / Calculated / Suspect    │
   └────┬─────────────────────────────────────────────┘
        │  validated samples
        ├──────────────────────┬──────────────────────┐
        ▼                      ▼                      ▼
  ┌───────────┐        ┌──────────────┐       ┌──────────────┐
  │ Ring      │        │ Session      │       │ Write queue  │
  │ buffers   │        │ engine       │       │ (batched)    │
  │ (memory)  │        │ (state m/c)  │       └──────┬───────┘
  └─────┬─────┘        └──────┬───────┘              │
        │                     │                       ▼
        │                     │                ┌──────────────┐
        │                     │                │   SQLite     │
        │                     │                └──────┬───────┘
        │                     │                       │
        ▼                     ▼                       ▼
  ┌──────────────────────────────────────────────────────────┐
  │  Throttled UI aggregator → dispatcher → ViewModels       │
  └──────────────────────────────────────────────────────────┘
```

The critical property: **the UI path and the database path are independent.** The
UI reads memory; the database receives batches. Neither waits on the other. A slow
disk cannot stutter the UI, and a busy UI cannot delay persistence.

---

## 3. Sampling schedule

| Subsystem | Default | Mode | Rationale |
|---|---|---|---|
| Battery state | Event + 30 s verify | Hybrid | Power events (S5) carry the transitions; periodic verification catches missed events |
| Power / voltage / current | 5 s | Timer | Spec §13 default; fine enough for live charts, cheap enough to sustain |
| Temperature | 10 s | Timer | Thermal mass makes faster sampling meaningless |
| Process | 10 s | Timer | The most expensive sampler by far — see §5 |
| Screen / lock / sleep | Event only | Event | Transitions are the data; polling would add nothing |
| Rollup / retention | 60 s / 1 h | Timer | Off the hot path entirely |

All intervals are user-configurable (spec §30, §35).

### Adaptive sampling

Rates back off automatically when detail cannot matter:

| Condition | Adjustment |
|---|---|
| Screen off, on battery | Power and process intervals ×3 |
| Screen off, on AC, battery full | Power ×6, process paused |
| Battery < 20 % | Battery and power intervals ×0.5 (finer) — the interesting region |
| Main window hidden (tray only) | Chart feed suspended; DB sampling continues |
| Charging session active | Power interval ×1 (unchanged — charge curves matter) |
| Monitoring paused by user | All samplers stopped, session state preserved |

The screen-off case is the significant win: the machine spends most of its
battery life with the display off, and that is exactly when the app should be
quietest. Reducing the app's own wakeups when the user cannot see the UI directly
extends the battery it is monitoring.

Adaptive sampling never changes what is *recorded* about a transition — events are
always captured at full fidelity. It only reduces the rate of routine telemetry.

---

## 4. Validation

Applied to every sample before it reaches memory or disk (spec §63).

| Check | Rule | Failure action |
|---|---|---|
| Sentinel | `0xFFFFFFFF`, `0xFFFF`, `0x80000000`, `int.MinValue` | → **Unavailable** (never stored as a number) |
| Percentage | 0 ≤ p ≤ 100 | Clamp with log if within 1 %, else **Suspect** |
| Voltage | 1,000–30,000 mV | **Suspect** |
| Temperature | 233–353 K (−40 °C to 80 °C) | **Suspect** |
| Capacity | > 0, and remaining ≤ full × 1.05 | **Suspect** |
| Rate | \|mW\| < 300,000 | **Suspect** |
| Timestamp | Must not move backwards | Reject, log; keep prior sample |
| Percentage jump | > 25 % between adjacent samples while awake | **Suspect**; excluded from rate maths |

`Suspect` rows are stored with `DataQuality = 4` and excluded from aggregates,
rates and insights. Storing them rather than dropping them means a genuine
hardware fault leaves a diagnosable trail instead of a silent gap — while never
polluting a computed average.

The percentage-jump check has an explicit awake condition: a large jump *across a
sleep gap* is entirely normal (the machine charged while suspended) and must not be
flagged.

---

## 5. Process sampling — the expensive one

Enumerating processes and computing CPU deltas is by far the heaviest operation in
the application, and naive implementations are the usual reason monitoring tools
show up in their own "top consumers" list.

Mitigations:

1. **Delta-based CPU, no busy sampling.** CPU percent comes from the difference in
   cumulative kernel+user time between two samples, divided by elapsed wall time ×
   core count. No sleeping, no repeated probing.
2. **Bounded working set.** Only the top `N` processes by CPU (default 40) are
   sampled in full; the remainder are aggregated into a single "Other" row.
3. **Cached static metadata.** Executable path, product name and icon are resolved
   once per process and cached by `(pid, startTime)` — never re-resolved per sample.
4. **No file I/O on the sample path.** Icon and version extraction happen lazily,
   off the sampler, only when the App Usage page is actually visible.
5. **Skip when idle.** If system CPU is below a floor and the screen is off, the
   sampler skips the cycle entirely.

Process grouping (spec §15) maps many processes to one application by executable
path, then by known multi-process patterns (browser renderer/GPU children), then by
process name. The grouping table is data, not code, so new applications can be
recognised without a rebuild.

**As built (Phase 7).** The sampler runs on its own timer at `ProcessSampleSeconds`
(10 s default). CPU percent is "% of one logical processor" from cumulative-time
deltas — the core count only caps a single runaway process, and the sum across
processes can exceed 100. Protected processes that deny `TotalProcessorTime`
without elevation are skipped, not guessed at. GPU and I/O per-process rates are
not observed (ETW/admin) — the weight is CPU + foreground. The first tick after a
start records the CPU baseline and publishes nothing. Persisted rows are
per-application (`top-N + Other + baseline`), not per-process. The grouping table
ships as a built-in default plus an optional `process-groups.json` override.

---

## 6. Write batching

```
sample → in-memory queue → flush trigger → single transaction → SQLite
```

Flush triggers, whichever comes first:

- **Time:** 30 s
- **Count:** 200 queued rows
- **Priority:** immediately, for session boundaries, health snapshots, alerts and
  system events
- **Lifecycle:** on suspend, on window close, on shutdown

One transaction per flush, prepared statements reused across the batch. Roughly
360 samples/minute across all subsystems collapse from 360 transactions to about 2.

Priority writes exist because losing 30 seconds of routine telemetry is
inconsequential, while losing a session boundary corrupts history permanently. The
two are not worth treating identically.

If the queue exceeds a hard cap (database locked, disk full), the oldest *routine*
samples are dropped, a `Degraded` state is raised on the Diagnostics page, and
priority rows are still retained. The application keeps running — spec §44 requires
that a database failure not take down monitoring.

---

## 7. UI update path

Spec §75 forbids refreshing the dashboard per sample. Concretely:

- **Numeric readouts** update at most **1 Hz**, regardless of a 5 s or 1 s sampling
  interval, coalescing intermediate values.
- **Charts** hold **bounded** collections — a fixed point budget per series
  (default 600). Beyond it, points are downsampled with min/max preservation so
  spikes survive decimation rather than being averaged away.
- **Lists** (processes, sessions, alerts) are virtualised and re-sorted on a
  throttle, not per sample.
- **Hidden pages do not update.** Navigating away unsubscribes; navigating back
  resubscribes and repaints from the ring buffer.
- **Tray-only mode** suspends all chart feeds while sampling and persistence
  continue.

Min/max-preserving downsampling matters more than it sounds: plain averaging would
erase exactly the transient current spikes that the Power page exists to show.

---

## 8. Failure isolation

Each sampler owns a small state machine:

```
Healthy ──failure──▶ Retrying ──threshold──▶ Degraded ──success──▶ Healthy
```

- **Retrying** — exponential backoff from the base interval up to a cap (default
  5 min). Logged at Warning.
- **Degraded** — the subsystem reports Unavailable to the UI, surfaces on the
  Diagnostics page with its last error, and continues retrying at the capped
  interval. Logged at Error once per transition, **not** per attempt.

Logging once per state transition rather than per failure is deliberate: a sensor
that has been absent for three months must not have written three months of
identical log lines. That would violate both the log-rotation requirement and the
disk-write budget.

No sampler failure can affect another. A dead temperature sensor degrades exactly
one page.
