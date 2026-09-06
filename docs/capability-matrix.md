# Windows API Capability Matrix

Status: Phase 2 — verified live. Version 1.0.0.

**Phase 2 confirmation (2026-09-05):** every figure in section 1 below was
re-verified through the running application (`CompositeBatteryProvider` +
`BatteryCapabilityDetector`), not merely probed ad hoc as in the Phase 0
baseline. Design capacity, full-charge capacity, retention (40.0%), chemistry,
manufacturer, serial and the two Unavailable rows (cycle count, temperature) all
matched exactly. See `docs/roadmap.md` Phase 2 exit criteria for the full table.

This document records **what this application can actually measure**, which API
supplies each value, and what happens when a source is missing. It is the
concrete backing for the Core Engineering Principle (spec §3):

> Measured → Calculated → Estimated → Unavailable, in that order. Never fabricate.

Two distinct things are recorded per metric:

- **Capability** — can the value be obtained *on this class of hardware at all*.
- **Grade** — Measured / Calculated / Estimated / Unavailable, decided **at runtime
  per machine**, never hardcoded.

---

## 1. Reference measurement — the development machine

Probed 2026-09-03 on the target dev machine. This is a **sample of one** and is
recorded to ground the design in reality, *not* to be treated as universal. Every
value below is re-detected at runtime by `IBatteryCapabilityDetector`.

| Property | Value |
|---|---|
| OS | Windows 11 Pro, build 26200, x64 |
| Battery | DELL 68ND307, manufacturer SMP, serial 642 |
| Chemistry | Li-Polymer (raw `0x50694C` = `"LiP"`) |
| Design capacity | 95,008 mWh |
| Full-charge capacity | 38,008 mWh |
| Remaining capacity | 37,381 mWh |
| Design voltage | 11,794 mV |
| Live voltage | 11,791 mV |
| Charge rate (at probe) | 6,332 mW |
| Cycle count | **Not reported** (firmware returns 0 / `-`) |
| Battery temperature | **Not reported** (`BatteryTemperature` yields no instances) |
| Capacity retention | **40.0 %** — a genuinely worn battery |

The 40 % retention is a real firmware reading, not an error. It is useful: the
health features have a meaningful, non-trivial signal to display during development
rather than a flat "100 %, all good" that would hide bugs.

---

## 2. Source inventory

Sources are tried in priority order. The **first** source that returns a valid
reading wins, and the resulting `MeasurementSource` is recorded on every sample so
the UI and the Diagnostics page can always explain provenance.

| ID | Source | Access | Notes |
|---|---|---|---|
| `S1` | `Windows.Devices.Power.Battery` (WinRT) | User | Per-battery + aggregate reports. Cleanest API. Works unpackaged via Windows App SDK. mWh/mW only. |
| `S2` | `GetSystemPowerStatus` (Win32) | User | AC line status, percent, crude remaining seconds. Always present. |
| `S3` | WMI `root\wmi` battery classes | User | Voltage, cycle count, manufacturer, chemistry, serial, temperature. Per-battery. |
| `S4` | `IOCTL_BATTERY_QUERY_INFORMATION` (SetupDi + DeviceIoControl) | User | Richest source. Cycle count, temperature, manufacture date, unique ID. Used to enrich/repair S1–S3. |
| `S5` | `RegisterPowerSettingNotification` | User | Event-driven power/display/AC transitions. |
| `S6` | `WTSRegisterSessionNotification` | User | Lock / unlock / logon / logoff. |
| `S7` | `WM_POWERBROADCAST` | User | Suspend / resume. |
| `S8` | `ProcessDiagnosticInfo` + `Process` + PDH counters | User | Per-process CPU, memory, lifetime. |
| `S9` | SRUM / Energy Estimation Engine (`SRUDB.dat`) | **Admin** | Per-process *measured* energy. **Out of scope** — spec §23 forbids requiring admin. |
| `S10` | `MSAcpi_ThermalZoneTemperature` | **Admin** | CPU thermal zone. **Deliberately unused** — spec §14 forbids substituting CPU temp for battery temp. |

**S9 and S10 are recorded here specifically to document that they were considered
and rejected**, so a future maintainer does not "helpfully" reintroduce an admin
requirement.

---

## 3. Capability matrix

Legend — **M** Measured · **C** Calculated · **E** Estimated · **U** Unavailable

| # | Metric | Grade | Source chain | Fallback behaviour |
|---|---|---|---|---|
| C01 | Battery present / count | M | S1 → S3 → S2 | Zero batteries ⇒ desktop mode, monitoring pages show "No battery detected" |
| C02 | Charge percentage | M | S1 (remaining÷full) → S2 | S2 always available; never unavailable on a battery machine |
| C03 | Charging / discharging / idle / full | M | S1 → S3 → S2 | — |
| C04 | AC line connected | M | S2 → S5 | — |
| C05 | Remaining capacity (mWh) | M | S1 → S3 → S4 | U ⇒ percentage-only mode; capacity cards show unavailable state |
| C06 | Full-charge capacity (mWh) | M | S1 → S3 → S4 | U ⇒ health % unavailable (**do not** invent one — spec §9) |
| C07 | Design capacity (mWh) | M | S1 → S4 → S3 | U ⇒ health % unavailable |
| C08 | Capacity retention / wear % | **C** | C06 ÷ C07 | Requires both; else U. Always labelled Calculated |
| C09 | Voltage (mV) | M | S3 → S4 | **Not exposed by S1.** U ⇒ current cannot be derived (see C11) |
| C10 | Energy rate / power (mW) | M | S1 → S3 → S4 | Signed: + charging, − discharging |
| C11 | Current (mA) | **C** | C10 ÷ C09 × 1000 | Needs both power and voltage. **Never labelled Measured** |
| C12 | Cycle count | M | S4 → S3 | **U on the reference machine.** Health score must degrade gracefully without it |
| C13 | Battery temperature | M | S4 → S3 | **U on the reference machine.** Show "sensor not exposed by this device". **Never** substitute S10 |
| C14 | Manufacturer / model / serial / chemistry | M | S3 → S4 | Display "Unknown" per-field, not a blank card |
| C15 | Screen on / off / dimmed | M | S5 `GUID_CONSOLE_DISPLAY_STATE` | Win8+; assume "on" if unavailable and flag in Diagnostics |
| C16 | Locked / unlocked | M | S6 | — |
| C17 | Sleep / hibernate / resume | M | S7 | Gap-detection cross-check (see §5) |
| C18 | Lid open / closed | M | S5 `GUID_LIDSWITCH_STATE_CHANGE` | Optional; absent on many desktops |
| C19 | Per-process CPU / memory / lifetime | M | S8 | — |
| C20 | Process foreground state | M | `GetForegroundWindow` + `GetWindowThreadProcessId` | — |
| C21 | **Per-process energy (J/mWh)** | **E** | Model `v1` over C19/C20/C10 | **Always Estimated.** S9 rejected (admin). See `estimation-strategy.md` |
| C22 | System-wide remaining runtime | **E** | Rolling model, not S2's instantaneous figure | "Calculating…" until enough data |
| C23 | "Held awake" / power requests | **U** | `powercfg /requests` needs admin | Diagnostics states it requires elevation; feature disabled, **not faked** |
| C24 | Battery health score | **C** | Explainable weighted model, versioned | Degrades as inputs (C12, C13) go U; publishes which factors it used |

### Grades that must never be promoted

Three rows carry a hard rule, because getting them wrong is exactly the failure the
spec's core principle exists to prevent:

- **C11 (current)** is arithmetic on two measurements. It is *Calculated*, forever.
- **C21 (per-process energy)** is a model. It is *Estimated*, forever.
- **C22 (remaining runtime)** is a prediction. It is *Estimated*, forever, and ships
  with a confidence level.

---

## 4. Known source quirks (measured, not theoretical)

**Q1 — `BatteryStaticData` fails on the modern CIM path.**
`Get-CimInstance -Namespace root\wmi -ClassName BatteryStaticData` returns
`Generic failure`, while the legacy `Get-WmiObject` path against the same class
returns full, correct data (design capacity, manufacturer, chemistry, serial).

*Consequence:* the WMI provider (S3) cannot be a single call. It needs a
per-class fallback chain, and a failure of one class must not abandon the others.
This is why S3 is modelled as several independent probes rather than one query.

**Q2 — Rate units are not guaranteed to be milliwatts.**
ACPI batteries may report rate and capacity in **mA/mAh** instead of **mW/mWh**,
indicated by a capability bit in the battery information structure. Treating mA as
mW silently produces power figures wrong by roughly an order of magnitude.

*Consequence:* the provider reads the capability bit via S4 and normalises to mW,
recording the original unit. If the bit cannot be read and voltage is available,
normalise via voltage; if neither, mark the rate **U** rather than guess.

**Q3 — `Win32_Battery` is largely empty on this hardware.**
`DesignCapacity`, `FullChargeCapacity`, `TimeToFullCharge` and `ExpectedLife` all
return null, and `EstimatedRunTime` returns `71582788` — the well-known
"unknown" sentinel (`0xFFFFFFFF` minutes), *not* a 136-year runtime.
`BatteryRuntime.EstimatedRuntime` likewise returns `4294967295`.

*Consequence:* sentinel filtering is mandatory in the validation layer
(spec §63). `0xFFFFFFFF` / `0xFFFF` / `0x80000000` must map to **U**, never be
stored as a number. `Win32_Battery` is demoted to a last-resort source.

**Q4 — Chemistry encodings differ per source.**
`Win32_Battery.Chemistry` returned `2` = *Unknown*, while `BatteryStaticData`
returned `0x50694C` — a packed ASCII tag, `"LiP"` (Lithium Polymer). The richer
source disagrees with, and beats, the enum.

*Consequence:* prefer the ACPI ASCII tag; fall back to the enum; display "Unknown"
only when both fail.

**Q5 — Cycle count of 0 means "not reported", not "brand new".**
A zero from firmware on a battery at 40 % retention is self-evidently absent data.

*Consequence:* treat `0` as **U** for cycle count, and exclude the factor from the
health score rather than scoring it as a pristine battery.

---

## 5. Sleep and resume integrity

`WM_POWERBROADCAST` (S7) is the primary signal, but it is not sufficient alone:
suspend notifications can be missed, and an abrupt power loss delivers nothing.

Every sampler therefore also performs **gap detection** — comparing wall-clock
delta against monotonic uptime delta between consecutive samples. A wall-clock jump
materially exceeding the expected sampling interval implies an unobserved
suspend/hibernate, which is reconstructed and written as a `SystemEvent` with
`Inferred = true`.

This means sessions stay correct across a crash or a hard power-off, satisfying
spec §24 and §49 without trusting a single fragile notification.

---

## 6. Diagnostics page contract

Spec §26 makes the Diagnostics page **mandatory**. It renders this matrix live,
one row per capability:

```
Feature                  Status        Source                      Grade
Battery percentage       Available     Windows.Devices.Power       Measured
Battery voltage          Available     WMI root\wmi                Measured
Electric current         Derived       power / voltage             Calculated
Cycle count              Unavailable   not reported by firmware        —
Battery temperature      Unavailable   sensor not exposed              —
Per-process energy       Estimated     model v1 (fallback)         Estimated
Held-awake requests      Unavailable   requires elevation              —
```

The page must render the real detected state, including the unavailable rows.
Hiding an unavailable row would defeat the purpose of the page.
