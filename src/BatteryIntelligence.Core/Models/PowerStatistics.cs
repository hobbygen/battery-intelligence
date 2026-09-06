using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// Min / max / average of one electrical metric over a time window
/// (specification section 13, "Min/max/avg per metric").
/// </summary>
/// <remarks>
/// Every field is nullable: a window with no trustworthy samples yields
/// <see cref="Count"/> 0 and null extremes, which the UI must render as
/// "Not enough data yet" rather than as zeros (docs/estimation-strategy.md
/// section 1). Suspect samples are excluded before these are computed, so
/// <see cref="Grade"/> is the worst grade among the samples that <em>did</em>
/// contribute.
/// </remarks>
/// <param name="Min">Smallest value in the window, or null when empty.</param>
/// <param name="Max">Largest value in the window, or null when empty.</param>
/// <param name="Average">Arithmetic mean over the window, or null when empty.</param>
/// <param name="Count">How many samples contributed.</param>
/// <param name="Grade">Worst grade among the contributing samples.</param>
public sealed record MetricStatistics(
    double? Min,
    double? Max,
    double? Average,
    int Count,
    DataQuality Grade)
{
    /// <summary>An empty result — no samples in the window.</summary>
    public static MetricStatistics Empty { get; } = new(null, null, null, 0, DataQuality.Unknown);

    /// <summary>Whether at least one sample contributed.</summary>
    public bool HasData => Count > 0;
}

/// <summary>
/// The three electrical metrics' <see cref="MetricStatistics"/> for one
/// <see cref="PowerWindow"/>, ready for the Power page's stats strip.
/// </summary>
/// <param name="Power">Energy-rate statistics, milliwatts.</param>
/// <param name="Voltage">Voltage statistics, millivolts.</param>
/// <param name="Current">Current statistics, milliamps (always Calculated).</param>
public sealed record PowerWindowStatistics(
    MetricStatistics Power,
    MetricStatistics Voltage,
    MetricStatistics Current)
{
    /// <summary>An all-empty result.</summary>
    public static PowerWindowStatistics Empty { get; } =
        new(MetricStatistics.Empty, MetricStatistics.Empty, MetricStatistics.Empty);
}
