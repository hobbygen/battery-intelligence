# Architecture

Status: Phase 0. Version 1.0.0.

---

## 1. Architectural drivers

Ranked. Where two conflict, the higher one wins.

1. **Truthfulness** — a displayed number is either measured, calculated from
   measurements, or explicitly labelled as an estimate. Never fabricated. (spec §3, §66)
2. **Resilience** — one failing subsystem must not take down the others, nor the app. (spec §44)
3. **Lightness** — this monitors a battery; it must not meaningfully drain one. (spec §45)
4. **Locality** — no cloud, no account, no telemetry. (spec §33)
5. **Extensibility** — future modules slot in without redesign. (spec §78)

Driver 3 deserves emphasis: a battery monitor whose own CPU and disk activity
distorts the thing it measures is self-defeating. This constrains sampling rates,
database write batching and chart redraw policy throughout.

---

## 2. Layering

Strict downward dependency. A layer may reference only layers beneath it.

```
┌──────────────────────────────────────────────────────────────┐
│  Presentation      App (WinUI 3, XAML, Views, ViewModels)    │
├──────────────────────────────────────────────────────────────┤
│  Application       Analytics · Sessions · Notifications      │
│                    Reporting · monitoring orchestration      │
├──────────────────────────────────────────────────────────────┤
│  Domain            Core — models, interfaces, enums,         │
│                    value objects, constants. No dependencies │
├──────────────────────────────────────────────────────────────┤
│  Infrastructure    Battery · Power · Thermal · Process       │
│                    Data (SQLite) · Windows (Win32/WinRT)     │
└──────────────────────────────────────────────────────────────┘
```

**Core depends on nothing.** Not on WinUI, not on Win32, not on SQLite. Every
other project references Core; Core references only the BCL. This is what makes
the analytics engine unit-testable without a battery present, and it is the single
most important structural rule in the codebase.

The inversion that makes this work: infrastructure projects *implement* interfaces
that Core *declares*. `Core` owns `IBatteryProvider`; `Battery` implements
`WindowsBatteryProvider`. Composition happens once, at the top, in `App`.

### The rule that spec §73 demands

```
BatteryViewModel → IBatteryMonitoringService → IBatteryProvider → WindowsBatteryProvider
```

A ViewModel never touches a Win32 call, a WMI query or a SQL statement. If a
ViewModel needs `using System.Management`, the design has gone wrong.

---

## 3. Project structure

```
BatteryIntelligence.sln
│
├── src/
│   ├── BatteryIntelligence.Core             — domain, zero dependencies
│   ├── BatteryIntelligence.Data             — SQLite, repositories, migrations
│   ├── BatteryIntelligence.Windows          — Win32/WinRT interop, events, tray, startup
│   ├── BatteryIntelligence.Battery          — battery providers, health, capability detection
│   ├── BatteryIntelligence.Power            — voltage/current/power providers, estimator
│   ├── BatteryIntelligence.Thermal          — temperature providers
│   ├── BatteryIntelligence.ProcessMonitoring— process sampling, grouping, attribution
│   ├── BatteryIntelligence.Sessions         — session state machine, timeline
│   ├── BatteryIntelligence.Analytics        — statistics, trends, health score, insights
│   ├── BatteryIntelligence.Notifications    — alert engine, Windows notifications
│   ├── BatteryIntelligence.Reporting        — CSV/JSON export
│   └── BatteryIntelligence.App              — WinUI 3 shell, views, viewmodels
│
└── tests/
    ├── BatteryIntelligence.Tests.Unit
    ├── BatteryIntelligence.Tests.Integration
    └── BatteryIntelligence.Tests.Simulation
```

This follows the spec's §4 module map, with two deliberate adjustments:

- **`src/` and `tests/` roots.** The spec listed a flat layout. Splitting them keeps
  the solution root readable and lets test projects share config without leaking
  test-only packages into shipping projects.
- **Tests split by kind, not by subject.** The spec listed `UnitTests`,
  `IntegrationTests`, `AnalyticsTests`, `HardwareSimulationTests` as folders inside
  one project. Unit and integration tests have genuinely different runtime
  characteristics — integration tests touch a real SQLite file and must not run in
  parallel with each other — so they are separate assemblies. Analytics tests live
  inside the unit project, since that is what they are.

### Dependency graph

```
                          ┌──────┐
                          │ Core │   (no dependencies)
                          └───┬──┘
        ┌────────┬────────┬───┴────┬─────────┬──────────┐
        │        │        │        │         │          │
     Windows   Data   Battery    Power    Thermal   ProcessMonitoring
        │        │        │        │         │          │
        └────────┴────┬───┴────────┴─────────┴──────────┘
                      │
              ┌───────┴────────┬──────────────┐
           Sessions        Analytics      Notifications
                      │        │               │
                      └────────┴───────┬───────┘
                                   Reporting
                                       │
                                     App
```

Acyclic by construction. `Battery`, `Power`, `Thermal` and `ProcessMonitoring` do
not reference each other — they are siblings that share only Core. Correlating
their outputs is the job of the layer above (`Sessions`, `Analytics`), which keeps
each provider independently testable and independently failable.

---

## 4. Composition and lifetime

`Microsoft.Extensions.Hosting` builds the container at startup. Lifetimes:

| Kind | Lifetime | Rationale |
|---|---|---|
| Providers (`IBatteryProvider`, …) | Singleton | Hold native handles and device queries; creating them per-use is wasteful |
| Monitors / samplers | Singleton `IHostedService` | Long-running background loops |
| Repositories | Singleton | Wrap a pooled SQLite connection |
| Analytics engines | Singleton | Stateless, hold cached aggregates |
| ViewModels | Transient | One per page navigation |
| `MainWindow` | Singleton | Single-instance app |

Monitors run as hosted services so start/stop is uniform, cancellation is
uniform, and "pause monitoring" (spec §22, §35) is a single coordinated operation
rather than eleven ad-hoc flags.

---

## 5. The monitoring pipeline

Every telemetry kind follows the same five stages. Uniformity here is what makes
the "one subsystem fails, others continue" requirement achievable rather than
aspirational.

```
  Provider          Sampler           Validator         Aggregator        Sink
 ┌────────┐       ┌─────────┐       ┌──────────┐      ┌──────────┐    ┌────────┐
 │ reads  │──────▶│ timer / │──────▶│ range +  │─────▶│ ring     │───▶│ batch  │
 │ HW/API │       │ event   │       │ sentinel │      │ buffer + │    │ writer │
 └────────┘       │ driven  │       │ checks   │      │ minute   │    │ SQLite │
                  └─────────┘       └──────────┘      │ rollup   │    └────────┘
                       │                  │           └────┬─────┘
                       │                  │                │
                       ▼                  ▼                ▼
                  fault isolation    reject/flag      live UI feed
                  per subsystem      suspicious       (throttled)
```

**Fault isolation.** Each sampler wraps its provider call in a boundary that
catches, logs, increments a consecutive-failure counter, and applies exponential
backoff. After a threshold the subsystem is marked *degraded* and surfaces on the
Diagnostics page — while every other sampler keeps running. A dead temperature
sensor costs the temperature page, nothing else.

**Two consumers, different rates.** The live UI reads from the in-memory ring
buffer (throttled, bounded, never blocking). The database receives batched writes
on a timer. The UI never waits on a disk write; the disk never sees a write per
sample. This is the concrete answer to spec §32 and §75.

---

## 6. Threading model

- Monitoring runs entirely on background threads. No sampler ever touches the
  dispatcher directly.
- Providers expose `async` APIs taking `CancellationToken`, honoured throughout.
- Results reach the UI through a throttled aggregator that marshals to the
  dispatcher at a bounded rate (default 1 Hz for numeric readouts) regardless of
  the underlying sampling rate.
- SQLite access is serialised through a single writer to avoid lock contention,
  with WAL mode enabling concurrent readers.

The UI thread does no I/O, no interop and no SQL. Ever.

---

## 7. Key technology decisions

| Concern | Choice | Why this, and not the alternative |
|---|---|---|
| UI | WinUI 3 / Windows App SDK 1.8 | Mandated by spec §2. Verified building and launching on this machine. |
| Target framework | `net10.0-windows10.0.19041.0`, min `10.0.17763.0` | .NET 10 SDK installed. TPV 19041 unlocks the modern WinRT surface; min 17763 preserves the Windows 10 support spec §2 requires. |
| Persistence | `Microsoft.Data.Sqlite` + hand-rolled migrations | **Not EF Core.** EF adds startup cost, memory and query-translation overhead against driver 3. The access pattern is narrow — batch inserts and time-range aggregate reads — which is exactly where raw SQL with prepared statements wins. Migrations are simple enough to own. |
| MVVM | `CommunityToolkit.Mvvm` | Source-generated observables; no runtime reflection cost. MIT. |
| DI / hosting | `Microsoft.Extensions.*` | First-party, matches the hosted-service model above. |
| Logging | Serilog + rolling file sink | Structured (spec §48), with size and retention caps so logs cannot grow without bound. |
| Charting | `LiveChartsCore.SkiaSharpView.WinUI` | MIT, actively maintained, GPU-accelerated via Skia, supports the bounded/streaming datasets driver 3 demands. Held behind `IChartSeriesSource` so it can be replaced (spec §56). |
| Tray | `H.NotifyIcon.WinUI` | MIT. WinUI 3 has no built-in tray support; this is the maintained option. |
| Single instance | `AppInstance.FindOrRegisterForKey` + `Redirected` | First-party Windows App SDK mechanism (spec §59). |
| Packaging | Unpackaged for dev, MSIX for release | Unpackaged keeps the inner loop fast; MSIX for distribution (spec §58). |

### On charting behind an abstraction

Spec §56 says "keep charting behind an abstraction *where practical*". Full
abstraction of a charting library is usually a mistake — it produces a
lowest-common-denominator wrapper that costs more than it saves. The middle course
adopted here: ViewModels expose **plain domain series** (`IReadOnlyList<TimePoint>`
plus axis metadata), and only the XAML control binding is library-specific.
Swapping libraries means rewriting chart controls, not the analytics layer.

**Phase 5 confirmation (2026-09-05):** the seam is `Core.Models.ChartSeries` /
`PowerSeriesSet` (a `TimePoint` list plus label and unit), produced by
`IPowerMonitoringService` and consumed by `App/Controls/PowerChart` — the only
type in the solution that references `LiveChartsCore.SkiaSharpView.WinUI`. The
`libSkiaSharp.dll` native asset deploys correctly for the unpackaged app.

---

## 8. Data location

Per the decision recorded for this project, runtime data lives at:

```
%LocalAppData%\BatteryIntelligence\
├── battery.db          SQLite database (WAL: -wal, -shm siblings)
├── settings.json       user configuration
└── logs\
    └── app-YYYYMMDD.log   rolling, size-capped, retention-capped
```

This satisfies spec §58 — user data outside the install directory, surviving
upgrade and uninstall. The path is configurable (spec §35 "Database location"),
with the configured value validated at startup and falling back to the default if
unwritable.

---

## 9. Extension points

Interfaces designed now so spec §78's future modules need no redesign:

| Interface | Present implementation | Future |
|---|---|---|
| `IInsightProvider` | `RuleBasedInsightProvider` | `LocalAIInsightProvider`, `ExternalAIInsightProvider` (spec §34) |
| `IReportExporter` | `CsvExporter`, `JsonExporter` | `PdfReportExporter` (spec §37) |
| `IBatteryProvider` | `WindowsBatteryProvider`, `SimulatedBatteryProvider` | UPS, external battery (spec §78) |
| `IHealthScoreAlgorithm` | `HealthScoreV1` | versioned successors (spec §19) |
| `IProcessEnergyEstimator` | `HeuristicEstimatorV1` | measured-energy implementation if a non-admin API appears |

Versioned algorithms are stored **by version tag** alongside their outputs, so a
health score computed under `v1` remains interpretable after `v2` ships. Without
this, a scoring change would silently rewrite history — exactly the kind of quiet
data corruption spec §64 forbids.

---

## 10. Simulation

`SimulatedBatteryProvider` and siblings implement the same interfaces as the real
providers, driven by scripted scenarios (spec §61): discharge curves, charge
curves, thermal ramps, multi-battery, sensor dropout, API failure, sleep/resume.

Registration is gated on a **compile-time symbol** (`SIMULATION`) present only in
Debug, not merely a runtime flag. A runtime-only guard could be flipped by a
corrupted settings file; the spec's "never expose simulation mode accidentally in
production" is better served by the code not existing in the Release binary.

---

## 11. Risks

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| R1 | Per-process energy is an estimate that users may read as fact | Undermines driver 1 | Permanent "Estimated" badge, published methodology, confidence values, Diagnostics disclosure |
| R2 | Rate reported in mA not mW (quirk Q2) | Power figures off by ~10× | Read ACPI capability bit; normalise via voltage; mark U if undeterminable |
| R3 | Sentinel values stored as real numbers (quirk Q3) | Absurd UI values, poisoned aggregates | Central sentinel filter in the validator, unit-tested against known sentinels |
| R4 | Missed suspend notification corrupts sessions | Wrong history, wrong statistics | Wall-clock vs monotonic gap detection, inferred events |
| R5 | Chart/DB growth degrades performance over months | Violates driver 3 | Tiered aggregation, bounded chart buffers, indexed time ranges, background retention |
| R6 | Health score treated as authoritative | Misleads on a replacement decision | Labelled "Battery Health Score", "How this is calculated" always reachable, versioned |
| R7 | Cycle count and temperature absent on the dev machine | Those paths ship untested | Simulation providers must cover present-and-absent for both; unavailable states are first-class test cases |
| R8 | WinUI 3 unpackaged tray/notification quirks | Tray or toast may misbehave | Validate early in Phase 1, before dependent features are built |

R7 is worth calling out: because this development machine reports neither cycle
count nor battery temperature, the code paths that *consume* those values would
otherwise never execute during development. Simulation coverage for them is not
optional polish — it is the only way they get exercised at all before release.
