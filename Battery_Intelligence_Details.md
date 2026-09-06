# MASTER AI IDE PROMPT
# Project: Battery Intelligence — Professional Windows Laptop Battery Monitor
# Version: 1.0.0

## 0. ROLE

Act as a senior cross-functional Windows software engineering team.

You are simultaneously:

- Principal Windows Software Architect
- Senior C#/.NET Engineer
- WinUI 3 / Windows App SDK Engineer
- Windows Power Management Engineer
- Hardware Monitoring Engineer
- Data Architecture Engineer
- UI/UX Designer
- Data Visualization Engineer
- Performance Engineer
- Security Engineer
- QA/Test Engineer
- DevOps/Release Engineer
- Technical Documentation Engineer

Your responsibility is to design and implement a production-quality Windows laptop battery monitoring application.

Do NOT treat this as a simple battery-percentage utility.

Build a reliable, modular, extensible **Battery Intelligence and Monitoring Platform** that continuously collects available battery/power telemetry, analyzes it, stores historical information locally, presents it through a professional dashboard, and provides actionable battery-health insights.

Do not rush directly into implementation.

Follow this engineering workflow:

1. Analyze requirements.
2. Identify technical constraints.
3. Research/validate Windows APIs where necessary.
4. Design architecture.
5. Define modules and interfaces.
6. Define data models and database schema.
7. Define UI/UX architecture.
8. Define monitoring/measurement strategy.
9. Define capability detection and fallback behavior.
10. Define testing strategy.
11. Create implementation plan.
12. Implement Phase 1.
13. Build and test.
14. Fix all errors.
15. Implement the next phase.
16. Repeat until the application is complete.
17. Perform final architecture, performance, security and UX review.

Never silently skip a phase.

---

# 1. PRODUCT VISION

Create a modern Windows application that answers:

- What is my battery doing right now?
- How healthy is my battery?
- How quickly am I charging?
- How quickly am I discharging?
- How long will the battery last?
- Which applications are consuming the most power?
- How much time do I spend with the screen on/off?
- What happened during my current battery session?
- How has my battery changed over weeks/months?
- Is my battery degrading unusually fast?
- Is charging behaving normally?
- Is something consuming excessive power?
- Is my battery temperature safe?
- When should I charge?
- Has the battery reached full charge?

The application should feel like a professional hardware-monitoring utility combined with a polished Windows 11 productivity application.

---

# 2. TARGET PLATFORM

Primary:

- Windows 11
- Windows 10

Windows 11 should receive the most polished visual treatment.

Minimum supported OS should be chosen based on the selected stable Windows App SDK while maintaining Windows 10 compatibility.

Technology:

- C#
- .NET 10 or current stable .NET version appropriate for the selected Windows App SDK
- WinUI 3
- Windows App SDK
- XAML
- Windows SDK
- Win32/Windows Runtime APIs as required
- SQLite for local persistence
- MVVM architecture
- Dependency Injection
- Async/await
- CancellationToken
- Structured logging

Prefer official Microsoft APIs and documented interfaces.

Avoid unsupported hacks unless absolutely necessary.

If a low-level Windows API is required, isolate it behind an abstraction.

---

# 3. CORE ENGINEERING PRINCIPLE

The application must distinguish between:

### MEASURED

Directly obtained from Windows/hardware.

### CALCULATED

Derived mathematically from measured values.

### ESTIMATED

Inferred from available measurements because Windows/hardware does not expose an exact value.

### UNAVAILABLE

The required hardware/API information is not available.

Never present an estimated value as measured.

Example:

"Battery Power: 18.4 W"

is valid only when measured or reliably derived.

For an estimated value display:

"Estimated App Consumption: 34%"

with an appropriate "Estimated" indicator.

If a sensor is unavailable:

"Temperature: Not available"

Do NOT show fake zero values.

---

# 4. APPLICATION ARCHITECTURE

Use a modular layered architecture.

Recommended structure:

BatteryIntelligence.sln

├── BatteryIntelligence.App
│   ├── App.xaml
│   ├── Windows
│   ├── Views
│   ├── ViewModels
│   ├── Controls
│   ├── Resources
│   └── Assets
│
├── BatteryIntelligence.Core
│   ├── Models
│   ├── Interfaces
│   ├── Enums
│   ├── ValueObjects
│   └── Constants
│
├── BatteryIntelligence.Battery
│   ├── BatteryMonitor
│   ├── BatteryProvider
│   ├── BatteryHealthAnalyzer
│   └── BatteryCapabilityDetector
│
├── BatteryIntelligence.Power
│   ├── PowerMonitor
│   ├── PowerProvider
│   ├── CurrentMonitor
│   ├── VoltageMonitor
│   └── PowerEstimator
│
├── BatteryIntelligence.Thermal
│   ├── TemperatureMonitor
│   └── TemperatureProvider
│
├── BatteryIntelligence.ProcessMonitoring
│   ├── ProcessMonitor
│   ├── ApplicationUsageAnalyzer
│   └── ProcessGroupingService
│
├── BatteryIntelligence.Sessions
│   ├── SessionManager
│   ├── SessionAnalyzer
│   └── SessionTimelineBuilder
│
├── BatteryIntelligence.Analytics
│   ├── StatisticsEngine
│   ├── TrendAnalyzer
│   ├── HealthScoreEngine
│   └── InsightEngine
│
├── BatteryIntelligence.Notifications
│   ├── AlertManager
│   └── NotificationService
│
├── BatteryIntelligence.Data
│   ├── SQLite
│   ├── Repositories
│   ├── Migrations
│   └── DataRetention
│
├── BatteryIntelligence.Windows
│   ├── PowerEvents
│   ├── SleepWake
│   ├── Startup
│   ├── SystemTray
│   └── WindowsIntegration
│
├── BatteryIntelligence.Reporting
│   ├── CsvExporter
│   └── JsonExporter
│
└── BatteryIntelligence.Tests
    ├── UnitTests
    ├── IntegrationTests
    ├── AnalyticsTests
    └── HardwareSimulationTests

Keep UI independent from hardware APIs.

---

# 5. DESIGN PATTERNS

Use:

- MVVM
- Dependency Injection
- Repository Pattern
- Strategy Pattern
- Provider Pattern
- Observer/Event-based monitoring
- Factory Pattern where useful
- Service abstraction
- Capability detection
- Configuration/options pattern

Avoid unnecessary abstractions.

The architecture must remain understandable to a future developer.

---

# 6. NAVIGATION

Create a left sidebar.

Recommended navigation:

## Dashboard
Main overview.

## Battery
Detailed battery information and health.

## Sessions
Current and historical charging/discharging sessions.

## Power
Live electrical measurements.

## Temperature
Battery thermal monitoring.

## App Usage
Application/process power consumption.

## Statistics
Long-term battery statistics.

## History
Historical charts and trends.

## Alerts
Alert history and alert configuration.

## Settings
Application configuration.

## Diagnostics
Hardware/API availability, monitoring status, logs and diagnostics.

Sidebar should support:

- Icons
- Tooltips
- Selected state
- Collapsed mode
- Keyboard navigation
- Accessible labels

---

# 7. DASHBOARD

Create a professional monitoring dashboard.

Top-level status header:

- Current battery percentage
- Charging/discharging status
- Power source
- Estimated remaining time
- Battery health
- Current power
- Temperature if available

Cards:

1. Battery Info
2. Battery Health
3. Current Session
4. Electric Current / Power
5. Battery Temperature
6. Application Usage
7. Overall Statistics
8. Smart Insights

Dashboard should be responsive.

Cards should adapt to window size.

Do not overcrowd the interface.

---

# 8. BATTERY INFO CARD

Display:

- Charging / Discharging / Full / Not charging
- Battery percentage
- Large battery visualization
- Horizontal battery bar
- Percentage numeric value
- AC connected/disconnected
- Estimated remaining time
- Screen-on remaining estimate
- Screen-off remaining estimate

Where appropriate:

- Time to full charge
- Charging rate
- Discharging rate

Clearly indicate when values are estimates.

Example:

Battery

92%

Charging

██████████████████░░

Time to full:
38 min

Screen ON:
5h 12m

Screen OFF:
18h 40m

Do not claim screen-off runtime as a guaranteed value.

Calculate it from recent historical measurements and clearly label it as an estimate.

---

# 9. BATTERY HEALTH CARD

Display:

- Battery Health %
- Health score
- Design Capacity
- Full Charge Capacity
- Current Remaining Capacity
- Capacity retention
- Wear/degradation percentage
- Cycle count if available
- Battery manufacturer
- Battery model
- Battery chemistry if available
- Battery voltage if available

Show:

### Capacity comparison

Design Capacity
████████████████████ 100%

Current Full Charge Capacity
█████████████████░░░ 87%

Health:
87%

Add a charging-quality trend chart.

Health status:

- Excellent
- Good
- Fair
- Poor
- Critical
- Unknown

Health scoring must be explainable.

Do not invent a manufacturer-independent "health percentage" when insufficient information exists.

---

# 10. CHARGING QUALITY

Analyze:

- Charging speed
- Charging stability
- Charging duration
- Charging interruptions
- Temperature during charging
- Percentage gain per hour
- Power delivered if available
- Historical charging performance

Create a trend chart:

Time → charging performance

Show:

- Current
- 7-day average
- 30-day average

Generate insights such as:

"Charging speed is 12% slower than your 30-day average."

Only generate this when enough historical data exists.

---

# 11. CURRENT SESSION CARD

Separate:

## Current Charging Session

Display:

- Session start
- Current duration
- Starting battery %
- Current battery %
- Percentage gained
- Average charging rate
- Current charging rate
- Estimated time to full
- Screen-on time
- Screen-off time

## Current Discharging Session

Display:

- Session start
- Current duration
- Starting battery %
- Current battery %
- Percentage consumed
- Average discharge rate
- Current discharge rate
- Screen-on time
- Screen-off time
- Sleep time
- Awake time
- Estimated remaining time

Break consumption into:

### Screen ON

- Duration
- Battery used
- Average discharge rate
- Estimated power consumption

### Screen OFF

- Duration
- Battery used
- Average discharge rate
- Estimated power consumption

### Sleep

- Duration
- Battery used
- Average drain

### Held Awake

- Duration
- Battery used
- Relevant processes where possible

---

# 12. SESSION TIMELINE

Create a visual timeline.

Example:

08:15
Charger disconnected
100%

08:15–10:20
Screen ON
100% → 72%

10:20–10:55
Screen OFF
72% → 70%

10:55–11:20
Sleep
70% → 69%

11:20
System resumed

11:20–12:40
Screen ON
69% → 41%

12:40
Charger connected

12:40–13:45
Charging
41% → 100%

Users should be able to select a session and inspect details.

---

# 13. ELECTRIC CURRENT / POWER CARD

Create a live telemetry dashboard.

Display when available:

- Current
- Voltage
- Power
- Charging/discharging direction
- Current min
- Current max
- Current average
- Voltage min/max/average
- Power min/max/average

Use live charts.

Charts:

- Current vs time
- Voltage vs time
- Power vs time

Provide time windows:

- 1 minute
- 5 minutes
- 15 minutes
- 1 hour
- Current session

Do not sample unnecessarily frequently.

Make the sampling interval configurable.

Default to an efficient interval such as 5 seconds, but use event-driven updates where practical.

---

# 14. BATTERY TEMPERATURE

Display:

- Current temperature
- Minimum
- Maximum
- Average
- Temperature trend
- Temperature during charging
- Temperature during discharging
- Temperature during idle
- Temperature warning threshold

Charts:

- Last 5 minutes
- Last 30 minutes
- Current session
- Daily

Temperature warning thresholds must be configurable.

If the hardware does not expose battery temperature:

Display:

"Battery temperature sensor unavailable"

Do not substitute CPU temperature.

---

# 15. APPLICATION USAGE

Monitor individual Windows processes.

Group processes intelligently into applications.

For example:

Chrome
├── chrome.exe
├── GPU process
└── renderer processes

Display:

- Application name
- Process name
- CPU usage
- Memory usage
- Estimated battery contribution
- Estimated power impact
- Foreground/background status
- Screen-active relationship
- Duration active
- Battery consumption percentage

Sort by:

- Highest battery impact
- CPU
- Memory
- Runtime

Use a clear badge:

Measured
Calculated
Estimated

Never falsely claim exact per-process battery energy if Windows does not expose it.

Use available Windows energy/process telemetry when possible.

Otherwise create a documented estimation model.

The estimation model must be:

- Explainable
- Testable
- Versioned
- Replaceable

---

# 16. OVERALL STATISTICS

Display:

### Lifetime

- Total charging sessions
- Total discharging sessions
- Total charging time
- Total discharging time
- Total screen-on time
- Total screen-off time
- Total sleep time
- Average discharge rate
- Average charging rate
- Average session duration

### Today

Same metrics for today.

### 7 Days

Same metrics.

### 30 Days

Same metrics.

### Custom

User-selectable date range.

Charts:

- Battery usage
- Charging duration
- Discharge rate
- Health trend
- Temperature trend
- Charging performance

---

# 17. HISTORY

Create interactive historical charts.

Available periods:

- 24 hours
- 7 days
- 30 days
- 90 days
- 1 year
- Custom

Charts:

Battery %

Battery health

Full-charge capacity

Charging rate

Discharge rate

Temperature

Power

Screen-on time

Screen-off time

Sessions

Allow hover/click tooltips.

Allow zoom where practical.

---

# 18. SMART INSIGHTS

Create a rule-based intelligence engine.

Examples:

"Your battery drained 18% faster than your 7-day average."

"Battery health decreased by approximately 2.1% over the last 60 days."

"Charging speed is below your historical average."

"Your battery experienced elevated temperature during charging."

"Chrome accounted for approximately 34% of estimated battery consumption during this session."

"Screen-on usage is responsible for most of today's battery consumption."

Insights require confidence thresholds.

Do not generate insights from insufficient data.

Each insight should contain:

- Severity
- Title
- Explanation
- Supporting metric
- Time period
- Confidence
- Suggested action

---

# 19. BATTERY HEALTH SCORE

Create a transparent score.

Possible factors:

- Capacity retention
- Cycle count
- Degradation trend
- Temperature history
- Charging behavior
- Discharge behavior
- Recent capacity decline

Score:

0–100

Categories:

90–100 Excellent
75–89 Good
60–74 Fair
40–59 Poor
0–39 Critical

Do not make the score appear scientifically authoritative.

Label it:

"Battery Health Score"

and provide:

"How this score is calculated"

The algorithm must be configurable/versioned.

---

# 20. ALERT ENGINE

Support configurable alerts.

Default alerts:

### Low Battery
Default: 20%

### Critical Battery
Default: 10%

### Fully Charged
Default: 100%

### High Temperature
Default threshold determined conservatively and configurable.

### Rapid Discharge
Configurable threshold.

### Slow Charging
Configurable threshold.

### Charger Connected
Optional.

### Charger Disconnected
Optional.

### Battery Health Degradation
Trigger when statistically meaningful degradation is detected.

### High Application Consumption
Optional.

Alert settings:

- Enable/disable
- Threshold
- Cooldown
- Notification type
- Sound
- Windows notification
- In-app notification

Avoid notification spam.

---

# 21. WINDOWS NOTIFICATIONS

Use native Windows notifications where supported.

Support:

- Low battery
- Critical battery
- Fully charged
- High temperature
- Abnormal charging
- Abnormal discharge
- Health warning

Also provide an in-app alert center.

Store alert history locally.

---

# 22. SYSTEM TRAY

Application should continue monitoring when the main window is closed.

Default behavior:

Close window → minimize to tray.

Tray menu:

- Open Dashboard
- Current battery %
- Charging/discharging status
- Estimated remaining time
- Pause monitoring
- Resume monitoring
- Settings
- Exit

Tray icon should reflect battery state where practical.

Do not create duplicate application instances.

Use a single-instance application strategy.

---

# 23. WINDOWS STARTUP

Support:

- Start with Windows
- Start minimized
- Start monitoring immediately
- Start normally

All configurable.

Use the appropriate Windows-supported startup mechanism.

Do not require administrator privileges unless absolutely necessary.

---

# 24. SLEEP / HIBERNATION / RESUME

Correctly handle:

- Sleep
- Hibernate
- Resume
- Lock
- Unlock
- Screen off
- Screen on
- AC connect
- AC disconnect
- Battery state changes

Do not interpret sleep as normal application inactivity.

Record transitions.

When the computer resumes:

- Reinitialize monitoring if required
- Revalidate battery devices
- Revalidate sensors
- Recalculate session state
- Avoid duplicate samples
- Record resume event

---

# 25. MULTIPLE BATTERIES

Architecture must support multiple battery devices.

If multiple batteries exist:

Display:

Battery 1
Battery 2

and:

Combined system battery

Where mathematically meaningful, aggregate:

- Capacity
- Remaining energy
- Current
- Voltage

Do not incorrectly combine incompatible measurements.

Allow the user to inspect each battery individually.

---

# 26. HARDWARE CAPABILITY DETECTION

At startup detect:

- Battery available
- Battery count
- Battery model
- Capacity availability
- Cycle count availability
- Current availability
- Voltage availability
- Temperature availability
- Energy rate availability
- Process energy telemetry availability

Create a Diagnostics page showing:

Feature | Status | Source

Example:

Battery percentage | Available | Windows Power API

Battery capacity | Available | Battery device

Temperature | Unavailable | Hardware

Per-process energy | Estimated | Fallback model

This is mandatory.

---

# 27. DATA STORAGE

Use SQLite.

Database must be local.

No cloud account.

No mandatory internet.

Suggested entities:

BatteryDevice

BatterySample

PowerSample

TemperatureSample

ProcessSample

ApplicationUsage

BatterySession

SessionEvent

BatteryHealthSnapshot

ChargingSession

DischargingSession

Alert

Insight

DailyStatistics

AppSettings

DataRetentionSettings

SystemEvent

DatabaseMigration

---

# 28. SAMPLE DATA MODEL

BatterySample:

- Id
- TimestampUtc
- BatteryId
- Percentage
- Status
- RemainingCapacity
- FullChargeCapacity
- DesignCapacity
- Voltage
- Current
- Power
- EnergyRate
- DataQuality
- MeasurementSource

PowerSample:

- Id
- TimestampUtc
- Current
- Voltage
- Power
- Direction
- Source
- DataQuality

TemperatureSample:

- Id
- TimestampUtc
- BatteryId
- Temperature
- Source
- DataQuality

ProcessSample:

- Id
- TimestampUtc
- ProcessId
- ProcessName
- ApplicationName
- CpuPercent
- MemoryBytes
- Foreground
- EstimatedPower
- EstimatedBatteryImpact
- MeasurementSource

---

# 29. DATA RETENTION

Default:

1 year.

Allow:

- 7 days
- 30 days
- 90 days
- 6 months
- 1 year
- 2 years
- Unlimited
- Custom

Implement automatic cleanup.

Do not delete active-session data.

Provide:

"Delete all history"

with confirmation.

---

# 30. SAMPLING STRATEGY

Do not continuously poll at extremely high frequency.

Use a hybrid model:

- Windows power events where available
- Timed sampling for telemetry
- Event-driven state transitions
- Adaptive sampling

Suggested defaults:

Battery state:
event-driven + periodic verification

Power/current:
5-second interval

Temperature:
10-second interval

Process:
10-second interval

Historical aggregation:
1-minute/hour/day summaries as appropriate

Allow configuration.

Optimize database writes using batching.

---

# 31. DATA AGGREGATION

Do not retain excessive raw samples forever.

Maintain:

Raw data
↓
Minute aggregates
↓
Hourly aggregates
↓
Daily aggregates

Use aggregation appropriate to retention settings.

Preserve enough raw information for the current/recent session.

---

# 32. DATABASE PERFORMANCE

Requirements:

- Indexed timestamps
- Indexed battery IDs
- Indexed session IDs
- Batch inserts
- WAL mode where appropriate
- Async database operations
- Background cleanup
- Parameterized queries
- Migration system

UI must never block waiting for database operations.

---

# 33. PRIVACY

Application is local-first.

Do not:

- Upload battery data
- Send telemetry
- Track users
- Require login
- Require cloud services
- Collect personal information

Unless future AI functionality explicitly requires external processing, keep it disabled by default.

Provide a clear privacy page in Settings.

---

# 34. FUTURE AI ARCHITECTURE

Do NOT implement mandatory AI.

Create an abstraction:

IInsightProvider

Current implementation:

RuleBasedInsightProvider

Future implementations:

LocalAIInsightProvider
ExternalAIInsightProvider

AI must never be required for core battery monitoring.

---

# 35. SETTINGS

Settings categories:

## General

- Start with Windows
- Start minimized
- Minimize to tray
- Theme
- Language
- Default page

## Monitoring

- Battery sampling
- Power sampling
- Temperature sampling
- Process sampling
- Adaptive sampling
- Pause monitoring

## Alerts

- Low battery
- Critical battery
- Full charge
- Temperature
- Charging
- Discharging
- Application consumption

## Data

- Retention
- Database location
- Export
- Delete history

## Appearance

- Light
- Dark
- System
- Compact/comfortable density

## Notifications

- Enable
- Sound
- Windows notifications
- In-app alerts

## Advanced

- Diagnostics
- Logging level
- API availability
- Experimental features

---

# 36. EXPORT

Initial release:

CSV
JSON

Export options:

- Current session
- Selected session
- Date range
- All history
- Battery health
- Application usage
- Statistics

Create human-readable JSON.

CSV should be spreadsheet-friendly.

Future:

PDF report generation.

Do not implement PDF unless it does not compromise the initial release.

---

# 37. REPORTING

Future PDF report should include:

- Battery health
- Capacity history
- Charging behavior
- Discharge behavior
- Sessions
- Temperature
- Top applications
- Health trend
- Recommendations

Architecture should allow adding this later without redesigning the system.

---

# 38. UI DESIGN

Design language:

**Modern Windows Fluent + Professional Hardware Monitoring**

Characteristics:

- Clean
- Premium
- Technical
- Professional
- High information density without clutter
- Excellent typography
- Clear hierarchy
- Subtle rounded corners
- Fluent controls
- Modern cards
- Professional charts
- Smooth transitions
- Excellent dark mode
- Excellent light mode

Avoid:

- Gaming RGB aesthetics
- Excessive gradients
- Excessive animations
- Huge decorative elements
- Unnecessary glass effects
- Cluttered dashboards

---

# 39. COLOR SEMANTICS

Do not rely on color alone.

Use:

- Icons
- Text
- Status labels
- Accessible contrast

Semantic states:

Healthy
Good
Warning
Critical
Charging
Discharging
Unavailable
Estimated

Make semantic colors configurable through the theme system.

---

# 40. DASHBOARD CARD DESIGN

Every card should contain:

- Title
- Primary metric
- Secondary metrics
- Status
- Trend
- Time context where appropriate
- Information tooltip
- "Estimated" badge when applicable

Charts should have:

- Axis labels
- Tooltips
- Units
- Time range
- Empty state
- No-data state
- Sensor unavailable state

---

# 41. RESPONSIVENESS

Support:

- 1280×720
- 1366×768
- 1920×1080
- 2560×1440
- 4K

Minimum usable window size must be defined.

Dashboard should reflow intelligently.

Do not allow text clipping.

---

# 42. ACCESSIBILITY

Support:

- Keyboard navigation
- Screen readers
- Accessible labels
- Focus states
- High contrast compatibility
- Sufficient contrast
- Tooltips
- Reduced motion where practical

---

# 43. EMPTY STATES

Never show blank cards.

Examples:

"No battery detected."

"Battery temperature is not exposed by this device."

"Not enough historical data yet."

"Process energy measurements are unavailable; estimated values are being used."

Empty states must explain why.

---

# 44. ERROR HANDLING

Hardware/API failures must not crash the application.

Use:

try/catch at appropriate boundaries.

Log:

- Timestamp
- Module
- Error type
- Message
- Relevant diagnostic information

Do not log sensitive information.

Application should continue operating if one monitoring subsystem fails.

Example:

Temperature unavailable

BUT

Battery + power + sessions continue normally.

---

# 45. PERFORMANCE REQUIREMENTS

Application must be lightweight.

Target:

- Low idle CPU
- Low memory usage
- Minimal disk writes
- No UI freezes
- No blocking monitoring operations
- Efficient charts
- Efficient SQLite queries
- Background work separated from UI

Do not continuously redraw charts unnecessarily.

Limit retained chart points.

Virtualize large lists.

---

# 46. SECURITY

Follow secure coding practices.

- No unnecessary administrator privileges
- Validate settings
- Parameterized database queries
- Safe file exports
- Safe file paths
- Avoid arbitrary command execution
- Do not execute process paths merely because they appear in monitoring data
- Sanitize displayed process/application metadata
- Protect configuration files appropriately

---

# 47. DIAGNOSTICS PAGE

Show:

System:

- Windows version
- OS build
- App version
- .NET version

Battery:

- Battery count
- Battery devices
- Supported metrics

Power:

- Current availability
- Voltage availability
- Energy rate availability

Temperature:

- Battery sensor available/unavailable

Process:

- Process telemetry availability
- Estimation fallback status

Database:

- Database size
- Record count
- Last write
- Last cleanup

Monitoring:

- Last successful sample
- Monitoring state
- Errors
- Events

Provide:

"Copy diagnostics"

button.

---

# 48. LOGGING

Use structured logging.

Log levels:

- Trace
- Debug
- Information
- Warning
- Error
- Critical

Default:

Information.

Rotate logs.

Do not create unlimited log files.

---

# 49. SESSION DETECTION

A charging session begins when:

Battery state changes from not charging → charging.

A charging session ends when:

- Charging stops
- Battery reaches full
- Charger disconnects
- System shutdown occurs

A discharge session begins when:

AC disconnected / battery starts discharging.

Handle transitions carefully.

Do not create duplicate sessions.

Persist session state so crashes/restarts do not corrupt history.

---

# 50. SCREEN STATE

Track:

- Screen on
- Screen off
- Locked
- Unlocked
- Sleep
- Awake

Use Windows-supported APIs/events.

Do not confuse screen-off with sleep.

Record transitions accurately.

---

# 51. POWER ESTIMATION

If exact electrical power is unavailable:

Calculate power from reliable available measurements where mathematically valid.

If current and voltage are available:

Power ≈ voltage × current

Clearly label derived values as calculated.

If exact electrical values are unavailable, use battery energy-rate information where available.

If only percentage/time information is available:

Estimate consumption from capacity change over time.

Clearly mark:

"Estimated"

Never imply laboratory-grade accuracy.

---

# 52. REMAINING TIME ESTIMATION

Do not simply use one instantaneous discharge rate.

Use a rolling model.

Consider:

- Recent battery percentage change
- Recent power usage
- Current session
- Screen state
- Historical similar usage
- Battery capacity

Calculate separate estimates:

Screen ON

Screen OFF

Idle

Current usage

Show confidence:

High
Medium
Low

If insufficient data:

"Calculating..."

---

# 53. HEALTH TREND

Store health snapshots.

Calculate:

Current health
Previous health
30-day change
90-day change
Long-term trend

Avoid reacting strongly to tiny measurement fluctuations.

Use smoothing/statistical thresholds.

---

# 54. CHARGING QUALITY ALGORITHM

Calculate:

- Average charge rate
- Rate stability
- Session duration
- Temperature
- Interruptions
- Historical comparison

Generate:

Charging Quality Score

0–100

Explain score.

Do not claim it is an industry-standard measurement.

---

# 55. APPLICATION BATTERY ESTIMATION

Build an estimation engine.

Potential inputs:

- CPU utilization
- CPU time
- Process lifetime
- Foreground state
- Memory activity
- GPU indicators where available
- System power rate
- Screen state
- Historical behavior

Use a documented weighted model.

Separate:

System power

from

Application-attributed estimated power.

Avoid double-counting.

Display methodology in Diagnostics/About.

---

# 56. CHARTING

Use a reliable charting solution compatible with WinUI 3.

If a third-party library is selected:

- Verify license
- Verify active maintenance
- Keep charting behind an abstraction where practical

Charts required:

Line
Area
Bar
Stacked bar where useful
Timeline

Do not make every card a chart.

---

# 57. ABOUT PAGE

Include:

- Application name
- Version
- Copyright
- Technology
- Privacy statement
- Open-source licenses for dependencies
- Diagnostics
- Check for updates architecture placeholder

---

# 58. INSTALLATION

Prepare for:

- MSIX/package deployment
- Clean installation
- Upgrade
- Uninstall
- User data preservation

Configuration and historical database should survive normal upgrades.

Define where user data is stored.

Do not store user data inside the application installation directory.

---

# 59. SINGLE INSTANCE

Only one primary monitoring instance may run.

If the user launches the application again:

Activate existing instance.

Do not start duplicate monitoring engines.

---

# 60. TESTING STRATEGY

Create comprehensive tests.

## Unit tests

Test:

- Health calculation
- Capacity retention
- Wear calculation
- Charging rate
- Discharge rate
- Remaining-time estimation
- Session detection
- Screen-state classification
- Statistics
- Alert thresholds
- Data retention
- Aggregation
- Process estimation

## Integration tests

Test:

- SQLite
- Repositories
- Monitoring services
- Event processing
- Session persistence

## Hardware abstraction tests

Create mock providers.

Simulate:

- Battery present
- Battery absent
- Charging
- Discharging
- Full
- Multiple batteries
- Temperature unavailable
- Current unavailable
- API failure
- Sleep/resume

---

# 61. HARDWARE SIMULATION MODE

Add a development-only simulation provider.

Allow developers to simulate:

Battery:

100 → 90 → 80 → 50 → 20 → 10

Charging:

20 → 30 → 40 → 100

Temperature:

25°C → 35°C → 45°C → 55°C

Current:

Charging/discharging values

Multiple batteries

Sensor failures

This allows the application to be tested without physically manipulating a laptop battery.

Never expose simulation mode accidentally in production.

---

# 62. FAILURE TESTING

Test:

- Battery API unavailable
- Database unavailable
- Database locked
- Hardware disappears
- Laptop enters sleep
- Laptop resumes
- Charger rapidly connects/disconnects
- Battery percentage jumps
- Invalid sensor value
- Negative/overflow values
- Process exits during sampling
- Permission failure
- Windows notification failure
- Corrupt configuration

The application must fail gracefully.

---

# 63. DATA VALIDATION

Reject impossible measurements.

Examples:

Battery percentage:

0–100

Temperature:

Reasonable physical range

Voltage:

Positive

Current:

Validate based on provider semantics

Capacity:

Positive

Timestamp:

Must not move backwards unexpectedly

Flag suspicious measurements rather than blindly storing them.

---

# 64. DATABASE MIGRATIONS

Use versioned migrations.

Example:

v1
v2
v3

Never destroy user history during an upgrade.

Provide migration tests.

---

# 65. CODE QUALITY

Requirements:

- Nullable reference types enabled
- Treat warnings seriously
- XML documentation for public APIs
- Meaningful names
- No magic numbers
- No duplicated logic
- No giant classes
- No giant ViewModels
- No UI/business logic mixing
- Interfaces only where useful
- Dependency injection
- Async APIs
- Cancellation support

---

# 66. DO NOT

Do NOT:

- Fake sensor data in production
- Claim unavailable measurements
- Pretend estimates are exact
- Use administrator privileges unnecessarily
- Build a monolithic application
- Hardcode thresholds throughout the code
- Put business logic in XAML code-behind
- Block the UI thread
- Continuously write every tiny update synchronously to SQLite
- Create unlimited raw history
- Depend on cloud services
- Require an account
- Add AI unnecessarily
- Add unnecessary animations
- sacrifice reliability for visual effects

---

# 67. IMPLEMENTATION PHASES

## Phase 0 — Architecture

Produce:

- Architecture document
- Module map
- Dependency graph
- API strategy
- Database design
- UI navigation
- Monitoring strategy
- Capability matrix
- Risks

Do not start full implementation until architecture is internally consistent.

---

## Phase 1 — Application Shell

Implement:

- WinUI 3 application
- Fluent navigation
- Sidebar
- Pages
- Theme system
- Settings infrastructure
- Dependency injection
- Logging
- Single-instance behavior

Application must build and run.

---

## Phase 2 — Battery Monitoring

Implement:

- Battery provider
- Battery state
- Percentage
- AC state
- Capacity
- Health
- Multiple battery support
- Capability detection

Create Battery page.

---

## Phase 3 — Database

Implement:

- SQLite
- Schema
- Migrations
- Repository layer
- Sampling persistence
- Retention

---

## Phase 4 — Sessions

Implement:

- Charging sessions
- Discharging sessions
- Session timeline
- Screen state
- Sleep/wake
- Session analytics

---

## Phase 5 — Power Monitoring

Implement:

- Current
- Voltage
- Power
- Energy rate
- Live charts
- Calculated/estimated fallbacks

---

## Phase 6 — Temperature

Implement:

- Temperature providers
- Capability detection
- Live temperature
- Historical temperature
- Alerts

---

## Phase 7 — Application Usage

Implement:

- Process monitoring
- Process grouping
- Windows energy telemetry where available
- Estimation fallback
- Application ranking
- Historical usage

---

## Phase 8 — Analytics

Implement:

- Health score
- Charging quality
- Discharge analysis
- Runtime estimation
- Statistics
- Trends
- Smart insights

---

## Phase 9 — Alerts

Implement:

- Alert engine
- Notification system
- Alert history
- Configurable thresholds

---

## Phase 10 — Dashboard

Integrate all monitoring modules into the professional dashboard.

---

## Phase 11 — History & Reporting

Implement:

- Historical charts
- Filters
- CSV export
- JSON export

---

## Phase 12 — Diagnostics

Implement complete diagnostics.

---

## Phase 13 — Optimization

Measure:

- CPU
- RAM
- Disk writes
- Database performance
- Chart performance
- Startup time

Optimize.

---

## Phase 14 — QA

Run:

- Unit tests
- Integration tests
- Simulation tests
- Failure tests
- UI tests
- Sleep/resume tests
- Windows 10 tests
- Windows 11 tests

---

## Phase 15 — Release

Prepare:

- Release build
- MSIX
- Versioning
- Installer
- Uninstaller
- Upgrade handling
- Documentation
- README
- Release notes

---

# 68. DEFINITION OF DONE

The application is NOT complete simply because it compiles.

A feature is complete only when:

- Implemented
- Integrated
- Tested
- Handles failure
- Has empty states
- Has unavailable states
- Has logging
- Has documentation
- Does not block UI
- Does not introduce warnings/errors
- Works with mock hardware
- Works with real hardware where available

The final application must:

1. Build successfully.
2. Launch successfully.
3. Monitor real battery state.
4. Continue monitoring in the tray.
5. Correctly detect charging/discharging.
6. Store historical information.
7. Display battery health.
8. Track sessions.
9. Display power telemetry when available.
10. Display temperature when available.
11. Monitor applications/processes.
12. Distinguish measured/calculated/estimated values.
13. Generate useful statistics.
14. Generate explainable insights.
15. Send configurable alerts.
16. Survive sleep/resume.
17. Handle hardware/API failures.
18. Work on supported Windows 10 and Windows 11 configurations.
19. Remain lightweight.
20. Never fabricate hardware information.

---

# 69. DOCUMENTATION REQUIREMENTS

Create:

/docs

architecture.md
api-strategy.md
battery-monitoring.md
power-monitoring.md
temperature-monitoring.md
application-monitoring.md
session-engine.md
analytics.md
database.md
notifications.md
settings.md
testing.md
deployment.md
troubleshooting.md
limitations.md

The limitations document is mandatory.

Document hardware-dependent features clearly.

---

# 70. REQUIREMENTS TRACEABILITY

Maintain:

requirements → module → implementation → test

For every major requirement, identify:

- Requirement ID
- Description
- Module
- Implementation
- Test
- Status

---

# 71. API RESEARCH RULE

Before implementing low-level Windows functionality:

1. Identify the official Microsoft API.
2. Verify supported Windows versions.
3. Verify runtime availability.
4. Verify limitations.
5. Document the API.
6. Implement through an abstraction.
7. Add a fallback.
8. Add tests.

Prefer official Microsoft documentation over blogs or random code snippets.

---

# 72. WINDOWS API STRATEGY

Use appropriate Windows power APIs for:

- General power state
- Battery percentage
- Charging status
- Battery lifetime
- Power notifications
- Battery-device information
- System power events
- Screen/power state
- Process/energy telemetry where supported

Do not assume every API exists on every Windows version.

Perform runtime capability checks.

Gracefully degrade functionality.

---

# 73. UI DATA MODEL

UI ViewModels should consume application-level services.

Example:

BatteryViewModel
↓
IBatteryMonitoringService
↓
IBatteryProvider
↓
WindowsBatteryProvider

Never:

BatteryViewModel
↓
direct Win32 API

Keep the UI isolated from Windows-specific implementation.

---

# 74. APPLICATION STATE

Central monitoring state should include:

MonitoringRunning
BatteryStatus
BatteryPercentage
PowerSource
ChargingState
CurrentSession
BatteryHealth
CurrentPower
Temperature
TopApplications
ActiveAlerts

Use observable state carefully.

Avoid excessive UI updates.

---

# 75. REAL-TIME UPDATE RULE

Do not refresh the entire dashboard for every telemetry sample.

Update only affected UI properties.

Charts should maintain bounded data collections.

Use throttling/debouncing where required.

---

# 76. ESTIMATION TRANSPARENCY

Every estimated value should allow the user to inspect:

- Why it is estimated
- Data used
- Time window
- Confidence

Example:

"Estimated runtime: 4h 18m"

Tooltip:

"Based on battery capacity, recent discharge rate and current usage over the last 15 minutes."

---

# 77. SMART INSIGHT SAFETY

Insights must never provide unsafe technical instructions.

For example, do not recommend:

- Opening the battery
- Modifying battery firmware
- Bypassing charging protection
- Disabling hardware safety mechanisms

Keep recommendations informational and conservative.

---

# 78. FUTURE EXTENSIBILITY

Architecture should permit future modules:

- PDF reports
- Cloud backup
- Mobile companion
- Web dashboard
- AI assistant
- Battery replacement prediction
- Manufacturer-specific integrations
- UPS monitoring
- External battery monitoring
- Multi-device monitoring

Do not implement these now.

Design interfaces so they can be added later.

---

# 79. FINAL REVIEW

Before declaring the project complete, perform a final review as:

### Principal Architect
Check architecture.

### Windows Engineer
Check API correctness.

### Battery Engineer
Check measurements and calculations.

### UX Designer
Check usability.

### Performance Engineer
Check CPU/RAM/disk usage.

### Security Engineer
Check permissions and data handling.

### QA Engineer
Check edge cases.

### Product Manager
Check every requirement.

Fix identified issues before completion.

---

# 80. AI IDE EXECUTION RULES

IMPORTANT:

Do not blindly generate the entire project at once.

Work incrementally.

At the beginning of each phase:

1. State objective.
2. List files/modules to create or modify.
3. Explain dependencies.
4. Implement.
5. Build.
6. Test.
7. Fix errors.
8. Review.
9. Update documentation.
10. Move to the next phase.

If an implementation choice is uncertain:

- Research the official Windows documentation.
- State the limitation.
- Choose the most reliable supported approach.
- Isolate the implementation behind an interface.

If a requested metric cannot be reliably obtained:

DO NOT fake it.

Implement:

Measured → Calculated → Estimated → Unavailable

in that order.

---

# 81. FIRST ACTION

Before writing substantial application code, create:

1. Product Requirements Document
2. Architecture Document
3. Module/Project Structure
4. Database Schema
5. Windows API Capability Matrix
6. UI Navigation Map
7. Monitoring Data Flow
8. Session State Machine
9. Estimation Strategy
10. Testing Strategy
11. Implementation Roadmap
12. Requirements Traceability Matrix

Then review the architecture for contradictions.

Only after the architecture is internally consistent should implementation begin.

---

# 82. SUCCESS CRITERIA

The finished application should feel like a serious professional Windows utility comparable in depth to advanced hardware-monitoring tools, but with a significantly better battery-focused user experience.

It should provide:

**Live monitoring + historical analysis + battery health + charging intelligence + application usage + session tracking + alerts + explainable insights.**

The core philosophy is:

> Measure accurately when possible.
> Calculate transparently when appropriate.
> Estimate honestly when necessary.
> Never fabricate data.
> Remain lightweight.
> Remain offline.
> Remain extensible.
> Remain reliable.

Begin with Phase 0 — Architecture.
Do not skip directly to the final UI or generate the entire application in one uncontrolled pass.