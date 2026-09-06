using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// A named, unit-labelled sequence of <see cref="TimePoint"/>s — the
/// library-neutral series a ViewModel hands to a chart control
/// (docs/architecture.md section 7).
/// </summary>
/// <remarks>
/// Points are already bounded and, past the point budget, downsampled with
/// min/max preservation (<c>MinMaxDownsampler</c>), so a consumer can bind them
/// directly without further thinning (docs/monitoring-dataflow.md section 7).
/// </remarks>
/// <param name="Label">Human-readable series name, e.g. "Power".</param>
/// <param name="Unit">Unit suffix for axis and tooltip, e.g. "mW".</param>
/// <param name="Points">Time-ordered points, oldest first.</param>
public sealed record ChartSeries(string Label, string Unit, IReadOnlyList<TimePoint> Points)
{
    /// <summary>An empty series with the given label and unit.</summary>
    public static ChartSeries Empty(string label, string unit) => new(label, unit, []);

    /// <summary>Whether the series has any points to draw.</summary>
    public bool HasPoints => Points.Count > 0;
}

/// <summary>
/// The Power page's three synchronised series plus the exact span they cover,
/// so all three charts share one X axis range (docs/ui-navigation.md section 6).
/// </summary>
/// <param name="Power">Energy rate over time, milliwatts.</param>
/// <param name="Voltage">Voltage over time, millivolts.</param>
/// <param name="Current">Current over time, milliamps.</param>
/// <param name="FromUtc">Start of the covered span.</param>
/// <param name="ToUtc">End of the covered span.</param>
public sealed record PowerSeriesSet(
    ChartSeries Power,
    ChartSeries Voltage,
    ChartSeries Current,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc)
{
    /// <summary>An all-empty set spanning an instant.</summary>
    public static PowerSeriesSet Empty { get; } = new(
        ChartSeries.Empty("Power", "mW"),
        ChartSeries.Empty("Voltage", "mV"),
        ChartSeries.Empty("Current", "mA"),
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    /// <summary>Whether any of the three series has points.</summary>
    public bool HasPoints => Power.HasPoints || Voltage.HasPoints || Current.HasPoints;
}
