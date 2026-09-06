using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// The snapshot <see cref="Alerts.AlertRuleEngine"/> judges each tick. Every field
/// is nullable — a rule whose input is absent simply does not fire
/// (specification section 20).
/// </summary>
/// <param name="PercentagePercent">Charge percentage 0–100.</param>
/// <param name="State">Charging / discharging / idle / full.</param>
/// <param name="AcOnline">Whether AC power is connected.</param>
/// <param name="TemperatureCelsius">Battery temperature, or <see langword="null"/> when there is no sensor.</param>
/// <param name="DischargeRateMw">Recent discharge magnitude in milliwatts, or <see langword="null"/>.</param>
/// <param name="ChargeRateMw">Recent charge rate in milliwatts, or <see langword="null"/>.</param>
/// <param name="BaselineDischargeRateMw">This battery's own recent baseline discharge rate, for the rapid-discharge rule.</param>
/// <param name="BaselineChargeRateMw">This battery's own recent baseline charge rate, for the slow-charging rule.</param>
/// <param name="HealthScore">The current Battery Health Score, or <see langword="null"/>.</param>
/// <param name="DegradationSlopePercentPerMonth">Retention change per 30 days (negative = declining), or <see langword="null"/>.</param>
/// <param name="TrendConfidence">How much history backs the degradation trend.</param>
/// <param name="TopAppDisplayName">The highest-impact application right now, or <see langword="null"/>.</param>
/// <param name="TopAppSharePercent">That application's share of the attributable budget, 0–100, or <see langword="null"/> (e.g. on AC).</param>
/// <param name="NowUtc">The reference "now".</param>
public sealed record AlertEvaluationInput(
    double? PercentagePercent,
    BatteryState? State,
    bool? AcOnline,
    double? TemperatureCelsius,
    double? DischargeRateMw,
    double? ChargeRateMw,
    double? BaselineDischargeRateMw,
    double? BaselineChargeRateMw,
    double? HealthScore,
    double? DegradationSlopePercentPerMonth,
    EstimateConfidence TrendConfidence,
    string? TopAppDisplayName,
    double? TopAppSharePercent,
    DateTimeOffset NowUtc);
