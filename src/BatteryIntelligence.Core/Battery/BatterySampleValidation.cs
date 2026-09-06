using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Battery;

/// <summary>
/// Plausibility checks applied to every reading before it reaches the UI, the
/// ring buffer, or the database (docs/monitoring-dataflow.md section 4;
/// specification section 63).
/// </summary>
/// <remarks>
/// A value that fails a check is re-graded <see cref="Enums.DataQuality.Suspect"/>
/// rather than dropped: the reading is stored for diagnosis but excluded from
/// aggregates, rates and insights (docs/database.md section 1, point 5, and
/// <see cref="Enums.DataQualityExtensions.IsTrustworthy"/>). This is pure,
/// stateless arithmetic — it does not need a battery to test, only numbers.
/// </remarks>
public static class BatterySampleValidation
{
    private const int MinPlausibleVoltageMv = 1_000;
    private const int MaxPlausibleVoltageMv = 30_000;
    private const double MinPlausibleTemperatureC = -40.0;
    private const double MaxPlausibleTemperatureC = 80.0;
    private const int MaxPlausibleRateMw = 300_000;
    private const double CapacityOverFullTolerance = 1.05;

    /// <summary>
    /// Returns <paramref name="info"/> with any implausible field re-graded
    /// <see cref="Enums.DataQuality.Suspect"/>. Fields that pass are returned
    /// unchanged, including their original grade and source.
    /// </summary>
    public static BatteryInfo ApplyPlausibilityChecks(BatteryInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Measurement<int> remaining = info.RemainingCapacityMWh;
        Measurement<int> full = info.FullChargeCapacityMWh;

        if (remaining.HasValue && remaining.Value!.Value <= 0)
        {
            remaining = ToSuspect(remaining);
        }
        else if (remaining.HasValue && full.HasValue && remaining.Value!.Value > full.Value!.Value * CapacityOverFullTolerance)
        {
            // Firmware occasionally reports a remaining capacity slightly above
            // full during the last moments of charging; only a genuine excess
            // beyond the tolerance is flagged.
            remaining = ToSuspect(remaining);
        }

        Measurement<int> voltage = info.VoltageMv;
        if (voltage.HasValue && (voltage.Value!.Value < MinPlausibleVoltageMv || voltage.Value!.Value > MaxPlausibleVoltageMv))
        {
            voltage = ToSuspect(voltage);
        }

        Measurement<double> temperature = info.TemperatureCelsius;
        if (temperature.HasValue && (temperature.Value!.Value < MinPlausibleTemperatureC || temperature.Value!.Value > MaxPlausibleTemperatureC))
        {
            temperature = ToSuspect(temperature);
        }

        Measurement<int> power = info.PowerMw;
        if (power.HasValue && Math.Abs(power.Value!.Value) > MaxPlausibleRateMw)
        {
            power = ToSuspect(power);
        }

        Measurement<double> percentage = info.Percentage;
        if (percentage.HasValue && (percentage.Value!.Value < 0.0 || percentage.Value!.Value > 100.0))
        {
            double clamped = Math.Clamp(percentage.Value!.Value, 0.0, 100.0);
            percentage = Measurement<double>.Suspect(clamped, percentage.Source);
        }

        // Allocate a new record only if a check actually changed something, so a
        // fully plausible reading — the overwhelming majority — costs nothing
        // extra.
        if (percentage.Equals(info.Percentage)
            && remaining.Equals(info.RemainingCapacityMWh)
            && voltage.Equals(info.VoltageMv)
            && power.Equals(info.PowerMw)
            && temperature.Equals(info.TemperatureCelsius))
        {
            return info;
        }

        return info with
        {
            Percentage = percentage,
            RemainingCapacityMWh = remaining,
            VoltageMv = voltage,
            PowerMw = power,
            TemperatureCelsius = temperature,
        };
    }

    private static Measurement<T> ToSuspect<T>(Measurement<T> measurement)
        where T : struct =>
        Measurement<T>.Suspect(measurement.Value!.Value, measurement.Source);
}
