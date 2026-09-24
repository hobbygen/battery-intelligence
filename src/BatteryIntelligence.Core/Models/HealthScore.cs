using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// One factor's contribution to a health or charging-quality score — the shape
/// serialised into <c>FactorsJson</c> and rendered by "How this score is
/// calculated" (docs/estimation-strategy.md section 4; specification section 19).
/// </summary>
/// <param name="Key">Stable identifier, e.g. "retention".</param>
/// <param name="Label">Human-readable name.</param>
/// <param name="Weight">The factor's design weight before renormalisation, or — for a
/// <see cref="HealthFactorRole.Penalty"/> factor — the largest share of the score it can remove.</param>
/// <param name="NormalisedWeight">Its weight after renormalising across the available
/// weighted factors (those sum to 1.0). Always 0 for a penalty factor, which takes no
/// share of the weighted mean.</param>
/// <param name="Score01">The factor's own 0–1 sub-score, or <see langword="null"/> when the factor is unavailable and its weight was redistributed.</param>
/// <param name="Basis">A one-line explanation of what produced <paramref name="Score01"/>.</param>
/// <param name="Role">Whether the factor is averaged into the score or can only subtract from it.</param>
public sealed record HealthFactor(
    string Key,
    string Label,
    double Weight,
    double NormalisedWeight,
    double? Score01,
    string Basis,
    HealthFactorRole Role = HealthFactorRole.Weighted)
{
    /// <summary>Whether this factor contributed (had data) or had its weight redistributed.</summary>
    public bool Contributed => Score01 is not null;
}

/// <summary>How a <see cref="HealthFactor"/> enters the score.</summary>
public enum HealthFactorRole
{
    /// <summary>Averaged into the score with <see cref="HealthFactor.NormalisedWeight"/>.</summary>
    Weighted = 0,

    /// <summary>
    /// Can only subtract points, never add them. Used for factors that describe how
    /// the machine is used rather than the state of the cell: gentle use is not
    /// evidence of a healthy battery, so it must not raise the score, while harsh
    /// use is a legitimate reason to lower it.
    /// </summary>
    Penalty = 1,
}

/// <summary>
/// A Battery Health Score — always labelled "Battery Health Score", never
/// "Battery Health" alone, always beside its methodology (specification section 19).
/// </summary>
/// <remarks>
/// <see cref="Score"/> is <see langword="null"/> — Unavailable — when capacity
/// retention could not be computed. A score built only on behavioural proxies
/// would be "a number with nothing real underneath it" (docs/estimation-strategy.md
/// section 4), so it is not produced.
/// </remarks>
/// <param name="Score">0–100, or <see langword="null"/> when Unavailable.</param>
/// <param name="Category">The band, or <see cref="HealthCategory.Unknown"/> when Unavailable.</param>
/// <param name="Factors">Every factor, contributing or redistributed, for the explanation.</param>
/// <param name="AlgorithmVersion">e.g. "HealthScoreV1" — stored per snapshot so a v2 cannot rewrite past history.</param>
/// <param name="TimestampUtc">When the score was computed.</param>
/// <param name="Grade">Always <see cref="DataQuality.Estimated"/> when a score exists — it is a model output.</param>
public sealed record HealthScore(
    double? Score,
    HealthCategory Category,
    IReadOnlyList<HealthFactor> Factors,
    string AlgorithmVersion,
    DateTimeOffset TimestampUtc,
    DataQuality Grade)
{
    /// <summary>Whether a usable score exists.</summary>
    public bool IsAvailable => Score is not null;

    /// <summary>An Unavailable score with an explanation of the missing input.</summary>
    public static HealthScore Unavailable(string algorithmVersion, DateTimeOffset timestampUtc, IReadOnlyList<HealthFactor> factors) =>
        new(null, HealthCategory.Unknown, factors, algorithmVersion, timestampUtc, DataQuality.Unknown);
}
