using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Analytics;

/// <summary>
/// The rule-based insight provider — the four confidence gates, and above all
/// "effect must exceed noise" so within-variation deviations produce nothing
/// (docs/estimation-strategy.md section 7). Traceability: R-083, R-084.
/// This is a Phase 8 exit criterion.
/// </summary>
public sealed class RuleBasedInsightProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly RuleBasedInsightProvider _provider = new();

    [Fact]
    public void RuleVersion_IsStable()
    {
        Assert.Equal("InsightRulesV1", _provider.RuleVersion);
    }

    [Fact]
    public async Task WithinNoiseVariation_ProducesNoInsights()
    {
        // Prior discharge sessions around 8 %/h with real spread; recent ones a
        // hair faster but well inside that spread.
        double[] priorRates = [7.0, 9.0, 6.5, 9.5, 8.0, 7.5, 8.5, 10.0, 6.0];
        double[] recentRates = [8.6, 8.8, 8.4];

        AnalyticsContext context = Context(
            BuildDischargeSessions(priorRates, recentRates),
            DegradationTrend.NotEnoughData(3, Now.AddDays(-10), Now));

        IReadOnlyList<AnalyticsInsight> insights = await _provider.GenerateAsync(context);

        Assert.Empty(insights);
    }

    [Fact]
    public async Task AGenuineFastDrain_PastEveryGate_ProducesExactlyOneInsight()
    {
        double[] priorRates = [7.8, 8.2, 8.0, 7.9, 8.1, 8.0, 7.7, 8.3];
        double[] recentRates = [13.0, 12.5, 13.5, 12.8]; // ~60% faster, far outside the ~0.2 sd

        AnalyticsContext context = Context(
            BuildDischargeSessions(priorRates, recentRates),
            DegradationTrend.NotEnoughData(3, Now.AddDays(-10), Now));

        IReadOnlyList<AnalyticsInsight> insights = await _provider.GenerateAsync(context);

        AnalyticsInsight insight = Assert.Single(insights);
        Assert.Equal(InsightType.FastDrain, insight.Type);
        Assert.Equal("InsightRulesV1", insight.RuleVersion);
        Assert.True(insight.Confidence >= 0.7);
        Assert.True(insight.Supporting.ContainsKey("baselineRate"));
    }

    [Fact]
    public async Task AConfirmedHealthDecline_ProducesAWarning()
    {
        AnalyticsContext context = Context(
            [],
            new DegradationTrend(
                SlopePercentPerMonth: -1.2,
                ProjectedRetentionPercentIn90Days: 76.0,
                Confidence: EstimateConfidence.High,
                SampleCount: 40,
                FromUtc: Now.AddDays(-120),
                ToUtc: Now));

        IReadOnlyList<AnalyticsInsight> insights = await _provider.GenerateAsync(context);

        Assert.Contains(insights, i => i.Type == InsightType.HealthDegrading && i.Severity == InsightSeverity.Warning);
    }

    [Fact]
    public async Task ConfidenceBelowTheThreshold_Suppresses()
    {
        AnalyticsContext context = Context(
            [],
            new DegradationTrend(-1.2, 76.0, EstimateConfidence.High, 40, Now.AddDays(-120), Now))
            with { ConfidenceThreshold = 0.95 };

        IReadOnlyList<AnalyticsInsight> insights = await _provider.GenerateAsync(context);

        Assert.DoesNotContain(insights, i => i.Type == InsightType.HealthDegrading);
    }

    private static AnalyticsContext Context(IReadOnlyList<BatterySessionInfo> sessions, DegradationTrend trend) =>
        new(sessions, trend, DischargeAnalysis.Empty, null, 45.0, 0.7, Now);

    private static List<BatterySessionInfo> BuildDischargeSessions(double[] priorPercentPerHour, double[] recentPercentPerHour)
    {
        List<BatterySessionInfo> sessions = [];

        // Prior: spread over 10–55 days ago.
        for (int i = 0; i < priorPercentPerHour.Length; i++)
        {
            DateTimeOffset end = Now.AddDays(-12 - (i * 5));
            sessions.Add(Discharge(end.AddHours(-2), end, 2 * priorPercentPerHour[i]));
        }

        // Recent: last 6 days.
        for (int i = 0; i < recentPercentPerHour.Length; i++)
        {
            DateTimeOffset end = Now.AddDays(-1 - i);
            sessions.Add(Discharge(end.AddHours(-2), end, 2 * recentPercentPerHour[i]));
        }

        return sessions;
    }

    private static BatterySessionInfo Discharge(DateTimeOffset start, DateTimeOffset end, double percentDrop) => new()
    {
        BatteryId = "b0",
        Type = SessionType.Discharging,
        StartUtc = start,
        EndUtc = end,
        StartPercentage = 90,
        EndPercentage = Math.Max(2, 90 - percentDrop),
    };
}
