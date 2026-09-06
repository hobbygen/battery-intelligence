# Release QA Checklist

Status: Phase 14. Version 1.0.0. Covers `testing.md` §7–§8, spec §62/§68 and
`prd.md` §6.

Everything a test can verify is in the automated suites (374 tests as of Phase
14: 270 unit, 37 simulation, 67 integration). This checklist is the rest — the
things that need a human, a second machine, real hardware transitions, or wall
time. Run it once per release candidate on the reference machine and on a
Windows 10 machine (or VM), and record pass/fail + notes.

Legend for the "Covers" column: `Nx` = the invariant in `prd.md` §5;
`§62` = a failure-matrix row; `R-0xx` = a traceability requirement.

## A. Hardware & power transitions — reference machine

| # | Check | Covers | Result |
|---|---|---|---|
| A1 | Sleep → wake: `SleepSeconds` on the spanning session is correct; no interpolated samples across the gap; session not fragmented | N6, §62, R-047 | ☐ |
| A2 | Lid close → open (same as A1, via lid) | N6, §62 | ☐ |
| A3 | Hibernate → resume: session preserved, `InferredSleepGap` written if no suspend notification arrived | §62 | ☐ |
| A4 | Charger connect while discharging: a discharge session closes `ChargerConnected`, a charge session opens immediately (no debounce) | R-042, §62 | ☐ |
| A5 | Charger disconnect while charging: symmetric to A4 | R-042 | ☐ |
| A6 | Charger bounce (connect/disconnect 3× in 10 s): **one** session pair, no churn | §62 | ☐ |
| A7 | Run to < 20 %: sampling visibly gets finer (Diagnostics footprint / logs), low-battery alert fires once | R-035, R-086 | ☐ |
| A8 | Charge to 100 %: charge session closes `ReachedFull`, "fully charged" alert fires once | R-086 | ☐ |
| A9 | Leave on AC at 100 %, screen off, 10 min: battery interval widens to ×6, process sampler pauses (logs) | R-035 | ☐ |

## B. Windows integration — both OS versions

| # | Check | Covers | Result |
|---|---|---|---|
| B1 | Close button with "keep running in tray" on → window hides, monitoring continues (DB keeps growing), tray icon present | R-088 | ☐ |
| B2 | Tray icon left-click restores the window; Exit from the tray menu actually exits | R-088 | ☐ |
| B3 | Second launch of the exe redirects to the running instance (no 2nd window) | R-089 | ☐ |
| B4 | "Start with Windows" on → reboot → app starts (minimized if also set), no admin prompt | R-090, N2 | ☐ |
| B5 | A real alert produces a Windows toast (not just the in-app centre) | R-087 | ☐ |
| B6 | Toast disabled in Settings → only the in-app centre updates | R-087 | ☐ |
| B7 | **Windows 10:** window renders correctly with no Mica (solid backdrop), all pages legible, no clipped chrome | R-091 | ☐ |
| B8 | Windows 10: capability matrix reflects Win10 API availability honestly (e.g. any Win11-only row shown Unavailable with its reason) | R-005, N8 | ☐ |

## C. Accessibility — reference machine

| # | Check | Covers | Result |
|---|---|---|---|
| C1 | Keyboard only: every page reachable via the nav pane; every interactive control focusable in a sensible order; no keyboard trap | R-092 | ☐ |
| C2 | Narrator: each control announces a meaningful name (the Phase 14 audit added names to every `Segmented`, the `ToggleRow` switches, and icon buttons) | R-092 | ☐ |
| C3 | Narrator: the Power / Temperature / History charts read their automation summary ("Power chart in mW, N points from … to …, range … to …") | R-092 | ☐ |
| C4 | Narrator: the `Sparkline` mini-trends are skipped (marked decorative); the figure they illustrate is read as text | R-092 | ☐ |
| C5 | High-contrast themes (all 4): text remains readable, focus rectangles visible, no invisible-on-invisible | R-092, R-094 | ☐ |
| C6 | Colour-blind check: alert severity, monitoring health and the footprint budgets are all distinguishable without colour (icon + text present) | R-094 | ☐ |
| C7 | The `ⓘ` on Battery (Current, runtime), Power (Current) shows the provenance sentence on hover and reads it to Narrator | R-095 | ☐ |
| C8 | 125 % / 150 % / 200 % display scaling: layouts reflow, nothing clipped | R-091 | ☐ |

## D. Layout & rendering

| # | Check | Covers | Result |
|---|---|---|---|
| D1 | 1366×768: dashboard shows 2–3 columns, no horizontal scroll, no text shrink | R-091 | ☐ |
| D2 | 4K / 3840×2160: dashboard shows 4 columns at a bounded card width, no giant whitespace | R-091 | ☐ |
| D3 | Resize slowly from min (960×640) to maximised: columns reflow smoothly, no `LayoutCycleException` in the log | R-091 | ☐ |
| D4 | Light / Dark / System theme switch is instant and complete (no half-themed card) | R-091 | ☐ |

## E. Failure matrix (spec §62) — the automated column is done; these are the manual confirmations

The passing bar for every row: no crash, no fabricated data, honest surfacing on
Diagnostics, unaffected subsystems keep working, recovery without a restart.

| # | Check | Automated by | Manual confirm | Result |
|---|---|---|---|---|
| E1 | Database file locked by another process (e.g. a backup tool) for 60 s | `DatabaseFailureTests.LockedDatabase…` | Diagnostics → Monitoring → **Database: Degraded**; recovers to Healthy after; no data lost | ☐ |
| E2 | Disk full during a flush | `DatabaseFailureTests.UnreachableDatabase…` | App stays responsive, no crash | ☐ |
| E3 | Corrupt database on launch (truncate `battery.db`) | `DatabaseFailureTests.CorruptDatabase…` | App starts, Diagnostics says history recording is paused, **the file is not deleted** | ☐ |
| E4 | Temperature sensor absent (reference machine's real state) | `SimulatedBatteryProviderTests` (NoTemperatureSensor) | Temperature page shows the unavailable state with the §14 wording; nothing persisted | ☐ |
| E5 | Every battery provider throws | `SimulatedBatteryProviderTests.ApiFailure` | All pages show Unavailable; app responsive; Diagnostics Monitoring shows Battery Degraded | ☐ |
| E6 | Provider recovers after a dropout mid-session | `SimulatedBatteryProviderTests.SensorDropoutMidSession` + `ProcessMonitoringServiceTests` | Subsystem returns to Healthy on its own | ☐ |

## F. Performance — reference machine, on battery

| # | Check | Target (`prd.md` §6) | Measured | Result |
|---|---|---|---|---|
| F1 | Cold start to interactive (log line `Application ready in … ms`) | < 2 s | Phase 14 in-session: **~1.0–1.4 s** | ☐ |
| F2 | Idle CPU over 10 min (Diagnostics footprint, or Task Manager) | < 0.5 % avg | Phase 14 in-session: negligible | ☐ |
| F3 | Working set with the window open | < 150 MB | Phase 14 in-session: **~230–270 MB — over budget** (WinUI 3 runtime baseline; see roadmap Phase 13/14 deviations) | ☐ |
| F4 | Working set tray-only (window hidden 10 min) | < 80 MB | not yet measured | ☐ |
| F5 | Disk writes over 1 h | < 1 MB/min sustained | Phase 14 in-session: WAL grew ~0.4 MB/min | ☐ |
| F6 | **24-hour soak on battery + AC:** working set flat (no monotonic climb), log dir bounded (rotation working), DB growth tracks the size estimate, no `LayoutCycleException`, no subsystem stuck Degraded | N7, R-099 | not run | ☐ |
| F7 | Database size after a representative week | on track for < 100 MB/year | ☐ |

## G. Data integrity

| # | Check | Covers | Result |
|---|---|---|---|
| G1 | Export CSV of a week of data → opens in Excel, quoted fields intact, one section per table | R-096 | ☐ |
| G2 | Export JSON → parses, `range`/`tables` shape, values readable | R-096 | ☐ |
| G3 | "Delete all battery history" (type `DELETE`) → all telemetry gone, device + settings kept, app keeps running and starts recording again | R-069 | ☐ |
| G4 | Upgrade from a build with a V001 database → V002 applied, `battery.db.bak-v002` written, **every row still present**, history charts unchanged | N6, R-066 | ☐ (automated: `MigrationTests.LaterMigration…`) |
| G5 | Kill the process (Task Manager) mid-session → relaunch → the open session is adopted within the grace window, or closed `Interrupted` beyond it | N6, §62 | ☐ |

## H. Definition of done (spec §68 / testing.md §8) — spot check

For each shipped feature: implemented ∧ integrated ∧ tested ∧ handles failure ∧
has empty/unavailable states ∧ logs ∧ documented ∧ does not block the UI ∧ no
new warnings ∧ works against mock **and** real hardware.

| # | Check | Result |
|---|---|---|
| H1 | `dotnet build -c Release` — 0 warnings, 0 errors | ☐ |
| H2 | `dotnet test` — all green (run twice; the integration suite has SQLite-file-lock flakiness) | ☐ |
| H3 | Every capability-matrix row has an unavailable-path test (N-requirement) | ☐ |
| H4 | Every Estimated value has a grade-propagation test | ☐ |
| H5 | `Release` binary contains no simulation types (N9 — verify with a decompiler or a reflection check) | ☐ |
| H6 | Every page has a documented empty / loading / unavailable state (audited Phase 14 — all 14 `EmptyStateView` uses carry a "why") | ☐ |

---

**Sign-off:** _______________  Date: _______  Build: _______
