namespace BatteryIntelligence.Core.Primitives;

/// <summary>
/// One point on a time series: a value at an instant. The library-neutral unit
/// the charting seam trades in (docs/architecture.md section 7 — ViewModels
/// expose plain domain series, only the chart control binding is library-specific).
/// </summary>
/// <param name="TimestampUtc">When the value was observed, in UTC.</param>
/// <param name="Value">The observed value, in the series' own unit.</param>
public readonly record struct TimePoint(DateTimeOffset TimestampUtc, double Value);
