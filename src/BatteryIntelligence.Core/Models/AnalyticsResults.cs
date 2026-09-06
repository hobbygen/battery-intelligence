using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// The smoothed capacity-retention trend over a long window (specification
/// section 53; docs/estimation-strategy.md section 4).
/// </summary>
/// <remarks>
/// The slope is a Theil–Sen median slope, not an ordinary least-squares fit, so a
/// single spurious full-charge reading cannot swing it (docs/roadmap.md Phase 8
/// deviations). Below the data floor the confidence is
/// <see cref="EstimateConfidence.Calculating"/> and the slope is
/// <see langword="null"/>.
/// </remarks>
/// <param name="SlopePercentPerMonth">Change in retention percentage points per 30 days (negative = declining), or <see langword="null"/> below the floor.</param>
/// <param name="ProjectedRetentionPercentIn90Days">Retention extrapolated 90 days out, or <see langword="null"/>.</param>
/// <param name="Confidence">How much history backs the trend.</param>
/// <param name="SampleCount">Health snapshots used.</param>
/// <param name="FromUtc">Start of the covered span.</param>
/// <param name="ToUtc">End of the covered span.</param>
public sealed record DegradationTrend(
    double? SlopePercentPerMonth,
    double? ProjectedRetentionPercentIn90Days,
    EstimateConfidence Confidence,
    int SampleCount,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc)
{
    /// <summary>The "not enough history yet" trend.</summary>
    public static DegradationTrend NotEnoughData(int sampleCount, DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        new(null, null, EstimateConfidence.Calculating, sampleCount, fromUtc, toUtc);

    /// <summary>Whether a slope is available.</summary>
    public bool IsAvailable => SlopePercentPerMonth is not null;
}

/// <summary>
/// A charging-quality score over a completed charging session (specification
/// sections 10 and 54; docs/estimation-strategy.md section 6).
/// </summary>
/// <param name="Score">0–100, or <see langword="null"/> below the ≥5-prior-sessions floor.</param>
/// <param name="Components">Per-component contributions, for the explanation (thermal omitted + renormalised when no sensor).</param>
/// <param name="Confidence">How much comparable history backs the score.</param>
/// <param name="AlgorithmVersion">e.g. "ChargingQualityV1".</param>
public sealed record ChargingQuality(
    double? Score,
    IReadOnlyList<HealthFactor> Components,
    EstimateConfidence Confidence,
    string AlgorithmVersion)
{
    /// <summary>The "not enough history yet" result.</summary>
    public static ChargingQuality NotEnoughData(string algorithmVersion) =>
        new(null, [], EstimateConfidence.Calculating, algorithmVersion);

    /// <summary>Whether a score is available.</summary>
    public bool IsAvailable => Score is not null;
}

/// <summary>
/// The discharge breakdown for one session or window — mean rate and the
/// screen-on vs screen-off split (specification section 11; closes
/// docs/traceability.md R-048).
/// </summary>
/// <param name="AvgRateMw">Mean discharge magnitude in milliwatts, or <see langword="null"/> when no usable rate samples exist.</param>
/// <param name="ScreenOnRateMw">Mean discharge magnitude during screen-on periods, or <see langword="null"/>.</param>
/// <param name="ScreenOffRateMw">Mean discharge magnitude during screen-off periods, or <see langword="null"/> — never extrapolated from screen-on.</param>
/// <param name="ScreenOnFraction">Fraction of the analysed time the screen was on, 0–1.</param>
/// <param name="Confidence">How much of the window had usable rate data.</param>
public sealed record DischargeAnalysis(
    double? AvgRateMw,
    double? ScreenOnRateMw,
    double? ScreenOffRateMw,
    double ScreenOnFraction,
    EstimateConfidence Confidence)
{
    /// <summary>An empty analysis — nothing to break down.</summary>
    public static DischargeAnalysis Empty { get; } = new(null, null, null, 0, EstimateConfidence.Calculating);
}

/// <summary>
/// The rolling remaining-runtime estimate (specification sections 8 and 52;
/// docs/estimation-strategy.md section 3).
/// </summary>
/// <remarks>
/// Any field is <see langword="null"/> when that figure is Unavailable — in
/// particular <see cref="ScreenOff"/> stays <see langword="null"/> until real
/// screen-off history exists, because extrapolating it from screen-on data would
/// be invention. When <see cref="Confidence"/> is
/// <see cref="EstimateConfidence.Calculating"/> the UI shows "Calculating…" and no
/// number at all.
/// </remarks>
/// <param name="AtCurrentUsage">Blended-rate estimate, or <see langword="null"/>.</param>
/// <param name="ScreenOn">Screen-on-rate estimate, or <see langword="null"/>.</param>
/// <param name="ScreenOff">Screen-off-rate estimate, or <see langword="null"/> when no screen-off history exists.</param>
/// <param name="Confidence">Overall confidence tier.</param>
/// <param name="Basis">One-line description of the rate window used.</param>
public sealed record RuntimeEstimate(
    TimeSpan? AtCurrentUsage,
    TimeSpan? ScreenOn,
    TimeSpan? ScreenOff,
    EstimateConfidence Confidence,
    string Basis)
{
    /// <summary>The pre-data state — "Calculating…".</summary>
    public static RuntimeEstimate Calculating { get; } =
        new(null, null, null, EstimateConfidence.Calculating, "Not enough discharge history yet");

    /// <summary>Whether at least the "at current usage" figure is available.</summary>
    public bool IsAvailable => Confidence != EstimateConfidence.Calculating && AtCurrentUsage is not null;
}
