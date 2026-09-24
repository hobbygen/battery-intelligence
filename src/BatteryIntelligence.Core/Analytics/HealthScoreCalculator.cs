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
/// <c>HealthScoreV2</c> — a weighted, versioned, explainable Battery Health Score
/// over the <em>available</em> factors only, with weights renormalised across them
/// (docs/estimation-strategy.md section 4; specification section 19).
/// </summary>
/// <remarks>
/// <para>
/// Retention is mandatory: a score built only on behavioural proxies would be "a
/// number with nothing real underneath it". Every factor — contributing or
/// redistributed — is returned in <see cref="HealthScore.Factors"/> and shown in
/// "How this score is calculated".
/// </para>
/// <para>
/// V2 corrects two ways V1 could state the opposite of the truth. Factors that
/// describe how the machine is used rather than the state of the cell now only
/// ever subtract (<see cref="HealthFactorRole.Penalty"/>), and the score is capped
/// at measured retention, so no combination of gentle habits can rate a worn pack
/// above the charge it actually holds. The degradation and behaviour factors also
/// gained dead bands, so ordinary ageing — steepest on a new pack, and noisiest
/// when the history is short — no longer reads as a fault.
/// </para>
/// </remarks>
public static class HealthScoreCalculator
{
    /// <summary>The algorithm tag stored with every snapshot.</summary>
    public const string Version = "HealthScoreV2";

    // Weighted factors — evidence about the state of the cell itself. These are
    // renormalised across whichever are available and averaged into the score.
    private const double RetentionWeight = 0.65;
    private const double DegradationWeight = 0.20;
    private const double CycleWeight = 0.10;
    private const double ThermalWeight = 0.05;

    // Penalty factors — evidence about how the machine is used. Capped small and
    // subtractive only; see HealthFactorRole.Penalty.
    private const double ChargeBehaviourPenalty = 0.03;
    private const double RateStabilityPenalty = 0.02;

    /// <summary>
    /// Monthly retention decline treated as normal ageing. Lithium-ion sheds a few
    /// tenths of a point a month in ordinary service, fastest in its first months,
    /// and a short history makes the fitted slope noisier than the real trend — so
    /// decline up to this figure costs nothing. Without the dead band a new pack
    /// scored worse than a worn one that had already flattened out.
    /// </summary>
    private const double BenignDeclinePercentPerMonth = 0.5;

    /// <summary>Monthly decline at which the degradation factor reaches zero.</summary>
    private const double SevereDeclinePercentPerMonth = 3.0;

    /// <summary>Average depth of discharge below which nothing is deducted.</summary>
    private const double BenignDepthOfDischargePercent = 50.0;

    /// <summary>Charge-rate variation below which nothing is deducted.</summary>
    private const double BenignChargeRateCoefficientOfVariation = 0.5;

    public static HealthScore Compute(HealthScoreInputs inputs, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        // Score each factor to 0..1 (higher = healthier), or null when its input is absent.
        double? retention = inputs.RetentionPercent is double r ? Clamp01(r / 100.0) : null;

        double? degradation = inputs.DegradationSlopePercentPerMonth is double slope
            ? Clamp01(1.0 - Ramp(Math.Max(0.0, -slope), BenignDeclinePercentPerMonth, SevereDeclinePercentPerMonth))
            : null;

        double? cycle = inputs.CycleCount is int cycles
            ? Clamp01(1.0 - (cycles / (double)TypicalCycleRating(inputs.Chemistry)))
            : null;

        double? thermal = inputs.ThermalExposureFraction is double frac
            ? Clamp01(1.0 - (Clamp01(frac) * 3.0))
            : null;

        double? chargeBehaviour = inputs.AvgDepthOfDischargePercent is double dod
            ? Clamp01(1.0 - Ramp(dod, BenignDepthOfDischargePercent, 100.0))
            : null;

        double? rateStability = inputs.ChargeRateCoefficientOfVariation is double cov
            ? Clamp01(1.0 - Ramp(cov, BenignChargeRateCoefficientOfVariation, 1.0))
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
            MakeFactor("charge_behaviour", "Charge behaviour", ChargeBehaviourPenalty, chargeBehaviour,
                chargeBehaviour is null ? "No discharge history yet" : $"average depth of discharge {inputs.AvgDepthOfDischargePercent:F0}%",
                HealthFactorRole.Penalty),
            MakeFactor("rate_stability", "Charge-rate stability", RateStabilityPenalty, rateStability,
                rateStability is null ? "No charging history yet" : $"rate variation {inputs.ChargeRateCoefficientOfVariation:F2}",
                HealthFactorRole.Penalty),
        ];

        // Retention is required.
        if (retention is null)
        {
            return HealthScore.Unavailable(Version, nowUtc, factors);
        }

        double availableWeight = 0;
        foreach (HealthFactor f in factors)
        {
            if (f.Role == HealthFactorRole.Weighted && f.Contributed)
            {
                availableWeight += f.Weight;
            }
        }

        // Renormalise across the available weighted factors and average them; the
        // penalty factors then subtract from the result rather than joining the mean.
        double score01 = 0;
        double penalty01 = 0;
        List<HealthFactor> normalised = new(factors.Count);
        foreach (HealthFactor f in factors)
        {
            if (f.Role == HealthFactorRole.Penalty)
            {
                normalised.Add(f);
                if (f.Score01 is double sub)
                {
                    penalty01 += f.Weight * (1.0 - sub);
                }

                continue;
            }

            double normWeight = f.Contributed && availableWeight > 0 ? f.Weight / availableWeight : 0;
            normalised.Add(f with { NormalisedWeight = normWeight });
            if (f.Score01 is double s)
            {
                score01 += normWeight * s;
            }
        }

        score01 = Clamp01(score01 - penalty01);

        // Retention is the one directly measured statement of how much of the pack
        // is left, so it caps the result. Every other factor explains why that figure
        // will get worse, and none of them is grounds for claiming the pack holds
        // more charge than it measurably does — which is how a battery kept
        // permanently on AC, with a flat trend and no deep discharges, could score
        // Excellent while holding half its design capacity.
        score01 = Math.Min(score01, retention.Value);

        double score = Math.Round(score01 * 100.0, 1);
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

    /// <summary>
    /// 0 at or below <paramref name="benign"/>, rising linearly to 1 at
    /// <paramref name="severe"/>. Gives a factor a dead band, so an ordinary
    /// reading — and the noise in a short history — costs nothing.
    /// </summary>
    private static double Ramp(double value, double benign, double severe) =>
        severe <= benign ? 0.0 : Clamp01((value - benign) / (severe - benign));

    private static int TypicalCycleRating(string? chemistry)
    {
        string c = chemistry?.Trim().ToUpperInvariant() ?? string.Empty;
        return c is "LFP" or "LIFEPO4" or "LIFEPO" ? 2_000 : 500;
    }

    private static HealthFactor MakeFactor(
        string key,
        string label,
        double weight,
        double? score01,
        string basis,
        HealthFactorRole role = HealthFactorRole.Weighted) =>
        new(key, label, weight, NormalisedWeight: 0, score01, basis, role);

    private static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0.0, 1.0);
}
