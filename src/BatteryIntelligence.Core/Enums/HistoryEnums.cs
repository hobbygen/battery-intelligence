namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// Which aggregate table a history query reads (docs/database.md section 6).
/// Chosen by <see cref="History.HistoryTierSelector"/> from the requested span so
/// a year of history loads without stalling the UI (specification section 17).
/// </summary>
public enum HistoryTier
{
    /// <summary>Per-sample <c>BatterySample</c> rows — short spans only.</summary>
    Raw = 0,

    /// <summary>Minute averages from <c>SampleMinute</c>.</summary>
    Minute = 1,

    /// <summary>Hourly averages from <c>SampleHour</c>.</summary>
    Hour = 2,

    /// <summary>Daily aggregates from <c>DailyStatistics</c>.</summary>
    Daily = 3,
}

/// <summary>A metric the History page can chart over time. Not persisted.</summary>
public enum HistoryMetric
{
    /// <summary>Charge percentage, 0–100.</summary>
    ChargePercent = 0,

    /// <summary>Signed energy rate, milliwatts.</summary>
    PowerMw = 1,

    /// <summary>Voltage, millivolts.</summary>
    VoltageMv = 2,

    /// <summary>Battery temperature, degrees Celsius (only where a sensor exists).</summary>
    TemperatureCelsius = 3,
}

/// <summary>A range selection on the History page. Not persisted.</summary>
public enum HistoryRange
{
    Last24Hours = 0,
    Last7Days = 1,
    Last30Days = 2,
    Last90Days = 3,
    LastYear = 4,

    /// <summary>An explicit date range supplied by the caller.</summary>
    Custom = 5,
}

/// <summary>Which tables an export includes (specification section 36).</summary>
[Flags]
public enum ExportScope
{
    None = 0,
    BatterySamples = 1 << 0,
    PowerSamples = 1 << 1,
    TemperatureSamples = 1 << 2,
    Sessions = 1 << 3,
    Alerts = 1 << 4,
    HealthSnapshots = 1 << 5,
    DailyStatistics = 1 << 6,
    ApplicationUsage = 1 << 7,

    All = BatterySamples | PowerSamples | TemperatureSamples | Sessions | Alerts | HealthSnapshots | DailyStatistics | ApplicationUsage,
}
