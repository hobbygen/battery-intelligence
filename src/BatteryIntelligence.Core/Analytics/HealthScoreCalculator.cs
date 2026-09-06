using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Analytics;

/// <summary>Everything <see cref="HealthScoreCalculator"/> needs. Any field may be <see langword="null"/> — its weight is then redistributed.</summary>
/// <param name="RetentionPercent">Capacity retention (full ÷ design × 100). <strong>Required</strong> — absent ⇒ the whole score is Unavailable.</param>
/// <param name="DegradationSlopePercentPerMonth">Retention change per 30 days (negative = declining), or <see langword="null"/> below the trend floor.</param>
/// <param name="CycleCount">Charge cycles, or <see langword="null"/> when firmware does not report (the reference machine).</param>
/// <param name="Chemistry">Battery chemistry tag, for the typical-cycle rating.</param>
/// <param name="ThermalExposureFraction">Fraction of recent time spent above the warning temperature (0–1), or <see langword="null"/> when no sensor.</param>
/// <param name="AvgDepthOfDischargePercent">Mean depth of discharge across recent discharge sessions (0–100; lower is gentler), or <see langword="null"/>.</param>
/// <param name="ChargeRateCoefficientOfVariation">Spread of charge rate across recent charging sessions (std ÷ mean), or <see langword="null"/>.</param>
public sealed record HealthScoreInputs(
    double? RetentionPercent,
    double? DegradationSlopePercentPerMonth,
    int? CycleCount,
    string? Chemistry,
    double? ThermalExposureFraction,
    double? AvgDepthOfDischargePercent,
    double? ChargeRateCoefficientOfVariation);

/// <summary>
/// <c>HealthScoreV1</c> — a weighted, versioned, explainable Battery Health Score
/// over the <em>available</em> factors only, with weights renormalised across them
/// (docs/estimation-strategy.md section 4; specification section 19).
/// </summary>
/// <remarks>
/// Retention is mandatory: a score built only on behavioural proxies would be "a
/// number with nothing real underneath it". Every factor — contributing or
/// redistributed — is returned in <see cref="HealthScore.Factors"/> and shown in
/// "How this score is calculated".
/// </remarks>
public static class HealthScoreCalculator
{
    /// <summary>The algorithm tag stored with every snapshot.</summary>
    public const string Version = "HealthScoreV1";

    private const double RetentionWeight = 0.50;
    private const double DegradationWeight = 0.20;
    private const double CycleWeight = 0.10;
    private const double ThermalWeight = 0.10;
    private const double ChargeBehaviourWeight = 0.05;
    private const double RateStabilityWeight = 0.05;

    public static HealthScore Compute(HealthScoreInputs inputs, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        // Score each factor to 0..1 (higher = healthier), or null when its input is absent.
        double? retention = inputs.RetentionPercent is double r ? Clamp01(r / 100.0) : null;
        double? degradation = inputs.DegradationSlopePercentPerMonth is double slope
            ? Clamp01(1.0 - (Math.Max(0.0, -slope) / 3.0))
            : null;
        double? cycle = inputs.CycleCount is int cycles
            ? Clamp01(1.0 - (cycles / (double)TypicalCycleRating(inputs.Chemistry)))
            : null;
        double? thermal = inputs.ThermalExposureFraction is double frac
            ? Clamp01(1.0 - (Clamp01(frac) * 3.0))
            : null;
        double? chargeBehaviour = inputs.AvgDepthOfDischargePercent is double dod
            ? Clamp01(1.0 - (Clamp01(dod / 100.0) * 0.8))
            : null;
        double? rateStability = inputs.ChargeRateCoefficientOfVariation is double cov
            ? Clamp01(1.0 - Math.Min(1.0, cov))
            : null;

        List<HealthFactor> factors =
        [
            MakeFactor("retention", "Capacity retention", RetentionWeight, retention,
                retention is null ? "Full-charge or design capacity unavailable" : $"{inputs.RetentionPercent:F0}% of design capacity"),
            MakeFactor("degradation", "Degradation rate", DegradationWeight, degradation,
                degradation is null ? "Needs 30+ days of history" : $"{inputs.DegradationSlopePercentPerMonth:+0.0;-0.0;0.0} pts/month"),
            MakeFactor("cycles", "Cycle count", CycleWeight, cycle,
                cycle is null ? "Not reported by this battery's firmware" : $"{inputs.CycleCount} of ~{TypicalCycleRating(inputs.Chemistry)} typical"),
            MakeFactor("thermal", "Thermal exposure", ThermalWeight, thermal,
                thermal is null ? "No battery temperature sensor" : $"{inputs.ThermalExposureFraction * 100:F0}% of recent time above the warning threshold"),
            MakeFactor("charge_behaviour", "Charge behaviour", ChargeBehaviourWeight, chargeBehaviour,
                chargeBehaviour is null ? "No discharge history yet" : $"average depth of discharge {inputs.AvgDepthOfDischargePercent:F0}%"),
            MakeFactor("rate_stability", "Charge-rate stability", RateStabilityWeight, rateStability,
                rateStability is null ? "No charging history yet" : $"rate variation {inputs.ChargeRateCoefficientOfVariation:F2}"),
        ];

        // Retention is required.
        if (retention is null)
        {
            return HealthScore.Unavailable(Version, nowUtc, factors);
        }

        double availableWeight = 0;
        foreach (HealthFactor f in factors)
        {
            if (f.Contributed)
            {
                availableWeight += f.Weight;
            }
        }

        // Renormalise across the available factors and compute the score.
        double score01 = 0;
        List<HealthFactor> normalised = new(factors.Count);
        foreach (HealthFactor f in factors)
        {
            double normWeight = f.Contributed && availableWeight > 0 ? f.Weight / availableWeight : 0;
            normalised.Add(f with { NormalisedWeight = normWeight });
            if (f.Score01 is double s)
            {
                score01 += normWeight * s;
            }
        }

        double score = Math.Round(Clamp01(score01) * 100.0, 1);
        return new HealthScore(score, Categorise(score), normalised, Version, nowUtc, DataQuality.Estimated);
    }

    /// <summary>The specification section 19 band for a 0–100 score.</summary>
    public static HealthCategory Categorise(double score) => score switch
    {
        >= 90 => HealthCategory.Excellent,
        >= 75 => HealthCategory.Good,
        >= 60 => HealthCategory.Fair,
        >= 40 => HealthCategory.Poor,
        _ => HealthCategory.Critical,
    };

    private static int TypicalCycleRating(string? chemistry)
    {
        string c = chemistry?.Trim().ToUpperInvariant() ?? string.Empty;
        return c is "LFP" or "LIFEPO4" or "LIFEPO" ? 2_000 : 500;
    }

    private static HealthFactor MakeFactor(string key, string label, double weight, double? score01, string basis) =>
        new(key, label, weight, NormalisedWeight: 0, score01, basis);

    private static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0.0, 1.0);
}
