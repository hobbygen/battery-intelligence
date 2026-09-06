# Requirements Traceability Matrix

Status: 1.0.0 — all phases complete. Version 1.0.0.

Covers spec §70: requirement → module → implementation → test → status.

**Status key:** `📋` designed (Phase 0) · `🔨` in progress · `✅` done · `⛔` blocked

---

## Core principles

| ID | Requirement | Spec | Module | Implementation | Test | Status |
|---|---|---|---|---|---|---|
| R-001 | Measured / Calculated / Estimated / Unavailable taxonomy | §3 | Core | `DataQuality`, `MeasurementSource`, `Measurement<T>` | `MeasurementTests`, `DataQualityTests` | ✅ |
| R-002 | Never present estimated as measured | §3, §66 | Core, App | Named factories + `Worst` grade propagation | `Combine_OfTwoMeasured_YieldsCalculated_NotMeasured` | ✅ |
| R-003 | Never show fake zeros | §3, §43 | Core, App | Nullable `Measurement<T>.Value`; `EmptyStateView` | `Unavailable_IsNotZero` | ✅ |
| R-004 | Capability detection at startup and resume | §26, §72 | Battery | `BatteryCapabilityDetector` → `CapabilitySnapshot`; re-run on `WM_DEVICECHANGE` | `BatteryCapabilityDetectorTests` (present/absent sensors, no-battery, provider failure) | ✅ |
| R-005 | Diagnostics page mandatory | §26, §47 | App | `DiagnosticsPage`: live capability matrix (every Unavailable row with its reason), storage stats, per-subsystem **Monitoring** section, **Recent activity** log viewer, "Copy report" (user-profile path redacted) | Manual, verified on reference machine (Phase 12: Monitoring + log viewer render, no crash) | ✅ |

## Architecture

| ID | Requirement | Spec | Module | Implementation | Test | Status |
|---|---|---|---|---|---|---|
| R-010 | Layered modular architecture | §4 | all | 12 projects, acyclic; pure layers on `net10.0` | Solution build | ✅ |
| R-011 | UI isolated from hardware APIs | §4, §73 | App | ViewModel to service to provider | Core/Data cannot reference Windows APIs by TFM | ✅ |
| R-012 | MVVM + DI | §5 | App | CommunityToolkit.Mvvm + generic host | App launches with DI | ✅ |
| R-013 | Repository pattern | §5 | Data | `BatteryDeviceRepository`, `BatterySampleRepository`, `PowerSampleRepository`, `BatterySessionRepository`, `SessionEventRepository`, `SystemEventRepository` — one per table/aggregate, prepared statements | `BatterySampleWriteQueueTests`, `PowerSampleWriteQueueTests`, `MigrationTests` | ✅ |
| R-014 | Provider + strategy for hardware | §5 | Battery/Power/Thermal | `CompositeBatteryProvider` merges S1/S3/S4 per field; `PowerEstimator` is the Power module's strategy ladder | `SimulatedBatteryProviderTests`, `PowerEstimatorTests`; live on reference machine | ✅ (Battery + Power) |
| R-015 | Nullable enabled, warnings serious | §65 | all | `Directory.Build.props`: nullable + warnings-as-errors | Debug and Release build clean | ✅ |

## Battery and health

| ID | Requirement | Spec | Module | Implementation | Test | Status |
|---|---|---|---|---|---|---|
| R-020 | Battery state, percentage, AC | §8 | Battery | `CompositeBatteryProvider`: state S1→S3→S2, AC from S2 | `SimulatedBatteryProviderTests`; live: Charging/84%/Connected verified against OS battery flyout | ✅ |
| R-021 | Capacity: design / full / remaining | §9 | Battery | S1→S3→S4 chain | Live: 95,008/38,008 mWh, exact match to `capability-matrix.md` §1 | ✅ |
| R-022 | Capacity retention / wear | §9 | Core (`BatteryCalculations`) | `full ÷ design`, Calculated. **Deviation:** placed in Core, not Analytics — it is exact arithmetic on every reading, not a statistics feature, and Core's zero-dependency rule keeps it unit-testable without a battery | `BatteryCalculationsTests`: missing design ⇒ Unavailable, not 0; live: 40.0% exact match | ✅ |
| R-023 | Cycle count where available | §9 | Battery/Core | S4→S3; `ApplyCycleCountZeroQuirk` ⇒ Unavailable | `BatteryCalculationsTests`, `SimulatedBatteryProviderTests`; live: reference machine correctly Unavailable | ✅ |
| R-024 | Manufacturer / model / chemistry | §9 | Battery | S3→S4; packed-ASCII chemistry tag decoded, little-endian (quirk Q4) | Live: "SMP / DELL 68ND307 / LiP", exact match to `capability-matrix.md` §1 | ✅ |
| R-025 | Explainable health score | §19 | Core.Analytics | `HealthScoreCalculator` = `HealthScoreV1`; weights renormalise across available factors; `HealthFactor[]` → `FactorsJson` + the "How this score is calculated" expander | `HealthScoreCalculatorTests` (renormalisation matrix, retention-required, band boundaries), `AnalyticsServiceTests`; live: reference machine scores 40 / Poor with retention the only contributing factor (weight renormalised to 1.0) | ✅ |
| R-026 | No invented health % when data absent | §9 | Core | Retention required, else Unavailable | `BatteryCalculationsTests.CalculateRetentionPercent_DesignCapacityUnavailable_YieldsUnavailable_NotZero` | ✅ |
| R-027 | Health trend with smoothing | §53 | Core.Analytics | `DegradationTrendCalculator` — Theil–Sen median slope over ≥30 days of `BatteryHealthSnapshot` retention; `Calculating` below the floor | `DegradationTrendTests` (flat noisy series ⇒ ~zero slope; one outlier does not move it), `AnalyticsServiceTests` | ✅ |
| R-028 | Multiple batteries | §25 | Battery/Core | `HardwareId`-keyed devices; `BatteryAggregation` sums only mathematically valid quantities | `BatteryAggregationTests`, `SimulatedBatteryProviderTests.MultipleBatteries_*` | ✅ |

## Power and temperature

| ID | Requirement | Spec | Module | Implementation | Test | Status |
|---|---|---|---|---|---|---|
| R-030 | Current / voltage / power live | §13 | Power | `PowerMonitoringService` over `CompositeBatteryProvider`'s Measured power/voltage + Calculated current, on the 5 s power cadence | `PowerMonitoringServiceTests`; live: `PowerSample` rows every ~5 s, 8,533 mW / 11,609 mV / 735 mA | ✅ |
| R-031 | Power fallback chain | §51 | Core (`PowerEstimator`) | Measured → Calculated (V×I) → Estimated (ΔmWh/Δt ≥ 60 s) → Unavailable; grade degrades only | `PowerEstimatorTests` (all four rungs; sub-60 s history ⇒ Unavailable; Suspect measured power not treated as rung 1) | ✅ |
| R-032 | Current always Calculated | §51 | Core (`BatteryCalculations`) | `P ÷ V`, guarded against implausible voltage | `BatteryCalculationsTests.CalculateCurrentMa_IsAlwaysCalculated_NeverMeasured`; live: 735 mA = 8,533 mW ÷ 11,609 mV | ✅ |
| R-033 | mA/mWh unit normalisation | quirk Q2 | Core/Battery | `BATTERY_CAPACITY_RELATIVE` bit (S4) / `Capabilities` (S3); normalise via voltage or U | `BatteryCalculationsTests`, `SimulatedBatteryProviderTests.MilliampReporting_*` | ✅ |
| R-034 | Min/max/avg per metric | §13 | Core (`RollingStatistics`) | Windowed accumulator per metric; Suspect excluded; grade = worst contributor; `SnapshotBetween` for sub-windows | `RollingStatisticsTests`, `PowerMonitoringServiceTests` | ✅ |
| R-035 | Configurable sampling interval | §13, §30 | Core (`MonitoringSettings`, `Monitoring.AdaptiveSamplingPolicy`) + Battery + ProcessMonitoring | `PowerSampleSeconds` / `ProcessSampleSeconds` (clamped 1–300) are the base rates; `AdaptiveSamplingPolicy` throttles them by screen/AC/charge-%/visibility/paused; the battery and process samplers rebuild their timer on every signal | `AdaptiveSamplingPolicyTests` (every §3 row), `ProcessMonitoringServiceTests` (screen-off ×3, low-battery ×0.5, restores); live: base cadence + "paused" logged | ✅ |
| R-036 | Battery temperature, or honest absence | §14 | Thermal | `ThermalMonitoringService` reads `BatteryInfo.TemperatureCelsius` (already resolved S4→S3); on the reference machine the sensor is absent, `SensorAvailable` is false, the page renders the unavailable state and never substitutes CPU temp | `ThermalMonitoringServiceTests` (sensor present + absent), `TemperatureBandClassifierTests`; live: reference machine renders "Sensor unavailable" with the §14 wording, nothing persisted | ✅ |
| R-036b | Per-band time breakdown + threshold events | §14 | Thermal | `TemperatureBandClassifier` (fixed 30/40/45 °C); `ThresholdEventDetector` (60 s dwell gate) | `ThresholdEventDetectorTests`, `ThermalMonitoringServiceTests.Ingest_RisingTemperature*` | ✅ |
| R-036c | `TemperatureSample` pipeline + retention | §14, §31 | Data | `TemperatureSampleWriteQueue` (deci-Kelvin, 200/30 s/flush, 1000 cap), raw-only age-cutoff retention | `TemperatureSampleWriteQueueTests` (deci-Kelvin, count trigger, unavailable persists nothing) | ✅ |
| R-037 | Configurable thermal thresholds | §14 | Core (`AlertSettings.HighTemperatureCelsius`) + Thermal + Notifications | Warning value drives `TemperatureThresholds` (critical = warn + 7 °C); the classifier and the `HighTemperature` alert rule both move with it | `TemperatureBandClassifierTests.Classify_SeverityShifts_…`, `AlertRuleEngineTests.HighTemperature_UsesHysteresisAroundTheThreshold` | ✅ |

## Sessions

| ID | Requirement | Spec | Module | Implementation | Test | Status |
|---|---|---|---|---|---|---|
| R-040 | Charge/discharge session detection | §49 | Core (`SessionStateMachine`) | Debounced rate-based opens; immediate AC-line-driven closes | `SessionStateMachineTests` (14 tests incl. full §8 scenario matrix) | ✅ |
| R-041 | No duplicate sessions | §49 | Sessions, Data | `UX_Session_Open` partial unique index (schema, Phase 3) + one open session per battery in `SessionMonitoringService` | Structural (index) + engine never opens a second session while one is tracked | ✅ |
| R-042 | Session state survives crash | §49 | Sessions, Data | `RecoverAsync`: adopt within 5 min grace, else close `Interrupted` | `Adopt_ContinuesAnExistingOpenSession`; live: clean startup path exercised (no crash induced) | ✅ (adoption logic); manual crash induction not performed |
| R-043 | Screen on/off/lock/sleep tracked separately | §50 | Core, Windows | `ScreenState`/`LockState`/`SystemPowerState` are independent enums; screen accumulates into session totals, lock/suspend tracked separately | `SessionStateMachineTests` sleep/gap tests; live: S5/S6/S7 all wired via `BatteryMessageWindow` | ✅ |
| R-044 | Sleep / hibernate / resume handled | §24 | Windows, Sessions | `PBT_APMSUSPEND`/`_RESUME*` → `ProcessSuspend`/`ProcessResume`; missed notifications caught by gap detection | `SuspendAndResumeMidDischarge_*`, `MissedSuspendNotification_*` | ✅ (hibernate not distinguished from standby — platform does not expose the distinction to user mode) |
| R-045 | Re-validate devices on resume | §24 | Battery | `BatteryMonitoringService.OnResumed`: re-enumerate + re-run capability detection | Code path shared with Phase 2's `DeviceChange` handling, which is live-verified | ✅ |
| R-046 | No duplicate/interpolated samples across sleep | §24 | Core | `ProcessResume`/gap detection only add to `SleepSeconds`; never synthesise a sample | `SuspendAndResumeMidDischarge_OneSession_SleepSecondsPopulated_NoInterpolation` | ✅ |
| R-047 | Session timeline | §12 | Data, Sessions | `ISessionStore.GetTimelineAsync`: real session spans + session/system event markers, merged and chronologically ordered, clipped to the range | `SessionTimelineTests` (merge order, range clip, empty range) | ✅ |
| R-048 | Consumption split by screen state | §11 | Core.Analytics | `DischargeAnalyzer` splits the discharge rate into screen-on and screen-off means from the screen-tagged `RollingRate`; screen-off is never extrapolated from screen-on | `DischargeAnalyzerTests` (split; no screen-off samples ⇒ null), `RuntimeEstimationServiceTests` | ✅ |

## Applications

| ID | Requirement | Spec | Module | Implementation | Test | Status |
|---|---|---|---|---|---|---|
| R-050 | Process monitoring | §15 | ProcessMonitoring | `ProcessCpuCalculator` delta CPU; `SystemProcessEnumerator` cached metadata; `ProcessMonitoringService` bounded top-N + "Other", skip-when-idle | `ProcessCpuCalculatorTests`, `ProcessMonitoringServiceTests` (top-N collapse, delta across ticks) | ✅ |
| R-051 | Intelligent process grouping | §15 | ProcessMonitoring, Core | `ProcessGrouping` + `ProcessGroupingTable.Default`; optional `process-groups.json` override | `ProcessGroupingTests` (browser renderer/GPU children → one key; path beats name; unknown → own name) | ✅ |
| R-052 | Never claim exact per-process energy | §15, §55 | ProcessMonitoring, Data | Permanent `DataQuality.Estimated` + `MeasurementSource.Model` on every row | `ProcessSampleWriteQueueTests` (grade + source), `AppEnergyEstimatorTests` | ✅ |
| R-053 | Documented, versioned, testable model | §55 | Core.Processes | `AppEnergyEstimator` = `AppEnergyV1`, `Version` const, `EstimatorVersion` on `ProcessSample`/`ApplicationUsage` | `AppEnergyEstimatorTests` (shares sum to 100 %; top-N "Other" keeps share; no double count) | ✅ |
| R-054 | Separate system from attributed power | §55 | Core.Processes | `BaselineEstimator` separates `E_baseline` first; baseline is a distinct `AppUsageEntry` | `BaselineEstimatorTests`, `AppEnergyEstimatorTests.OnBattery_BaselineIsSeparatedFirst_…` | ✅ |
| R-055 | Methodology visible to user | §55 | App | About → "Estimation Methodology" card (three steps, claims/non-claims, on-AC caveat); App Usage "How this is estimated" link | Live on reference machine | ✅ |

## Data

| ID | Requirement | Spec | Module | Implementation | Test | Status |
|---|---|---|---|---|---|---|
| R-060 | SQLite, local only | §27, §33 | Data, Core | `battery.db` under `AppPaths.DataDirectory`, which resolves from `%LOCALAPPDATA%` (Phase 15 — never redirected, so identical packaged/unpackaged) plus the optional `Data.DatabaseDirectory` override | `AppPathsTests` (under the user profile, stable, env-var-independent fallback); live: `-wal`/`-shm` siblings on the reference machine | ✅ |
| R-061 | Full entity set | §27, §28 | Data | 18 tables, `V001__InitialSchema.sql` | `MigrationTests.MigrateAsync_OnFreshDatabase_CreatesEveryTable` | ✅ |
| R-062 | Indexed time / battery / session | §32 | Data | Indexes per `database.md` §4 in V001; **V002** adds `IX_SampleMinute_Time` / `IX_SampleHour_Time` (the aggregate-tier reads filter on the time column alone, which the `WITHOUT ROWID` primary key cannot serve) | `QueryPlanTests` — `EXPLAIN QUERY PLAN` on the raw / minute / hour / session hot-path SELECTs asserts an index is used and no full table scan | ✅ |
| R-063 | Batched, async writes | §32, §66 | Data | `BatterySampleWriteQueue`: 200-row / 30 s / explicit-flush triggers, one prepared statement reused per batch | `BatterySampleWriteQueueTests` (4 tests) | ✅ |
| R-064 | WAL mode | §32 | Data | Pragmas applied on every connection (`SqliteConnectionFactory`) | `MigrateAsync_AppliesTheDocumentedPragmas`; live: `-wal`/`-shm` files present | ✅ |
| R-065 | UI never blocks on DB | §32 | Data, App | `BatteryPersistenceBridge.Enqueue` returns immediately; DB access confined to the write queue's own timer/task | Live: Battery page remained responsive with pending writes queued | ✅ |
| R-066 | Versioned migrations, no history loss | §64 | Data | `DatabaseMigrator`: `PRAGMA quick_check` first (a corrupt file aborts the run untouched), transactional per-migration apply, backup from the second migration onward, rollback on failure | `MigrationTests` — idempotency, version recording, V002 indexes present, and `LaterMigration_BacksUpFirst_AndLosesNoRows` (V001→V002 with seeded rows: `battery.db.bak-v002` written, zero row loss) | ✅ |
| R-067 | Tiered aggregation | §31 | Data | `DatabaseMaintenanceService`: minute → hour → daily rollup, each idempotent and gated on the tier below being settled; `DailyStatistics` from closed sessions. Minute/hour/daily retention now enforced | `RunRollupAsync_*` (minute), `RunRollupAsync_RollsSettledHours_IntoSampleHour_*`, `RunRollupAsync_RollsFullyElapsedDays_IntoDailyStatistics_*`, `…DoesNotRollToday_*` | ✅ |
| R-068 | Retention, no active-session deletion | §29 | Data | Deletes only rolled-up + past-window rows; open-session guard | `RunRetentionAsync_NeverDeletesARowInAnOpenSession`, `RunRetentionAsync_DeletesOldSamples_OnlyAfterTheyAreRolledUp` | ✅ |
| R-069 | Delete all history with confirmation | §29 | App, Data | `IHistoryMaintenance` / `HistoryMaintenance` — `DELETE FROM` per telemetry table in a transaction, then `VACUUM`; Settings "DATA" section, `ContentDialog` requiring the word `DELETE` | `HistoryMaintenanceTests` (tables emptied, device + schema survive) | ✅ |
| R-070 | Reject impossible measurements | §63 | Core | `BatterySentinels` (raw sentinel values), `BatterySampleValidation` (plausibility ranges), `PercentageJumpDetector` (awake jump check, Phase 4 — applied in both `BatteryMonitoringService` for sample grading and `SessionStateMachine` for session semantics), and `SessionStateMachine`'s backwards-clock rejection (Phase 4, the monotonic-timestamp guard) | `BatterySentinelsTests`, `BatterySampleValidationTests`, `PercentageJumpDetectorTests`, `SessionStateMachineTests.ClockStepsBackwards_Rejected_SessionPreserved` | ✅ (battery domain) |
| R-071 | Settings treated as untrusted input | §46 | Core, Data | `AppSettings.Validate()` clamping; corrupt file preserved, defaults used | `SettingsValidationTests`, `JsonSettingsServiceTests` | ✅ |

## Analytics, alerts, UX

| ID | Requirement | Spec | Module | Implementation | Test | Status |
|---|---|---|---|---|---|---|
| R-080 | Rolling runtime estimate + confidence | §52 | Core.Analytics + Analytics | `RuntimeEstimator` (`remaining ÷ rate` per screen state, four §3 tiers, `Calculating` below the floor) driven by `RuntimeEstimationService` over a screen-tagged `RollingRate` | `RuntimeEstimatorTests` (tiers; screen-off without history ⇒ Unavailable; below floor ⇒ Calculating), `RuntimeEstimationServiceTests` | ✅ |
| R-081 | Charging quality score | §10, §54 | Core.Analytics | `ChargingQualityScorer` = `ChargingQualityV1`; personal 30-day baseline; thermal component dropped + renormalised with no sensor; Unavailable below 5 prior sessions | `ChargingQualityScorerTests` | ✅ (scorer + tests; dedicated UI card is later polish) |
| R-082 | Statistics: lifetime/today/7d/30d/custom | §16 | Core.Analytics | `StatisticsEngine` — timezone-aware window resolution, midnight-straddling session proration; computes from `BatterySession` rows | `StatisticsEngineTests` (local-midnight start, straddle proration, empty window, DST day) | ✅ |
| R-083 | Confidence-gated insights | §18 | Core.Analytics | `RuleBasedInsightProvider` — every rule behind 4 gates incl. effect > the metric's own variance | `RuleBasedInsightProviderTests` (within-noise ⇒ zero; genuine effect ⇒ one; confidence below threshold ⇒ suppressed), `AnalyticsServiceTests` | ✅ |
| R-084 | `IInsightProvider` abstraction | §34 | Core | `IInsightProvider` in Core; `RuleBasedInsightProvider` the default impl; AI provider explicitly out of scope for v1 | `RuleBasedInsightProviderTests` | ✅ |
| R-085 | Insights never unsafe | §77 | Core.Analytics | Fixed, curated `Title`/`Explanation` strings — only figures substituted; conservative rule corpus (`InsightRulesV1`) | Review of the 7-rule corpus; `RuleBasedInsightProviderTests` | ✅ |
| R-086 | Configurable alerts + cooldown | §20 | Core.Alerts + Notifications | `AlertRuleEngine` — per-type hysteresis (fire on the entering edge, re-arm past a margin) + cooldown; one rule per `AlertSettings` toggle; `AlertMonitoringService` drives it from the battery/analytics/process monitors | `AlertRuleEngineTests` (fires once, never re-fires while below, cooldown blocks a repeat, disabled never fires, critical wins), `AlertMonitoringServiceTests` (oscillation → one alert) | ✅ |
| R-087 | Windows notifications + in-app centre | §21 | App (`WindowsToastPresenter`) + Notifications | `AppNotificationManager` for the unpackaged app, `Register()`/`Show()` fully guarded; the in-app centre (`IAlertMonitoringService` → bell flyout + Alerts page) is the guaranteed floor | `AlertMonitoringServiceTests.ANotificationFailure_DegradesToInApp_WithoutError`; live: reference machine | ✅ |
| R-088 | Tray, background monitoring | §22 | Windows, App | `H.NotifyIcon` tray icon, close-to-tray (`MainWindow.OnAppWindowClosing` → `AppWindow.Hide()` when the setting is on), left-click restore, Exit menu item | `qa-checklist.md` §B1–B2 (clean-VM manual) | 🔨 (implemented; hardware/OS manual pass pending) |
| R-089 | Single instance | §59 | App | `AppInstance.FindOrRegisterForKey` + redirection | Verified: second launch redirected | ✅ |
| R-090 | Start with Windows, configurable | §23 | App | `App.Services.StartupService` — a per-user `HKCU\…\Run` entry (no admin); the Settings "Start when I sign in" toggle. Packaged builds additionally get the `windows.startupTask` extension in `Package.appxmanifest` | `qa-checklist.md` §B4 (clean-VM reboot); the Run-key write path is a per-user registry op | 🔨 (implemented; reboot test pending) |
| R-091 | Responsive 1280×720 → 4K, no clipping | §41 | App | `Controls/ColumnGrid` masonry panel, 1/2/3/4 columns via `Core.Layout.ResponsiveColumns.ForWidth`; cards reflow, text never shrinks; min window 960×640 (scroll below) | `ResponsiveColumnsTests` (breakpoint table); live: 3 columns at ~1280, reflows 2 / 1 / 4 on resize, vertical scroll only | ✅ |
| R-092 | Accessibility | §42 | App | Every interactive control has an automation name (page-header buttons already; Phase 14 added it to every `Segmented`, and `ToggleRow` names its switch from the header); `Sparkline` marked decorative; charts emit an automation summary | Automatable parts done; keyboard-only + Narrator + high-contrast + scaling pass in `qa-checklist.md` §C/§D | 🔨 (code done; manual pass pending) |
| R-093 | Empty states explain why | §43 | App | Per-page states with reasons via `EmptyStateView` | Phase 14 audit: all 14 `EmptyStateView` uses carry a `Description` explaining why; selection logic is trivial boolean composition (live regression sweep) | ✅ |
| R-094 | Not colour alone | §39 | App | Severity / health / budget rendered as colour **+** icon **+** text everywhere (alerts, Monitoring, footprint, log levels) | Phase 14 audit; high-contrast + colour-blind confirm in `qa-checklist.md` §C6 | ✅ |
| R-095 | Estimation transparency on hover | §76 | App | `Controls/InfoDot` (ⓘ glyph — tooltip **and** accessible name); `MetricStat.InfoText`; Battery (Current, runtime) + Power (Current) carry it; dashboard `CardHeader.Info` + Battery expander + App Usage "How this is estimated" + About methodology already present | Live: hovers render; Narrator reads the same sentence | ✅ |
| R-096 | CSV + JSON export | §36 | Reporting, Data, App | `CsvExporter` (RFC 4180, BOM, `\r\n`) + `JsonExporter` (`Utf8JsonWriter`) behind `IReportExporter`, driven by `IExportDataSource` (per-scope range-filtered `SELECT`s, enums as names); `ExportService` = OS `FileSavePicker` → `FileStream`; scope checklist on the History page | `CsvExporterTests`, `JsonExporterTests`, `ExportDataSourceTests`; live export via picker | ✅ |
| R-097 | Structured logging, rotated | §48 | all | Serilog, 8 MB cap, 14-file retention | Log file written and rotating | ✅ |
| R-098 | Graceful subsystem failure | §44 | Core.Diagnostics + Core.Monitoring + all orchestrators | `MonitoringStatusRegistry` — `Healthy → Retrying` (1–2 failures) `→ Degraded` (≥3) `→ Healthy`, in the Diagnostics "Monitoring" section with the last error. `MonitoringBackoff` (base × 2ⁿ, cap 5 min) applied by the battery/process samplers: a failed tick widens the timer, a success restores the adaptive rate | `MonitoringStatusRegistryTests` (Retrying/Degraded transitions), `MonitoringBackoffTests`, `ProcessMonitoringServiceTests` (Degraded + isolation; interval widens/restores) | ✅ |
| R-099 | Performance budgets | §45 | all | Batched writes; 1 Hz UI coalescing; adaptive sampling; `ISelfMetrics` + Diagnostics footprint. Cold start **~1.0–1.4 s** (< 2 s), CPU negligible, disk ~0.4 MB/min. **Working set ~230–270 MB vs a 150 MB target** — the WinUI 3 / SkiaSharp / WinAppSDK runtime floor; an **accepted deviation for 1.0** (`docs/release.md` §8) | `AdaptiveSamplingPolicyTests`; in-session soak; `qa-checklist.md` §F (24 h soak by the release engineer) | 🔨 (all measures pass except working set — accepted; 24 h soak is a checklist item) |
| R-100 | Security: no admin, parameterised SQL, safe paths | §46 | all | `asInvoker`, no elevation; every SQL value bound as a `$`-parameter, the three identifier-interpolating readers use closed hard-coded sets; export writes only to an OS-picker path; log viewer reads only `AppPaths.LogsDirectory`; no network anywhere; copied report redacts `%USERPROFILE%` | `docs/security-review.md` (full audit — clean); `QueryPlanTests` run the exact hot-path SQL | ✅ |

---

## Coverage

All 82 specification sections map to at least one requirement above. Sections
without a distinct row are covered structurally:

| Spec | Covered by |
|---|---|
| §1, §2, §82 | `prd.md` §1–§3, §6 |
| §6, §7, §38, §40 | R-091–R-095 + `ui-navigation.md` |
| §17 | R-062, R-067 (History reads the aggregate tiers) |
| §30 | R-035, R-063, R-099 |
| §33 | R-060 + N3 |
| §35 | R-035, R-037, R-086, R-090 |
| §37, §78 | `architecture.md` §9 extension points |
| §56 | R-091 + `architecture.md` §7 |
| §57 | R-055, R-005 |
| §58 | R-060, R-066 + `docs/release.md` (data survives packaged↔unpackaged, upgrade, uninstall) |
| §60–§62 | `testing.md` in full |
| §65 | R-015 |
| §67, §80, §81 | `roadmap.md` |
| §68 | `roadmap.md` Phase 14 exit |
| §69 | This `/docs` set — 16 documents, `README.md` links them; headers current at 1.0.0 |
| §71, §72 | `api-strategy.md` |
| §79 | `roadmap.md` Phase 14 |

This table is updated at each phase boundary: statuses advance `📋 → 🔨 → ✅`, and
the Implementation and Test columns are replaced with concrete file and test names
as they come into existence.
