# Battery Intelligence

[![CI](https://github.com/myplexlink-ops/battery-intelligence/actions/workflows/ci.yml/badge.svg)](https://github.com/myplexlink-ops/battery-intelligence/actions/workflows/ci.yml)

A Windows desktop utility that tells you the truth about your laptop battery —
health, power draw, what's draining it, and how long it will last — **locally**,
with no account, no cloud, and no fabricated numbers.

**[Download for Windows](https://battery-intelligence.netlify.app)** ·
[Website](https://battery-intelligence.netlify.app) ·
[Terms of Use](docs/terms-of-use.md) ·
[Privacy Policy](docs/privacy-policy.md)

By **Naeem Ahmad**. Free and open source (MIT).

> Every value the app shows is either **Measured** (read from the hardware),
> **Calculated** (derived from measured values with the method disclosed),
> labelled **Estimated** (a documented, versioned model), or reported
> **Unavailable**. It never guesses and calls it a fact. The Diagnostics page
> shows exactly what *your* machine reports and where each figure comes from.

## What it does

- **Battery** — identity, design vs full-charge capacity, a 0–100 health score
  (`HealthScoreV1`, weighted over the factors your hardware can actually report),
  a Theil–Sen degradation trend, and a runtime estimate split by screen-on /
  screen-off.
- **Power** — live current / voltage / power with min/max/avg over a selectable
  window and synchronised charts. Current is always `P ÷ V` — calculated, never
  read directly.
- **Temperature** — per-band time breakdown and threshold events, *where a sensor
  exists*. Many laptops have none; the app says so and substitutes nothing.
- **Sessions** — charge/discharge sessions with a merged timeline of screen,
  lock and sleep events, correct across suspend/resume and app restarts.
- **Application usage** — a ranked, `Estimated`-badged apportionment of the
  measured battery draw among applications (`AppEnergyV1`), with the
  non-attributable baseline shown separately.
- **Analytics & alerts** — statistics windows, confidence-gated rule-based
  insights (`InsightRulesV1`), and configurable alerts with Windows toasts plus
  an in-app centre.
- **Dashboard** — eight cards on a responsive grid.
- **History & export** — tier-aware charts from 24 hours to a year, zoom, and
  CSV / JSON export.
- **Diagnostics** — the live capability matrix, per-subsystem monitoring health,
  a log viewer, this app's own resource footprint, and "Copy report".
- **Adaptive sampling** — the app quiets itself when the screen is off, samples
  finer below 20 %, and stops entirely when paused, so its own cost stays
  negligible.

## Build & run

Requires the .NET 10 SDK and the Windows App Runtime 1.8 (a prerequisite for
running; the SDK installs the build-time pieces).

```
dotnet build BatteryIntelligence.slnx -c Debug
dotnet run --project src/BatteryIntelligence.App/BatteryIntelligence.App.csproj -c Debug
dotnet test  BatteryIntelligence.slnx -c Debug
```

The Debug build includes scripted hardware simulation (`SIMULATION` compile
symbol); the Release build does not — the simulation code is physically absent,
not merely disabled (`tools/verify-no-simulation.ps1` is the release gate).

Distribution and MSIX packaging: see [`docs/release.md`](docs/release.md).

## Project map

```
src/BatteryIntelligence.Core          pure domain + interfaces, zero dependencies
src/BatteryIntelligence.Windows       Win32 / WinRT interop, the message-only window
src/BatteryIntelligence.{Battery,Data,Power,Thermal,ProcessMonitoring,
    Sessions,Analytics,Notifications,Reporting}
                                      infrastructure siblings — each references Core only
src/BatteryIntelligence.App           WinUI 3 shell, Views, ViewModels, composition root
tests/BatteryIntelligence.Tests.{Unit,Simulation,Integration}
```

Architecture, per-subsystem design, and the requirement-to-code trace live in
[`/docs`](docs/) — start with [`docs/architecture.md`](docs/architecture.md) and
[`docs/roadmap.md`](docs/roadmap.md).

## Where your data lives

`%LocalAppData%\BatteryIntelligence` — a single SQLite database, your settings,
and rotated logs. Never inside the install directory, so it survives upgrade and
uninstall. You can export or delete all of it from within the app. No network
access is performed by the application, ever.

## Install

Download `BatteryIntelligence-Setup-<version>.exe` from the
[latest release](https://github.com/myplexlink-ops/battery-intelligence/releases/latest)
or the [website](https://battery-intelligence.netlify.app) and run it. It is a
self-contained installer — no .NET, no Windows App Runtime, and no other
prerequisite is needed. Windows 10 (build 1809 / 17763) or later, 64-bit.

The installer is not code-signed yet, so SmartScreen shows a warning on first
run: choose **More info → Run anyway**.

## Licence

[MIT](LICENSE) © 2026 Naeem Ahmad. Third-party components and their licences are
listed on the About page and in
[`docs/security-review.md`](docs/security-review.md).
See also [Terms of Use](docs/terms-of-use.md) and
[Privacy Policy](docs/privacy-policy.md).
