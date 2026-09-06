using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// Aggregate usage over a window (specification section 16; docs/ui-navigation.md
/// "Statistics"). Every field is a real total over the covered span — an empty
/// window yields zeros with <see cref="Grade"/> <see cref="DataQuality.Unknown"/>,
/// never nulls presented as numbers.
/// </summary>
/// <param name="Window">Which window this covers.</param>
/// <param name="FromUtc">Start of the covered span.</param>
/// <param name="ToUtc">End of the covered span.</param>
/// <param name="ChargingSeconds">Total time in charging sessions.</param>
/// <param name="DischargingSeconds">Total time in discharging sessions.</param>
/// <param name="ScreenOnSeconds">Screen-on time across all sessions in the window.</param>
/// <param name="ScreenOffSeconds">Screen-off time across all sessions in the window.</param>
/// <param name="SleepSeconds">Time asleep across all sessions in the window.</param>
/// <param name="PercentCharged">Sum of positive percentage deltas across charging sessions.</param>
/// <param name="PercentDischarged">Sum of absolute negative percentage deltas across discharging sessions.</param>
/// <param name="AvgChargeRateMw">Mean charge rate, or <see langword="null"/> when no charging session had a rate.</param>
/// <param name="AvgDischargeRateMw">Mean discharge magnitude, or <see langword="null"/>.</param>
/// <param name="ChargeSessions">Charging session count.</param>
/// <param name="DischargeSessions">Discharging session count.</param>
/// <param name="EnergyThroughputMwh">Energy that flowed through the battery (charge + discharge), or <see langword="null"/>.</param>
/// <param name="Grade">The worst grade among the contributing data, or Unknown for an empty window.</param>
public sealed record StatisticsSummary(
    StatisticsWindow Window,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long ChargingSeconds,
    long DischargingSeconds,
    long ScreenOnSeconds,
    long ScreenOffSeconds,
    long SleepSeconds,
    double PercentCharged,
    double PercentDischarged,
    double? AvgChargeRateMw,
    double? AvgDischargeRateMw,
    int ChargeSessions,
    int DischargeSessions,
    long? EnergyThroughputMwh,
    DataQuality Grade)
{
    /// <summary>An all-zero summary for a window with no data.</summary>
    public static StatisticsSummary Empty(StatisticsWindow window, DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        new(window, fromUtc, toUtc, 0, 0, 0, 0, 0, 0, 0, null, null, 0, 0, null, DataQuality.Unknown);

    /// <summary>Whether any session fell in the window.</summary>
    public bool HasData => ChargeSessions > 0 || DischargeSessions > 0;

    /// <summary>Screen-on time as a fraction of screen-on + screen-off, 0–1.</summary>
    public double ScreenOnFraction =>
        ScreenOnSeconds + ScreenOffSeconds > 0
            ? ScreenOnSeconds / (double)(ScreenOnSeconds + ScreenOffSeconds)
            : 0;
}
