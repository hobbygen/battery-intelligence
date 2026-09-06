# Testing Strategy

Status: 1.0.0 — all phases complete. Version 1.0.0.

Covers spec §60, §61, §62, §68.

---

## 1. The central problem

Most of what this application does is hard to test on real hardware: you cannot
drain a laptop battery to 5 % on demand, force a firmware to report a temperature
it does not have, or make a charger fail mid-session — repeatedly, in CI, in
seconds.

The architecture answers this by making hardware access an **interface boundary**
(`architecture.md` §2). Everything above that boundary — sessions, analytics,
health, estimation, statistics, alerts — is pure logic over a data stream, and is
tested by feeding it scripted streams.

This is the main practical payoff of the layering, and it is why `Core` is
forbidden from referencing anything.

A second problem is specific to this machine: **the reference hardware reports
neither cycle count nor battery temperature.** Every code path that consumes those
values would otherwise never execute during development. Simulation coverage for
them is not optional (risk R7).

---

## 2. Test projects

| Project | Scope | Speed | Parallel |
|---|---|---|---|
| `Tests.Unit` | Pure logic: analytics, sessions, estimation, validation, grouping | ms | Yes |
| `Tests.Integration` | SQLite, repositories, migrations, batching, retention | s | No (shared DB files) |
| `Tests.Simulation` | Scripted hardware scenarios through the full pipeline | s | Yes |

Framework: xUnit + FluentAssertions. SQLite integration tests use a real temporary
database file, not in-memory — WAL behaviour, file locking and `busy_timeout` are
part of what is being tested, and an in-memory database exercises none of them.

---

## 3. Unit tests

| Area | Must cover |
|---|---|
| Health calculation | Retention maths; **unavailable design capacity ⇒ Unavailable, not 0** |
| Weight renormalisation | Score with all factors; with cycle count absent; with temperature absent; with **both** absent (the reference-machine case); retention absent ⇒ Unavailable |
| Wear / degradation | Slope over noisy series; insufficient span ⇒ no result |
| Charging / discharge rate | Sign conventions; mA↔mW normalisation (quirk Q2) |
| Runtime estimation | Confidence tiers; screen-state separation; **screen-off with no history ⇒ Unavailable, never extrapolated from screen-on** |
| Session detection | Full matrix in `session-engine.md` §8 |
| Screen-state classification | Screen-off ≠ sleep ≠ locked, all combinations |
| Statistics | Aggregation across day/week/month boundaries, DST, timezone change |
| Alert thresholds | Firing, cooldown, hysteresis, no double-fire |
| Retention | Never deletes open-session rows; never deletes un-rolled rows |
| Aggregation | Rollup idempotency; re-running produces identical output |
| Process estimation | Shares sum to attributable budget; baseline separated; **no double counting** |
| Validation | Every sentinel (`0xFFFFFFFF`, `0xFFFF`, `0x80000000`, `int.MinValue`) ⇒ Unavailable |
| Grade propagation | Combining grades yields the **worst** input grade, never better |

The grade-propagation tests deserve special weight. They are the executable form of
the core engineering principle: if a Calculated value can silently be labelled
Measured, the application's central promise is broken, and only a test will catch
it.

---

## 4. Simulation (spec §61)

`SimulatedBatteryProvider` and siblings implement the production interfaces and
replay scripted scenarios in accelerated time, driven by a virtual clock so an
eight-hour discharge runs in milliseconds.

Scenarios:

| Scenario | Script |
|---|---|
| Normal discharge | 100 → 90 → 80 → 50 → 20 → 10 %, screen on/off transitions |
| Normal charge | 20 → 30 → 40 → 100 %, tapering rate near full |
| Thermal ramp | 25 → 35 → 45 → 55 °C, crossing warning thresholds |
| Multiple batteries | Two devices, independent states, aggregate correctness |
| **No temperature sensor** | Temperature source absent — **the reference machine's real configuration** |
| **No cycle count** | Firmware reports 0 ⇒ treated as Unavailable, excluded from score |
| mA-reporting battery | `BATTERY_CAPACITY_RELATIVE` set; verifies normalisation |
| Sensor dropout mid-session | Provider fails at t+n; session survives, subsystem degrades |
| API failure | Every provider throws; app remains responsive, all pages show unavailable |
| Sleep/resume | Suspend, gap, resume; `SleepSeconds` correct, no interpolated samples |
| Missed suspend | No notification, only a clock gap; inferred event written |
| Charger bounce | Rapid connect/disconnect; no session churn |
| Percentage jump | 40 % → 90 % while awake; flagged Suspect, session intact |
| Battery removal | Device disappears mid-session; clean close, no crash |
| Clock skew | Wall clock steps backwards; sample rejected, history intact |

Registration is gated on the `SIMULATION` compile symbol, present only in Debug
(`architecture.md` §10). The Release binary does not contain the simulation
providers at all — a runtime flag could be flipped by a corrupt settings file, but
absent code cannot be enabled.

---

## 5. Integration tests

| Area | Must cover |
|---|---|
| Migrations | v1 populated with representative rows → every later migration → **zero row loss** |
| Repositories | CRUD, time-range queries, index usage on the hot paths |
| Batching | 200-row flush, 30 s flush, priority bypass, suspend flush |
| Concurrency | Reader during writer under WAL; `busy_timeout` honoured |
| Database locked | Writes queue, `Degraded` raised, priority rows still land, **app keeps running** |
| Disk full | Graceful degradation, no crash, no corruption |
| Retention | Cleanup respects open sessions and rollup tiers |
| Corrupt DB | Detected at startup; app enters read-only degraded mode; **does not delete the database** |
| Crash recovery | Open session adopted within grace; closed `Interrupted` beyond it |

The corrupt-database test encodes a deliberate policy: the application must never
"recover" by discarding user history. Months of battery data is the most valuable
thing the app holds, and a well-meaning auto-repair that deletes it is worse than
refusing to start.

---

## 6. Failure tests (spec §62)

Every item in spec §62 maps to a scenario above. The passing bar in all cases:

1. No crash.
2. No corrupt or fabricated data.
3. Failure surfaced honestly in the UI and on Diagnostics.
4. Unaffected subsystems keep working.
5. Recovery once the fault clears, without a restart.

Point 4 is what makes point 1 meaningful. An app that survives a temperature sensor
failure by disabling all monitoring has technically not crashed, and has also
failed.

---

## 7. Manual verification

Some things cannot be automated and are checked per release on real hardware:

| Check | Where |
|---|---|
| Real sleep/resume, lid close, hibernate | Reference machine |
| Actual charger connect/disconnect | Reference machine |
| Tray behaviour, close-to-tray, single instance | Both OS versions |
| Windows notifications actually appear | Both OS versions |
| Start-with-Windows across reboot | Both OS versions |
| Windows 10 rendering (no Mica) | Win10 VM/machine |
| High contrast, screen reader, keyboard-only | Reference machine |
| 1366×768 and 4K layout, no clipping | Both |
| Long-run soak: 24 h, CPU/RAM/disk within budget | Reference machine |

The 24-hour soak is the only test that can catch slow leaks, unbounded chart
buffers and log growth — the failure modes that never appear in a run measured in
seconds.

---

## 8. Definition of done (spec §68)

A feature is complete only when it is implemented, integrated, tested, handles
failure, has empty and unavailable states, logs, is documented, does not block the
UI, adds no warnings, and works against **both mock and real hardware**.

Compiling is not done. Two additions specific to this project:

- **Every unavailable path has a test.** On this hardware, absent cycle count and
  absent temperature are the normal case, not an edge case.
- **Every estimated value has a grade-propagation test.** The badge is a
  correctness requirement, not a UI detail.
