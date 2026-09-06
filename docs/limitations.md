# Limitations

Status: 1.0.0 — all phases complete. Version 1.0.0.

**This document is mandatory** (spec §69) and is surfaced in the application under
About → Limitations.

It exists because the application's central promise is that it never fabricates
data. Keeping that promise means stating plainly what it cannot know.

---

## 1. Limitations that are hardware-dependent

These vary by laptop. The Diagnostics page shows what **your** machine reports.

### Battery temperature

Many laptops do not expose a battery temperature sensor to Windows. When absent,
the Temperature page shows an unavailable state.

**We do not substitute CPU temperature.** CPU thermal zones are physically distant
from the battery, respond on a completely different timescale, and would produce a
number that looks like battery temperature while measuring something else. Showing
it would be worse than showing nothing.

> On the reference development machine, battery temperature is **not available**.

### Cycle count

Cycle count comes from battery firmware. Many batteries report `0`, meaning "not
tracked", not "unused". We treat `0` as unavailable and exclude the factor from the
health score, renormalising the remaining weights, rather than scoring the battery
as brand new.

> On the reference development machine, cycle count is **not available**.

### Design capacity

Without design capacity, capacity retention and health percentage cannot be
computed. We do not estimate a design capacity from the model name or from
observed maxima — both would be guesses presented as specifications.

### Electric current

Batteries report **power** (mW) and **voltage** (mV). Current is calculated as
`P ÷ V` and always labelled **Calculated**. If voltage is unavailable, current is
unavailable — we do not assume a nominal voltage.

### Power rate when no provider meters it

The Power page's energy-rate figure prefers a metered reading (mW from S1/S3/S4).
Where no provider reports one, it falls back to `V × I` (Calculated) and then to
capacity change over time — `ΔmWh ÷ Δhours` over a window of at least a minute —
which is **Estimated**, with the badge. Below a minute of history, or when
capacity is not readable, the rate is **Unavailable**, never a guess. The
reference machine meters power directly, so it stays Measured there.

### Power chart history

The Power page keeps roughly the last hour of samples in memory for its live
charts. The "This session" window therefore shows at most the last hour of a
session longer than that. Full-history charts over arbitrary ranges are the
History page's job (Phase 11), reading the rolled-up aggregate tiers.

### Reporting units

Some batteries report in mA/mAh rather than mW/mWh. We detect this via an ACPI
capability bit and normalise. If the bit cannot be read and voltage is unavailable,
the rate is reported unavailable rather than risking a figure wrong by roughly ten
times.

No source available to this application exposes a battery's *design* voltage —
neither WMI's static battery data nor the IOCTL information block carries it, on
every configuration examined so far. Normalisation therefore uses the
*current live* voltage as a stand-in. This is a reasonable approximation (voltage
sags only modestly across the charge curve) and the resulting value is always
labelled Calculated, never Measured — but it is not the identical figure a
design-voltage-based conversion would produce.

---

## 2. Limitations that are permanent by design

These will not change in a future version, because they follow from deliberate
choices.

### Per-application battery consumption is estimated, always

Windows can measure per-process energy, through the Energy Estimation Engine's
SRUM database — but reading it requires administrator privileges.

This application deliberately **does not require administrator rights**. The
consequence is that per-application figures are produced by a documented model
based on CPU, GPU, I/O and foreground activity, apportioning the *measured* total
system draw among running applications.

What that means in practice:

- Application **rankings** are meaningful.
- Individual application **wattages** are estimates, not measurements.
- Two applications with identical CPU time can differ in real energy use, and the
  model cannot see the difference.
- **GPU and disk-I/O activity are not observed per process.** That needs ETW or
  elevation, neither of which this application uses. The `AppEnergyV1` model
  keeps `W_gpu` and `W_io` terms so they can be populated later, but today the
  weight is CPU time plus a small foreground bonus.
- **Per-application icons are not shown yet.** Rows use a monogram tile.

Every such figure carries an "Estimated" badge. The full methodology is in
About → Estimation Methodology.

### "Held awake" attribution is unavailable

Identifying which process is preventing sleep requires `powercfg /requests`, which
needs elevation. Rather than partially guessing, this is reported as unavailable.

### While charging, absolute per-app power is unavailable

Attribution divides *measured battery draw* among applications. On AC there is no
battery draw to divide, so applications are ranked by relative activity while
absolute power figures are withheld.

### Remaining time is a prediction

Runtime estimates come from a rolling model over recent usage, with an explicit
confidence level. They cannot account for what you are about to do. Opening a
video call after ten idle minutes will invalidate the estimate — and that is a
property of prediction, not a defect.

Screen-off runtime is estimated from **observed screen-off periods only**. Until
enough screen-off history exists, it is reported unavailable rather than
extrapolated from screen-on behaviour.

### The health score is ours, not an industry standard

"Battery Health Score" is a transparent, versioned, weighted score defined by this
application. It is not a manufacturer specification and not an industry standard.
Every score records which factors contributed and how, viewable via "How this score
is calculated".

Capacity retention (`full charge ÷ design capacity`) *is* a straightforward
calculation from firmware values, and is shown separately from the score.

### No cloud, no account, no telemetry

All data stays on this machine. There is no sync, no backup and no remote
processing. If the database is deleted, the history is gone. Export (CSV/JSON) is
the supported way to keep a copy.

---

## 3. Measurement caveats

### Firmware values can be wrong

Reported capacity is what the battery's firmware claims. Firmware can be
miscalibrated — a battery that has not been fully cycled in a long time may report
drifted values. Implausible readings are flagged as suspect and excluded from
averages, but we cannot correct a systematically miscalibrated battery.

### Sampling gaps are real gaps

While the machine is asleep, no samples are taken. Elapsed time is attributed to
sleep and the battery delta across the gap is recorded, but **no samples are
invented** for that period. Charts show a genuine gap, because that is what
happened.

### Statistics need history

Comparative insights ("12 % slower than your 30-day average") require enough
history to compare against. Before that, they are not shown. Minimums are stated in
each empty state — a comparison against three days of data would be noise wearing
the costume of an insight.

### Multiple batteries

Where the system has more than one battery, aggregates are computed only where
mathematically valid. Capacities and energies sum; voltages do not. Per-battery
detail is always available separately.

Because WinRT, WMI and the battery device interface each identify batteries
through their own unrelated scheme, this application currently correlates the
same physical battery across those three sources by enumeration order rather
than a shared identifier. This is exact for a single battery — the large
majority of laptops, including the reference machine — and is expected to hold
on well-behaved multi-battery systems, but is not a guaranteed correlation on
unusual hardware.

---

## 4. Platform limitations

| Limitation | Detail |
|---|---|
| Windows only | Uses Windows-specific power APIs throughout |
| Minimum Windows 10 1809 (17763) | Required by the Windows App SDK and the screen-state APIs |
| Desktops | With no battery, monitoring pages report no battery detected |
| Virtual machines | Often expose synthetic or absent batteries; readings may be meaningless |
| External / USB-C batteries | Not monitored in v1; the provider interface leaves room to add them |
| UPS devices | Not monitored in v1 |
| Manufacturer utilities | Vendor tools (Dell Power Manager, Lenovo Vantage) may apply charge policies — such as holding at 80 % — that this app observes but cannot control or fully explain |
| Standby vs. hibernate | Windows delivers the same suspend/resume notification for both; sessions record one "suspended" state rather than a distinction the platform does not expose to user-mode applications |

The last row is worth noting when interpreting charging data: a session that stops
at 80 % is usually a vendor policy working correctly, not a fault, and the app will
record it as an interruption without being able to distinguish policy from problem.

---

## 5. What this application will never do

- Show a value it did not measure, calculate, or clearly label as an estimate.
- Substitute one sensor for another and imply they are the same.
- Show `0` for a missing reading.
- Claim laboratory accuracy for a model output.
- Require administrator rights for core monitoring.
- Send your data anywhere.
- Recommend opening the battery, altering firmware, or bypassing charging
  protections (spec §77).

If a value is not knowable on your hardware, the application says so and explains
why. That is the intended behaviour, not a gap in it.
