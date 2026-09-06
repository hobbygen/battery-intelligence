# Implementation Roadmap

Status: 1.0.0 — all 15 phases complete. Version 1.0.0.

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

## Phase 8 — Analytics (complete)

The layer the PRD calls the application's most valuable output — the long-term
degradation trend — plus the health score, charging quality, discharge analysis,
the rolling runtime estimate, the statistics engine and the rule-based insight
provider. Built like Phases 5–7: pure math in `Core.Analytics`, a promoted
infrastructure-sibling orchestrator, Data stores behind Core seams, coalesced
view models. `BatteryHealthSnapshot`, `Insight`, `SampleHour` and
`DailyStatistics` all exist verbatim from `V001` — **no migration**.

**What shipped:**

- `Core.Analytics` (pure, net10.0): `LinearFit` (Theil–Sen median slope +
  robust residual), `RollingRate` (recency-weighted discharge rate per screen
  state), `HealthScoreCalculator` (`HealthScoreV1` — retention required, weights
  renormalise across the available factors, `HealthFactor[]` for the
  explanation), `DegradationTrendCalculator` (smoothed slope + 90-day projection
  + confidence, `Calculating` below the floor), `ChargingQualityScorer`
  (`ChargingQualityV1`, thermal component dropped + renormalised with no sensor,
  ≥5 prior sessions), `DischargeAnalyzer` (screen-on/off rate split, never
  extrapolated — closes R-048), `RuntimeEstimator` (`remaining ÷ rate` per
  screen state, the four §3 confidence tiers, screen-off Unavailable without
  history), `StatisticsEngine` (timezone-aware window resolution, session
  proration across midnight — computes from sessions directly),
  `RuleBasedInsightProvider` (`InsightRulesV1` — 7 curated rules, each behind the
  four §7 gates, fixed text).
- New `Core` models/enums (`HealthScore`/`HealthFactor`, `DegradationTrend`,
  `ChargingQuality`, `DischargeAnalysis`, `RuntimeEstimate`, `StatisticsSummary`,
  `AnalyticsInsight`, `AnalyticsContext`, `DateRange`, `HealthCategory`,
  `EstimateConfidence`, `InsightType`, `InsightSeverity`, `StatisticsWindow`),
  interfaces (`IInsightProvider`, `IAnalyticsService`, `IRuntimeEstimationService`,
  `IHealthSnapshotStore`, `IInsightStore`, `IAnalyticsReadStore`), and an
  `AnalyticsSettings` config category (thresholds, not code constants — spec §66).
- `Analytics` project: **promoted from the `ModuleMarker` stub**, stays plain
  `net10.0` (analytics is arithmetic — no Windows APIs — so the whole engine is
  unit-testable), Core-only reference. `RuntimeEstimationService` rides
  `IBatteryMonitoringService.Updated` and exposes the live runtime estimate + the
  recent discharge breakdown. `AnalyticsService` (hosted, a slow timer + an
  `ISessionMonitoringService.Updated` hook) computes the health score, appends a
  `BatteryHealthSnapshot`, recomputes the trend, runs the insight provider and
  replaces the active insight set; also serves `GetStatisticsAsync`.
- `Data`: `BatteryHealthSnapshotRepository`/`InsightRepository` +
  `HealthSnapshotStore`/`InsightStore`/`AnalyticsReadStore` (Core seams, same
  shape as `SessionStore`). `DatabaseMaintenanceService` gained the `SampleHour`
  and `DailyStatistics` rollup producers (closes R-067) plus minute/hour/daily
  retention. `HealthSnapshotRowCount`/`InsightRowCount` on the Diagnostics facts.
- `App`: `StatisticsViewModel` + rebuilt `StatisticsPage` (Today/7d/30d/Lifetime
  selector, summary tiles, charge-vs-discharge duration bars, screen-on share).
  `BatteryViewModel` + `BatteryPage` gained the "Battery Health Score" card with a
  working "How this score is calculated" `Expander`, a "Degradation trend" card
  and a "Time remaining" card ("Calculating…" then real figures, screen-off
  honestly Unavailable). `InsightsViewModel` + the Dashboard "Smart Insights"
  card (qualifying insights or the honest suppressed state); the Dashboard
  "Battery Health" card shows the real score + trend line. About gained the full
  `HealthScoreV1` / trend / `ChargingQualityV1` / runtime / insight-gates
  methodology (spec §55).

**Depends on:** 3, 4, 5, 6, 7.

**Exit criteria:**

| Criterion | Result |
|---|---|
| Score renormalises with cycle count and temperature both absent (the reference config) | ✅ `HealthScoreCalculatorTests.CycleCountAndTemperatureBothAbsent_…` (contributing weights sum to 1.0), `AnalyticsServiceTests.FirstPass_…` |
| No fabricated score — retention absent ⇒ Unavailable | ✅ `HealthScoreCalculatorTests.RetentionAbsent_MakesTheWholeScoreUnavailable` |
| Insights suppressed below the confidence and effect-size thresholds | ✅ `RuleBasedInsightProviderTests.WithinNoiseVariation_ProducesNoInsights` / `…ConfidenceBelowTheThreshold_Suppresses`; `AnalyticsServiceTests.Insights_AreEmpty_…` |
| "How this score is calculated" renders real per-factor contributions | ✅ `HealthFactor[]` with raw + normalised weight + basis; rendered in the `Expander` |
| Degradation trend is not moved by noise | ✅ `DegradationTrendTests.AFlatNoisySeries_…` / `OneOutlierReading_DoesNotSwingTheSlope` (Theil–Sen) |
| Runtime tiers; screen-off without history ⇒ Unavailable | ✅ `RuntimeEstimatorTests`, `RuntimeEstimationServiceTests.ScreenOff_…` |
| Statistics window boundaries / DST | ✅ `StatisticsEngineTests` (local-midnight start, midnight-straddling proration, DST day) |
| `SampleHour` + `DailyStatistics` producers | ✅ `DatabaseMaintenanceServiceTests` (settled-hours only, fully-elapsed-days only, idempotent) |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Unit + Simulation + Integration tests | ✅ 264 passing (195 + 30 + 39) |

**Deviations from plan**

- **The degradation trend uses a Theil–Sen median slope**, not the OLS the design
  implies — the exit criterion is explicitly "noise does not move the trend", and
  one anomalous full-charge reading otherwise swings an OLS line.
- **The `StatisticsEngine` computes from `BatterySession` rows directly**, not from
  the `DailyStatistics` tier. Sessions are never pruned (retention removes
  `BatterySample` rows, not sessions), so this is exact and needs no tier. The
  `SampleHour` + `DailyStatistics` producers still ship (closing R-067) — Phase 11
  (History) consumes them.
- **`DailyStatistics` temperature and end-of-day-health columns are left null** for
  now — the reference machine has no temperature sensor, and the stats engine does
  not need them. A later pass fills them for History.
- **Charging quality is computed but has no dedicated card yet** — `ChargingQualityScorer`
  ships and is unit-tested; it currently feeds understanding of the "rate
  stability" health factor. A charging-quality UI is later polish.
- **`RuntimeEstimationService` is a separate hosted service**, not folded into
  `PowerMonitoringService`, so the §3 model stays isolated and Power keeps one
  responsibility. Its "comparable historical periods" input is approximated from
  the in-window sample count (no cross-session store on the live path).
- **The Statistics window selector is Today / 7d / 30d / Lifetime** — a custom
  date range shares the engine (`StatisticsWindow.Custom` + `DateRange`) but its
  picker UI lands with Phase 11 (History), which owns range selection.
- **Insight rule corpus is 7 conservative rules.** Data-shaped and extensible;
  spec §77 ("never unsafe") and §18 ("suppressed if none qualify") favour a small
  credible set over breadth.
- **No `V002` migration** — every table used exists verbatim from `V001`.

---

## Phase 9 — Alerts (complete)

The bridge between "the app knows something is wrong" and "the user finds out".
Built like Phases 5–8: a pure engine in `Core.Alerts`, a promoted
infrastructure-sibling orchestrator, a Data store behind a Core seam, coalesced
view models. `AlertSettings`, `NotificationSettings` and the `Alert` table all
existed from earlier phases — **no migration**.

**What shipped:**

- `Core.Alerts.AlertRuleEngine` (pure, stateful, clock-injected): one rule per
  `AlertType` (low / critical / fully-charged / high-temperature / rapid-discharge
  / slow-charging / charger-connected / charger-disconnected / health-degradation
  / high-app-consumption — spec §20). Each threshold rule has **hysteresis** (fires
  on the entering edge, re-arms only after recovering past a margin) and a
  **cooldown** (a type cannot re-fire within `CooldownMinutes`; health-degradation
  gets 24 h). Charger events are edge-triggered off the AC line. `Evaluate` returns
  only the alerts that fire *this* tick. New `Alert` / `AlertEvaluationInput`
  models, `AlertType` / `AlertSeverity` enums.
- Core interfaces `INotificationPresenter`, `IAlertMonitoringService`,
  `IAlertStore`; `DataSettings.AlertRetentionDays` (90, clamped 7–3650).
- `Notifications` project: **promoted from the `ModuleMarker` stub**, stays plain
  `net10.0` — the engine and orchestrator are pure logic over Core interfaces.
  `AlertMonitoringService` (hosted) rides the battery, analytics and process
  monitors (debounced), builds the input (percentage, state, AC, temperature,
  discharge/charge rate + 7-day baselines, health score, degradation slope, top
  app share), runs the engine, persists every fired alert (priority write),
  always raises it in the in-app centre, then asks the presenter for a toast —
  a toast failure logs at Debug and is otherwise silent. The engine is timed off
  the reading's timestamp, not wall-clock, so the orchestrator is deterministic
  under test.
- `Data`: `AlertRepository` + `AlertStore` (insert, recent, unacked count,
  acknowledge one/all); `DatabaseMaintenanceService` drops `Alert` rows past
  `AlertRetentionDays`; `AlertRowCount` on the Diagnostics facts.
- `App`: `WindowsToastPresenter` (`INotificationPresenter`) —
  `AppNotificationManager` for this unpackaged app, `Register()` at startup and
  every `Show` fully wrapped; on failure `IsAvailable` stays false and the alert
  is in-app only. Rebuilt `AlertsPage` (active list, per-alert rules with
  toggles + sliders, delivery toggles, history) using a new `ToggleRow` control.
  `ShellViewModel.AlertCount` is the real unacknowledged count; the title-bar
  bell gained a badge and a recent-alerts flyout with "Acknowledge all" and
  "View all". Diagnostics shows the alert row count and the notification-channel
  status.

**Depends on:** 2, 3, 8.

**Exit criteria:**

| Criterion | Result |
|---|---|
| No duplicate or storming alerts | ✅ `AlertRuleEngineTests` (fires once crossing down; never re-fires while below; re-arms only past the margin; cooldown blocks a repeat even after a re-arm), `AlertMonitoringServiceTests` (oscillation around the line → still one alert) |
| Every default alert from spec §20 configurable | ✅ one `AlertType` + one `AlertsPage` toggle per `AlertSettings` field; `AlertRuleEngineTests.ADisabledAlert_NeverFires` |
| Notification failure degrades to in-app without error | ✅ `AlertMonitoringServiceTests.ANotificationFailure_DegradesToInApp_WithoutError` (presenter throws → alert still persisted, counted, `LastError` null); `WindowsToastPresenter` catches everything |
| Critical wins over low; charger edges; temperature hysteresis; health-degradation only when confident | ✅ dedicated `AlertRuleEngineTests` |
| Alert history + retention | ✅ `AlertStoreTests`, `DatabaseMaintenanceServiceTests.RunRetentionAsync_DropsAlertsPastTheAlertRetentionWindow` |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Unit + Simulation + Integration tests | ✅ 281 passing (205 + 33 + 43) |

**Deviations from plan**

- **The Notifications project is plain `net10.0`, not `net10.0-windows`.** The
  alert engine and orchestrator are pure; the one Windows API
  (`AppNotificationManager`) lives in `WindowsToastPresenter` in the App project
  behind the `INotificationPresenter` seam — so the whole engine is
  unit-testable and Notifications stays an infra sibling.
- **`RapidDischarge` / `SlowCharging` ship as engine rules but stay off by
  default** (matching `AlertSettings`); they compare the live rate against the
  7-day baseline from `IAnalyticsService.GetStatisticsAsync`, so they only do
  anything once a week of history exists.
- **`HighApplicationConsumption` fires on the top app's estimated share**, not an
  absolute wattage, and only while on battery (per-app absolute power is
  Unavailable on AC — Phase 7).
- **The engine is timed off the battery reading's timestamp**, not wall-clock, so
  cooldown/hysteresis are testable with a scripted timeline. In production the
  two are identical to the millisecond.
- **No `V002` migration** — the `Alert` table exists verbatim from `V001`.

---

## Phase 10 — Dashboard (complete)

Not a new subsystem — finishing the Dashboard as a whole: the eighth card, a
responsive grid, per-card info tooltips, and the last throttle.

**What shipped:**

- `Controls/ColumnGrid` — a hand-written responsive masonry `Panel`: 1 / 2 / 3 / 4
  equal-width columns by width (`Core.Layout.ResponsiveColumns.ForWidth` — the
  pure, unit-tested breakpoint function: <700 / <1100 / <1600 / else), each card
  placed in the currently shortest column so cards reflow without their text
  shrinking (`ui-navigation.md` §3). Unbounded width (a vertical `ScrollViewer`)
  falls back to 4 columns at a bounded column width.
- `Controls/CardHeader` gained an optional `Info` string → a ⓘ `FontIcon` with a
  `ToolTip` (`ui-navigation.md` §4; R-095). Empty by default, so every existing
  use is unaffected.
- `Views/DashboardPage.xaml` rebuilt: the single-column `StackPanel` inside a
  horizontally-scrolling `ScrollViewer` (the earlier deviation) is replaced by
  `ColumnGrid` inside a vertically-scrolling one. All seven existing cards move
  in unchanged; the **eighth — a "Today" statistics card** — is added (bound to
  the existing `StatisticsViewModel` at its Today window: on-battery / charging /
  screen-on, with the honest "no sessions today" state). Every card header now
  carries an `Info` tooltip and links to its full page; the Electric Power card's
  "Current" keeps its Calculated badge, Health and App Usage keep their Estimated
  badges. Subtitle updated.
- `BatteryViewModel` — added the same 1 Hz coalescing the Power / Temperature /
  App Usage view models already had (`MinRefreshInterval` + trailing-refresh
  flag), so every dashboard view model is now explicitly throttled regardless of
  the sampling rate (R-099).

**Depends on:** 2, 4–9.

**Exit criteria:**

| Criterion | Result |
|---|---|
| Eight cards integrated | ✅ Battery Overview, Battery Health, Current Session, Electric Power, Temperature, Application Usage, Today (Statistics), Smart Insights |
| Responsive 1366×768 → 4K, no clipping, no text shrink | ✅ `ResponsiveColumnsTests` (breakpoint table + degenerate widths); live: 3 columns at ~1280, reflows to 2 / 1 narrowing and 4 past ~1600, vertical scroll only |
| Grade badges | ✅ Estimated on Health + App Usage, Calculated on Power/Current, Retention badge on the hero |
| Info tooltips | ✅ every `CardHeader` on the dashboard has an `Info` line |
| Empty / unavailable states per card | ✅ Temperature (unavailable), Current Session (no session), App Usage (loading), Smart Insights (suppressed), Today (no sessions) — each explains why |
| Updates throttled regardless of sampling rate | ✅ every dashboard VM coalesces to 1 Hz; verified live with `PowerSampleSeconds = 1` |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Unit + Simulation + Integration tests | ✅ 296 passing (220 + 33 + 43) |

**Deviations from plan**

- **`ColumnGrid` is a hand-written masonry `Panel`**, not `ItemsRepeater` +
  `UniformGridLayout` or a CommunityToolkit `WrapPanel` — the cards are
  heterogeneous hand-authored XAML, and a new UI package for one panel is not
  worth it. `ForWidth` is pure and unit-tested; the arrange maths is verified
  live.
- **The hero card does not span multiple columns** — it sits in column 1 like any
  other card. A column-spanning hero is a later refinement if 3–4 columns look
  unbalanced.
- **The Today card reuses `StatisticsViewModel`** at its default window rather
  than a bespoke lightweight view model.

---

## Phase 11 — History and reporting (complete)

The last read-side feature: a tier-aware History page and a CSV / JSON export,
plus "delete all history" in Settings (R-069).

**What shipped:**

- `Core.History.HistoryTierSelector` — the pure, unit-tested tier logic:
  `TierForSpan(TimeSpan)` → `Raw` (≤ 6 h) / `Minute` (≤ 7 d) / `Hour` (≤ 120 d) /
  `Daily`, and `ResolveRange(HistoryRange, now)` → a concrete `DateRange`.
  `HistoryMetric` { ChargePercent, PowerMw, VoltageMv, TemperatureCelsius },
  `HistoryRange`, `[Flags] ExportScope`, and the `HistoryRequest` / `ExportRequest`
  / `ExportTable` models. Seams `IHistoryReadStore`, `IExportDataSource`,
  `IReportExporter`, `IHistoryMaintenance` — declared in Core, implemented in Data
  (reads) and Reporting (formatting), the same pattern as `ISessionStore`.
- `Reporting` project promoted from its stub (plain `net10.0`, Core-only
  reference — formatting is pure). `CsvExporter` — RFC 4180 quoting (a field with
  `,` `"` newline or an edge space is quoted, `"` doubled), `\r\n`, a UTF-8 BOM,
  one `# <name>` section per table. `JsonExporter` — `Utf8JsonWriter` streaming a
  root `{ exportedUtc, range, tables: { "<name>": [ {col: value} ] } }`; values
  are the strings the data source produced. Both write incrementally to the stream.
- `Data.HistoryReadStore` — `TierForSpan` picks the table + column, a single
  `SELECT time, value WHERE time IN [from, to)` reads it, `MinMaxDownsampler`
  bounds the result to the point budget (default 800) → a `ChartSeries`.
  `GetExtentAsync` (`MIN`/`MAX` over `BatterySample` + `SampleHour`),
  `HasTemperatureDataAsync`. `Data.ExportDataSource` — one range-filtered `SELECT`
  per scope flag, every value invariant-stringified, enum columns written as
  names (`AlertType = "LowBattery"`). `Data.HistoryMaintenance` — one
  `DELETE FROM` per telemetry / session / health / insight / alert table in a
  transaction, then `VACUUM` (the one sanctioned `VACUUM`, `database.md` §6);
  `BatteryDevice` / `AppSettings` / `DataRetentionSettings` / `SchemaMigration`
  are kept.
- `Controls/PowerChart` extended: an `XLabelFormat` DP (or auto — `HH:mm:ss` /
  `HH:mm` / `ddd HH:mm` / `d MMM` / `MMM yyyy` chosen from the visible span, with
  a matching tick spacing), and an `EnableZoom` DP → `ZoomMode = X` plus
  double-tap-to-reset. Both off/empty by default, so Power and Temperature are
  unchanged.
- `ViewModels/HistoryViewModel` — range + metric `Segmented`s, the current
  `ChartSeries` and its window, a tier caption ("Minute averages · 7 days"),
  loading / empty / "no temperature sensor" states, an export-scope checklist,
  and `ExportCsv` / `ExportJson` commands over `Services/ExportService`
  (a `FileSavePicker` via `InitializeWithWindow` with the shell window resolved
  lazily through `App.ShellWindow`, then a plain `FileStream` to the picked path
  — the only path the app ever writes to). `Views/HistoryPage` rebuilt in the
  card language.
- Settings gains a **DATA** section: the data folder, and "Delete all battery
  history" behind a `ContentDialog` that requires typing `DELETE`
  (`SettingsViewModel.DeleteAllHistoryAsync` → `IHistoryMaintenance`).
- Diagnostics Storage section gains a **History extent** row (earliest → latest
  sample) from `IHistoryReadStore.GetExtentAsync`.

**Depends on:** 3, 8.

**Exit criteria:**

| Criterion | Result |
|---|---|
| History page, 24 h – 1 y ranges | ✅ five ranges; live: 24 h/7 d read minute averages, 30/90 d hourly, 1 y falls back to the hour tier |
| Tier-aware queries (raw → minute → hour → daily) | ✅ `HistoryTierSelectorTests` (every boundary); `HistoryReadStoreTests` (2 h → Raw, 3 d → Minute, 60 d → Hour, empty range → `ChartSeries.Empty`) |
| One-year range loads without stalling the UI | ✅ live: the query is off-thread and cancellable, the year range renders from ≤ a few thousand hour rows, downsampled to 800 |
| Zoom + tooltips | ✅ `EnableZoom` → X pan/zoom, double-tap reset; adaptive axis + tooltip labels |
| `CsvExporter` + `JsonExporter` behind `IReportExporter` | ✅ `CsvExporterTests` (RFC 4180 round-trip, BOM, `\r\n`, multi-table, empty table), `JsonExporterTests` (valid JSON, shape, escaping, `[]` for 0 rows) |
| Export scope selection | ✅ eight-table checklist → `ExportScope` flags; `ExportDataSourceTests` (scope selects tables, range filters rows, enums as names) |
| Safe path handling | ✅ the destination only ever comes from the OS picker; all export I/O in `try/catch` → a friendly message, never a throw |
| Delete all history (R-069) | ✅ `HistoryMaintenanceTests` (every telemetry table emptied, `BatteryDevice` + `SchemaMigration` survive); Settings dialog requires typing `DELETE` |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Unit + Simulation + Integration tests | ✅ 330 passing (244 + 33 + 53) |

**Deviations from plan**

- **The 1-year range charts every metric from the `Hour` tier** (its 365-day
  window), not `DailyStatistics` — `DailyStatistics` holds session aggregates, not
  a per-metric time series, so it cannot back a smooth line. The tier caption says
  "Hourly averages" for the year range.
- **`JsonExporter` writes numeric values as the data source's strings**, not JSON
  numbers — the data source is the single authority on formatting/rounding, and
  one representation avoids a class of precision/locale bugs. The JSON is still
  valid and readable.
- **Zoom is LiveCharts' built-in X pan/zoom (`ZoomMode`)**, not a custom
  range-brush — it is what the library provides and it covers "zoom and tooltips".
- **Custom date ranges are modelled (`HistoryRange.Custom`, `ResolveRange` honours
  it) but the History page ships the five presets only** — a date-picker UI is a
  small later addition; nothing downstream needs it yet.
- **`ExportService` resolves the shell window through `App.ShellWindow` at call
  time**, not by constructor injection — injecting `MainWindow` created a
  construction-time cycle when the startup page is History.
- **No `V002` migration** — every table used exists verbatim from `V001`.

---

## Phase 12 — Diagnostics (complete)

Finishing the Diagnostics/About surface. The capability matrix, database
statistics, "Copy report", and the About versions / privacy / methodology were
already in place from Phases 1–11; this phase adds the two missing deliverables
(per-subsystem monitoring state, and a log viewer) plus a licence list and report
redaction.

**What shipped:**

- `Core.Diagnostics.MonitoringStatusRegistry` (`IMonitoringStatusRegistry`) — a
  thread-safe singleton every hosted orchestrator reports each tick's outcome to.
  `MonitoringComponent` { Battery, Power, Temperature, ApplicationUsage, Sessions,
  Analytics, Alerts, Database }; `MonitoringHealth` { Starting, Healthy, Degraded }
  — `Degraded` after `DegradedThreshold` (3) consecutive failures, back to
  `Healthy` on the next success. `Changed` fires only on a rendered-state change,
  not per success tick. Pure C#, unit-tested.
- The eight orchestrators wired: one `IMonitoringStatusRegistry` constructor arg
  and a `ReportSuccess` / `ReportFailure` call beside each existing
  `LastError = …` line. `SessionMonitoringService` and `DatabaseMaintenanceService`
  (rollup + retention) gained the tick-error tracking they lacked.
- `Core.Diagnostics.LogLineParser` + `LogEntry` — parses the Serilog file sink's
  output template into typed entries, folding exception/continuation lines into
  the entry above; levels normalised (`INF→INFO`, `FTL→FATAL`, …). `ILogReader`
  (Core) / `LogFileReader` (App) reads the newest `app-*.log` with
  `FileShare.ReadWrite`, returns the last N entries, never throws.
- `DiagnosticsViewModel` — a **Monitoring** section (reusing the existing
  `DiagnosticSection` renderer: name · Healthy / Degraded — <error> / Starting… ·
  last-activity), rebuilt on `IMonitoringStatusRegistry.Changed`; a **Recent
  activity** log viewer (level `Segmented` filter, Refresh, Open log folder); and
  the copied report now redacts the user-profile path to `%USERPROFILE%`
  (R-100 / spec §46).
- `DiagnosticsPage.xaml` — a "Recent activity" card (monospace, capped-height
  scroll, level chips coloured by severity).
- About page — a **LICENCES** card (Windows App SDK / WinUI 3, CommunityToolkit.Mvvm,
  Serilog, LiveChartsCore, SkiaSharp, H.NotifyIcon, Microsoft.Data.Sqlite with
  their licences) above the acknowledgements paragraph.

**Depends on:** all prior phases.

**Exit criteria:**

| Criterion | Result |
|---|---|
| Live capability matrix, every unavailable row with its reason | ✅ unchanged from Phase 1; `BuildBatterySection` renders all rows incl. Unavailable |
| Monitoring state per subsystem with last error | ✅ `MonitoringStatusRegistry` + Diagnostics "Monitoring" section; `MonitoringStatusRegistryTests` (transitions, `Changed` semantics), `ProcessMonitoringServiceTests` failure-isolation test (enumerator throws → ApplicationUsage Degraded, others Starting, recovers) |
| Database statistics | ✅ unchanged (Storage section) |
| Log viewer | ✅ `LogFileReader` + `LogLineParser` (`LogLineParserTests`); Diagnostics "Recent activity" card with level filter + Open folder |
| "Copy diagnostics" contains no personal data | ✅ user-profile path redacted to `%USERPROFILE%`; the report has no names, serials, or file contents |
| About: versions, licences, privacy, methodology | ✅ all four present (licences new this phase) |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Unit + Simulation + Integration tests | ✅ 342 passing (255 + 34 + 53) |

**Deviations from plan**

- **Two health states (`Healthy` / `Degraded`) plus `Starting`, not the three of
  `monitoring-dataflow.md` §8** — the `Retrying` state needs an
  exponential-backoff retry layer that does not exist yet (orchestrators catch and
  continue at their normal cadence). The registry surfaces what the Diagnostics
  page needs now; backoff is Phase 13's adaptive-sampling work.
- **`Degraded` is a fixed threshold of ≥ 3 consecutive tick failures**, not
  per-subsystem-tuned.
- **The log viewer reads the current file only** (the newest `app-*.log`), not
  the full 14-file history — "recent activity" is what the page needs; the folder
  button opens the rest.
- **`Copy report` redacts only the user-profile path prefix** — the only personal
  data the report contains.

---

## Phase 13 — Optimisation (adaptive sampling + backoff; measurement)

The deferred adaptive-sampling table (`monitoring-dataflow.md` §3), the
`Retrying`/backoff layer (R-098), and a self-metrics surface to measure the §1
budgets against.

**What shipped:**

- `Core.Monitoring.AdaptiveSamplingPolicy` — the §3 table, pure and unit-tested:
  `Resolve(baseBattery, baseProcess, SamplingConditions) → SamplingPlan`. Screen
  off on battery ×3; screen off + AC + full → battery ×6, process paused; battery
  < 20 % discharging ×0.5 (finer, and it beats every back-off); charging session
  holds the base rate; window hidden → `ChartFeedSuspended`; monitoring paused →
  `AllStopped`; adaptive disabled → base rates. Every interval clamped to 1–300 s.
- `Core.Monitoring.MonitoringBackoff.NextInterval` — `base × 2^(n-1)` capped at
  5 min.
- `MonitoringHealth` gained `Retrying` (first 1–2 failures) between `Healthy` and
  `Degraded`; the registry and the Diagnostics "Monitoring" label follow.
- `Core.Interfaces.IAppVisibilityState` / `AppVisibilityState` — a tiny Core seam
  the App feeds from `MainWindow.VisibilityChanged`.
- `BatteryMonitoringService` (drives battery **and** power) and
  `ProcessMonitoringService` each rebuild their `Timer` interval from the policy
  on every relevant signal (`ScreenStateChanged`, `IBatteryMonitoringService.Updated`,
  `ISessionMonitoringService.Updated`, `IAppVisibilityState.Changed`,
  `ISettingsService.Changed`) and after every read, applying the back-off on a
  failure run and restoring the adaptive rate on success. `Monitoring.Paused`
  stops the timers and gates the event-driven refreshes.
  `ThermalMonitoringService` rides the battery cadence unchanged. Each service
  exposes `CurrentInterval` for tests/diagnostics.
- `Core.Diagnostics.ISelfMetrics` / `App.Services.SelfMetrics` — process CPU %
  (cumulative-time delta), working set, private bytes, GC heap, thread count,
  pending writes, last flush. A **"This app's footprint"** section on the
  Diagnostics page shows each against its budget.
- Chart point budget reconciled to 600 (`HistoryRequest.PointBudget` default was
  800; `PowerMonitoringService` already used 600).

**Depends on:** all prior phases (12 for the registry/Diagnostics surface).

**Exit criteria:**

| Criterion | Result |
|---|---|
| Adaptive sampling per §3 | ✅ `AdaptiveSamplingPolicyTests` (every row); `ProcessMonitoringServiceTests` (screen-off ×3 live-driven, low-battery ×0.5, restores); live: battery interval logged at the base rate, process "paused" logged with `Monitoring.Paused` |
| Graceful subsystem failure with backoff (R-098) | ✅ `MonitoringBackoffTests`, `MonitoringStatusRegistryTests` (Retrying → Degraded → Healthy), `ProcessMonitoringServiceTests` (interval widens on repeated failure, restores on success) |
| Budgets measured | 🔨 self-metrics section added; ~7-minute in-session soak: CPU negligible, WAL growth ~0.4 MB/min (under the 1 MB/min budget). **Working set ~230–270 MB exceeds the 150 MB budget** — a WinUI 3 runtime-baseline reality; flagged for Phase 14 |
| No leaks over 24 h | ⏳ **manual — Phase 14 QA** (not runnable in-session; working set appeared to plateau near 267 MB over ~7 min) |
| No frame > 16 ms attributable to monitoring | 🔨 UI updates already coalesce to 1 Hz (Phase 10); frame profiling deferred to Phase 14 |
| Solution builds, Debug and Release | ✅ 0 errors, 0 warnings |
| Unit + Simulation + Integration tests | ✅ 360 passing (270 + 37 + 53) |

**Deviations from plan**

- **The 24-hour soak and frame-time profiling are deferred to Phase 14 QA** — not
  runnable in this session. Phase 13 ships the mechanism, the self-metrics
  surface, and a short in-session soak.
- **Working set exceeds the §1 150 MB budget** (~230–270 MB with the window open)
  — the WinUI 3 / WindowsAppSDK runtime baseline is most of it. Recorded honestly
  in the footprint section; a real reduction (if feasible) is Phase 14/15 work,
  and the budget number itself may need revising for the platform.
- **"Chart feed suspended when hidden" is a ViewModel-side skip**, not a hard stop
  of the monitoring service — DB sampling continues.
- **`Monitoring.Paused` is honoured but has no Settings toggle yet** — the wiring
  is complete; the UI control is a later addition.
- **`Retrying` vs `Degraded` stays a fixed count threshold**; the backoff interval
  is the exponential part.

---

## Phase 14 — QA (automatable closure; manual checklist produced)

Closing every `traceability.md` gap a test can close, hardening the
database-failure paths, an accessibility pass, and turning the hardware/soak
requirements into a concrete release checklist.

**What shipped:**

- **Failure-matrix tests** — `DatabaseFailureTests` (integration): an unreachable
  database degrades gracefully and requeues (no data loss), the pending cap
  bounds memory, a locked database recovers without a restart, a reader and a
  writer coexist under WAL, and a **corrupt database is detected and never
  deleted**.
- **Corrupt-DB hardening** — `DatabaseMigrator` runs `PRAGMA quick_check` before
  touching anything; a failure returns `false` (the app runs without persistence
  and says so on Diagnostics) rather than crashing or "repairing". The four write
  queues now report `MonitoringComponent.Database` **Degraded** to the registry
  on a flush failure, so a locked/full/corrupt database is visible in the
  Diagnostics "Monitoring" section, not just the log.
- **R-047** — `SessionTimelineTests`: `GetTimelineAsync` merges sessions, session
  events and system events in chronological order and clips to the range.
- **R-062 / R-066** — `V002__AggregateTimeIndexes.sql` adds `IX_SampleMinute_Time`
  / `IX_SampleHour_Time` (the aggregate-tier history reads scanned without them).
  `QueryPlanTests` runs `EXPLAIN QUERY PLAN` on the hot-path SELECTs and asserts
  no full table scan. `MigrationTests.LaterMigration_BacksUpFirst_AndLosesNoRows`
  finally exercises backup-before-migration on a real V001→V002 with seeded rows.
- **R-092 / R-093 / R-094** (accessibility) — every `Segmented` got an
  `AutomationProperties.Name`; `ToggleRow` names its inner switch from the header;
  `Sparkline` is marked decorative (`AccessibilityView="Raw"`); the empty-state
  audit confirmed all 14 `EmptyStateView` uses carry a "why"; severity is always
  colour **+ icon + text** (verified). The page-header refresh buttons already
  had names.
- **R-095** — a reusable `Controls/InfoDot` (`ⓘ` glyph, tooltip **and**
  accessible name). `MetricStat` gained an `InfoText` DP; the Battery page
  (Current, runtime figures) and Power page (Current) now carry the provenance
  sentence on hover.
- **R-100** — `docs/security-review.md`: no admin, parameterised SQL (the three
  identifier-interpolating readers use closed hard-coded sets, never input), safe
  paths, no network, no secrets in logs. Clean; no code change needed.
- **Measurement** — `App` logs `Application ready in {ms}`; cold start measured at
  **~1.0–1.4 s** (< 2 s budget). `docs/qa-checklist.md` is the manual matrix for
  everything a human/second machine must verify, each row mapped to its §68/`Nx`
  criterion.

**Depends on:** all prior phases.

**Exit criteria:**

| Criterion | Result |
|---|---|
| Unit + Simulation + Integration suites green | ✅ 374 passing (270 + 37 + 67) |
| Failure matrix (spec §62) | ✅ automated column done (`DatabaseFailureTests` + existing simulation/session tests); manual confirmations in `qa-checklist.md` §E |
| Migrations, no history loss (R-066) | ✅ `MigrationTests.LaterMigration…` — V001→V002 with data, backup written, zero row loss |
| Indexed hot paths (R-062) | ✅ `QueryPlanTests` — every hot-path SELECT uses an index |
| Session timeline (R-047) | ✅ `SessionTimelineTests` |
| Accessibility (R-092/093/094) | ✅ automatable parts (names, decorative marks, empty-state audit, colour-not-alone); screen-reader + keyboard-only + high-contrast pass → `qa-checklist.md` §C |
| Estimation transparency (R-095) | ✅ `InfoDot` on Battery + Power; dashboard + About already done |
| Security review (R-100) | ✅ `docs/security-review.md` — clean |
| Cold start < 2 s | ✅ ~1.0–1.4 s measured in-session |
| Working set < 150 MB | ❌ ~230–270 MB (WinUI 3 baseline) — recorded in `qa-checklist.md` §F, carried to Phase 15 |
| 24 h soak, Win10, real transitions, screen reader | ⏳ **manual — `qa-checklist.md`** (not runnable in this session) |
| Builds Debug + Release, 0 warnings | ✅ |

**Deviations from plan**

- **The hardware / OS / soak checklist is not executed** — Windows 10 rendering, a
  screen-reader pass, real sleep/resume + charger cycling and the 24-hour leak
  soak need a human and a second machine. `docs/qa-checklist.md` enumerates them
  with their §68 mapping; everything a test can close is closed.
- **Corrupt-database handling is an integrity check + Degraded status + honest
  log**, not a full app-wide read-only banner — the app keeps reading cached
  state, writes degrade and surface, and the file is never deleted. A dedicated
  read-only UI mode is deferred.
- **Working set still exceeds the §1 150 MB budget** — the checklist records the
  number; a real reduction (or a revised platform budget) is Phase 15.
- **No ViewModel state-selection unit tests** — the App project (WinUI `WinExe`)
  cannot be referenced by a test project, and the state logic is trivial boolean
  composition. The empty-state *content* is audited; the *selection* is covered
  by the live regression sweep.

---

---

## Phase 15 — Release (authored; clean-machine verification is a runbook)

The release-engineering pass. Everything short of the clean-VM install/sign is
done; those steps are `docs/release.md` + `docs/qa-checklist.md`.

**What shipped:**

- **Data-path hardening** — `AppPaths.DataDirectory` now resolves the root from
  the `%LOCALAPPDATA%` environment variable (never redirected), not the
  `SpecialFolder` API (redirected into a package-private folder under some MSIX
  configs). So the data location is byte-identical packaged and unpackaged, an
  MSIX install finds an existing unpackaged database, and uninstall never touches
  it (spec §58). `AppPathsTests` covers it.
- **Simulation excluded from Release** — `tools/verify-no-simulation.ps1` builds
  `-c Release` and checks the shipping assemblies contain no
  `SimulatedBatteryProvider` / `BatterySimulationScenario` / `Fake*`. Runs
  `PASS` (N9). `SIMULATION` is Debug-only; every simulation file is `#if SIMULATION`.
- **MSIX packaging (authored)** — `src/BatteryIntelligence.App/Package.appxmanifest`
  (identity, `runFullTrust` only, `windows.startupTask`, splash/logos), a
  `-p:EnablePackaging=true` build path in `App.csproj` that leaves the default
  build unpackaged, and `tools/generate-msix-assets.ps1` (the tile PNGs, written
  from source like `generate-icon.ps1`). The **unpackaged** Debug + Release build
  is verified 0/0; the packaged build + signing + install is `docs/release.md` §4–§6.
- **Versioning** — `Directory.Build.props` gains `<InformationalVersion>` and a
  "bump both here and the manifest" comment; About shows the full version + MIT.
- **`README.md`, `CHANGELOG.md`, `LICENSE` (MIT)** at the repo root.
- **`docs/release.md`** — pre-flight gates, unpackaged + MSIX build, signing,
  the clean-VM install/upgrade/uninstall verification, rollback, and the
  release-time deviations.
- **About → LIMITATIONS card** — `docs/limitations.md` said it was "surfaced in
  the application under About" but nothing did; now a card summarises it (no
  temperature sensor on many laptops; per-app energy is a model; cycle-count-zero
  handling; estimates need history; the Diagnostics page shows *your* machine).
- **`/docs` §69 pass** — every stale `Status:` header refreshed to
  "1.0.0 — all phases complete"; `database.md` → "Schema version 2";
  `roadmap.md` → "all 15 phases complete". The mandatory set is complete:
  prd, architecture, api-strategy, capability-matrix, database, monitoring-dataflow,
  session-engine, estimation-strategy, ui-navigation, testing, limitations,
  traceability, roadmap, security-review, qa-checklist, release.

**Depends on:** all prior phases.

**Exit criteria:**

| Criterion | Result |
|---|---|
| Release config, simulation excluded | ✅ `tools/verify-no-simulation.ps1` → `PASS` |
| MSIX packaging | 🔨 manifest + build config + asset generator authored and the unpackaged build verified; the `.msix` build/sign/install is `release.md` §4–§6 (needs a CA cert) |
| Versioning, single source | ✅ `Directory.Build.props` + manifest, documented |
| Install / upgrade / uninstall, data preserved | 🔨 the mechanism is done and tested (`AppPathsTests` + `MigrationTests`); the clean-VM run is `qa-checklist.md` §G4/§G5 + `release.md` §6 |
| README, release notes | ✅ `README.md`, `CHANGELOG.md` (1.0.0) |
| `/docs` set complete per §69 | ✅ 16 docs, headers current, cross-links resolve |
| Builds Debug + Release (unpackaged), 0 warnings | ✅ |
| Unit + Simulation + Integration tests | ✅ 379 passing (275 + 37 + 67) |

**Deviations from plan**

- **The MSIX is not built, signed or installed in-house** — no code-signing
  certificate and no clean machine in this session. The manifest, gated build
  config, asset generator and the full runbook (`docs/release.md`) are complete;
  the unpackaged build is the tested 1.0 artifact.
- **`AppPaths` resolves from `%LOCALAPPDATA%`** rather than `SpecialFolder` — a
  deliberate change so MSIX virtualization cannot orphan an existing database.
- **Working set (~230–270 MB vs 150 MB) is an accepted deviation for 1.0** — the
  WinUI 3 / SkiaSharp / WinAppSDK runtime floor; documented in `release.md` §8,
  reported honestly by the Diagnostics footprint section.
- **The 24-hour soak, Windows 10 render pass, screen-reader pass and real
  power-transition testing are checklist items** for the release engineer
  (`qa-checklist.md`), not run here.

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
