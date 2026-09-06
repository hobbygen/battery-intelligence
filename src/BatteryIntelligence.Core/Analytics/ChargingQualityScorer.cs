using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Analytics;

/// <summary>Inputs for <see cref="ChargingQualityScorer"/> — one completed charging session plus this battery's own history.</summary>
/// <param name="SessionAvgRateMw">Mean charge rate for the session.</param>
/// <param name="SessionRateCoefficientOfVariation">Spread of the rate within the session, or <see langword="null"/> when only the endpoint capacities are known (treated as stable).</param>
/// <param name="PersonalMedianRateMw">This battery's 30-day median charge rate — the baseline (spec §54: "the user's own history, not a manufacturer figure").</param>
/// <param name="PriorComparableSessions">How many prior comparable charging sessions exist.</param>
/// <param name="Interruptions">Charging interruptions during the session.</param>
/// <param name="SessionHours">Session duration in hours.</param>
/// <param name="ThermalFractionAboveWarn">Fraction of the session above the warning temperature, or <see langword="null"/> when no sensor (component dropped + renormalised).</param>
public sealed record ChargingQualityInputs(
    double? SessionAvgRateMw,
    double? SessionRateCoefficientOfVariation,
    double? PersonalMedianRateMw,
    int PriorComparableSessions,
    int Interruptions,
    double SessionHours,
    double? ThermalFractionAboveWarn);

/// <summary>
/// <c>ChargingQualityV1</c> — a 0–100 score over a completed charging session,
/// against the battery's own 30-day baseline (docs/estimation-strategy.md
/// section 6; specification sections 10 and 54). Unavailable below the configured
/// minimum number of comparable sessions.
/// </summary>
public static class ChargingQualityScorer
{
    /// <summary>The algorithm tag.</summary>
    public const string Version = "ChargingQualityV1";

    private const double RateWeight = 0.40;
    private const double StabilityWeight = 0.25;
    private const double ThermalWeight = 0.20;
    private const double InterruptionWeight = 0.15;

    public static ChargingQuality Score(ChargingQualityInputs inputs, int minPriorSessions)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.PriorComparableSessions < minPriorSessions
            || inputs.PersonalMedianRateMw is not double median || median <= 0
            || inputs.SessionAvgRateMw is not double sessionRate || sessionRate <= 0)
        {
            return ChargingQuality.NotEnoughData(Version);
        }

        double ratio = sessionRate / median;
        double rateScore = Clamp01((ratio - 0.4) / 0.6);

        double cov = inputs.SessionRateCoefficientOfVariation ?? 0.0;
        double stabilityScore = Clamp01(1.0 - Math.Min(1.0, cov));

        double interruptionsPerHour = inputs.Interruptions / Math.Max(0.25, inputs.SessionHours);
        double interruptionScore = Clamp01(1.0 - Math.Min(1.0, interruptionsPerHour));

        double? thermalScore = inputs.ThermalFractionAboveWarn is double frac
            ? Clamp01(1.0 - (Clamp01(frac) * 3.0))
            : (double?)null;

        List<HealthFactor> components =
        [
            new("rate", "Rate vs your baseline", RateWeight, 0, rateScore,
                $"{ratio * 100:F0}% of your 30-day median rate"),
            new("stability", "Rate stability", StabilityWeight, 0, stabilityScore,
                $"variation {cov:F2}"),
            new("thermal", "Thermal behaviour", ThermalWeight, 0, thermalScore,
                thermalScore is null ? "No battery temperature sensor" : $"{inputs.ThermalFractionAboveWarn * 100:F0}% of the session above the warning threshold"),
            new("interruptions", "Interruptions", InterruptionWeight, 0, interruptionScore,
                $"{inputs.Interruptions} in {inputs.SessionHours:F1} h"),
        ];

        double availableWeight = 0;
        foreach (HealthFactor c in components)
        {
            if (c.Contributed)
            {
                availableWeight += c.Weight;
            }
        }

        double score01 = 0;
        List<HealthFactor> normalised = new(components.Count);
        foreach (HealthFactor c in components)
        {
            double normWeight = c.Contributed && availableWeight > 0 ? c.Weight / availableWeight : 0;
            normalised.Add(c with { NormalisedWeight = normWeight });
            if (c.Score01 is double s)
            {
                score01 += normWeight * s;
            }
        }

        EstimateConfidence confidence = inputs.PriorComparableSessions >= minPriorSessions * 3
            ? EstimateConfidence.High
            : EstimateConfidence.Medium;

        return new ChargingQuality(Math.Round(Clamp01(score01) * 100.0, 1), normalised, confidence, Version);
    }

    private static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0.0, 1.0);
}
