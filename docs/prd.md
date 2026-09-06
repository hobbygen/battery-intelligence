# Product Requirements Document

Status: 1.0.0 — all phases complete. Version 1.0.0.
Source of record: `Battery_Intelligence_Details.md` (spec §1–§82).

This document does not restate the specification. It fixes the decisions the
specification leaves open: scope boundaries, who this is for, what "good" means,
and what is explicitly not being built.

---

## 1. Problem

Windows tells you a percentage and a rough time estimate. It does not tell you
whether your battery is degrading, how fast, whether charging is behaving normally,
what is draining it, or what happened overnight. Vendor utilities partially fill
the gap but are model-specific, often bundled with unrelated software, and rarely
show their working.

Third-party battery tools tend to fail in one of two ways: they are thin
percentage widgets, or they present confident numbers they cannot actually
measure — invented per-app wattages, health percentages derived from nothing,
CPU temperature relabelled as battery temperature.

**Battery Intelligence occupies the space between:** a genuinely deep monitoring
platform that is rigorous about the difference between what it measured and what
it inferred.

---

## 2. Users

**Primary — the technical laptop owner.** Comfortable with hardware monitoring
tools, wants real telemetry, and will notice and resent a fabricated number. Asks:
is my battery degrading unusually fast, and what is draining it?

**Secondary — the pre-purchase evaluator.** Deciding whether a battery needs
replacing. Needs a defensible retention figure and a trend, not a vague "Good".

**Tertiary — the curious owner.** Wants to understand yesterday's usage without
reading a manual. Served by the Dashboard and Insights; never required to go deeper.

The primary user sets the bar. A tool that satisfies someone who checks its
arithmetic will satisfy the others; the reverse is not true.

---

## 3. Product principles

1. **Provenance over polish.** Every number knows where it came from. When forced
   to choose between a satisfying display and an honest one, honesty wins.
2. **Unavailable is a valid answer.** Stated clearly, with a reason.
3. **Lightweight, or it is self-defeating.** A battery monitor that measurably
   drains the battery has failed regardless of its features.
4. **Local by default.** No account, no cloud, no telemetry.
5. **Explainable.** Every score, estimate and insight can show its working.

---

## 4. Scope — v1.0

**In scope**

| Area | Delivered |
|---|---|
| Live battery state | Percentage, status, AC, capacity, voltage, power |
| Battery health | Retention, wear, explainable score, trend |
| Sessions | Charge/discharge detection, timeline, screen/sleep breakdown |
| Power telemetry | Current, voltage, power; live charts; min/max/avg |
| Temperature | Where the hardware exposes it |
| Application usage | Ranked, grouped, estimated attribution |
| History | 24 h to 1 year, interactive charts |
| Statistics | Lifetime, today, 7 d, 30 d, custom |
| Insights | Rule-based, confidence-gated |
| Alerts | Configurable, Windows notifications + in-app |
| Tray | Background monitoring, single instance |
| Export | CSV, JSON |
| Diagnostics | Live capability matrix, monitoring state, logs |

**Explicitly out of scope for v1** — deferred, with interfaces in place
(spec §78):

PDF reports · cloud sync/backup · mobile or web companion · AI insight providers ·
UPS and external battery monitoring · vendor-specific SDK integrations ·
multi-device monitoring · battery replacement date prediction · any control over
charging behaviour.

The last is worth stating plainly: this application **observes**. It never modifies
power plans, charge thresholds or firmware settings. Monitoring and control are
different products with different risk profiles, and mixing them would make every
bug potentially damaging rather than merely wrong.

---

## 5. Non-negotiable requirements

Failure on any of these is a release blocker, regardless of feature completeness.

| # | Requirement |
|---|---|
| N1 | No fabricated values. Every displayed number is Measured, Calculated, labelled Estimated, or reported Unavailable |
| N2 | No administrator privileges required for any core function |
| N3 | No network access required; none performed without explicit user action |
| N4 | One subsystem's failure never crashes the app or stops the others |
| N5 | The UI thread performs no I/O, interop or SQL |
| N6 | User history survives upgrade, crash, sleep and power loss |
| N7 | Idle cost within budget: <0.5 % CPU, <150 MB, <1 MB/min writes |
| N8 | Every unavailable state explains why |
| N9 | Simulation code is absent from Release binaries |
| N10 | Health score, estimator and insight rules are versioned, and stored versions are never reinterpreted |

N10 protects history: if a scoring change could retroactively alter past values,
the long-term trend — the single most valuable output of the application — becomes
untrustworthy.

---

## 6. Success criteria

**Functional:** the twenty numbered criteria of spec §68.

**Quality:**

| Measure | Target |
|---|---|
| Idle CPU | < 0.5 % |
| Working set | < 150 MB visible, < 80 MB tray-only |
| Disk writes | < 1 MB/min sustained |
| Cold start to interactive | < 2 s |
| Database after 1 year | < 100 MB |
| 24-hour soak | No leak, no unbounded growth, no degradation |
| Unavailable-path test coverage | 100 % of capability-matrix rows |

**Experiential** — the questions a user should answer without help:

- Is my battery healthy, and is it getting worse? *(Battery page, ≤1 click)*
- What is draining it right now? *(Dashboard, 0 clicks)*
- What happened overnight? *(Sessions timeline, ≤2 clicks)*
- Why is that number an estimate? *(hover, 0 clicks)*
- Does my hardware support X? *(Diagnostics, ≤1 click)*

The fourth is the distinguishing one. In most tools it is unanswerable.

---

## 7. Constraints

| Constraint | Value | Source |
|---|---|---|
| Platform | Windows 10 1809+ / Windows 11 | spec §2 |
| Framework | .NET 10, WinUI 3, Windows App SDK 1.8 | spec §2; verified installed |
| Persistence | SQLite, local | spec §27, §33 |
| Privileges | Standard user | spec §23 |
| Architecture | MVVM + DI, layered | spec §4, §5 |
| Data location | `%LocalAppData%\BatteryIntelligence` | spec §58 |
| Distribution | MSIX | spec §58 |

### Verified environment

| Item | Status |
|---|---|
| .NET SDK | 10.0.400 ✅ |
| Windows App Runtime | 1.8 (8000.946.1701.0) ✅ |
| WinUI 3 build + launch | ✅ verified end to end |
| Reference battery | DELL 68ND307, 40 % retention, no cycle count, no temperature sensor |

The reference machine's missing sensors are recorded as a **feature of the test
environment**: they force the unavailable paths to be exercised continuously during
development, which is the best possible defence of requirement N8.

---

## 8. Open questions

None blocking Phase 1. Deferred decisions, each with a working default:

| # | Question | Default until decided |
|---|---|---|
| Q1 | Should the tray icon render live percentage as a glyph? | Static state icon; revisit in Phase 10 |
| Q2 | Default retention — 1 year (spec §29) vs the tiered scheme? | Tiered (7 d raw / 90 d minute / 365 d hour / daily forever), which preserves a year of history at ~3 % of the size |
| Q3 | Expose estimator weights in Settings → Advanced? | Configurable in file, not surfaced in UI, until there is evidence users want to tune them |
| Q4 | Localisation beyond English? | Strings externalised from Phase 1 so it stays possible; only English shipped |

Q2 deserves a note: it satisfies spec §29's one-year default in the sense that
matters — a year of history remains queryable — while satisfying spec §31's demand
not to retain excessive raw samples. The two requirements are only compatible
through tiering.
