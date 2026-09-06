-- V001: initial schema. See docs/database.md section 4.
-- Eighteen tables, matching specification section 27. Sequential, forward-only,
-- additive-only migrations from here per docs/database.md section 7.

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
    DailyRetentionDays  INTEGER NOT NULL DEFAULT 0,
    LastCleanupUtc      INTEGER
);

CREATE TABLE BatteryDevice (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    HardwareId          TEXT    NOT NULL UNIQUE,
    DeviceName          TEXT,
    Manufacturer        TEXT,
    SerialNumber        TEXT,
    Chemistry           TEXT,
    DesignCapacityMwh   INTEGER,
    DesignVoltageMv     INTEGER,
    ReportsInMilliamps  INTEGER NOT NULL DEFAULT 0,
    FirstSeenUtc        INTEGER NOT NULL,
    LastSeenUtc         INTEGER NOT NULL,
    IsPresent           INTEGER NOT NULL DEFAULT 1
);

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
    CurrentMa           INTEGER,
    PowerMw             INTEGER,
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
    TemperatureDk       INTEGER NOT NULL,
    ChargeState         INTEGER,
    DataQuality         INTEGER NOT NULL,
    MeasurementSource   INTEGER NOT NULL
);
CREATE INDEX IX_TemperatureSample_Time ON TemperatureSample(TimestampUtc);

CREATE TABLE ProcessSample (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc          INTEGER NOT NULL,
    SessionId             INTEGER REFERENCES BatterySession(Id),
    ProcessId             INTEGER NOT NULL,
    ProcessName           TEXT    NOT NULL,
    ApplicationKey        TEXT    NOT NULL,
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

CREATE TABLE BatterySession (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    BatteryId           INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    SessionType         INTEGER NOT NULL,
    StartUtc            INTEGER NOT NULL,
    EndUtc              INTEGER,
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

CREATE TABLE BatteryHealthSnapshot (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc        INTEGER NOT NULL,
    BatteryId           INTEGER NOT NULL REFERENCES BatteryDevice(Id),
    FullChargeMwh       INTEGER,
    DesignMwh           INTEGER,
    RetentionPercent    REAL,
    CycleCount          INTEGER,
    HealthScore         REAL,
    HealthCategory      INTEGER,
    AlgorithmVersion    TEXT    NOT NULL,
    FactorsJson         TEXT,
    UNIQUE (BatteryId, TimestampUtc)
);

CREATE TABLE SystemEvent (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc  INTEGER NOT NULL,
    EventType     INTEGER NOT NULL,
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

CREATE TABLE SampleHour (
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
