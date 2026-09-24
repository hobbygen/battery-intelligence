# Changelog

All notable changes to Battery Intelligence. This project follows
[Semantic Versioning](https://semver.org/).

## 1.1.0 — 2026-09-25

A correctness release for the Battery Health Score. If you run 1.0.0, the score
it showed you may have been wrong in either direction; this release fixes the
causes and discards the stored snapshots that cannot be trusted.

### Fixed

- **Battery Health Score could state the opposite of the truth** — a nearly new
  pack rated Poor, a nearly dead one rated Excellent. Three causes, all fixed:
  - The milliamp/milliwatt-hour unit flag is stated per source, but the composite
    provider applied a single borrowed flag to values selected from other sources.
    Capacity retention divides full-charge capacity by design capacity, so when the
    two halves came from sources with different unit conventions the result was out
    by roughly the pack voltage — a healthy pack read in the single digits, a worn
    one read well over 100%. Each source's values are now converted with that
    source's own flag, and both halves of retention are taken from one source
    wherever one reports both. Retention pieced together across sources is graded
    `Estimated` rather than `Calculated`.
  - Retention outside 15–125% is now graded `Suspect` and excluded from the score
    and the degradation trend, so the score reads Unavailable instead of clamping an
    impossible ratio into a confident verdict.
  - `HealthScoreV2`: charge behaviour and charge-rate stability describe how the
    machine is used, not the state of the cell, so they can now only deduct (capped
    at 5 points) rather than average in — a pack kept permanently on AC no longer
    scores Excellent on habits alone. The score is capped at measured retention, and
    the degradation and habit factors gained dead bands so ordinary early ageing,
    which is steepest on a new pack, is no longer scored as a fault. Snapshots
    written by `HealthScoreV1` keep their own version tag.

- **Migration V003 discards every battery-health snapshot written before the fix**
  (`AlgorithmVersion = 'HealthScoreV1'`). An affected snapshot's stored retention
  is out by roughly the pack voltage, nothing in the row says which rows were
  affected, and the raw source values were never persisted, so the figures can be
  neither trusted nor recomputed — and they feed a trend fitted over 180 days. The
  database is copied to `battery.db.bak-v003` first, so the rows stay recoverable.
  The degradation trend and retention sparkline are unavailable until about 30
  days of fresh snapshots accumulate; raw samples, sessions and alerts are
  untouched.

### Changed

- The title-bar theme button shows the theme in effect — a sun for Light, a moon
  for Dark, a monitor for System — instead of a pen glyph, and cycles all three
  preferences, so System is reachable without opening Settings.

## 1.0.0 — 2026-09-08

First public release, by **Naeem Ahmad**. Built over fifteen phases (see
`docs/roadmap.md`); the highlights:

### Distribution

- Self-contained, machine-wide **`Setup.exe`** installer (Inno Setup) — runs on
  any 64-bit Windows 10 (1809+) / 11 with no prerequisites; shows in Control
  Panel and Settings → Apps; installer and uninstaller close a running instance
  first and never touch `%LocalAppData%\BatteryIntelligence`.
- Application branded to Naeem Ahmad (icon, version info, MSIX publisher, About
  page); new app icon from `branding/`.
- Website (`battery-intelligence.netlify.app`) with features, usage and the
  download link; `docs/terms-of-use.md` and `docs/privacy-policy.md`.

### Monitoring

- Battery monitoring over a four-source composite provider (WinRT / IOCTL / WMI /
  system power status), per-capability priority chains, and live capability
  detection.
- Sentinel + plausibility validation on every sample; implausible fields are
  re-graded `Suspect`, never dropped or silently corrected.
- Power (current / voltage / power) on a 5-second cadence with a
  Measured → Calculated → Estimated → Unavailable fallback chain.
- Battery temperature with per-band time and threshold events **where a sensor
  exists** — and an honest unavailable state where it does not.
- Application-usage attribution (`AppEnergyV1`): the measured battery draw
  apportioned among applications by activity weight, with the non-attributable
  baseline separated and every figure `Estimated`-badged.

### Sessions & history

- Charge/discharge sessions with a merged timeline (screen, lock, sleep),
  correct across suspend/resume, missed suspend notifications, clock skew,
  charger bounce, percentage jumps, battery removal and app restarts.
- Tiered storage (raw → minute → hour → daily) with adaptive-tier history reads
  and min/max-preserving downsampling.
- History page: 24 hours to a year, zoom, adaptive axis labels.
- CSV (RFC 4180) and JSON export with table-scope selection; safe path handling
  (OS picker only). "Delete all battery history" with typed confirmation.

### Analytics & alerts

- Battery Health Score (`HealthScoreV1`) with weight renormalisation across the
  factors the hardware reports; Theil–Sen degradation trend; charging-quality
  score (`ChargingQualityV1`); recency-weighted runtime estimate.
- Confidence-gated, fixed-text rule-based insights (`InsightRulesV1`).
- Configurable alerts with hysteresis and cooldown; real Windows toasts plus a
  guaranteed in-app centre.

### UI & diagnostics

- Eight-card responsive dashboard; per-value estimation transparency on hover.
- Diagnostics: live capability matrix, per-subsystem monitoring health
  (`Healthy → Retrying → Degraded`), a log viewer, this app's own resource
  footprint against its budgets, and a redacted "Copy report".
- Accessibility: automation names on every interactive control, decorative marks
  on non-semantic visuals, colour never used alone, chart automation summaries.

### Performance

- Batched writes (200 rows / 30 s / priority bypass), 1 Hz UI coalescing.
- Adaptive sampling: ×3 when the screen is off on battery, ×6 + process paused on
  AC at full, ×0.5 below 20 %, and a hard stop when paused.
- Exponential retry back-off on a failing sampler.
- Cold start ~1.0–1.4 s.

### Data & security

- Single local SQLite database in `%LocalAppData%\BatteryIntelligence`, resolved
  from `%LOCALAPPDATA%` so the location is identical packaged and unpackaged and
  survives upgrade / uninstall.
- Schema V001 + V002 (aggregate-tier time indexes); a corrupt database is
  detected (`PRAGMA quick_check`) and **never deleted**.
- No administrator rights, no network access, parameterised SQL throughout — see
  `docs/security-review.md`.

### Known limitations

- **Working set** with the window open is ~230–270 MB against a 150 MB target —
  the WinUI 3 / SkiaSharp runtime floor. Accepted for 1.0; see `docs/release.md`.
- Distribution is **unpackaged-first**; an MSIX manifest and build configuration
  are provided but the unpackaged framework-dependent build is the tested path.
- The 24-hour soak, a Windows 10 render pass, a screen-reader pass and real
  charger/sleep cycling are release-engineer checklist items — `docs/qa-checklist.md`.
- Full detail of what the app cannot know: `docs/limitations.md` (also on the
  About page).
