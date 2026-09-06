using BatteryIntelligence.Core.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Unit.Analytics;

/// <summary>
/// The statistics engine — timezone-aware window resolution and midnight-straddling
/// session proration (specification section 16). Traceability: R-082.
/// </summary>
public sealed class StatisticsEngineTests
{
    // UTC+2, no DST for simplicity in the range tests.
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(2), "t", "t");

    [Fact]
    public void ResolveRange_Today_StartsAtLocalMidnight()
    {
        DateTimeOffset now = new(2026, 9, 6, 8, 30, 0, TimeSpan.Zero); // 10:30 local
        DateRange range = StatisticsEngine.ResolveRange(StatisticsWindow.Today, now, Tz);

        DateTimeOffset expectedLocalMidnightUtc = new(2026, 9, 5, 22, 0, 0, TimeSpan.Zero); // 2026-09-06 00:00 +02
        Assert.Equal(expectedLocalMidnightUtc, range.FromUtc);
        Assert.Equal(now, range.ToUtc);
    }

    [Fact]
    public void ResolveRange_Last7Days_SpansSevenCalendarDays()
    {
        DateTimeOffset now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        DateRange range = StatisticsEngine.ResolveRange(StatisticsWindow.Last7Days, now, Tz);

        Assert.Equal(6.0, (range.ToUtc.Date - range.FromUtc.Date).TotalDays, 0);
    }

    [Fact]
    public void Summarize_ASessionStraddlingTheWindowStart_IsProrated()
    {
        DateTimeOffset windowStart = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        // A 4-hour discharge session, half before the window, half inside.
        BatterySessionInfo session = new()
        {
            BatteryId = "b0",
            Type = SessionType.Discharging,
            StartUtc = windowStart.AddHours(-2),
            EndUtc = windowStart.AddHours(2),
            StartPercentage = 80,
            EndPercentage = 40,
            ScreenOnSeconds = 3600,
            ScreenOffSeconds = 3600,
        };

        StatisticsSummary summary = StatisticsEngine.Summarize(
            StatisticsWindow.Today, new DateRange(windowStart, now), [session], now);

        Assert.Equal(1, summary.DischargeSessions);
        Assert.Equal(7200, summary.DischargingSeconds); // 2h of the 4h fell inside
        Assert.Equal(20.0, summary.PercentDischarged, 1); // half of the 40-point drop
        Assert.Equal(1800, summary.ScreenOnSeconds);
    }

    [Fact]
    public void Summarize_EmptyWindow_YieldsZeros_NotNullsAsNumbers()
    {
        DateTimeOffset now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        StatisticsSummary summary = StatisticsEngine.Summarize(
            StatisticsWindow.Last30Days, new DateRange(now.AddDays(-30), now), [], now);

        Assert.False(summary.HasData);
        Assert.Equal(0, summary.DischargingSeconds);
        Assert.Equal(DataQuality.Unknown, summary.Grade);
    }

    [Fact]
    public void Summarize_DstTransitionDay_DoesNotThrow_AndCountsTheSession()
    {
        // A timezone that actually has DST.
        TimeZoneInfo dstZone;
        try
        {
            dstZone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Central European Standard Time" : "Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return; // environment without the zone database — skip
        }

        DateTimeOffset now = new(2026, 3, 29, 12, 0, 0, TimeSpan.Zero); // EU spring-forward day
        DateRange range = StatisticsEngine.ResolveRange(StatisticsWindow.Today, now, dstZone);

        BatterySessionInfo s = new()
        {
            BatteryId = "b0",
            Type = SessionType.Charging,
            StartUtc = range.FromUtc.AddHours(1),
            EndUtc = range.FromUtc.AddHours(2),
            StartPercentage = 50,
            EndPercentage = 90,
        };

        StatisticsSummary summary = StatisticsEngine.Summarize(StatisticsWindow.Today, range, [s], now);
        Assert.Equal(1, summary.ChargeSessions);
    }
}
