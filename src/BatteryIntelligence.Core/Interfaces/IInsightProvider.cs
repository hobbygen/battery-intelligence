using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Produces rule-based insights from an <see cref="AnalyticsContext"/>
/// (specification sections 18 and 34). The default implementation is
/// rule-based and conservative; an AI-backed provider is explicitly out of scope
/// for v1 (docs/prd.md "Out of scope").
/// </summary>
/// <remarks>
/// Implementations must apply the four confidence gates from
/// docs/estimation-strategy.md section 7 — minimum sample size, minimum span,
/// effect exceeds the metric's own variance, confidence ≥ threshold — and must
/// emit only fixed, curated text (specification section 77).
/// </remarks>
public interface IInsightProvider
{
    /// <summary>The rule-corpus version, stamped on every emitted insight.</summary>
    string RuleVersion { get; }

    /// <summary>Generates the insights that currently qualify. May be empty — that is the correct result when nothing clears the gates.</summary>
    Task<IReadOnlyList<AnalyticsInsight>> GenerateAsync(AnalyticsContext context, CancellationToken cancellationToken = default);
}
