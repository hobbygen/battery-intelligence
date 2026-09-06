# Changelog

All notable changes to Battery Intelligence. This project follows
[Semantic Versioning](https://semver.org/).

## 1.0.0 — 2026-09-07

First release. Built over fifteen phases (see `docs/roadmap.md`); the highlights:

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
