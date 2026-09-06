using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Analytics;

/// <summary>
/// Breaks a discharge window down into its mean rate and the screen-on vs
/// screen-off split (specification section 11; closes docs/traceability.md R-048).
/// </summary>
/// <remarks>
/// The screen-off rate is only reported when screen-off samples actually exist —
/// it is never derived from screen-on data.
/// </remarks>
public static class DischargeAnalyzer
{
    public static DischargeAnalysis Analyze(RollingRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);

        if (rate.Count == 0)
        {
            return DischargeAnalysis.Empty;
        }

        double? avg = rate.WeightedMeanMw();

        int onCount = rate.CountFor(ScreenState.On);
        int offCount = rate.CountFor(ScreenState.Off) + rate.CountFor(ScreenState.Dimmed);

        double? screenOn = onCount > 0 ? rate.WeightedMeanMw(ScreenState.On) : null;
        double? screenOff = rate.CountFor(ScreenState.Off) > 0 ? rate.WeightedMeanMw(ScreenState.Off) : null;

        double onFraction = onCount + offCount > 0 ? onCount / (double)(onCount + offCount) : 0;

        TimeSpan span = rate.SpanFor();
        EstimateConfidence confidence =
            span >= TimeSpan.FromMinutes(15) && rate.Count >= 10 ? EstimateConfidence.High
            : span >= TimeSpan.FromMinutes(5) && rate.Count >= 4 ? EstimateConfidence.Medium
            : rate.Count >= 2 ? EstimateConfidence.Low
            : EstimateConfidence.Calculating;

        return new DischargeAnalysis(
            avg is double a ? Math.Round(a, 0) : null,
            screenOn is double on ? Math.Round(on, 0) : null,
            screenOff is double off ? Math.Round(off, 0) : null,
            Math.Round(onFraction, 3),
            confidence);
    }
}
