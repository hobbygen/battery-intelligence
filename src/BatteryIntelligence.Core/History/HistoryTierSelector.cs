using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.History;

/// <summary>
/// Picks the aggregate tier for a requested time span, and resolves a
/// <see cref="HistoryRange"/> to a concrete <see cref="DateRange"/>
/// (docs/database.md section 6; specification section 17). Pure — the boundaries
/// are unit-tested without a database.
/// </summary>
public static class HistoryTierSelector
{
    private static readonly TimeSpan RawCutoff = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinuteCutoff = TimeSpan.FromDays(7);
    private static readonly TimeSpan HourCutoff = TimeSpan.FromDays(120);

    /// <summary>
    /// The tier whose granularity keeps the chart legible and the query cheap:
    /// ≤ 6 h raw, ≤ 7 d minute, ≤ 120 d hour, otherwise daily. No tier returns
    /// more than a few thousand rows before downsampling.
    /// </summary>
    public static HistoryTier TierForSpan(TimeSpan span)
    {
        if (span <= RawCutoff)
        {
            return HistoryTier.Raw;
        }

        if (span <= MinuteCutoff)
        {
            return HistoryTier.Minute;
        }

        return span <= HourCutoff ? HistoryTier.Hour : HistoryTier.Daily;
    }

    /// <summary>A short human label for the tier, for the chart caption.</summary>
    public static string Describe(HistoryTier tier) => tier switch
    {
        HistoryTier.Raw => "individual samples",
        HistoryTier.Minute => "minute averages",
        HistoryTier.Hour => "hourly averages",
        _ => "daily aggregates",
    };

    /// <summary>Resolves a range selection to a concrete UTC range ending now.</summary>
    public static DateRange ResolveRange(HistoryRange range, DateTimeOffset nowUtc, DateRange? custom = null)
    {
        if (range == HistoryRange.Custom)
        {
            return custom ?? new DateRange(nowUtc, nowUtc);
        }

        TimeSpan back = range switch
        {
            HistoryRange.Last24Hours => TimeSpan.FromHours(24),
            HistoryRange.Last7Days => TimeSpan.FromDays(7),
            HistoryRange.Last30Days => TimeSpan.FromDays(30),
            HistoryRange.Last90Days => TimeSpan.FromDays(90),
            HistoryRange.LastYear => TimeSpan.FromDays(365),
            _ => TimeSpan.FromHours(24),
        };

        return new DateRange(nowUtc - back, nowUtc);
    }
}
