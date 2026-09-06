# Windows API Strategy

Status: Phase 0. Version 1.0.0.

Covers spec §71, §72. The per-metric availability table lives in
`capability-matrix.md`; this document covers **which APIs are used, how they are
reached, and the rules for adding more**.

---

## 1. Selection rules (spec §71)

Before any low-level Windows functionality is implemented:

1. Identify the official Microsoft API.
2. Verify supported Windows versions against our floor (`10.0.17763`).
3. Verify runtime availability — presence at compile time does not imply presence
   at run time.
4. Document limitations, including units and sentinel values.
5. Implement behind an interface declared in `Core`.
6. Provide a fallback.
7. Add tests, including the unavailable path.

Preference order: **WinRT → documented Win32 → WMI → IOCTL**. Undocumented
interfaces, registry spelunking and vendor-specific hacks are out of scope.

The one place this ordering is deliberately inverted is battery *detail*
(cycle count, temperature, manufacture date): WinRT does not expose it at all,
so IOCTL becomes the primary source there rather than a last resort.

---

## 2. API inventory

### WinRT — `Windows.Devices.Power`

```
Battery.FromIdAsync(id) / Battery.AggregateBattery
Battery.GetReport()  →  BatteryReport {
    Status, ChargeRateInMilliwatts,
    DesignCapacityInMilliwattHours,
    FullChargeCapacityInMilliwattHours,
    RemainingCapacityInMilliwattHours }
Battery.ReportUpdated                    (event)
DeviceInformation.FindAllAsync(Battery.GetDeviceSelector())
```

Available since Windows 10 1507; reachable from an unpackaged Windows App SDK app.
Gives clean per-battery and aggregate reports in mWh/mW. **Does not expose
voltage, current, temperature, or cycle count** — which is precisely why the other
sources exist.

`AggregateBattery` is used for the system-level headline figure; individual
`Battery` instances back the per-battery views spec §25 requires.

### Win32 — power status and events

```c
BOOL GetSystemPowerStatus(LPSYSTEM_POWER_STATUS);
    // ACLineStatus, BatteryFlag, BatteryLifePercent,
    // BatteryLifeTime, BatteryFullLifeTime

HPOWERNOTIFY RegisterPowerSettingNotification(HANDLE, LPCGUID, DWORD);
HPOWERNOTIFY RegisterSuspendResumeNotification(HANDLE, DWORD);
BOOL WTSRegisterSessionNotification(HWND, DWORD);
EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE);   // read-only use
```

`BatteryLifeTime` and `BatteryFullLifeTime` return `0xFFFFFFFF` when unknown —
observed on the reference machine (quirk Q3). They are treated as a **last-resort**
source only, never as the runtime estimate (see `estimation-strategy.md` §3).

### Power setting GUIDs (S5)

| GUID | Delivers |
|---|---|
| `GUID_ACDC_POWER_SOURCE` | AC ↔ battery transition |
| `GUID_BATTERY_PERCENTAGE_REMAINING` | Percentage change |
| `GUID_CONSOLE_DISPLAY_STATE` | Screen off / on / dimmed |
| `GUID_MONITOR_POWER_ON` | Legacy display state (Win7 fallback) |
| `GUID_SESSION_DISPLAY_STATUS` | Per-session display state |
| `GUID_LIDSWITCH_STATE_CHANGE` | Lid open / closed |
| `GUID_SYSTEM_AWAYMODE` | Away-mode entry / exit |
| `GUID_POWERSCHEME_PERSONALITY` | Power plan change |

`GUID_CONSOLE_DISPLAY_STATE` is the authoritative screen-state source and requires
Windows 8+. Our floor is 17763, so it is always available; the
`GUID_MONITOR_POWER_ON` fallback is retained only for defensive completeness.

### Window messages

| Message | Meaning |
|---|---|
| `WM_POWERBROADCAST` / `PBT_APMSUSPEND` | Entering sleep |
| `WM_POWERBROADCAST` / `PBT_APMRESUMEAUTOMATIC` | Resumed (may still be locked) |
| `WM_POWERBROADCAST` / `PBT_APMRESUMESUSPEND` | Resumed by user action |
| `WM_POWERBROADCAST` / `PBT_POWERSETTINGCHANGE` | A registered GUID fired |
| `WM_WTSSESSION_CHANGE` / `WTS_SESSION_LOCK` `_UNLOCK` | Lock / unlock |
| `WM_DEVICECHANGE` | Battery arrival / removal |

WinUI 3 does not surface a window procedure directly. A dedicated **message-only
window** (`HWND_MESSAGE`) is created by `BatteryIntelligence.Windows` to own all
registrations, keeping interop out of the UI window and letting event plumbing be
tested without a visible window.

### WMI — `root\wmi` (S3)

| Class | Supplies |
|---|---|
| `BatteryStaticData` | Design capacity, manufacturer, chemistry, serial, device name |
| `BatteryStatus` | Voltage, charge/discharge rate, remaining capacity, flags |
| `BatteryFullChargedCapacity` | Full-charge capacity |
| `BatteryCycleCount` | Cycle count |
| `BatteryTemperature` | Temperature (deci-Kelvin) |
| `BatteryRuntime` | Firmware runtime estimate (sentinel-prone) |
| `BatteryStatusChange` / `BatteryTagChange` | Event classes |

**Each class is probed independently** — quirk Q1 showed `BatteryStaticData`
failing on the modern CIM path while its siblings succeed. A single combined query
would lose all of them to one failure. Where the CIM path fails, the provider
retries via the legacy WMI path before declaring the class unavailable.

### IOCTL — battery device interface (S4)

```c
SetupDiGetClassDevs(&GUID_DEVCLASS_BATTERY, ...)
CreateFile(devicePath, ...)
DeviceIoControl(h, IOCTL_BATTERY_QUERY_TAG,          ...)
DeviceIoControl(h, IOCTL_BATTERY_QUERY_INFORMATION,  ...)  // BatteryInformation
DeviceIoControl(h, IOCTL_BATTERY_QUERY_STATUS,       ...)  // BATTERY_STATUS
```

`BATTERY_INFORMATION.Capabilities` carries `BATTERY_CAPACITY_RELATIVE`, the bit
that resolves quirk Q2 (mA vs mW reporting). `CycleCount`, `Temperature`,
`ManufactureDate` and `UniqueID` come from here.

`UniqueID` is the preferred `BatteryDevice.HardwareId`, since it survives reboots
and distinguishes a replaced battery from the original — which is what keeps
health history attached to the right physical battery.

The battery tag must be re-queried after resume; a stale tag causes subsequent
IOCTLs to fail with `ERROR_FILE_NOT_FOUND`, and re-enumeration on resume
(`session-engine.md` §5) exists partly for this reason.

### Process telemetry (S8)

```
Windows.System.Diagnostics.ProcessDiagnosticInfo.GetForProcesses()
System.Diagnostics.Process.TotalProcessorTime / WorkingSet64
GetForegroundWindow + GetWindowThreadProcessId
QueryFullProcessImageName
```

CPU percent is computed from **cumulative time deltas**, never by sampling in a
loop. See `monitoring-dataflow.md` §5.

---

## 3. APIs deliberately rejected

Recorded so they are not reintroduced by a future contributor who sees an easy win.

| API | Why rejected |
|---|---|
| SRUM / `SRUDB.dat` (Energy Estimation Engine) | Would give **measured** per-process energy, but requires administrator. Spec §23 forbids requiring elevation. Trade resolved in favour of no-admin; per-process energy stays Estimated. |
| `MSAcpi_ThermalZoneTemperature` | Requires admin, and reports **CPU** thermal zones. Spec §14 explicitly forbids substituting CPU temperature for battery temperature. |
| `powercfg /requests` | Requires admin. "Held awake" attribution is reported as Unavailable rather than partially faked. |
| Vendor SDKs (Dell/Lenovo/HP) | Not portable, often undocumented, sometimes require signed drivers. Rejected for v1; `IBatteryProvider` leaves room to add them later. |
| Direct EC / SMBus register reads | Undocumented, model-specific, and capable of destabilising firmware. Out of scope permanently. |
| `Win32_Battery` as a primary source | Mostly null on the reference machine (quirk Q3). Retained only as a last-resort identity fallback. |

---

## 4. Interop conventions

- All interop is confined to `BatteryIntelligence.Windows` and the provider
  projects. Nothing above the provider layer sees a `DllImport`.
- `LibraryImport` source generation (not `DllImport`) throughout, for AOT-friendly,
  allocation-free marshalling.
- Every native handle is wrapped in a `SafeHandle`.
- Every native call's failure is converted to a domain result — providers return
  "unavailable", they do not throw across the boundary for expected conditions.
- Structures are declared with explicit layout and verified sizes.

---

## 5. Runtime capability checks

Compile-time availability is not run-time availability. At startup, and again after
resume, `IBatteryCapabilityDetector`:

1. Enumerates battery devices via all four sources.
2. Attempts one read of each metric.
3. Records which source succeeded, with what grade.
4. Publishes an immutable `CapabilitySnapshot` consumed by the UI and Diagnostics.

Providers then take the fast path for known-good sources and skip known-absent ones
rather than retrying a failing API on every sample — the difference between a
sensor that is absent costing nothing and one costing a failed call every 10
seconds for the life of the process.

Re-detection after resume is mandatory: docking, undocking and hot-swappable
batteries all change the answer.
