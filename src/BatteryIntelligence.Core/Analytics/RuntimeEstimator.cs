using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Analytics;

/// <summary>Pre-computed inputs for <see cref="RuntimeEstimator"/>.</summary>
/// <param name="RemainingCapacityMwh">Measured remaining capacity, or <see langword="null"/>.</param>
/// <param name="BlendedRateMw">Recency-weighted mean discharge magnitude over the recent window, or <see langword="null"/>.</param>
/// <param name="ScreenOnRateMw">Mean discharge magnitude during screen-on periods, or <see langword="null"/>.</param>
/// <param name="ScreenOffRateMw">Mean discharge magnitude during screen-off periods, or <see langword="null"/> — never extrapolated from screen-on.</param>
/// <param name="DataSpanCurrentState">How much wall-clock time the current screen state has usable rate samples over.</param>
/// <param name="RateCoefficientOfVariation">Spread of the recent rate (std ÷ mean).</param>
/// <param name="ComparablePeriods">Roughly how many prior comparable discharge periods exist.</param>
public sealed record RuntimeEstimatorInputs(
    double? RemainingCapacityMwh,
    double? BlendedRateMw,
    double? ScreenOnRateMw,
    double? ScreenOffRateMw,
    TimeSpan DataSpanCurrentState,
    double RateCoefficientOfVariation,
    int ComparablePeriods);

/// <summary>
/// The rolling, screen-state-aware remaining-runtime estimate
/// (docs/estimation-strategy.md section 3; specification sections 8 and 52).
/// </summary>
/// <remarks>
/// <c>runtime = remaining_mWh ÷ rate_mW</c>, computed separately per screen state.
/// The screen-off figure is <see langword="null"/> when no screen-off history
/// exists — the display is usually the largest single consumer, so extrapolating
/// screen-off from screen-on would be invention (spec §8). Below the data floor
/// the confidence is <see cref="EstimateConfidence.Calculating"/> and no figure is
/// produced.
/// </remarks>
public static class RuntimeEstimator
{
    private static readonly TimeSpan HighFloor = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MediumFloor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LowFloor = TimeSpan.FromSeconds(60);

    public static RuntimeEstimate Estimate(RuntimeEstimatorInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.RemainingCapacityMwh is not double remaining || remaining <= 0
            || inputs.BlendedRateMw is not double blended || blended <= 0
            || inputs.DataSpanCurrentState < LowFloor)
        {
            return RuntimeEstimate.Calculating;
        }

        EstimateConfidence confidence =
            inputs.DataSpanCurrentState >= HighFloor
                && inputs.RateCoefficientOfVariation < 0.25
                && inputs.ComparablePeriods >= 3 ? EstimateConfidence.High
            : inputs.DataSpanCurrentState >= MediumFloor
                && inputs.RateCoefficientOfVariation < 0.5 ? EstimateConfidence.Medium
            : EstimateConfidence.Low;

        TimeSpan atCurrent = Hours(remaining / blended);
        TimeSpan? screenOn = inputs.ScreenOnRateMw is double onRate && onRate > 0 ? Hours(remaining / onRate) : null;
        TimeSpan? screenOff = inputs.ScreenOffRateMw is double offRate && offRate > 0 ? Hours(remaining / offRate) : null;

        string basis = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{blended:F0} mW mean over the last {inputs.DataSpanCurrentState.TotalMinutes:F0} min");

        return new RuntimeEstimate(atCurrent, screenOn, screenOff, confidence, basis);
    }

    private static TimeSpan Hours(double hours) =>
        TimeSpan.FromHours(Math.Clamp(hours, 0, 1_000));
}
