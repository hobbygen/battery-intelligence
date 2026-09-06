namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// Battery Health Score band (specification section 19; docs/estimation-strategy.md
/// section 4). Persisted (<c>BatteryHealthSnapshot.HealthCategory</c>); never renumber.
/// </summary>
public enum HealthCategory
{
    /// <summary>The score is Unavailable — retention could not be computed.</summary>
    Unknown = 0,

    /// <summary>90–100.</summary>
    Excellent = 1,

    /// <summary>75–89.</summary>
    Good = 2,

    /// <summary>60–74.</summary>
    Fair = 3,

    /// <summary>40–59.</summary>
    Poor = 4,

    /// <summary>0–39.</summary>
    Critical = 5,
}

/// <summary>
/// Confidence tier for a rolling estimate (docs/estimation-strategy.md section 3).
/// <see cref="Calculating"/> means no number may be shown at all — the estimate is
/// still below the data floor.
/// </summary>
public enum EstimateConfidence
{
    /// <summary>Below the data floor. Show "Calculating…", never a figure.</summary>
    Calculating = 0,

    /// <summary>≥60 s of data, or high variance.</summary>
    Low = 1,

    /// <summary>≥5 min of data, moderate variance.</summary>
    Medium = 2,

    /// <summary>≥15 min in the current state, low variance, ≥3 comparable historical periods.</summary>
    High = 3,
}

/// <summary>
/// The kind of a rule-based insight (specification section 18). Persisted
/// (<c>Insight.InsightType</c>); never renumber.
/// </summary>
public enum InsightType
{
    Unknown = 0,

    /// <summary>Discharge is measurably faster than this battery's own recent baseline.</summary>
    FastDrain = 1,

    /// <summary>Charging is measurably slower than this battery's own 30-day median.</summary>
    ChargingSlow = 2,

    /// <summary>The 90-day retention trend is declining faster than noise.</summary>
    HealthDegrading = 3,

    /// <summary>The battery has spent sustained time above the warning temperature.</summary>
    HighTemperatureExposure = 4,

    /// <summary>The display accounts for most of the measured drain.</summary>
    ScreenDominatesDrain = 5,

    /// <summary>Charging habits have been consistently good over the window.</summary>
    GoodChargingHabits = 6,

    /// <summary>Most discharge sessions run the battery very low before recharging.</summary>
    DeepDischargeHabit = 7,
}

/// <summary>How prominently an insight is shown (specification section 18).</summary>
public enum InsightSeverity
{
    /// <summary>Neutral observation.</summary>
    Info = 0,

    /// <summary>An actionable suggestion.</summary>
    Advice = 1,

    /// <summary>Something the user should probably address.</summary>
    Warning = 2,
}

/// <summary>
/// The window a statistics summary covers (specification section 16). Not
/// persisted — a UI selection — but in Core so <c>StatisticsEngine</c> stays
/// testable without the App.
/// </summary>
public enum StatisticsWindow
{
    /// <summary>Since local midnight today.</summary>
    Today = 0,

    /// <summary>The last seven days.</summary>
    Last7Days = 1,

    /// <summary>The last thirty days.</summary>
    Last30Days = 2,

    /// <summary>Everything on record.</summary>
    Lifetime = 3,

    /// <summary>An explicit date range supplied by the caller.</summary>
    Custom = 4,
}
