# Database Design

Status: Phase 3 — implemented and verified live. Schema version 1.

**Phase 3 confirmation (2026-09-05):** `V001__InitialSchema.sql` transcribes
section 4 below verbatim; all eighteen tables and every index were verified to
exist after migration (`MigrationTests`), and the pragmas in section 5 were
verified applied on every connection. The batching design in section 1, point 4
and the retention/rollup rules in section 6 are implemented by
`BatterySampleWriteQueue` and `DatabaseMaintenanceService` respectively — see
`docs/roadmap.md` Phase 3 for what shipped versus what remains deferred (hour/
daily rollup have no producer yet; only the minute tier is live).

**Phase 5 confirmation (2026-09-05):** the `PowerSample` table now has a producer
— `PowerSampleWriteQueue` (same batching design as `BatterySampleWriteQueue`),
fed by `PowerMonitoringService` on the 5 s power cadence. `PowerSample` is
**raw-only**: it backs the Power page's live charts, has no rollup tier (section
6's `SampleMinute` already carries power aggregated from `BatterySample`) and no
session link, so `DatabaseMaintenanceService` retention drops it on a plain age
cutoff at `RawRetentionDays`.

Engine: SQLite via `Microsoft.Data.Sqlite`. Location:
`%LocalAppData%\BatteryIntelligence\battery.db`.

---

## 1. Principles

1. **Time is UTC, always.** Every timestamp column stores Unix milliseconds UTC as
   `INTEGER`. Local time is a presentation concern. Storing local time would make
   history unreadable across a DST boundary or a timezone change.
2. **Every sample carries provenance.** `DataQuality` and `MeasurementSource` are
   not optional columns. A row that cannot say where it came from cannot be trusted
   later (spec §3).
3. **Raw data is transient; aggregates are durable.** Tiered rollup (§6) keeps the
   database bounded without discarding the shape of history.
4. **The writer is single and batched.** No sample is written synchronously
   (spec §66).
5. **Absent is `NULL`, never `0`.** A missing temperature is `NULL`. Writing `0 °C`
   for an absent sensor would be fabrication.

Point 5 is the schema-level enforcement of the core principle. Nullable columns are
used deliberately and widely; a `NOT NULL DEFAULT 0` on a sensor column would be a
bug, not a convenience.

---

## 2. Storage conventions

| Concept | Storage | Note |
|---|---|---|
| Timestamp | `INTEGER` Unix ms UTC | Sortable, indexable, timezone-proof |
| Energy | `INTEGER` mWh | ACPI native unit; no float drift |
| Power / rate | `INTEGER` mW, signed | Positive = charging, negative = discharging |
| Voltage | `INTEGER` mV | ACPI native unit |
| Current | `INTEGER` mA, signed | Calculated; sign follows power |
| Temperature | `INTEGER` deci-Kelvin | ACPI native unit; converted for display only |
| Percentage | `REAL` 0–100 | |
| Enum | `INTEGER` | Values pinned in `Core.Enums`, never renumbered |
| Boolean | `INTEGER` 0/1 | |

Storing in the hardware's native units and converting only at the display boundary
avoids accumulating rounding error across millions of samples, and makes a stored
row directly comparable to what the firmware reported.

### DataQuality

```
0 Unknown    1 Measured    2 Calculated    3 Estimated    4 Suspect
```

`Suspect` (4) is for readings that passed structural validation but failed a
plausibility check — stored, flagged, and excluded from aggregates and insights
(spec §63: "Flag suspicious measurements rather than blindly storing them").

---

## 3. Entity overview

Eighteen tables, matching spec §27.

```
BatteryDevice ──┬── BatterySample ──── (rolls up to) SampleMinute / SampleHour / DailyStatistics
                ├── TemperatureSample
                └── BatteryHealthSnapshot

BatterySession ─┬── SessionEvent
                └── (referenced by) BatterySample, ProcessSample

PowerSample                    ProcessSample ──── ApplicationUsage
SystemEvent                    Alert            Insight
AppSettings                    DataRetentionSettings          SchemaMigration
```

`ChargingSession` and `DischargingSession` from spec §27 are modelled as **one
`BatterySession` table discriminated by `SessionType`**, rather than two tables.
They share every column except interpretation, the state machine transitions
directly between them, and the timeline query (spec §12) needs them interleaved in
one ordered result. Two tables would force a `UNION` on the hottest query in the
application for no benefit.

---

## 4. Schema (v1)

### Identity and configuration

```sql
CREATE TABLE SchemaMigration (
    Version      INTEGER PRIMARY KEY,
    Name         TEXT    NOT NULL,
    AppliedUtc   INTEGER NOT NULL
);

CREATE TABLE AppSettings (
    Key          TEXT    PRIMARY KEY,
    Value        TEXT    NOT NULL,
    UpdatedUtc   INTEGER NOT NULL
);

CREATE TABLE DataRetentionSettings (
    Id                  INTEGER PRIMARY KEY CHECK (Id = 1),
    RawRetentionDays    INTEGER NOT NULL DEFAULT 7,
    MinuteRetentionDays INTEGER NOT NULL DEFAULT 90,
    HourRetentionDays   INTEGER NOT NULL DEFAULT 365,
    DailyRetentionDays  INTEGER NOT NULL DEFAULT 0,   -- 0 = keep forever
    LastCleanupUtc      INTEGER
);
```

`DailyRetentionDays = 0` meaning "forever" is deliberate: daily rows are tiny
(one per battery per day), and they are what long-term health trends are built
from. Discarding them would destroy the most valuable and cheapest data in the
system.

### Devices

```sql
CREATE TABLE BatteryDevice (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    HardwareId          TEXT    NOT NULL UNIQUE,   -- stable ACPI/unique ID
    DeviceName          TEXT,
    Manufacturer        TEXT,
    SerialNumber        TEXT,
    Chemistry           TEXT,
    DesignCapacityMwh   INTEGER,
    DesignVoltageMv     INTEGER,
    ReportsInMilliamps  INTEGER NOT NULL DEFAULT 0, -- ACPI capability bit (quirk Q2)
    FirstSeenUtc        INTEGER NOT NULL,
    LastSeenUtc         INTEGER NOT NULL,
    IsPresent           INTEGER NOT NULL DEFAULT 1
);
```

`HardwareId` rather than an enumeration index is what makes multi-battery support
(spec §25) and battery replacement survive a reboot. If the user replaces the
battery, a new `HardwareId` appears — a new row, preserving the old battery's
history instead of silently grafting two batteries' health curves together.

`ReportsInMilliamps` records quirk Q2 per device, detected once and reused.

### Samples

```sql
CREATE TABLE BatterySample (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc        INTEGER NOT NULL,
    BatteryId           INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    SessionId           INTEGER REFERENCES BatterySession(Id),
    Percentage          REAL,
    Status              INTEGER NOT NULL,
    RemainingMwh        INTEGER,
    FullChargeMwh       INTEGER,
    DesignMwh           INTEGER,
    VoltageMv           INTEGER,
    CurrentMa           INTEGER,     -- calculated
    PowerMw             INTEGER,     -- signed
    EnergyRateMw        INTEGER,
    ScreenState         INTEGER,
    DataQuality         INTEGER NOT NULL,
    MeasurementSource   INTEGER NOT NULL
);
CREATE INDEX IX_BatterySample_Time     ON BatterySample(TimestampUtc);
CREATE INDEX IX_BatterySample_BattTime ON BatterySample(BatteryId, TimestampUtc);
CREATE INDEX IX_BatterySample_Session  ON BatterySample(SessionId, TimestampUtc);

CREATE TABLE PowerSample (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc        INTEGER NOT NULL,
    BatteryId           INTEGER REFERENCES BatteryDevice(Id),
    CurrentMa           INTEGER,
    VoltageMv           INTEGER,
    PowerMw             INTEGER,
    Direction           INTEGER NOT NULL,
    DataQuality         INTEGER NOT NULL,
    MeasurementSource   INTEGER NOT NULL
);
CREATE INDEX IX_PowerSample_Time ON PowerSample(TimestampUtc);

CREATE TABLE TemperatureSample (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc        INTEGER NOT NULL,
    BatteryId           INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    TemperatureDk       INTEGER NOT NULL,   -- deci-Kelvin
    ChargeState         INTEGER,            -- charging/discharging/idle context
    DataQuality         INTEGER NOT NULL,
    MeasurementSource   INTEGER NOT NULL
);
CREATE INDEX IX_TemperatureSample_Time ON TemperatureSample(TimestampUtc);
```

On the reference machine `TemperatureSample` will simply stay empty — the correct
outcome, and what the "sensor not exposed" empty state renders from.

`ScreenState` is denormalised onto `BatterySample` rather than joined from
`SystemEvent`. Screen state qualifies nearly every discharge query
(spec §11 splits consumption by screen on/off), and resolving it by
interval-join on every read would dominate query cost.

### Processes

```sql
CREATE TABLE ProcessSample (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc          INTEGER NOT NULL,
    SessionId             INTEGER REFERENCES BatterySession(Id),
    ProcessId             INTEGER NOT NULL,
    ProcessName           TEXT    NOT NULL,
    ApplicationKey        TEXT    NOT NULL,   -- grouping key, e.g. "chrome"
    CpuPercent            REAL,
    MemoryBytes           INTEGER,
    IsForeground          INTEGER NOT NULL DEFAULT 0,
    EstimatedPowerMw      INTEGER,
    EstimatedSharePercent REAL,
    EstimatorVersion      TEXT,
    DataQuality           INTEGER NOT NULL,
    MeasurementSource     INTEGER NOT NULL
);
CREATE INDEX IX_ProcessSample_Time ON ProcessSample(TimestampUtc);
CREATE INDEX IX_ProcessSample_App  ON ProcessSample(ApplicationKey, TimestampUtc);

CREATE TABLE ApplicationUsage (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    DayUtc                INTEGER NOT NULL,
    ApplicationKey        TEXT    NOT NULL,
    DisplayName           TEXT    NOT NULL,
    TotalSeconds          INTEGER NOT NULL DEFAULT 0,
    ForegroundSeconds     INTEGER NOT NULL DEFAULT 0,
    AvgCpuPercent         REAL,
    EstimatedEnergyMwh    INTEGER,
    EstimatorVersion      TEXT,
    UNIQUE (DayUtc, ApplicationKey)
);
```

`EstimatorVersion` on both tables is what makes spec §55's "versioned, replaceable"
model honest. When the estimator changes, old rows keep their old version tag and
the UI can decline to compare across versions rather than presenting a
discontinuity as a real behavioural change.

### Sessions

```sql
CREATE TABLE BatterySession (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    BatteryId           INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    SessionType         INTEGER NOT NULL,        -- 1 charging, 2 discharging
    StartUtc            INTEGER NOT NULL,
    EndUtc              INTEGER,                 -- NULL = in progress
    StartPercentage     REAL,
    EndPercentage       REAL,
    StartCapacityMwh    INTEGER,
    EndCapacityMwh      INTEGER,
    ScreenOnSeconds     INTEGER NOT NULL DEFAULT 0,
    ScreenOffSeconds    INTEGER NOT NULL DEFAULT 0,
    SleepSeconds        INTEGER NOT NULL DEFAULT 0,
    AvgRateMw           INTEGER,
    PeakTemperatureDk   INTEGER,
    Interruptions       INTEGER NOT NULL DEFAULT 0,
    QualityScore        REAL,
    ClosedCleanly       INTEGER NOT NULL DEFAULT 0,
    EndReason           INTEGER
);
CREATE INDEX IX_Session_Start ON BatterySession(StartUtc);
CREATE UNIQUE INDEX UX_Session_Open
    ON BatterySession(BatteryId) WHERE EndUtc IS NULL;

CREATE TABLE SessionEvent (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId     INTEGER NOT NULL REFERENCES BatterySession(Id) ON DELETE CASCADE,
    TimestampUtc  INTEGER NOT NULL,
    EventType     INTEGER NOT NULL,
    Percentage    REAL,
    Detail        TEXT,
    Inferred      INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IX_SessionEvent_Session ON SessionEvent(SessionId, TimestampUtc);
```

`UX_Session_Open` is a partial unique index enforcing **at most one open session
per battery** at the database level. Spec §49 requires "do not create duplicate
sessions"; a race between the resume handler and the periodic sampler is exactly
how duplicates arise, and a constraint catches it where a code convention would not.

`ClosedCleanly` distinguishes a session ended by a real transition from one
terminated by a crash or power loss. On startup, any session with
`ClosedCleanly = 0` and `EndUtc IS NULL` is repaired from the last known sample
rather than left dangling or silently deleted.

### Health, events, alerts, insights

```sql
CREATE TABLE BatteryHealthSnapshot (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc        INTEGER NOT NULL,
    BatteryId           INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    FullChargeMwh       INTEGER,
    DesignMwh           INTEGER,
    RetentionPercent    REAL,
    CycleCount          INTEGER,          -- NULL when firmware does not report
    HealthScore         REAL,
    HealthCategory      INTEGER,
    AlgorithmVersion    TEXT    NOT NULL,
    FactorsJson         TEXT,             -- per-factor contributions, for "how calculated"
    UNIQUE (BatteryId, TimestampUtc)
);

CREATE TABLE SystemEvent (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc  INTEGER NOT NULL,
    EventType     INTEGER NOT NULL,   -- sleep/resume/lock/screen/AC/etc.
    Detail        TEXT,
    Inferred      INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IX_SystemEvent_Time ON SystemEvent(TimestampUtc);

CREATE TABLE Alert (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc    INTEGER NOT NULL,
    AlertType       INTEGER NOT NULL,
    Severity        INTEGER NOT NULL,
    Title           TEXT    NOT NULL,
    Message         TEXT    NOT NULL,
    TriggerValue    REAL,
    ThresholdValue  REAL,
    Acknowledged    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IX_Alert_Time ON Alert(TimestampUtc);

CREATE TABLE Insight (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    GeneratedUtc    INTEGER NOT NULL,
    InsightType     INTEGER NOT NULL,
    Severity        INTEGER NOT NULL,
    Title           TEXT    NOT NULL,
    Explanation     TEXT    NOT NULL,
    SupportingJson  TEXT,
    Confidence      REAL    NOT NULL,
    PeriodStartUtc  INTEGER,
    PeriodEndUtc    INTEGER,
    RuleVersion     TEXT    NOT NULL,
    Dismissed       INTEGER NOT NULL DEFAULT 0
);
```

`FactorsJson` is what makes spec §19's "How this score is calculated" a real feature
rather than static help text — the dialog renders the *actual* factor contributions
from the snapshot being viewed, including which factors were unavailable.

### Aggregates

```sql
CREATE TABLE SampleMinute (
    BatteryId       INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    MinuteUtc       INTEGER NOT NULL,
    AvgPercentage   REAL, MinPercentage REAL, MaxPercentage REAL,
    AvgPowerMw      INTEGER, MinPowerMw INTEGER, MaxPowerMw INTEGER,
    AvgVoltageMv    INTEGER,
    AvgTemperatureDk INTEGER,
    ScreenOnSeconds INTEGER NOT NULL DEFAULT 0,
    SampleCount     INTEGER NOT NULL,
    PRIMARY KEY (BatteryId, MinuteUtc)
) WITHOUT ROWID;

CREATE TABLE SampleHour (   -- same shape, HourUtc
    BatteryId INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    HourUtc   INTEGER NOT NULL,
    AvgPercentage REAL, MinPercentage REAL, MaxPercentage REAL,
    AvgPowerMw INTEGER, MinPowerMw INTEGER, MaxPowerMw INTEGER,
    AvgVoltageMv INTEGER, AvgTemperatureDk INTEGER,
    ScreenOnSeconds INTEGER NOT NULL DEFAULT 0,
    SampleCount INTEGER NOT NULL,
    PRIMARY KEY (BatteryId, HourUtc)
) WITHOUT ROWID;

CREATE TABLE DailyStatistics (
    BatteryId               INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    DayUtc                  INTEGER NOT NULL,
    ChargeSessions          INTEGER NOT NULL DEFAULT 0,
    DischargeSessions       INTEGER NOT NULL DEFAULT 0,
    ChargingSeconds         INTEGER NOT NULL DEFAULT 0,
    DischargingSeconds      INTEGER NOT NULL DEFAULT 0,
    ScreenOnSeconds         INTEGER NOT NULL DEFAULT 0,
    ScreenOffSeconds        INTEGER NOT NULL DEFAULT 0,
    SleepSeconds            INTEGER NOT NULL DEFAULT 0,
    PercentCharged          REAL, PercentDischarged REAL,
    AvgChargeRateMw         INTEGER, AvgDischargeRateMw INTEGER,
    MinTemperatureDk        INTEGER, MaxTemperatureDk INTEGER, AvgTemperatureDk INTEGER,
    EndFullChargeMwh        INTEGER,
    EndHealthScore          REAL,
    PRIMARY KEY (BatteryId, DayUtc)
) WITHOUT ROWID;
```

`WITHOUT ROWID` on aggregates: the primary key *is* the natural access path
(battery + time range), so eliminating the rowid indirection reduces both size and
read cost for exactly the range scans the History page issues.

---

## 5. Connection configuration

```sql
PRAGMA journal_mode = WAL;         -- concurrent readers during writes
PRAGMA synchronous  = NORMAL;      -- safe under WAL, far fewer fsyncs
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
PRAGMA temp_store   = MEMORY;
PRAGMA cache_size   = -8000;       -- ~8 MB
```

`synchronous = NORMAL` under WAL is the standard durability/wear trade-off: it
risks losing only the last transaction on an OS crash — a few seconds of battery
samples — while dramatically reducing disk writes. Given driver 3 and that this app
writes continuously for months on a laptop, `FULL` would be the wrong default.
Session boundaries and health snapshots, where loss would matter, are committed
immediately rather than batched.

---

## 6. Retention and rollup

```
Raw samples ──1 min──▶ SampleMinute ──1 hour──▶ SampleHour ──1 day──▶ DailyStatistics
   7 days                  90 days                365 days              forever
```

Rollup runs on a background timer, well after the boundary has passed, and is
idempotent — re-running over an already-rolled period produces the same result,
so a crash mid-rollup is harmless.

The **read** side mirrors this ladder. `Core.History.HistoryTierSelector.TierForSpan`
maps a requested window to the coarsest tier that still reads well: ≤ 6 h → raw,
≤ 7 d → minute, ≤ 120 d → hour, otherwise daily. Because `DailyStatistics` holds
session aggregates rather than a per-metric time series, a window wide enough to
select the daily tier is served from `SampleHour` instead, clamped to the hour
tier's 365-day window (`Data.HistoryReadStore`). Every tier read is a single
indexed range scan on its time column, then `MinMaxDownsampler` bounds the result
to the chart's point budget — so a one-year range never returns more than a few
thousand rows and never stalls the UI.

Cleanup rules:

- Never delete rows belonging to an **open session** (spec §29).
- Never delete a row until it has been rolled into the next tier.
- Delete in bounded batches with pauses, so cleanup cannot cause a UI stall or a
  disk-activity spike.
- `VACUUM` only on explicit user action ("Delete all history"), never automatically —
  it rewrites the entire file, which is precisely the kind of disk burst driver 3 forbids.

### Size estimate

At default intervals (battery 5 s, power 5 s, temperature 10 s, process 10 s),
one battery, ~120 bytes/row:

| Tier | Retention | Approx. size |
|---|---|---|
| Raw | 7 days | ~35 MB |
| Minute | 90 days | ~15 MB |
| Hour | 365 days | ~2.5 MB |
| Daily | forever | ~0.1 MB/year |

Steady state settles around **50 MB**, which is acceptable. Without tiering, raw
samples alone would exceed 1.8 GB per year — the concrete reason spec §31 exists.

---

## 7. Migrations

Sequential, forward-only, each in a transaction:

```
V001__InitialSchema.sql
V002__...
```

Runner: read `SchemaMigration`, apply every embedded script with a higher version
in order, record each. Rules per spec §64:

- Never drop or rewrite a user-data column destructively.
- Additive changes preferred; a table rebuild must copy all existing rows.
- The database is backed up to `battery.db.bak-vNNN` before any migration that is
  not purely additive.
- A failed migration rolls back and the app starts in a read-only degraded mode
  with a clear Diagnostics message — rather than deleting the database to "recover",
  which would destroy exactly the history the user cares most about.

Migration tests (spec §64) run a v1 database populated with representative rows
through every subsequent migration and assert no row loss.
