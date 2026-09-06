using BatteryIntelligence.Analytics;
using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// The analytics orchestrator over a scripted history: it appends a health
/// snapshot, the degradation trend turns from Calculating to a real slope once
/// enough span exists, and insights are non-empty only when an effect clears the
/// gates (docs/estimation-strategy.md §§4, 7). Traceability: R-025, R-027, R-083.
/// </summary>
public sealed class AnalyticsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstPass_AppendsAHealthSnapshot_WithTheReferenceRenormalisation()
    {
        (AnalyticsService service, AnalyticsFakeBattery battery, FakeHealthSnapshotStore health, FakeInsightStore _, FakeAnalyticsReadStore _) = Build();
        battery.Push(AnalyticsFakeBattery.Discharging(Now, 9_000, 22_000, cycleCount: null, retention: 82.0));

        await service.RefreshAsync();

        HealthSnapshotRow snapshot = Assert.Single(health.Rows);
        Assert.Equal("HealthScoreV1", snapshot.AlgorithmVersion);
        Assert.NotNull(snapshot.HealthScore);

        // Cycle count and temperature both absent → the score's contributing weights renormalise to 1.
        Assert.True(service.CurrentHealth.IsAvailable);
        Assert.Equal(1.0, service.CurrentHealth.Factors.Where(f => f.Contributed).Sum(f => f.NormalisedWeight), 3);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DegradationTrend_TurnsFromCalculatingToARealSlope_OnceThirtyDaysOfHistoryExist()
    {
        (AnalyticsService service, AnalyticsFakeBattery battery, FakeHealthSnapshotStore health, _, _) = Build();

        // Seed 40 days of gently declining retention snapshots (older day = higher retention).
        for (int day = 40; day >= 1; day--)
        {
            health.Rows.Add(new HealthSnapshotRow(Now.AddDays(-day), 85.0 - ((40 - day) * 0.03), 38_000, null, 80, "HealthScoreV1"));
        }

        battery.Push(AnalyticsFakeBattery.Discharging(Now, 9_000, 22_000, retention: 83.8));
        await service.RefreshAsync();

        Assert.Null(service.LastError);
        Assert.True(service.Trend.IsAvailable, $"slope={service.Trend.SlopePercentPerMonth} conf={service.Trend.Confidence}");
        Assert.True(service.Trend.SlopePercentPerMonth < 0);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Insights_AreEmpty_WhenNothingClearsTheGates()
    {
        (AnalyticsService service, AnalyticsFakeBattery battery, _, FakeInsightStore insights, FakeAnalyticsReadStore read) = Build();

        // A quiet, consistent history — no rule should fire.
        for (int i = 0; i < 10; i++)
        {
            DateTimeOffset end = Now.AddDays(-2 - (i * 3));
            read.Sessions.Add(new BatterySessionInfo
            {
                BatteryId = "battery0",
                Type = SessionType.Discharging,
                StartUtc = end.AddHours(-3),
                EndUtc = end,
                StartPercentage = 90,
                EndPercentage = 60,
            });
        }

        battery.Push(AnalyticsFakeBattery.Discharging(Now, 9_000, 22_000, retention: 82.0));
        await service.RefreshAsync();

        Assert.Empty(insights.Active);
        Assert.Empty(service.Insights);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Insights_IncludeAWarning_WhenTheHealthTrendIsClearlyDeclining()
    {
        (AnalyticsService service, AnalyticsFakeBattery battery, FakeHealthSnapshotStore health, FakeInsightStore insights, _) = Build();

        // ~1.5 %/month decline over 120 days — well past every gate (older day = higher retention).
        for (int day = 120; day >= 1; day--)
        {
            health.Rows.Add(new HealthSnapshotRow(Now.AddDays(-day), 90.0 - ((120 - day) * 1.5 / 30.0), 38_000, null, 78, "HealthScoreV1"));
        }

        battery.Push(AnalyticsFakeBattery.Discharging(Now, 9_000, 22_000, retention: 84.0));
        await service.RefreshAsync();

        Assert.Contains(insights.Active, i => i.Type == InsightType.HealthDegrading && i.Severity == InsightSeverity.Warning);

        await service.StopAsync(CancellationToken.None);
    }

    private static (AnalyticsService Service, AnalyticsFakeBattery Battery, FakeHealthSnapshotStore Health, FakeInsightStore Insights, FakeAnalyticsReadStore Read) Build()
    {
        AnalyticsFakeBattery battery = new();
        AnalyticsFakeSessions sessions = new();
        FakeRuntimeEstimationService runtime = new();
        FakeAnalyticsReadStore read = new();
        FakeHealthSnapshotStore health = new();
        FakeInsightStore insights = new();
        RuleBasedInsightProvider provider = new();
        AnalyticsFakeSettings settings = new();

        AnalyticsService service = new(
            battery, sessions, runtime, read, health, insights, provider, settings, NullLogger<AnalyticsService>.Instance);

        return (service, battery, health, insights, read);
    }
}
