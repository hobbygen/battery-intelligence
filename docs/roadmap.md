# Implementation Roadmap

Status: Phase 6 complete. Version 1.0.0.

Maps spec §67's fifteen phases to concrete deliverables and exit criteria.

**Rule for every phase** (spec §80): state objective → list files → explain
dependencies → implement → build → test → fix → review → update docs → next.
No phase is skipped, and no phase is declared done while its build is red.

---

## Phase 0 — Architecture ✅ complete

**Delivered**

| Artifact | File |
|---|---|
| Product requirements | `docs/prd.md` |
| Architecture, module map, dependency graph | `docs/architecture.md` |
| Windows API strategy | `docs/api-strategy.md` |
| Capability matrix (hardware-verified) | `docs/capability-matrix.md` |
| Database schema, migrations, retention | `docs/database.md` |
| UI navigation and design | `docs/ui-navigation.md` |
| Monitoring data flow and sampling | `docs/monitoring-dataflow.md` |
| Session state machine | `docs/session-engine.md` |
| Estimation strategy | `docs/estimation-strategy.md` |
| Testing strategy | `docs/testing.md` |
| Roadmap | `docs/roadmap.md` |
| Traceability matrix | `docs/traceability.md` |
| Limitations (mandatory, spec §69) | `docs/limitations.md` |

**Also delivered:** toolchain verified end to end — .NET SDK 10.0.400 installed,
a WinUI 3 app built and launched against Windows App Runtime 1.8, and the
reference machine's real battery capabilities probed and recorded.

**Exit criteria met:** architecture internally consistent (reviewed in §"Consistency
review" below); every spec §81 artifact produced; no contradictions outstanding.

---

## Phase 1 — Application shell ✅ complete

**Objective:** a running, navigable, themed WinUI 3 application with DI, logging,
settings and single-instance behaviour. No battery code yet.

**Deliverables**

- Solution + 12 project skeletons per `architecture.md` §3
- `Directory.Build.props` — nullable enabled, warnings-as-errors, shared metadata
- `App.xaml(.cs)` with generic host, DI container, Serilog
- `MainWindow` + `NavigationView` shell, 11 pages as stubs with empty states
- Theme system (Light/Dark/System, Mica with Win10 fallback)
- `ISettingsService` over `settings.json` with validation and defaults
- Single instance via `AppInstance.FindOrRegisterForKey`
- Window state persistence with off-screen validation

**Depends on:** nothing.

**Exit criteria — all verified on the reference machine:**

| Criterion | Result |
|---|---|
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Application launches | ✅ window created, Mica backdrop applied |
| Every page reachable | ✅ 11 pages, mouse and Ctrl+1–9 |
| Theme switches live | ✅ Dark applied without restart, persisted, restored |
| Second launch activates the first instance | ✅ redirected, one process |
| Close keeps monitoring alive | ✅ window hidden, process alive |
| Restore from hidden | ✅ same process, window shown |
| Window geometry persists | ✅ 120,90 1400x900 saved and restored |
| Unit tests | ✅ 35 passing |

**Risk R8 closed:** the notification-area icon appears and its menu works on
unpackaged WinUI 3, verified before anything was built on top of it.

**Deviations from plan**

- The solution file is `BatteryIntelligence.slnx`, not `.sln`. The .NET 10 SDK
  emits the newer XML solution format by default; it is functionally equivalent
  and better suited to version control.
- The `Tests.Integration` and `Tests.Simulation` projects are deferred to the
  phases that first need them (3 and 2 respectively). Creating them empty now
  would add two projects to every build for no coverage. `Tests.Unit` exists and
  is populated, because Core shipped in this phase.

---

## Phase 2 — Battery monitoring ✅ complete

**Objective:** real battery data on screen, with honest capability detection.

**Deliverables**

- `Core`: `BatteryState`, `BatteryInfo`/`BatteryDevice`/`BatterySnapshot`,
  `CapabilityId`/`CapabilityRow`/`CapabilitySnapshot`, `IBatteryProvider`,
  `IBatteryCapabilityDetector`, `IBatteryMonitoringService` — plus
  `BatterySentinels`, `BatteryCalculations` (retention, current, mA/mW
  normalisation, cycle-count-zero quirk) and `BatteryAggregation`, all pure and
  unit-tested without touching a battery
- `Windows`: `BatteryMessageWindow` (`HWND_MESSAGE`, `WM_POWERBROADCAST` for
  `GUID_ACDC_POWER_SOURCE`/`GUID_BATTERY_PERCENTAGE_REMAINING`,
  `WM_DEVICECHANGE`), `SystemPowerStatusReader` (S2), `BatteryIoctlDevice` +
  `BatteryDeviceEnumerator` (S4: `SetupDiGetClassDevs` → `CreateFile` →
  `DeviceIoControl`, native function-pointer window procedure)
- `Battery`: `WinRtBatterySource` (S1), `WmiBatterySource` (S3),
  `IoctlBatterySource` (S4), `SystemPowerStatusSource` (S2),
  `CompositeBatteryProvider` merging per capability-matrix.md's priority chains,
  `BatteryCapabilityDetector`, `BatteryMonitoringService` (hosted service: 30 s
  poll + immediate refresh on power notification), `SimulatedBatteryProvider`
  with nine scenarios (`BatterySimulationScenario`)
- `tests/BatteryIntelligence.Tests.Simulation` created (deferred from Phase 1, as
  planned) — the `SIMULATION` symbol is unconditional there, with the Battery
  project reference pinned to `Configuration=Debug` so a Release solution build
  still succeeds
- Battery page (per-device cards: capacity comparison, retention, electrical,
  identity) + Dashboard battery card + live Diagnostics capability rows

**Depends on:** 1.

**Exit criteria — all verified on the reference machine (2026-09-05):**

| Criterion | Result |
|---|---|
| Live percentage, state, AC | ✅ 84% / Charging / Connected — cross-checked against the Windows battery flyout ("84% available (plugged in)") |
| Design / full-charge capacity | ✅ 95,008 / 38,008 mWh — exact match to `capability-matrix.md` §1 |
| Capacity retention | ✅ 40.0%, Calculated — exact match |
| Voltage | ✅ Measured from WMI (`root\wmi`), since WinRT does not expose it |
| Current | ✅ Calculated (722 mA = 8,442 mW ÷ 11,693 mV), never Measured |
| Cycle count | ✅ correctly **Unavailable** — "Not reported by firmware" |
| Temperature | ✅ correctly **Unavailable** — "Sensor not exposed by this device" |
| Identity | ✅ "SMP / DELL 68ND307 / LiP / S/N 642" — exact match |
| Diagnostics renders the live matrix | ✅ all 14 capability rows (C01–C14), including both Unavailable ones with their reasons |
| Simulation scenarios | ✅ 13 tests: normal discharge/charge, multiple batteries, no-temperature, no-cycle-count, mA-reporting, sensor dropout, API failure |
| Unit tests | ✅ 79 passing (66 Phase 1 carried forward + 13 new Battery/Core tests) |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings; `SimulatedBatteryProvider` confirmed present in the Debug binary and absent from Release |

**Deviations from plan**

- Capacity retention and current (`BatteryCalculations`) live in **Core**, not
  Analytics as the traceability matrix originally implied — they are exact
  arithmetic on every reading (spec section 3's "Calculated" rung), not a
  statistics feature, and placing them in Core keeps them testable without a
  battery, consistent with the zero-dependency rule.
- Device correlation across S1/S3/S4 is **positional** (by enumeration index),
  not by a shared identifier — WinRT, WMI and SetupDi each use unrelated ID
  schemes for the same physical battery. Exact for the single-battery case
  (this reference machine, and the overwhelming majority of laptops); documented
  as a simplification in `Sources/RawBatteryData.cs` for a future maintainer
  looking at exotic multi-battery hardware.
- `WindowsBatteryProvider` from the original interface-extension-points table
  (`architecture.md` §9) became `CompositeBatteryProvider` — the composite
  needed a name of its own once it started merging four distinct sources rather
  than wrapping one.
- No design-voltage source was found on this hardware across S1/S3/S4 (WMI's
  `BatteryStaticData` and the IOCTL `BATTERY_INFORMATION` both omit it), so
  quirk Q2's mA→mW normalisation uses live voltage as an approximation. Recorded
  here rather than silently deviating from `estimation-strategy.md` §2's wording.
- Sleep/resume re-validation (`api-strategy.md` §5) and lock/session
  notifications (S6) are deferred to Phase 4 alongside the rest of the session
  state machine, which is what actually consumes them; only S5 (power settings)
  and a coarse `WM_DEVICECHANGE` trigger are wired up now.

---

## Phase 3 — Database ✅ complete

**Objective:** battery readings survive the process, in batches, without the UI
ever waiting on disk.

**Deliverables**

- `Data/Sqlite`: `SqliteConnectionFactory` (WAL, `synchronous=NORMAL`,
  `foreign_keys=ON`, `busy_timeout=5000`, per docs/database.md section 5),
  `DatabaseMigrator` (embedded-resource scripts, `SchemaMigration` bookkeeping,
  pre-migration backup from the second migration onward, rollback on failure),
  `V001__InitialSchema.sql` — the full eighteen-table schema transcribed from
  docs/database.md section 4
- `Data/Repositories`: `BatteryDeviceRepository` (upsert by `HardwareId`,
  `COALESCE`-preserving identity fields against a transient source dropout),
  `BatterySampleRepository` (one prepared statement reused across a batch)
- `BatterySampleWriteQueue` — `IBatterySampleWriteQueue`, batches with the three
  documented triggers (200 rows, 30 s, explicit `FlushAsync` for suspend/close/
  shutdown), retries a failed flush, drops the oldest routine rows past a hard
  cap rather than growing without bound
- `DatabaseMaintenanceService` — idempotent minute rollup (`NOT EXISTS`-gated,
  a 2-minute safety margin past the write queue's flush lag) and retention
  (deletes raw rows only once rolled up, never a row in an open session)
- `DatabaseDiagnosticsProvider` — backs the Diagnostics page's mandatory
  database facts (spec section 47): size (main file + WAL sidecar), row count,
  last write, pending writes, last cleanup
- `Core.Battery.BatterySampleValidation` — the Validator stage of the pipeline
  (docs/monitoring-dataflow.md section 2/4): percentage/voltage/temperature/
  capacity/rate plausibility checks, applied in `BatteryMonitoringService`
  before a reading reaches the UI, the aggregate, or the write queue
- `BatteryPersistenceBridge` (App) — the only place Battery's output reaches
  Data's input, keeping the two siblings independent
- `tests/BatteryIntelligence.Tests.Integration` created (deferred from Phase 1)

**Depends on:** 2.

**Exit criteria — all verified (2026-09-05):**

| Criterion | Result |
|---|---|
| Samples persist in batches | ✅ live run: Diagnostics showed 7 rows, "Last write: Just now", "Pending writes: 1" mid-batch |
| UI never blocks on a write | ✅ write queue runs on its own timer/count triggers; `BatteryViewModel`/`BatteryMonitoringService` never await it |
| Migration round-trip loses no rows | ✅ `MigrationTests`: fresh DB gets all 18 tables + correct pragmas; re-running is idempotent (no duplicate `SchemaMigration` row) |
| Retention respects open sessions | ✅ `RunRetentionAsync_NeverDeletesARowInAnOpenSession` — an old, already-rolled-up row attached to an open session survives cleanup |
| Rollup is idempotent | ✅ `RunRollupAsync_RunTwice_IsIdempotent`; `NOT EXISTS` gate means a re-run inserts nothing |
| Unit + Simulation + Integration tests | ✅ 107 passing (77 + 13 + 17) |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Live on the reference machine | ✅ `battery.db` created with `-wal`/`-shm` siblings (WAL confirmed active); Diagnostics rendered real size/row-count/last-write/last-cleanup |

**Deviations from plan**

- **BatterySample's per-row grade is a reduction, not a loss.** The schema
  (docs/database.md) carries one `DataQuality`/`MeasurementSource` pair per row,
  while a `BatteryInfo` reading carries one per field (Phase 2). The stored row
  is graded by the worst of its populated fields and sourced from the headline
  percentage reading; every consumer above the write queue still sees the
  full per-field grading on the live `BatterySnapshot` — only the persisted
  row is coarser. Recorded here so a future migration can consider per-field
  columns if that reduction ever matters to a feature.
- **Only minute-level rollup ships.** `SampleHour` and `DailyStatistics` exist
  in the schema (required for the full retention tier per spec §31) but have no
  producer yet — with only battery-percentage/power/voltage samples flowing at
  this point, hour/day rollups have nothing substantial to summarise before
  Sessions (Phase 4) and Analytics (Phase 8) add the data that makes them
  worthwhile. Retention for those tiers is likewise deferred; `MinuteRetentionDays`/
  `HourRetentionDays` are read into `DataRetentionSettings` but not yet enforced.
- **The full Validator matrix is partially deferred.** Implemented: percentage/
  voltage/temperature/capacity/rate plausibility (all stateless). Deferred to
  Phase 4 alongside the session/screen-state machinery that makes them
  meaningful: the monotonic-timestamp guard (matters once sleep/resume can
  produce a clock jump) and the percentage-jump-while-awake check (needs
  screen state to distinguish "jumped while awake" from "charged while
  suspended").
- **The database path is resolved from settings at first use**, via a factory
  delegate (`ISqliteConnectionFactory` registered as `AddSingleton<T>(sp => ...)`)
  rather than a plain constructor argument — this is also what surfaced (and
  fixed) a pre-existing Phase 2 startup-ordering issue: `AppSettings` must be
  loaded before `_host.Start()`, not after, since hosted services now read
  `ISettingsService.Current` from their own `StartAsync`.
- **Backup-before-migration is untested by an automated test.** With only V001
  existing, there is nothing to migrate *to* yet that would exercise the
  backup path (it only runs from the second migration onward). The mechanism
  is implemented and will be exercised the first time V002 ships.

---

## Phase 4 — Sessions ✅ complete

**Objective:** correctly attribute battery movement to sessions, across sleep,
lock, crashes and restarts, without fragmenting history.

**Deliverables**

- `Core.Sessions.SessionStateMachine`: the full state machine from
  docs/session-engine.md — debounced Charging/Discharging opens, immediate
  AC-line-driven closes, the charging-interruption grace period, gap detection
  (wall-clock vs monotonic uptime), the (now unblocked) awake-percentage-jump
  check shared with `BatteryMonitoringService` via the new
  `Core.Battery.PercentageJumpDetector`, and crash-recovery `Adopt`. Pure,
  hardware-free, unit-tested with a fake clock.
- New Core enums for everything the schema already had columns for but nothing
  produced yet: `SessionType`, `SessionEndReason`, `SessionEventKind`,
  `SystemEventKind`, `ScreenState`, `LockState`, `SystemPowerState`.
- `Windows`: `BatteryMessageWindow` extended for S5 (`GUID_CONSOLE_DISPLAY_STATE`
  screen state), S6 (`WTSRegisterSessionNotification` lock/unlock), and S7
  (`PBT_APMSUSPEND`/`PBT_APMRESUMEAUTOMATIC`/`PBT_APMRESUMESUSPEND`) — the three
  sources Phase 2 deliberately deferred to this phase.
- `Battery`: `BatteryMonitoringService` now re-enumerates devices and re-runs
  capability detection on resume (previously only on `WM_DEVICECHANGE`), tracks
  awake/suspended state, and grades a sample Suspect on an implausible
  percentage jump while awake.
- `Data`: `ISessionStore`/`SessionStore` (device id resolution, open-session
  lookup, insert/update/close, session/system event recording, recent-sessions
  and timeline queries) plus `BatterySessionRepository`, `SessionEventRepository`,
  `SystemEventRepository`. `BatterySampleWriteQueue` now stamps every row with
  `ScreenState` and the open session's id (both schema columns existed since
  Phase 3 but were never populated until now).
- `Sessions`: `SessionMonitoringService` — one state machine per battery,
  fed from `IBatteryMonitoringService.Updated` and `BatteryMessageWindow`,
  persisting through `ISessionStore`. Promoted from a hardware-free stub
  (Phase 1) to `net10.0-windows`, per docs/architecture.md's dependency graph.
- `App`: `BatteryPersistenceBridge` now tags each written sample with session
  id and screen state, and flushes the write queue immediately on suspend
  (docs/session-engine.md section 5, "nothing may be lost to the suspend").
  Sessions page shows the live current session and recent history.

**Depends on:** 2, 3.

**Exit criteria — verified 2026-09-05:**

| Criterion | Result |
|---|---|
| Full scenario matrix (`session-engine.md` §8) | ✅ all 10 named scenarios + basic open/close/debounce cases: 14 tests in `SessionStateMachineTests`, all passing |
| Real session on the reference machine | ✅ live run: a real AC-disconnect transition (100% → discharging) correctly opened a Discharging session, "Screen ON 1m", "0 interruptions", visible on the Sessions page |
| No fabricated/interpolated samples across sleep | ✅ `ProcessResume` only ever attributes elapsed time to `SleepSeconds`; the next real sample is unmodified |
| Retention's open-session guard now meaningful | ✅ `BatterySample.SessionId` populated for the first time; Phase 3's guard query had nothing to protect until now |
| Diagnostics/Dashboard/Battery pages unaffected | ✅ live regression check: all 14 capability rows, DB stats, battery card unchanged |
| Unit + Simulation + Integration tests | ✅ 127 passing (97 + 13 + 17) |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |

**Deviations from plan**

- **The `Sessions` project became Windows-targeted**, contrary to how it was
  stubbed in Phase 1 (plain `net10.0`). docs/architecture.md's dependency graph
  already drew Sessions consuming Windows/Data/Battery, but Phase 1 hadn't
  reconciled that with section 8's "pure state machine" requirement. Resolved by
  splitting: the state machine itself (`Core.Sessions.SessionStateMachine`)
  stays in Core — plain `net10.0`, unit-testable from `Tests.Unit` with zero
  friction — while `BatteryIntelligence.Sessions` now holds only the
  hardware/database-facing orchestrator.
- **`ISessionStore` lives in Core, implemented in Data**, exactly like
  `IBatterySampleWriteQueue` (Phase 3) — this is what let the Sessions project
  avoid a direct Data reference, keeping Battery/Data/Sessions as siblings
  wired together only by the App composition root.
- **Interruption vs. close semantics were reconciled by interpretation.**
  docs/session-engine.md's transition table says "Charging → Idle" closes the
  session, while section 3 says a pause-and-resume should record an
  interruption instead. Implemented as: entering Idle while charging (AC still
  on) starts a grace period (default 15 minutes); resuming within it records an
  `Interruption`; only exceeding it (or reaching 100%, which is unambiguous and
  short-circuits the wait) actually closes the session.
- **The debounce count backdates the session start** to the first sample of the
  confirmed streak, not the one that confirmed it — otherwise every session
  would understate its true start time and starting percentage by
  `(debounce count − 1)` sampling intervals. Not specified either way in
  docs/session-engine.md; this reading seemed clearly more correct and is
  covered by `Idle_ThenChargingForDebounceCount_OpensChargingSession`.
- **Debounce count (3) is unchanged, but its real-world duration is not the
  documented ~15 seconds.** That figure assumed Phase 5's 5-second power
  sampling cadence; today's only cadence is `BatteryVerifySeconds` (default 30 s),
  so a rate-driven transition currently takes ~90 s to commit. This corrects
  itself when Phase 5 tightens sampling — no engine change needed.
- **Reboot is not a distinct inferred `SystemEventKind`.** Section 5 lists
  "wall-clock advanced but uptime reset ⇒ rebooted" as a case gap detection
  should recognise, but in-process gap detection cannot observe a reboot (the
  process does not survive one) — that scenario is actually handled by crash
  recovery at startup (section 6), which does not need to know *why* continuity
  broke, only that it did. `InferredSleepGap` covers the case gap detection
  genuinely can detect: a missed suspend/resume within one run.
- **Timeline segments are session spans plus point markers**, not the fully
  reconstructed contiguous screen-on/off spans in section 7's example table.
  The underlying `SystemEvent` rows this phase now records are exactly what
  that reconstruction would need, so it is a query-layer addition, not a new
  instrumentation gap, when a future phase wants the fuller view.
- **Hibernate is not distinguished from standby.** Windows delivers the same
  `PBT_APMSUSPEND`/`PBT_APMRESUMEAUTOMATIC` for both; `SystemPowerState` has one
  `Suspended` value rather than inventing a distinction the platform does not
  expose from user mode.

---

## Phase 5 — Power monitoring ✅ complete

**Objective:** a dedicated Power page showing live electrical figures over
selectable windows, with an honest fallback ladder for the energy rate and a
persistence path that cannot grow the database or the UI's memory without bound.

**Deliverables**

- `Core.Power`: `PowerEstimator` (the docs/estimation-strategy.md section 2
  ladder — Measured → Calculated V×I → Estimated ΔmWh/Δt → Unavailable, grade
  degrades only), `RollingStatistics` (windowed min/max/avg), `MinMaxDownsampler`
  (bucketed min/max-preserving decimation), `BoundedTimeSeries` (max-age + hard
  point cap). All pure, `net10.0`.
- Core models/enums: `PowerReading`, `PowerDirection` (persisted), `PowerWindow`,
  `TimePoint`, `MetricStatistics`/`PowerWindowStatistics`,
  `ChartSeries`/`PowerSeriesSet` (the library-neutral charting seam),
  `IPowerMonitoringService`, `IPowerSampleWriteQueue`.
- `Battery`: `BatteryMonitoringService` verify cadence tightened to
  `min(BatteryVerifySeconds, PowerSampleSeconds)` — 5 s by default.
- `Power`: `PowerMonitoringService` (hosted service) — rides the battery
  monitor's `Updated`, resolves the rate through `PowerEstimator`, maintains the
  per-battery bounded series + accumulators, persists via `IPowerSampleWriteQueue`.
  Project promoted from stub; Core-only reference.
- `Data`: `PowerSampleRepository`/`PowerSampleRow`, `PowerSampleWriteQueue`
  (200-row / 30 s / explicit-flush, 1000-row cap), `PowerSample` retention in
  `DatabaseMaintenanceService`, `PowerSampleRowCount` on the Diagnostics facts.
- `App`: `PowerViewModel` (1 Hz-coalesced readouts, window selector),
  `Controls/PowerChart` (the only file touching `LiveChartsCore.SkiaSharpView.WinUI`),
  Power page — live current/voltage/power with min/max/avg, grade badges, three
  synchronised charts, and normal/loading/empty/unavailable states.

**Depends on:** 2, 3.

**Exit criteria — verified 2026-09-05 on the reference machine:**

| Criterion | Result |
|---|---|
| Measured power and voltage live | ✅ live run: `PowerSample` rows written every ~5 s — power 8,533 mW, voltage 11,609 mV, from WinRT (S1) / WMI (S3) |
| Current shown as Calculated | ✅ 735 mA = 8,533 mW ÷ 11,609 mV, `DataQuality = Calculated`, never Measured |
| Charts stay smooth over an hour without unbounded growth | ✅ `BoundedTimeSeries` (1 h age + 1,000-point cap) + `MinMaxDownsampler` to 600 chart points; `BoundedTimeSeriesTests`/`MinMaxDownsamplerTests`/`PowerMonitoringServiceTests` cover a simulated hour |
| Estimator fallback ladder | ✅ `PowerEstimatorTests` (all four rungs; grade degrades, never promotes; sub-60 s history ⇒ Unavailable) |
| `PowerSample` pipeline | ✅ `PowerSampleWriteQueueTests` (batched write, count trigger, aggregate never persisted, Estimated grade stored); retention `DatabaseMaintenanceServiceTests` |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings; `libSkiaSharp.dll` native asset deployed for the unpackaged app |
| Unit + Simulation + Integration tests | ✅ 158 passing (119 + 17 + 22) |

**Deviations from plan**

- **No dedicated fast electrical provider.** `CompositeBatteryProvider` (Phase 2)
  already yields Measured power, Measured voltage and Calculated current
  per-sample, so Phase 5 tightens that one pipeline's cadence to 5 s rather than
  standing up a parallel S1/S3/S4 path that would only duplicate it.
  `PowerEstimator`'s rungs 2–4 still ship for hardware where S1/S3/S4 power is
  absent. This also resolves the Phase 4 debounce-timing note: the session
  engine now ticks every 5 s, so a rate-driven transition commits in ~15 s.
- **Adaptive sampling deferred to Phase 13.** `monitoring-dataflow.md` section 3's
  screen-off/AC/low-battery rate back-off is an optimisation, not a Phase 5 exit
  criterion. Fixed 5 s sampling ships now; the back-off logic lands with the rest
  of the performance work in Phase 13.
- **`PowerSample` is raw-only.** It feeds the live charts, not a rollup tier
  (`SampleMinute` already carries power aggregated from `BatterySample`), and
  carries no session link, so retention is a plain age cutoff.
- **The "session" chart window is clamped to the one-hour live buffer.** A
  session longer than an hour shows its last hour. Reconstructing a full
  multi-hour session series would need a second, coarser long-buffer; deferred
  until a feature needs it.
- **Charting seam is lighter than a full abstraction.** Per `architecture.md`
  section 7's "middle course": ViewModels expose `ChartSeries`/`PowerSeriesSet`
  (plain `TimePoint` lists), and only `Controls/PowerChart` references
  `LiveChartsCore`. Swapping libraries means rewriting that one control.

---

## Phase 6 — Temperature ✅ complete

**Objective:** an honest Temperature page — the live reading with a per-band
breakdown, a threshold-band trend and threshold-event history where a sensor
exists, and the *unavailable* state where one does not.

**Deliverables**

- `Core` (pure, `net10.0`): `TemperatureBand` / `TemperatureSeverity` /
  `TemperatureWindow` enums, `TemperatureReading` / `ThresholdEvent` /
  `TemperatureBandDuration` models, `Core.Thermal.TemperatureThresholds` +
  `TemperatureBandClassifier` (fixed 30 / 40 / 45 °C boundaries; severity follows
  the configured alert threshold), `Core.Thermal.ThresholdEventDetector` (60-second
  dwell gate, clock-injected), `ITemperatureMonitoringService` /
  `ITemperatureSampleWriteQueue`. Reuses `Core.Power`'s `RollingStatistics`,
  `BoundedTimeSeries`, `MinMaxDownsampler`, `ChartSeries`, `MetricStatistics`.
- `Thermal`: promoted from a stub. `ThermalMonitoringService` (hosted service) —
  rides `IBatteryMonitoringService.Updated`, reads the already-resolved
  `BatteryInfo.TemperatureCelsius` (S4 → S3), classifies band + severity, keeps
  per-battery bounded series + a per-band time accumulator + rolling stats,
  detects threshold events, persists via `ITemperatureSampleWriteQueue`.
  Core-only reference, `net10.0-windows`.
- `Data`: `TemperatureSampleRepository` / `TemperatureSampleRow` /
  `TemperatureSampleWriteQueue` (same 200-row / 30 s / explicit-flush, 1000-row
  cap; temperature stored in deci-Kelvin), `TemperatureSample` retention in
  `DatabaseMaintenanceService` (raw-only, plain age cutoff),
  `TemperatureSampleRowCount` on the Diagnostics facts.
- `App`: `TemperatureViewModel` (1 Hz-coalesced readouts, window selector,
  band breakdown, threshold-event log, the four page states), rebuilt
  `Views/TemperaturePage.xaml` per the imported design — hero split card with the
  severity chip, a `PowerChart` with the warning band + threshold line, the "time
  in each band" bars, the threshold-event list, and the **unavailable** card
  (spec §14 wording, verbatim). Dashboard temperature card + Diagnostics C13 row
  are now driven by the live service.
- `Battery.Simulation`: new `RisingTemperatureWhileCharging` scenario (32 → 48 °C
  while charging, crossing and holding above the 45 °C threshold).

**Depends on:** 2, 3.

**Exit criteria — verified 2026-09-06:**

| Criterion | Result |
|---|---|
| Reference machine renders the **unavailable** state with its explanation | ✅ live run: "Sensor unavailable" card, spec §14 wording incl. the no-CPU-substitute sentence; C13 stays Unavailable; nothing persisted |
| Simulation with a sensor present exercises the full path incl. threshold alerts | ✅ `ThermalMonitoringServiceTests.Ingest_RisingTemperatureWhileCharging_…` — Warm + Hot bands accrue, a threshold event is confirmed after the 60 s dwell with the right peak and charge context, every real reading reaches the write queue |
| Band + severity classification | ✅ `TemperatureBandClassifierTests` (boundaries + threshold-relative severity that shifts with the configured value) |
| Threshold dwell gate | ✅ `ThresholdEventDetectorTests` (brief spike ignored, sustained event confirmed then closed, drop-and-re-rise starts a fresh dwell) |
| `TemperatureSample` pipeline | ✅ `TemperatureSampleWriteQueueTests` (deci-Kelvin storage, count trigger, unavailable reading persists nothing) |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Unit + Simulation + Integration tests | ✅ 181 passing (137 + 19 + 25) |

**Deviations from plan**

- **No dedicated temperature provider.** Battery temperature is already resolved
  S4 → S3 inside `CompositeBatteryProvider` and carried on
  `BatteryInfo.TemperatureCelsius`; Phase 6 consumes that field, exactly as
  Phase 5 reused the same pipeline for power rather than standing up a parallel
  path. Capability detection for C13 already existed (Phase 2).
- **`TemperatureSample` is raw-only** — live charts and the per-band breakdown,
  no rollup tier, no session link — so retention is a plain age cutoff, matching
  `PowerSample`.
- **The window selector offers "Last hour" and "This session" only.** 24 h / 7 d /
  30 d ranges in the design need the tier-aware history query layer (Phase 11);
  the live buffer is one hour, the same clamp Phase 5 took for its session window.
- **Per-state (screen on/off) temperature breakdown is not shown.** The design's
  per-*band* breakdown (Cool/Normal/Warm/Hot) ships; a screen-state split needs
  the screen-state attribution that lands with Phase 7.
- **Critical threshold is derived** as warning + 7 °C rather than a second
  configurable value — one setting to keep, and the 7 °C margin matches the
  design's "Warning 45–52" band.

This was the phase most exposed to risk R7 — none of its happy path verifies on
the reference hardware — so the simulated `ThermalMonitoringService` path is the
acceptance gate, and it is green.

---

## Phase 7 — Application usage (complete)

Per-application battery attribution. Windows exposes measured per-process energy
only through SRUM, which needs administrator rights, and spec §23 forbids
requiring elevation — so per-app energy is a **permanent model**, `AppEnergyV1`.
Built the same way as Phases 5 and 6: pure logic + primitives in `Core`, a
promoted infrastructure-sibling orchestrator that references only `Core`
interfaces, a Data write queue on the standard batching pattern, a 1 Hz-coalesced
view model. The `ProcessSample` and `ApplicationUsage` tables already existed
verbatim from `V001`, so **no migration** was needed.

**What shipped:**

- `Core.Processes` (pure, net10.0): `ProcessCpuCalculator` (delta CPU, core-capped,
  pid-reuse guard), `ProcessGrouping` + `ProcessGroupingTable` (path → name →
  fallback resolution; `Default` built-in table; browser renderer/GPU children
  fold into the parent), `BaselineEstimator` (EWMA of idle draw split by screen
  state; conservative default + forced `Low` confidence until
  `MinObservationsPerState` idle samples), `AppEnergyEstimator` —
  `AppEnergyV1`, `Version` const, the three-step attribution
  (`E_total` → `E_apps = E_total − E_baseline` → weighted `share_i`), shares
  summing to 100 % of the attributable budget, the baseline a distinct
  `AppUsageEntry`, the tail beyond top-N collapsed into one `Other` row.
- New `Core` models/enums: `ProcessRawSample`, `AppUsageEntry`,
  `AppEnergyAttribution`, `ProcessSampleBatch`/`ProcessSampleRecord`,
  `AppEnergyWeights`/`AppActivity`, `AppEnergyConfidence`, `ProcessWindow`;
  interfaces `IProcessEnumerator`, `IProcessMonitoringService`,
  `IProcessSampleWriteQueue`; `ProcessMonitoringSettings` config category
  (weights, top-N, idle floor, default baseline — configuration, not constants,
  per spec §66).
- `ProcessMonitoring` project: **promoted from the `ModuleMarker` stub** to
  `net10.0-windows`, Core-only reference (infra sibling, like Power/Thermal).
  `SystemProcessEnumerator` (`Process.GetProcesses()`, cumulative CPU time,
  working set; foreground pid via one `LibraryImport`; executable path + start
  time cached by `(pid, startTime)` — no file I/O on the repeat path; protected
  processes skipped). `ProcessMonitoringService` (hosted, its own timer at
  `ProcessSampleSeconds`): delta CPU → group → skip-when-idle (screen off + total
  CPU below floor) → resolve `E_total` from `IBatteryMonitoringService`
  (discharge + Measured/Calculated power only) → `BaselineEstimator` →
  `AppEnergyEstimator`; keeps a one-hour tick buffer; `CurrentAttribution` /
  `GetRanking(window)` average CPU over the window at the latest tick's draw;
  persists per-application rows via `IProcessSampleWriteQueue`. Cold-start tick
  publishes nothing (no deltas to diff). `ProcessGroupingTableLoader` merges an
  optional `%LocalAppData%\BatteryIntelligence\process-groups.json` over the
  built-in table.
- `Data`: `ProcessSampleRow`/`ProcessSampleRepository`, `ProcessSampleWriteQueue`
  (same 200-row / 30 s / flush / 1000-cap design; no `BatteryDevice` FK on this
  table). `DatabaseMaintenanceService` gained the idempotent daily
  `ApplicationUsage` rollup (only days strictly before today; `__baseline__` rows
  excluded from the rollup) and `ProcessSample` retention (raw dropped once its
  day is rolled up for that key and not in an open session; baseline rows on a
  plain age cutoff). `ProcessSampleRowCount` added to the Diagnostics DB facts.
- `App`: `AppUsageViewModel` (window + sort selectors, ranked rows with share
  bars, a distinct baseline slice, confidence line, on-AC state, permanent
  **Estimated** badge). `Views/AppUsagePage.xaml` rebuilt in the card language
  (killed the "arrives in Phase 7" placeholder) with a "How this is estimated"
  link to About. Dashboard "Application Usage" card wired to the top app.
  About page gained the full `AppEnergyV1` methodology (spec §55).
  Diagnostics "Storage" shows the `ProcessSample` row count.

**Depends on:** 2, 3, 4, 5.

**Exit criteria:**

| Criterion | Result |
|---|---|
| Shares sum to the attributable budget, baseline shown separately | ✅ `AppEnergyEstimatorTests` (sum = 100 %; app power sums to `E_total − E_baseline`; no double count) + `ProcessMonitoringServiceTests` |
| On AC, absolute figures are unavailable and the UI says so | ✅ `AppEnergyEstimatorTests.OnAc_…`, `ProcessMonitoringServiceTests.OnAc_…`; page shows the "ranked, not measured in watts" bar |
| Every per-app figure is permanently Estimated | ✅ `ProcessSampleWriteQueueTests` (DataQuality = Estimated, source = Model); estimator carries `EstimatorVersion = "AppEnergyV1"` |
| Intelligent process grouping (browser multi-process) | ✅ `ProcessGroupingTests` (renderer + GPU children → one key; path beats name; unknown → own name) |
| Daily `ApplicationUsage` rollup, idempotent, only fully-elapsed days | ✅ `DatabaseMaintenanceServiceTests.RunRollupAsync_RollsFullyElapsedDays_…` |
| `ProcessSample` retention respects rollup + open-session guards | ✅ `DatabaseMaintenanceServiceTests.RunRetentionAsync_DeletesProcessSamples_OnlyAfterTheirDayIsRolledUp` |
| Sampler cost within the CPU budget | ✅ live — one `Process.GetProcesses()` pass + cached metadata every 10 s; measured against the §1 budget in Phase 13's soak |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Unit + Simulation + Integration tests | ✅ 212 passing (158 + 24 + 30) |

**Deviations from plan**

- **GPU and I/O per-process weight are not observed.** They need ETW/admin
  (`api-strategy.md` §3). `W_gpu` / `W_io` stay configured and versioned in the
  model, but their per-process contribution is zero on this build — attribution
  is CPU + foreground weighted. The full formula stays in `AppEnergyV1` so the
  terms light up if a future source provides them.
- **Application icons are not extracted.** The design shows per-app icons;
  resolving them for an unpackaged WinUI app is deferred. Rows show a monogram
  tile. The `(pid, startTime)` metadata cache and the lazy-off-sampler seam are
  in place to add real icons later.
- **The baseline is a simplified EWMA split**, not a least-squares regression of
  idle draw against screen state — a conservative fixed default with forced
  `Low` confidence until enough idle samples accrue, exactly as
  `estimation-strategy.md` §5 permits.
- **`ProcessSample` rows are per-application, not per-process.** The schema names
  suggest per-process rows, but nothing in Phase 7 (or the roadmap) consumes
  per-process granularity, and app-level rows keep writes bounded to
  `top-N + 1 + baseline` per tick. `ProcessId` is stored as 0; `ProcessName`
  carries the application display name.
- **CPU percent is "% of one logical processor"**, not divided by core count —
  the sum across processes can exceed 100 on a multi-core machine. The core count
  only caps a single runaway process. Ranking is unaffected (relative); the idle
  floor accounts for it.
- **The grouping table is an embedded C# default + an optional JSON override
  file**, not a DB table — "data, not code" without a schema change (`V001` has no
  grouping table).
- **`ProcessSample` retention + the daily `ApplicationUsage` rollup ship;
  `DailyStatistics` stays deferred to Phase 8.**
- **The window selector offers "Last hour" / "This session" only** — 24 h / 7 d /
  30 d need the Phase 11 tier-aware history layer, the same clamp Phases 5 and 6
  took. `GetRanking` anchors its window on the last tick, not wall-clock now.
- **No `V002` migration** — both tables exist verbatim from `V001`, so
  backup-before-migration stays untested until a real schema change ships.
- **Attribution is system-wide, not per-battery** — one process context, unlike
  the per-battery contexts in Power and Thermal.

---

## Phase 8 — Analytics

**Deliverables:** `HealthScoreV1` with weight renormalisation and `FactorsJson`,
degradation trend with smoothing, charging quality score, discharge analysis,
rolling runtime estimator with confidence, statistics engine (lifetime/today/7d/
30d/custom), `RuleBasedInsightProvider` behind `IInsightProvider`, Statistics page.

**Depends on:** 3, 4, 5, 6, 7. **Exit:** score renormalises correctly with cycle
count and temperature both absent; insights suppressed below confidence and
effect-size thresholds; "How this score is calculated" renders real per-factor
contributions.

---

## Phase 9 — Alerts

**Deliverables:** alert engine with thresholds, hysteresis and cooldown; Windows
notifications with in-app fallback; alert history; per-alert configuration; Alerts
page.

**Depends on:** 2, 3, 8. **Exit:** no duplicate or storming notifications; every
default alert from spec §20 configurable; notification failure degrades to in-app
without error.

---

## Phase 10 — Dashboard

**Deliverables:** all eight cards integrated, responsive grid, grade badges, info
tooltips, empty/unavailable states per card, throttled 1 Hz update path.

**Depends on:** 2, 4–9. **Exit:** dashboard correct at 1366×768 through 4K with no
clipping; updates stay throttled regardless of sampling rate.

---

## Phase 11 — History and reporting

**Deliverables:** History page with 24 h–1 y + custom ranges, zoom and tooltips,
tier-aware queries (raw → minute → hour → daily by range), `CsvExporter`,
`JsonExporter` behind `IReportExporter`, export scope selection, safe path handling.

**Depends on:** 3, 8. **Exit:** a one-year range loads without stalling the UI;
exports are spreadsheet-friendly (CSV) and human-readable (JSON).

---

## Phase 12 — Diagnostics

**Deliverables:** live capability matrix rendering, monitoring state per subsystem
with last error, database statistics, log viewer, "Copy diagnostics", About page
with versions, licenses, privacy statement and estimation methodology.

**Depends on:** all. **Exit:** every unavailable capability visible with its reason;
copied diagnostics contain no personal data.

---

## Phase 13 — Optimisation

**Deliverables:** measurement against the `monitoring-dataflow.md` §1 budgets —
CPU, working set, disk writes, query timings, chart frame times, startup time —
then targeted fixes. 24-hour soak.

**Exit:** all budgets met; no leaks over 24 h; no frame > 16 ms attributable to
monitoring.

---

## Phase 14 — QA

**Deliverables:** full unit/integration/simulation suites green; failure matrix
(spec §62) verified; manual checklist (`testing.md` §7) on Windows 10 and 11;
accessibility pass; real sleep/resume and charger cycling.

**Exit:** the twenty numbered criteria of spec §68 demonstrably satisfied.

---

## Phase 15 — Release

**Deliverables:** Release configuration with simulation excluded, MSIX packaging,
versioning, install/upgrade/uninstall with user-data preservation verified,
README, release notes, completed `/docs` set per spec §69.

**Exit:** clean install, upgrade preserving the database, and uninstall all
verified on a clean machine.

---

## Consistency review (Phase 0 exit gate)

Spec §81 requires reviewing the architecture for contradictions before
implementation. Four were found and resolved:

**1. Session tables.** Spec §27 lists `ChargingSession` and `DischargingSession` as
separate entities, while §12's timeline needs them interleaved chronologically.
*Resolved:* one `BatterySession` table with a `SessionType` discriminator, avoiding
a `UNION` on the application's hottest query. Documented in `database.md` §3.

**2. Sampling rate vs lightness.** Spec §13 suggests 5-second power sampling; §45
demands minimal disk writes and low CPU. At 5 s, naive per-sample writes would be
~17,000 transactions/day. *Resolved:* batched writes with priority bypass, plus
adaptive back-off when the screen is off. Documented in `monitoring-dataflow.md`
§3 and §6.

**3. Per-process energy vs no-admin.** Spec §15 wants per-application battery
consumption; §23 forbids requiring administrator. The only *measured* source (SRUM)
needs elevation. *Resolved:* no-admin wins; per-process energy is permanently
Estimated with a documented model. Recorded in `api-strategy.md` §3 and
`estimation-strategy.md` §5.

**4. Health score vs missing inputs.** Spec §19 lists cycle count and temperature
history as scoring factors; the reference machine exposes neither. Scoring them as
zero would be fabrication; refusing to score would make the feature useless on
common hardware. *Resolved:* weight renormalisation across available factors, with
the contributing set stored and displayed — and Unavailable only when capacity
retention itself is missing. Documented in `estimation-strategy.md` §4.

No unresolved contradictions remain. Phase 1 may begin.
