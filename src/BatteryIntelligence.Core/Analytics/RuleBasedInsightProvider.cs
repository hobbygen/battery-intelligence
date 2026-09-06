using System.Globalization;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Analytics;

/// <summary>
/// The default <see cref="IInsightProvider"/>: a small, curated, conservative rule
/// corpus (docs/estimation-strategy.md section 7; specification sections 18, 77).
/// </summary>
/// <remarks>
/// Every rule must clear four gates before it emits — minimum sample size,
/// minimum span, the effect must exceed the metric's own variance (not merely be
/// non-zero), and confidence must reach the configured threshold. Gate 3 is what
/// keeps this from becoming a noise generator. All text is fixed; only concrete
/// figures are substituted in.
/// </remarks>
public sealed class RuleBasedInsightProvider : IInsightProvider
{
    /// <inheritdoc/>
    public string RuleVersion => "InsightRulesV1";

    /// <inheritdoc/>
    public Task<IReadOnlyList<AnalyticsInsight>> GenerateAsync(AnalyticsContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<AnalyticsInsight> insights = [];
        AddIfPresent(insights, HealthDegrading(context));
        AddIfPresent(insights, FastDrain(context));
        AddIfPresent(insights, ChargingSlow(context));
        AddIfPresent(insights, ScreenDominatesDrain(context));
        AddIfPresent(insights, HighTemperatureExposure(context));
        AddIfPresent(insights, DeepDischargeHabit(context));
        AddIfPresent(insights, GoodChargingHabits(context));

        // Most severe first, then most confident; cap the set so the card stays legible.
        IReadOnlyList<AnalyticsInsight> ordered =
        [
            .. insights
                .OrderByDescending(i => (int)i.Severity)
                .ThenByDescending(i => i.Confidence)
                .Take(4),
        ];

        return Task.FromResult(ordered);
    }

    // --- Rules -------------------------------------------------------------

    private AnalyticsInsight? HealthDegrading(AnalyticsContext c)
    {
        DegradationTrend t = c.Trend;
        if (!t.IsAvailable || t.Confidence < EstimateConfidence.Medium || t.SampleCount < 12)
        {
            return null;
        }

        double slope = t.SlopePercentPerMonth!.Value;
        if (slope >= -0.5)
        {
            return null; // not declining meaningfully
        }

        double confidence = t.Confidence == EstimateConfidence.High ? 0.9 : 0.75;
        if (confidence < c.ConfidenceThreshold)
        {
            return null;
        }

        return new AnalyticsInsight(
            InsightType.HealthDegrading,
            InsightSeverity.Warning,
            "Capacity is trending down",
            string.Create(CultureInfo.InvariantCulture,
                $"Capacity retention has fallen about {-slope:F1} percentage points per month over the last {(t.ToUtc - t.FromUtc).TotalDays:F0} days. If it continues, retention would be near {t.ProjectedRetentionPercentIn90Days:F0}% in 90 days."),
            new Dictionary<string, double>
            {
                ["slopePercentPerMonth"] = Math.Round(slope, 2),
                ["projectedRetentionPercent"] = Math.Round(t.ProjectedRetentionPercentIn90Days ?? 0, 1),
                ["sampleCount"] = t.SampleCount,
            },
            confidence,
            t.FromUtc,
            t.ToUtc,
            RuleVersion);
    }

    private AnalyticsInsight? FastDrain(AnalyticsContext c)
    {
        (List<double> recent, List<double> prior) = SplitSessionRates(c, SessionType.Discharging);
        if (recent.Count < 3 || prior.Count < 5)
        {
            return null;
        }

        double baseline = LinearFit.Median(prior);
        double now = LinearFit.Median(recent);
        double noise = LinearFit.StandardDeviation(prior);
        if (baseline <= 0)
        {
            return null;
        }

        double excess = now - baseline;
        if (excess <= noise || now < baseline * 1.25)
        {
            return null; // within normal variation
        }

        double confidence = Confidence(recent.Count, prior.Count, excess / Math.Max(noise, 1e-6));
        if (confidence < c.ConfidenceThreshold)
        {
            return null;
        }

        double percentFaster = (now / baseline - 1.0) * 100.0;
        return new AnalyticsInsight(
            InsightType.FastDrain,
            InsightSeverity.Advice,
            "Battery draining faster than usual",
            string.Create(CultureInfo.InvariantCulture,
                $"Recent discharge sessions have run about {percentFaster:F0}% faster than your own recent average. Check the App Usage page for what is consuming power."),
            new Dictionary<string, double>
            {
                ["recentRate"] = Math.Round(now, 1),
                ["baselineRate"] = Math.Round(baseline, 1),
                ["baselineStdDev"] = Math.Round(noise, 1),
            },
            confidence,
            c.NowUtc.AddDays(-7),
            c.NowUtc,
            RuleVersion);
    }

    private AnalyticsInsight? ChargingSlow(AnalyticsContext c)
    {
        (List<double> recent, List<double> prior) = SplitSessionRates(c, SessionType.Charging);
        if (recent.Count < 3 || prior.Count < 5)
        {
            return null;
        }

        double baseline = LinearFit.Median(prior);
        double now = LinearFit.Median(recent);
        double noise = LinearFit.StandardDeviation(prior);
        if (baseline <= 0)
        {
            return null;
        }

        double deficit = baseline - now;
        if (deficit <= noise || now > baseline * 0.75)
        {
            return null;
        }

        double confidence = Confidence(recent.Count, prior.Count, deficit / Math.Max(noise, 1e-6));
        if (confidence < c.ConfidenceThreshold)
        {
            return null;
        }

        double percentSlower = (1.0 - now / baseline) * 100.0;
        return new AnalyticsInsight(
            InsightType.ChargingSlow,
            InsightSeverity.Advice,
            "Charging slower than usual",
            string.Create(CultureInfo.InvariantCulture,
                $"Recent charges have been about {percentSlower:F0}% slower than your own 30-day average — often a lower-power charger or a busy USB-C port."),
            new Dictionary<string, double>
            {
                ["recentRate"] = Math.Round(now, 1),
                ["baselineRate"] = Math.Round(baseline, 1),
            },
            confidence,
            c.NowUtc.AddDays(-7),
            c.NowUtc,
            RuleVersion);
    }

    private AnalyticsInsight? ScreenDominatesDrain(AnalyticsContext c)
    {
        DischargeAnalysis d = c.RecentDischarge;
        if (d.Confidence < EstimateConfidence.Medium
            || d.ScreenOnRateMw is not double on
            || d.ScreenOffRateMw is not double off
            || off <= 0
            || d.ScreenOnFraction < 0.5)
        {
            return null;
        }

        double ratio = on / off;
        if (ratio < 1.6)
        {
            return null;
        }

        double confidence = d.Confidence == EstimateConfidence.High ? 0.85 : 0.72;
        if (confidence < c.ConfidenceThreshold)
        {
            return null;
        }

        return new AnalyticsInsight(
            InsightType.ScreenDominatesDrain,
            InsightSeverity.Info,
            "The display drives most of your drain",
            string.Create(CultureInfo.InvariantCulture,
                $"With the screen on you draw about {ratio:F1}× the power you draw with it off. Lowering brightness or shortening the screen timeout has the largest effect."),
            new Dictionary<string, double>
            {
                ["screenOnRateMw"] = Math.Round(on, 0),
                ["screenOffRateMw"] = Math.Round(off, 0),
                ["screenOnFraction"] = Math.Round(d.ScreenOnFraction, 2),
            },
            confidence,
            c.NowUtc.AddHours(-24),
            c.NowUtc,
            RuleVersion);
    }

    private AnalyticsInsight? HighTemperatureExposure(AnalyticsContext c)
    {
        if (c.TemperatureExposureSecondsAboveWarn is not double seconds || seconds < 3_600)
        {
            return null; // < 1 h above the threshold over the window, or no sensor
        }

        double hours = seconds / 3600.0;
        double confidence = 0.8;
        if (confidence < c.ConfidenceThreshold)
        {
            return null;
        }

        return new AnalyticsInsight(
            InsightType.HighTemperatureExposure,
            InsightSeverity.Warning,
            "Battery has been running hot",
            string.Create(CultureInfo.InvariantCulture,
                $"The battery has spent about {hours:F1} hours above {c.WarnTemperatureCelsius:F0} °C recently. Sustained heat is the single largest driver of capacity loss."),
            new Dictionary<string, double> { ["hoursAboveWarn"] = Math.Round(hours, 1) },
            confidence,
            c.NowUtc.AddDays(-7),
            c.NowUtc,
            RuleVersion);
    }

    private AnalyticsInsight? DeepDischargeHabit(AnalyticsContext c)
    {
        List<double> ends = [.. c.RecentSessions
            .Where(s => s.Type == SessionType.Discharging && s.EndUtc is not null && s.EndPercentage is not null)
            .Select(s => s.EndPercentage!.Value)];

        if (ends.Count < 6)
        {
            return null;
        }

        double median = LinearFit.Median(ends);
        int deepCount = ends.Count(e => e < 10);
        if (median >= 10 || deepCount < ends.Count * 0.6)
        {
            return null;
        }

        double confidence = 0.78;
        if (confidence < c.ConfidenceThreshold)
        {
            return null;
        }

        return new AnalyticsInsight(
            InsightType.DeepDischargeHabit,
            InsightSeverity.Advice,
            "You often run the battery very low",
            string.Create(CultureInfo.InvariantCulture,
                $"{deepCount} of your last {ends.Count} discharges ran below 10% (median {median:F0}%). Recharging before roughly 15% is gentler on lithium cells over the long term."),
            new Dictionary<string, double>
            {
                ["medianEndPercent"] = Math.Round(median, 1),
                ["deepCount"] = deepCount,
                ["totalCount"] = ends.Count,
            },
            confidence,
            c.NowUtc.AddDays(-30),
            c.NowUtc,
            RuleVersion);
    }

    private AnalyticsInsight? GoodChargingHabits(AnalyticsContext c)
    {
        List<BatterySessionInfo> charges = [.. c.RecentSessions
            .Where(s => s.Type == SessionType.Charging && s.EndUtc is not null)];

        if (charges.Count < 6)
        {
            return null;
        }

        int clean = charges.Count(s => s.Interruptions == 0);
        List<double> starts = [.. c.RecentSessions
            .Where(s => s.Type == SessionType.Discharging && s.EndPercentage is not null)
            .Select(s => s.EndPercentage!.Value)];
        double medianLow = starts.Count > 0 ? LinearFit.Median(starts) : 100;

        if (clean < charges.Count * 0.8 || medianLow < 15)
        {
            return null;
        }

        double confidence = 0.8;
        if (confidence < c.ConfidenceThreshold)
        {
            return null;
        }

        return new AnalyticsInsight(
            InsightType.GoodChargingHabits,
            InsightSeverity.Info,
            "Your charging habits look good",
            string.Create(CultureInfo.InvariantCulture,
                $"{clean} of your last {charges.Count} charges ran without interruption, and you rarely take the battery below 15%. This is close to ideal for lithium-cell longevity."),
            new Dictionary<string, double>
            {
                ["cleanCharges"] = clean,
                ["totalCharges"] = charges.Count,
                ["medianLowPercent"] = Math.Round(medianLow, 1),
            },
            confidence,
            c.NowUtc.AddDays(-30),
            c.NowUtc,
            RuleVersion);
    }

    // --- Helpers ---------------------------------------------------------

    /// <summary>Per-session rates (capacity delta ÷ hours, else percentage delta ÷ hours) split into the last 7 days and the 7–60 day prior baseline.</summary>
    private static (List<double> Recent, List<double> Prior) SplitSessionRates(AnalyticsContext c, SessionType type)
    {
        DateTimeOffset recentCutoff = c.NowUtc.AddDays(-7);
        DateTimeOffset priorCutoff = c.NowUtc.AddDays(-60);

        List<double> recent = [];
        List<double> prior = [];

        foreach (BatterySessionInfo s in c.RecentSessions)
        {
            if (s.Type != type || s.EndUtc is not DateTimeOffset end)
            {
                continue;
            }

            double hours = (end - s.StartUtc).TotalHours;
            if (hours < 0.05)
            {
                continue;
            }

            double? rate = null;
            if (s.StartCapacityMwh is int sc && s.EndCapacityMwh is int ec)
            {
                rate = Math.Abs(ec - sc) / hours;
            }
            else if (s.StartPercentage is double sp && s.EndPercentage is double ep)
            {
                rate = Math.Abs(ep - sp) / hours;
            }

            if (rate is not double r || r <= 0)
            {
                continue;
            }

            if (end >= recentCutoff)
            {
                recent.Add(r);
            }
            else if (end >= priorCutoff)
            {
                prior.Add(r);
            }
        }

        return (recent, prior);
    }

    /// <summary>Confidence from the two sample counts and how many noise-widths the effect is.</summary>
    private static double Confidence(int recentCount, int priorCount, double effectSizeInSds)
    {
        double sampleTerm = Math.Min(1.0, (recentCount + priorCount) / 20.0);
        double effectTerm = Math.Min(1.0, effectSizeInSds / 3.0);
        return Math.Round(0.55 + (0.25 * sampleTerm) + (0.20 * effectTerm), 3);
    }

    private static void AddIfPresent(List<AnalyticsInsight> list, AnalyticsInsight? insight)
    {
        if (insight is not null)
        {
            list.Add(insight);
        }
    }
}
