using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Analytics;

/// <summary>
/// Aggregate usage over a window (specification section 16; docs/ui-navigation.md
/// "Statistics"). Computes directly from <see cref="BatterySessionInfo"/> rows,
/// clipped to the range — sessions are never pruned (retention removes
/// <c>BatterySample</c> rows, not sessions), so this needs no aggregate tier.
/// </summary>
/// <remarks>
/// Range resolution is timezone-aware: "Today" is since <em>local</em> midnight,
/// and a session that straddles midnight is prorated by its overlap with the
/// window (docs/traceability.md R-082 boundary/DST tests). Pure.
/// </remarks>
public static class StatisticsEngine
{
    /// <summary>Resolves a window to a concrete UTC range in the given timezone.</summary>
    public static DateRange ResolveRange(StatisticsWindow window, DateTimeOffset nowUtc, TimeZoneInfo timeZone, DateRange? custom = null)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (window == StatisticsWindow.Custom)
        {
            return custom ?? new DateRange(nowUtc, nowUtc);
        }

        DateTime localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime;
        DateTime localMidnightToday = localNow.Date;

        DateTime localStart = window switch
        {
            StatisticsWindow.Today => localMidnightToday,
            StatisticsWindow.Last7Days => localMidnightToday.AddDays(-6),
            StatisticsWindow.Last30Days => localMidnightToday.AddDays(-29),
            _ => DateTime.MinValue,
        };

        if (window == StatisticsWindow.Lifetime)
        {
            return new DateRange(DateTimeOffset.UnixEpoch, nowUtc);
        }

        DateTimeOffset fromUtc = new(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), timeZone.GetUtcOffset(localStart));
        return new DateRange(fromUtc, nowUtc);
    }

    /// <summary>Summarises the sessions that fall in <paramref name="range"/>.</summary>
    public static StatisticsSummary Summarize(
        StatisticsWindow window, DateRange range, IReadOnlyList<BatterySessionInfo> sessions, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _ = nowUtc;

        long chargeSec = 0, dischargeSec = 0, screenOn = 0, screenOff = 0, sleep = 0;
        double pctCharged = 0, pctDischarged = 0;
        long energyThroughput = 0;
        bool anyEnergy = false;
        int chargeSessions = 0, dischargeSessions = 0;
        List<double> chargeRates = [];
        List<double> dischargeRates = [];
        DataQuality grade = DataQuality.Measured;

        foreach (BatterySessionInfo s in sessions)
        {
            DateTimeOffset start = s.StartUtc;
            DateTimeOffset end = s.EndUtc ?? nowUtc;
            DateTimeOffset clipStart = start > range.FromUtc ? start : range.FromUtc;
            DateTimeOffset clipEnd = end < range.ToUtc ? end : range.ToUtc;
            if (clipEnd <= clipStart)
            {
                continue;
            }

            double totalSeconds = (end - start).TotalSeconds;
            double overlapSeconds = (clipEnd - clipStart).TotalSeconds;
            double fraction = totalSeconds > 0 ? Math.Clamp(overlapSeconds / totalSeconds, 0, 1) : 1;

            long clippedScreenOn = (long)Math.Round(s.ScreenOnSeconds * fraction);
            long clippedScreenOff = (long)Math.Round(s.ScreenOffSeconds * fraction);
            long clippedSleep = (long)Math.Round(s.SleepSeconds * fraction);
            screenOn += clippedScreenOn;
            screenOff += clippedScreenOff;
            sleep += clippedSleep;

            double? pctDelta = s.StartPercentage is double sp && s.EndPercentage is double ep ? ep - sp : null;
            int? capDelta = s.StartCapacityMwh is int sc && s.EndCapacityMwh is int ec ? ec - sc : null;
            double hours = totalSeconds / 3600.0;

            if (s.Type == SessionType.Charging)
            {
                chargeSessions++;
                chargeSec += (long)Math.Round(overlapSeconds);
                if (pctDelta is double pd && pd > 0)
                {
                    pctCharged += pd * fraction;
                }

                if (capDelta is int cd && cd > 0 && hours > 0)
                {
                    chargeRates.Add(cd / hours);
                    energyThroughput += (long)Math.Round(cd * fraction);
                    anyEnergy = true;
                }
            }
            else if (s.Type == SessionType.Discharging)
            {
                dischargeSessions++;
                dischargeSec += (long)Math.Round(overlapSeconds);
                if (pctDelta is double pd && pd < 0)
                {
                    pctDischarged += -pd * fraction;
                }

                if (capDelta is int cd && cd < 0 && hours > 0)
                {
                    dischargeRates.Add(-cd / hours);
                    energyThroughput += (long)Math.Round(-cd * fraction);
                    anyEnergy = true;
                }
            }
        }

        if (chargeSessions == 0 && dischargeSessions == 0)
        {
            return StatisticsSummary.Empty(window, range.FromUtc, range.ToUtc);
        }

        return new StatisticsSummary(
            window,
            range.FromUtc,
            range.ToUtc,
            chargeSec,
            dischargeSec,
            screenOn,
            screenOff,
            sleep,
            Math.Round(pctCharged, 1),
            Math.Round(pctDischarged, 1),
            chargeRates.Count > 0 ? Math.Round(chargeRates.Average(), 0) : null,
            dischargeRates.Count > 0 ? Math.Round(dischargeRates.Average(), 0) : null,
            chargeSessions,
            dischargeSessions,
            anyEnergy ? energyThroughput : null,
            grade);
    }
}
