namespace BatteryIntelligence.Core.Models;

/// <summary>One historical <c>BatteryHealthSnapshot</c> row, as the analytics engine reads it.</summary>
/// <param name="TimestampUtc">When the snapshot was taken.</param>
/// <param name="RetentionPercent">Capacity retention at that time, or <see langword="null"/>.</param>
/// <param name="FullChargeMwh">Full-charge capacity, or <see langword="null"/>.</param>
/// <param name="CycleCount">Cycle count, or <see langword="null"/> when firmware did not report.</param>
/// <param name="HealthScore">The score recorded then, or <see langword="null"/>.</param>
/// <param name="AlgorithmVersion">The algorithm tag stored with that snapshot.</param>
public sealed record HealthSnapshotRow(
    DateTimeOffset TimestampUtc,
    double? RetentionPercent,
    int? FullChargeMwh,
    int? CycleCount,
    double? HealthScore,
    string AlgorithmVersion);

/// <summary>One point on the retention-over-time series, for the degradation trend.</summary>
public sealed record RetentionPoint(DateTimeOffset TimestampUtc, double RetentionPercent);

/// <summary>One <c>DailyStatistics</c> row, as the statistics engine reads it.</summary>
/// <param name="DayUtc">Midnight UTC of the day.</param>
/// <param name="ChargeSessions">Charging sessions that day.</param>
/// <param name="DischargeSessions">Discharging sessions that day.</param>
/// <param name="ChargingSeconds">Time charging.</param>
/// <param name="DischargingSeconds">Time discharging.</param>
/// <param name="ScreenOnSeconds">Screen-on time.</param>
/// <param name="ScreenOffSeconds">Screen-off time.</param>
/// <param name="SleepSeconds">Time asleep.</param>
/// <param name="PercentCharged">Percentage points gained across charging sessions.</param>
/// <param name="PercentDischarged">Percentage points lost across discharging sessions.</param>
/// <param name="AvgChargeRateMw">Mean charge rate, or <see langword="null"/>.</param>
/// <param name="AvgDischargeRateMw">Mean discharge magnitude, or <see langword="null"/>.</param>
public sealed record DailyStatRow(
    DateTimeOffset DayUtc,
    int ChargeSessions,
    int DischargeSessions,
    long ChargingSeconds,
    long DischargingSeconds,
    long ScreenOnSeconds,
    long ScreenOffSeconds,
    long SleepSeconds,
    double PercentCharged,
    double PercentDischarged,
    double? AvgChargeRateMw,
    double? AvgDischargeRateMw);

/// <summary>
/// Everything the rule-based insight provider needs, assembled once by
/// <c>AnalyticsService</c> so the rules stay pure and unit-testable
/// (docs/estimation-strategy.md section 7).
/// </summary>
/// <param name="RecentSessions">Charging and discharging sessions over the rule window (~60 days), newest first.</param>
/// <param name="Trend">The current degradation trend.</param>
/// <param name="RecentDischarge">Discharge breakdown over the recent window.</param>
/// <param name="TemperatureExposureSecondsAboveWarn">Seconds spent above the warning temperature over the window, or <see langword="null"/> when no sensor.</param>
/// <param name="WarnTemperatureCelsius">The configured warning threshold, for the exposure rule's wording.</param>
/// <param name="ConfidenceThreshold">Minimum confidence for an insight to be emitted (default 0.7).</param>
/// <param name="NowUtc">The reference "now".</param>
public sealed record AnalyticsContext(
    IReadOnlyList<BatterySessionInfo> RecentSessions,
    DegradationTrend Trend,
    DischargeAnalysis RecentDischarge,
    double? TemperatureExposureSecondsAboveWarn,
    double WarnTemperatureCelsius,
    double ConfidenceThreshold,
    DateTimeOffset NowUtc);
