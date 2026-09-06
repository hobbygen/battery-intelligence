using BatteryIntelligence.Core.Enums;

namespace BatteryIntelligence.Core.Thermal;

/// <summary>
/// The warning and critical battery-temperature thresholds, and the pure
/// classification they drive. Pure, no dependencies (spec §3 core principle
/// applies to thermal exactly as it does to power).
/// </summary>
/// <param name="WarnCelsius">At or above this, the reading is a warning.</param>
/// <param name="CriticalCelsius">At or above this, the reading is critical.</param>
public sealed record TemperatureThresholds(double WarnCelsius, double CriticalCelsius)
{
    /// <summary>Conservative defaults — the alert threshold (45 °C) plus a 7 °C critical margin.</summary>
    public static TemperatureThresholds Default { get; } = new(45.0, 52.0);

    /// <summary>Builds thresholds from a configured warning value (docs/… AlertSettings.HighTemperatureCelsius).</summary>
    public static TemperatureThresholds FromWarnCelsius(double warnCelsius) =>
        new(warnCelsius, warnCelsius + 7.0);

    /// <summary>Severity of a reading relative to these thresholds.</summary>
    public TemperatureSeverity Classify(double celsius)
    {
        if (celsius >= CriticalCelsius)
        {
            return TemperatureSeverity.Critical;
        }

        return celsius >= WarnCelsius ? TemperatureSeverity.Warning : TemperatureSeverity.Normal;
    }
}

/// <summary>
/// The fixed-boundary band classifier for the per-band time breakdown
/// (<see cref="TemperatureBand"/>). Pure.
/// </summary>
public static class TemperatureBandClassifier
{
    /// <summary>Which band a Celsius reading falls in.</summary>
    public static TemperatureBand Classify(double celsius) => celsius switch
    {
        < 30.0 => TemperatureBand.Cool,
        < 40.0 => TemperatureBand.Normal,
        < 45.0 => TemperatureBand.Warm,
        _ => TemperatureBand.Hot,
    };

    /// <summary>A short label for a band, including its range.</summary>
    public static string Describe(TemperatureBand band) => band switch
    {
        TemperatureBand.Cool => "Cool · below 30 °C",
        TemperatureBand.Normal => "Normal · 30–40 °C",
        TemperatureBand.Warm => "Warm · 40–45 °C",
        _ => "Hot · above 45 °C",
    };
}
