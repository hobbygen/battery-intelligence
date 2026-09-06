using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Battery;

/// <summary>
/// Pure arithmetic for values the specification requires to be derived rather
/// than read, per docs/estimation-strategy.md.
/// </summary>
/// <remarks>
/// Everything here is deterministic, hardware-free maths, which is what keeps it
/// unit-testable without a battery, a WMI namespace or a device handle.
/// </remarks>
public static class BatteryCalculations
{
    /// <summary>Plausible battery voltage range, millivolts. Guards <see cref="CalculateCurrentMa"/>.</summary>
    private const int MinPlausibleVoltageMv = 1_000;
    private const int MaxPlausibleVoltageMv = 30_000;

    /// <summary>
    /// Capacity retention: full-charge capacity as a percentage of design capacity
    /// (C08). Always <see cref="DataQuality.Calculated"/>; <see cref="Measurement{T}.Unavailable"/>
    /// when either input is missing or design capacity is non-positive.
    /// </summary>
    /// <remarks>
    /// Specification section 9 forbids inventing a health percentage when the
    /// inputs do not exist — there is deliberately no fallback value here.
    /// </remarks>
    public static Measurement<double> CalculateRetentionPercent(
        Measurement<int> fullChargeMWh,
        Measurement<int> designMWh)
    {
        if (designMWh.Value is int design && design <= 0)
        {
            return Measurement<double>.Unavailable(MeasurementSource.Derived);
        }

        return Measurement.Combine(
            fullChargeMWh,
            designMWh,
            (full, design) => full / (double)design * 100.0,
            DataQuality.Calculated,
            MeasurementSource.Derived);
    }

    /// <summary>
    /// Electric current: power divided by voltage (C11). Always
    /// <see cref="DataQuality.Calculated"/>, never <see cref="DataQuality.Measured"/>
    /// — the battery reports power and voltage, never current itself
    /// (docs/capability-matrix.md, "grades that must never be promoted").
    /// </summary>
    /// <remarks>
    /// Voltage must fall within a physically plausible range or the result is
    /// Unavailable rather than an absurd or infinite figure
    /// (docs/estimation-strategy.md section 2).
    /// </remarks>
    public static Measurement<double> CalculateCurrentMa(Measurement<int> powerMw, Measurement<int> voltageMv)
    {
        if (voltageMv.Value is int voltage && (voltage < MinPlausibleVoltageMv || voltage > MaxPlausibleVoltageMv))
        {
            return Measurement<double>.Unavailable(MeasurementSource.Derived);
        }

        return Measurement.Combine(
            powerMw,
            voltageMv,
            (power, voltage) => power / (double)voltage * 1000.0,
            DataQuality.Calculated,
            MeasurementSource.Derived);
    }

    /// <summary>
    /// Normalises a rate/capacity reported in milliamps/milliamp-hours to
    /// milliwatts/milliwatt-hours via design voltage (quirk Q2).
    /// </summary>
    /// <remarks>
    /// A value converted this way is <see cref="DataQuality.Calculated"/>, never
    /// <see cref="DataQuality.Measured"/> — arithmetic was applied to what the
    /// firmware reported. If the device does not report in milliamps, the raw
    /// value passes through unchanged with its original grade.
    /// </remarks>
    public static Measurement<int> NormalizeMilliampsToMilliwatts(
        Measurement<int> rawValue,
        bool reportsInMilliamps,
        Measurement<int> designVoltageMv)
    {
        if (!reportsInMilliamps)
        {
            return rawValue;
        }

        if (rawValue.Value is not int milliamps || designVoltageMv.Value is not int voltage || voltage <= 0)
        {
            return Measurement<int>.Unavailable(MeasurementSource.Derived);
        }

        int milliwatts = (int)Math.Round(milliamps * voltage / 1000.0);
        return Measurement<int>.Calculated(milliwatts);
    }

    /// <summary>
    /// Applies quirk Q5: a firmware-reported cycle count of exactly zero on a
    /// battery that is not new means "not reported", not "brand new", and must be
    /// excluded rather than scored as a pristine battery.
    /// </summary>
    public static Measurement<int> ApplyCycleCountZeroQuirk(Measurement<int> cycleCount) =>
        cycleCount.Value == 0 ? Measurement<int>.Unavailable(cycleCount.Source) : cycleCount;
}
