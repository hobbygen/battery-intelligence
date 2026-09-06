# Estimation Strategy

Status: Phase 0. Version 1.0.0.

Covers spec §3, §15, §51, §52, §55, §76.

This document defines every place the application computes a number it did not
directly measure, and the rules that keep those numbers honest.

---

## 1. The grade ladder

Every value carries a grade, resolved at runtime and stored with the data.

| Grade | Meaning | UI treatment |
|---|---|---|
| **Measured** | Read directly from hardware or a Windows API | Plain value |
| **Calculated** | Exact arithmetic on measured inputs | Plain value; formula in tooltip |
| **Estimated** | Model output; the true value is not obtainable | **"Estimated" badge**, confidence, methodology tooltip |
| **Unavailable** | Not obtainable on this hardware | "Not available" + why |

Two rules govern the ladder:

**Degrade, never promote.** A value combining inputs of different grades takes the
*worst* grade among them. An estimate multiplied by a measurement is an estimate.

**Unavailable is a real answer.** When the ladder bottoms out, the app says so.
It does not show `0`, `--`, an empty card, or a plausible-looking guess. This is
the requirement that most directly separates this application from the
battery utilities the spec is reacting against.

---

## 2. Power (spec §51)

Resolution order:

1. **Measured** — energy rate in mW from the battery (S1/S3/S4).
2. **Calculated** — `P = V × I`, when voltage and current are separately available.
3. **Estimated** — from capacity change over time:
   `P ≈ ΔmWh / Δhours`, over a window of at least 60 seconds.
4. **Unavailable** — when only percentage is known and design capacity is not.

On the reference machine step 1 succeeds (6,332 mW observed), so power is
**Measured** there. Steps 2–4 still ship, because other hardware will need them.

### Current (spec §13)

`I(mA) = P(mW) ÷ V(mV) × 1000`

Always **Calculated**, never Measured — even though both inputs are measured. The
battery reports power and voltage; it does not report current, and presenting a
derived quantity as a sensor reading would misstate its provenance.

Guards: voltage must be positive and physically plausible (1,000–30,000 mV);
otherwise current is **Unavailable**, not infinite.

### Unit normalisation (quirk Q2)

If the device reports in mA/mAh rather than mW/mWh, all rates and capacities are
normalised to mW/mWh at the provider boundary, and the conversion is recorded.
Everything above the provider works in one unit system. Values converted this way
are **Calculated**, not Measured.

**Phase 2 finding:** no source available to this application (WinRT, WMI or the
IOCTL information block) exposes a battery's *design* voltage on any hardware
examined so far, so the implementation (`BatteryCalculations.NormalizeMilliampsToMilliwatts`)
normalises via the current *live* voltage instead. This is documented as a known
approximation in `docs/limitations.md` section 1 rather than silently diverging
from this section's original "design voltage" wording.

---

## 3. Remaining runtime (spec §52)

Windows' own `BatteryLifeTime` is rejected as a primary source. On the reference
machine it returns `0xFFFFFFFF` (quirk Q3), and where it does return a number it
is an instantaneous extrapolation that swings wildly with momentary load.

### Model

A **rolling, screen-state-aware** estimate:

```
remaining_mWh = measured remaining capacity
rate_mW       = weighted mean discharge rate over a rolling window
runtime_h     = remaining_mWh / rate_mW
```

The rate is an exponentially weighted mean over the last 15 minutes, with samples
weighted by recency, computed **separately per screen state**. This yields the
three figures spec §8 asks for:

| Estimate | Rate source |
|---|---|
| At current usage | Blended rate over the recent window |
| Screen ON | Rate observed during screen-on periods |
| Screen OFF | Rate observed during screen-off periods |

Screen-off rate cannot be derived from screen-on data — the display is typically
the largest single consumer, and extrapolating one from the other would be
invention. If no screen-off history exists yet, the screen-off estimate is
**Unavailable**, not a guess. Spec §8 is explicit that screen-off runtime must not
be claimed as a guaranteed value.

### Confidence

| Confidence | Requires |
|---|---|
| **High** | ≥15 min in current screen state, rate variance low, ≥3 comparable historical periods |
| **Medium** | ≥5 min of data, moderate variance |
| **Low** | ≥60 s of data, or high variance |
| **"Calculating…"** | Below the floor — no number is shown at all |

Showing "Calculating…" rather than a low-quality number in the first minute is
deliberate. A wildly wrong estimate on launch costs more trust than a brief wait.

---

## 4. Battery health (spec §9, §19)

### Capacity retention — Calculated

```
retention% = full_charge_capacity / design_capacity × 100
```

On the reference machine: `38,008 / 95,008 = 40.0 %`. Both inputs are measured, the
operation is exact, so this is **Calculated** and displayed plainly.

If either input is unavailable, retention is **Unavailable**. Spec §9 forbids
inventing a manufacturer-independent health percentage when the information does
not exist, and no substitute is attempted.

### Health score — Calculated, versioned, explainable

A weighted score over available factors only:

| Factor | Weight | Input | If unavailable |
|---|---|---|---|
| Capacity retention | 0.50 | retention % | Score is Unavailable — this factor is required |
| Degradation rate | 0.20 | slope of retention over ≥30 days | Weight redistributed |
| Cycle count | 0.10 | cycles vs chemistry-typical rating | Weight redistributed |
| Thermal exposure | 0.10 | time above threshold | Weight redistributed |
| Charge behaviour | 0.05 | depth-of-discharge distribution | Weight redistributed |
| Rate stability | 0.05 | variance in charge rate | Weight redistributed |

Rules:

- **Weights renormalise** across available factors. On the reference machine,
  cycle count and temperature are both unavailable, so the score comes from
  retention, degradation, charge behaviour and rate stability, renormalised to 1.0.
- The **set of contributing factors is stored** with each snapshot (`FactorsJson`)
  and shown in "How this score is calculated".
- If retention is unavailable, the score is **Unavailable** — a score built only on
  behavioural proxies would be a number with nothing real underneath it.
- The algorithm is tagged (`HealthScoreV1`) and stored per snapshot, so a future
  `v2` cannot retroactively rewrite the meaning of past history.

Categories per spec §19: 90–100 Excellent · 75–89 Good · 60–74 Fair · 40–59 Poor ·
0–39 Critical.

The label is always **"Battery Health Score"**, never "Battery Health" alone, and
the methodology link is always adjacent. Spec §19 warns against making the score
appear scientifically authoritative; the naming and the always-visible explanation
are how that is honoured.

---

## 5. Per-application power (spec §15, §55)

**This is the least certain number in the application, and is always Estimated.**

Windows does expose measured per-process energy, through the Energy Estimation
Engine's SRUM database — but reading it requires administrator rights, and spec §23
forbids requiring elevation. That trade is resolved in favour of not requiring
admin, which means per-process attribution is a model, permanently.

### Model `AppEnergyV1`

Attribution proceeds in three steps.

**Step 1 — system energy budget.** Over an interval, the measured system draw is
the only ground truth:

```
E_total = |P_battery_mW| × Δt        (Measured, when on battery)
```

On AC, no battery draw exists to attribute. Application ranking continues on
relative CPU/GPU weight, but absolute power figures are **Unavailable** rather than
fabricated — a point the UI must state plainly on the App Usage page while charging.

**Step 2 — non-attributable baseline.** A share of draw belongs to no process:
display backlight, radios, chipset idle. It is separated first:

```
E_apps = E_total − E_baseline
E_baseline = display_estimate + platform_idle_estimate
```

`display_estimate` is derived per-device by regressing observed idle draw against
screen state over time — the difference in mean draw between screen-on-idle and
screen-off-idle periods. Until enough data exists, the baseline uses a conservative
default and the whole attribution is marked **Low** confidence.

**Step 3 — weighted attribution.** The remaining budget is divided by a weight:

```
w_i = (cpu_share_i × W_cpu)
    + (gpu_share_i × W_gpu)
    + (io_rate_i   × W_io)
    + (foreground_i × W_fg)

share_i = w_i / Σw          E_i = E_apps × share_i
```

Default weights: `W_cpu = 1.0`, `W_gpu = 1.2`, `W_io = 0.3`, `W_fg = 0.15`. These
are configuration, not constants in code (spec §66 forbids hardcoded thresholds),
and are versioned with the model.

### What this model does and does not claim

It **does** rank applications by likely battery impact, and apportion a measured
total among them.

It does **not** measure any individual application's consumption. Two processes
with identical CPU time can differ substantially in real energy use — instruction
mix, wake frequency and GPU work all matter, and none is fully observable here.

Consequences, all mandatory:

- Every per-app figure carries an **Estimated** badge (spec §15).
- Shares sum to 100 % of the *attributable* budget, with the baseline shown as an
  explicit separate slice — so the numbers add up and nothing is double-counted
  (spec §55).
- The methodology is reproduced in Diagnostics/About (spec §55).
- No per-app figure is ever presented as mWh "used" without the estimate framing.

---

## 6. Charging quality (spec §10, §54)

Score 0–100 over a completed charging session:

| Component | Weight | Basis |
|---|---|---|
| Rate vs personal baseline | 0.40 | Mean charge rate ÷ 30-day median for this battery |
| Rate stability | 0.25 | Coefficient of variation of rate |
| Thermal behaviour | 0.20 | Time above threshold (**omitted, weights renormalised, when no sensor**) |
| Interruptions | 0.15 | Count, normalised by duration |

The baseline is **the user's own history**, not a manufacturer figure. Absolute
charge rates vary enormously by charger, port and firmware policy, so "12 % slower
than your 30-day average" is meaningful where "12 % slower than spec" would not be.

Requires ≥5 prior comparable sessions. Below that the score is **Unavailable** with
"Not enough history yet" — satisfying spec §10's rule that comparative insights are
generated only when sufficient history exists.

---

## 7. Insight confidence (spec §18)

An insight is emitted only when all four hold:

1. **Minimum sample size** — enough observations for the comparison.
2. **Minimum time span** — the period is long enough to be meaningful.
3. **Effect exceeds noise** — the deviation is larger than the metric's own
   variability, not merely nonzero.
4. **Confidence ≥ threshold** (default 0.7).

Rule 3 is what prevents the insight engine from becoming a noise generator.
"Your battery drained 0.4 % faster than average" is technically true and completely
worthless; requiring the effect to exceed normal variance is what keeps the feature
credible.

Every insight stores its confidence, its supporting metrics, its period, and its
rule version, and the UI shows them.

---

## 8. Transparency contract (spec §76)

Every estimated value in the UI must be able to answer, on hover or click:

- **Why is this estimated?** — which measurement was unavailable.
- **What data was used?** — inputs and their grades.
- **Over what window?** — the time range.
- **How confident?** — High / Medium / Low, and why.

Example:

> **Estimated runtime: 4h 18m** · Medium confidence
>
> Based on 37,381 mWh remaining and an average draw of 8,700 mW measured over the
> last 15 minutes with the screen on. Confidence is Medium because only 9 minutes
> of screen-on data are available in the current state.

A tooltip that merely repeats the number is not compliance. It must name the
inputs, the window and the reason for the confidence level.
