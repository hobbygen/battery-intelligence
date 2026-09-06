using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// One rule-based insight (specification section 18; docs/estimation-strategy.md
/// section 7). Maps 1:1 to an <c>Insight</c> row.
/// </summary>
/// <remarks>
/// Emitted only when all four confidence gates pass — minimum sample size,
/// minimum span, effect exceeds the metric's own variance, and confidence ≥ the
/// configured threshold. The <see cref="Title"/> and <see cref="Explanation"/> are
/// fixed curated strings, never generated (specification section 77).
/// </remarks>
/// <param name="Type">Which rule produced it.</param>
/// <param name="Severity">How prominently to show it.</param>
/// <param name="Title">Fixed headline.</param>
/// <param name="Explanation">Fixed body, with the concrete figures substituted in.</param>
/// <param name="Supporting">The metrics behind the insight, for the "what data was used" disclosure.</param>
/// <param name="Confidence">0–1; shown to the user.</param>
/// <param name="PeriodStartUtc">Start of the period the insight is about.</param>
/// <param name="PeriodEndUtc">End of that period.</param>
/// <param name="RuleVersion">e.g. "InsightRulesV1" — stored so the corpus can change without reinterpreting old rows.</param>
public sealed record AnalyticsInsight(
    InsightType Type,
    InsightSeverity Severity,
    string Title,
    string Explanation,
    IReadOnlyDictionary<string, double> Supporting,
    double Confidence,
    DateTimeOffset? PeriodStartUtc,
    DateTimeOffset? PeriodEndUtc,
    string RuleVersion)
{
    /// <summary>The database id once persisted, or <see langword="null"/>.</summary>
    public long? Id { get; init; }
}
