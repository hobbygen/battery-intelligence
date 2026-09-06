# UI Navigation and Design

Status: Phase 0, refreshed after Phase 5. Version 1.0.0.

Covers spec §6–§8, §38–§43.

---

## 0. Visual language (imported from Claude Design, 2026-09-06)

The UI was reskinned to the design in `Battery Intelligence.dc.html` (Claude
Design project `a83de085…`): a Fluent-structured shell — persistent rail, custom
46 px title bar, bordered card grid, 4 px geometry — carrying the "Broadsheet"
ink palette (light paper ground, near-black text, process-cyan `#0088b0` as the
single interactive accent, magenta `#d6006c` as a rare second spot).

Tokens live in `src/BatteryIntelligence.App/Resources/AppTheme.xaml`
(`ThemeDictionaries` for Light / Default-dark / HighContrast, plus a focused set
of stock Fluent theme-resource overrides so built-in controls adopt the palette).
Shared type/card/badge styles are in `Resources/Styles.xaml`. New controls:
`Controls/Segmented` (the `data-seg` pill selector), `Controls/CardHeader`,
`Controls/MetricStat`, `Controls/Sparkline` (dependency-free mini trend).

**Deviations from the imported design (deliberate):**

- **Typeface is Segoe UI, not Source Serif 4.** The design sets everything in the
  serif; the app keeps the platform sans. Only the colour / layout / token system
  was imported.
- **Icons are Segoe Fluent Icons, not Phosphor duotone.** `FontIcon` glyphs map
  to the nearest Fluent equivalent per nav/card icon.
- **Cards stack in a single column** rather than the design's
  `repeat(auto-fit, minmax(430px, 1fr))` responsive grid — legible at every width
  without a `WrapPanel`; multi-column reflow is a later polish item. Page
  `ScrollViewer`s carry a horizontal-scroll fallback so nothing clips below the
  minimum window size (spec §41).
- **Density (Comfortable / Compact)** is a stored setting and a Settings
  `Segmented`, but the padding swap applies on next launch, not live.
- **Not built this pass** (each shows an honest "arrives in Phase N" state, never a
  fabricated card): App Usage, Statistics, History, Alerts, Smart Insights, the
  battery health score / degradation trend / charging quality, the alert-centre
  flyout contents (bell + placeholder only), and the Sessions detail drill-down.
- **New About page** (footer nav, below Settings / Diagnostics, under a "System"
  group header). Real static content only.
- **Title-bar status strip** now shipped (§1): live "NN% · State" pill, plus a
  theme-toggle button, an alert bell (placeholder flyout) and a Settings shortcut.

---

## 1. Shell

```
┌─────────────────────────────────────────────────────────────────┐
│ ☰  Battery Intelligence            92% ⚡ Charging · 38m to full │  title bar
├──────────────┬──────────────────────────────────────────────────┤
│ ▣ Dashboard  │                                                  │
│ ▤ Battery    │                                                  │
│ ▥ Sessions   │              page content                        │
│ ⚡ Power      │                                                  │
│ 🌡 Temperature│                                                  │
│ ▦ App Usage  │                                                  │
│ ▧ Statistics │                                                  │
│ ▨ History    │                                                  │
│ ⚠ Alerts     │                                                  │
├──────────────┤                                                  │
│ ⚙ Settings   │                                                  │
│ ⚕ Diagnostics│                                                  │
└──────────────┴──────────────────────────────────────────────────┘
```

`NavigationView` in `Left` mode, auto-collapsing to `LeftCompact` below 1008 px and
to `LeftMinimal` below 641 px. Settings and Diagnostics are pinned to the footer —
they are configuration and support, not monitoring, and separating them keeps the
primary list scannable.

The **status strip** in the title bar persists across every page, so the answer to
"what is my battery doing right now" is never more than zero clicks away.

### Navigation requirements (spec §6)

| Requirement | Implementation |
|---|---|
| Icons | Fluent System Icons, consistent weight |
| Tooltips | On every item; the only label source in compact mode |
| Selected state | Built-in indicator, plus `AutomationProperties` selected state |
| Collapsed mode | Automatic by width + manual toggle, persisted |
| Keyboard navigation | Tab/arrow through items; `Ctrl+1`…`Ctrl+9` jump to pages |
| Accessible labels | `AutomationProperties.Name` on every item and status element |

---

## 2. Page inventory

| Page | Answers | Primary content |
|---|---|---|
| **Dashboard** | What is happening now? | 8 cards (§3 below) |
| **Battery** | How healthy is it? | Identity, capacity comparison, health score + breakdown, per-battery selector |
| **Sessions** | What happened this session? | Current session detail, timeline, historical session list |
| **Power** | What are the electrical figures? | Live current/voltage/power, min/max/avg, three charts, window selector |
| **Temperature** | Is it running hot? | Current/min/max/avg, trend, per-state breakdown, thresholds |
| **App Usage** | What is consuming power? | Ranked applications, badges, sort controls, methodology link |
| **Statistics** | How do I use it overall? | Lifetime / Today / 7d / 30d / custom, aggregate charts |
| **History** | How has it changed? | Interactive charts, 24h–1y + custom, zoom, tooltips |
| **Alerts** | What was I warned about? | Alert history + per-alert configuration |
| **Settings** | Configuration | 7 categories per spec §35 |
| **Diagnostics** | Is it working? | Capability matrix, monitoring state, DB stats, logs, Copy diagnostics |

Every page must implement four states: **normal**, **loading**, **empty**
("Not enough historical data yet"), and **unavailable** ("This device does not
expose a battery temperature sensor"). Spec §43 forbids blank cards; the empty and
unavailable states must explain *why*, not merely that there is nothing to show.

---

## 3. Dashboard composition

Eight cards per spec §7, on a responsive grid:

| Card | Primary metric | Notes |
|---|---|---|
| Battery Info | Percentage + state | Battery visual, bar, time-to-full/empty, screen-on/off estimates |
| Battery Health | Health score | Retention bar, capacity comparison, category |
| Current Session | Duration + delta | Rate, screen split, live |
| Power / Current | Power (W) | Sparkline; **Calculated** badge on current |
| Temperature | °C | **Unavailable state on the reference machine** |
| App Usage | Top 5 apps | **Estimated** badge, always |
| Statistics | Today's summary | Charge/discharge time, screen-on |
| Smart Insights | 2–3 insights | Severity, confidence, suppressed if none qualify |

### Responsive grid

| Width | Columns |
|---|---|
| < 700 px | 1 |
| 700–1099 px | 2 |
| 1100–1599 px | 3 |
| ≥ 1600 px | 4 |

Minimum usable window: **960 × 640**. Below that, content is scrollable rather than
clipped — spec §41 forbids text clipping outright.

Cards reflow, they do not shrink text. A card that becomes illegible at 1366×768
has failed; the grid drops to fewer columns instead.

---

## 4. Card anatomy (spec §40)

```
┌──────────────────────────────────────────┐
│ Battery Health                       ⓘ   │   title + info tooltip
│                                          │
│ 40%                          [Poor]      │   primary metric + status
│ Capacity retention                       │
│                                          │
│ Design      ████████████████████ 95.0 Wh │   secondary metrics
│ Full charge ████████░░░░░░░░░░░░ 38.0 Wh │
│                                          │
│ ▼ 2.1% over 90 days      Calculated      │   trend + grade badge
└──────────────────────────────────────────┘
```

Mandatory elements: title, primary metric, secondary metrics, status, trend where
meaningful, info tooltip, and a grade badge whenever the value is not Measured.

### Grade badges

| Grade | Badge | Style |
|---|---|---|
| Measured | *(none)* | Plain — the default, unremarkable case |
| Calculated | `Calculated` | Subtle outline |
| Estimated | `Estimated` | Filled accent, always visible |
| Unavailable | — | Card body replaced by the unavailable state |

Measured values carry no badge deliberately. If everything is badged, nothing is,
and the Estimated badge is the one that must actually register.

---

## 5. Colour semantics (spec §39)

Never colour alone. Every semantic state pairs a colour with an icon and a text
label.

| State | Icon | Label | Light | Dark |
|---|---|---|---|---|
| Healthy | ✓ | "Excellent" / "Good" | `#0F7B0F` | `#6CCB5F` |
| Warning | ⚠ | "Fair" / "Warning" | `#9D5D00` | `#FCE100` |
| Critical | ✕ | "Poor" / "Critical" | `#C42B1C` | `#FF99A4` |
| Charging | ⚡ | "Charging" | `#005FB8` | `#60CDFF` |
| Discharging | ▼ | "Discharging" | `#616161` | `#C7C7C7` |
| Unavailable | — | "Not available" | `#8A8A8A` | `#8A8A8A` |
| Estimated | ~ | "Estimated" | `#8764B8` | `#B4A0FF` |

All pairs meet WCAG AA (≥4.5:1) against their surface in both themes. Semantic
colours are theme resources, overridable per spec §39.

The red/green pair is the reason the icon and label are mandatory: red-green colour
blindness affects roughly 8 % of men, and a health indicator distinguished only by
hue is unreadable to them.

---

## 6. Charts (spec §40, §56)

Every chart carries: axis labels, units, tooltips, an explicit time range, and
defined empty / no-data / sensor-unavailable states.

| Page | Chart | Type |
|---|---|---|
| Power | Current, voltage, power vs time | Line, three synchronised |
| Temperature | Temperature vs time | Area with threshold band |
| Battery | Health trend | Line with smoothing |
| Sessions | Session timeline | Horizontal segmented bar |
| Statistics | Charge/discharge duration | Stacked bar |
| History | All metrics | Line/area, zoomable |
| App Usage | Top app share | Horizontal bar |

Spec §56 warns against making every card a chart. The Dashboard uses only
sparklines; full charts live on their dedicated pages, where the user has asked for
detail.

---

## 7. Accessibility (spec §42)

- Full keyboard reachability; visible focus on every interactive element.
- `AutomationProperties.Name` on all controls; charts expose a text summary
  alternative, since a screen reader cannot interpret a plot.
- High-contrast theme support via theme resources, no hardcoded brushes.
- Contrast ≥4.5:1 for text, ≥3:1 for UI boundaries, in both themes.
- Transitions respect the system reduced-motion setting.
- Minimum touch target 32×32 effective pixels.

Chart text alternatives are the piece most often skipped. A chart whose only
representation is pixels excludes screen-reader users from the page's entire
content; each chart therefore exposes a summarised series description.

---

## 8. Empty and unavailable states (spec §43)

Both explain *why*, and offer the next step where one exists.

| Situation | Message |
|---|---|
| No battery | "No battery detected. This device appears to be a desktop, so battery monitoring is unavailable." |
| No temperature sensor | "This device does not expose a battery temperature sensor. Battery Intelligence does not substitute CPU temperature, which would not reflect battery conditions." |
| Insufficient history | "Not enough historical data yet. Charging quality comparisons need at least 5 completed charging sessions — you have 2." |
| Per-process energy | "Windows does not expose measured per-application energy without administrator rights. These figures are estimated from CPU, GPU and foreground activity." |
| No cycle count | "This battery's firmware does not report a cycle count. It has been excluded from the health score rather than assumed." |
| Charging (app power) | "Absolute power figures are unavailable while charging, because there is no battery draw to attribute. Applications are ranked by relative activity." |

Each states the limitation, and — where it is a deliberate design choice rather
than a hardware fact — says so. "We could show you CPU temperature but that would
be misleading" builds more trust than a bare "unavailable".

---

## 9. Window behaviour

- Restores last size, position and monitor; validated against current displays so a
  window cannot restore off-screen after a monitor is disconnected.
- Close → minimise to tray when enabled (spec §22), otherwise exit.
- Tray double-click activates; single-click opens the context menu.
- Single instance: a second launch activates the existing window (spec §59).
- Theme follows Light / Dark / System (spec §35), applied without restart.
- Mica backdrop on Windows 11, solid fallback on Windows 10.
