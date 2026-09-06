using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.History;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.History;

/// <summary>
/// The tier boundaries and range resolution (docs/database.md section 6;
/// specification section 17). Pure — no database.
/// </summary>
public sealed class HistoryTierSelectorTests
{
    [Theory]
    [InlineData(1, HistoryTier.Raw)]
    [InlineData(6, HistoryTier.Raw)]
    [InlineData(7, HistoryTier.Minute)]
    [InlineData(24 * 7, HistoryTier.Minute)]
    [InlineData(24 * 7 + 1, HistoryTier.Hour)]
    [InlineData(24 * 120, HistoryTier.Hour)]
    [InlineData(24 * 120 + 1, HistoryTier.Daily)]
    [InlineData(24 * 365, HistoryTier.Daily)]
    public void TierForSpan_PicksTheTierForTheWindow(int hours, HistoryTier expected)
    {
        Assert.Equal(expected, HistoryTierSelector.TierForSpan(TimeSpan.FromHours(hours)));
    }

    [Fact]
    public void Describe_GivesADistinctLabelPerTier()
    {
        string[] labels =
        [
            HistoryTierSelector.Describe(HistoryTier.Raw),
            HistoryTierSelector.Describe(HistoryTier.Minute),
            HistoryTierSelector.Describe(HistoryTier.Hour),
            HistoryTierSelector.Describe(HistoryTier.Daily),
        ];

        Assert.Equal(labels.Length, labels.Distinct().Count());
        Assert.DoesNotContain(labels, string.IsNullOrWhiteSpace);
    }

    [Theory]
    [InlineData(HistoryRange.Last24Hours, 24)]
    [InlineData(HistoryRange.Last7Days, 24 * 7)]
    [InlineData(HistoryRange.Last30Days, 24 * 30)]
    [InlineData(HistoryRange.Last90Days, 24 * 90)]
    [InlineData(HistoryRange.LastYear, 24 * 365)]
    public void ResolveRange_EndsNowAndSpansTheSelection(HistoryRange range, int expectedHours)
    {
        DateTimeOffset now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        DateRange resolved = HistoryTierSelector.ResolveRange(range, now);

        Assert.Equal(now, resolved.ToUtc);
        Assert.Equal(TimeSpan.FromHours(expectedHours), resolved.Duration);
    }

    [Fact]
    public void ResolveRange_Custom_ReturnsTheSuppliedRange()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateRange custom = new(now.AddDays(-3), now.AddDays(-1));

        DateRange resolved = HistoryTierSelector.ResolveRange(HistoryRange.Custom, now, custom);

        Assert.Equal(custom, resolved);
    }

    [Fact]
    public void ResolveRange_Custom_WithoutARange_CollapsesToAnInstant()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DateRange resolved = HistoryTierSelector.ResolveRange(HistoryRange.Custom, now);

        Assert.Equal(TimeSpan.Zero, resolved.Duration);
    }

    [Fact]
    public void ResolveRange_SpansADstTransition_ByExactElapsedTime()
    {
        // 2026-03-08 is the US spring-forward day. ResolveRange works in UTC, so
        // the elapsed span is exactly 24 h regardless of the local wall clock.
        DateTimeOffset now = new(2026, 3, 9, 6, 0, 0, TimeSpan.Zero);

        DateRange resolved = HistoryTierSelector.ResolveRange(HistoryRange.Last24Hours, now);

        Assert.Equal(TimeSpan.FromHours(24), resolved.Duration);
    }
}
