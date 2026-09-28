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
    /// Lowest retention treated as a real reading. A pack below this is not a worn
    /// battery, it is two capacities expressed in different units — the milliamp/
    /// milliwatt mix-up divides retention by roughly the pack voltage in volts, so a
    /// healthy pack surfaces here in the single digits. Firmware declares a pack
    /// end-of-life long before genuine retention reaches this figure.
    /// </summary>
    private const double MinPlausibleRetentionPercent = 15.0;

    /// <summary>
    /// Highest retention treated as a real reading. Slightly over 100% is ordinary
    /// on a new pack — manufacturers under-state design capacity and firmware
    /// recalibrates upward — but a large excess is the same unit mix-up inverted.
    /// </summary>
    private const double MaxPlausibleRetentionPercent = 125.0;

    /// <summary>
    /// Capacity retention: full-charge capacity as a percentage of design capacity
    /// (C08). <see cref="Measurement{T}.Unavailable"/> when either input is missing
    /// or design capacity is non-positive, and <see cref="Measurement{T}.Suspect"/>
    /// when the ratio is outside the physically plausible band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specification section 9 forbids inventing a health percentage when the
    /// inputs do not exist — there is deliberately no fallback value here.
    /// </para>
    /// <para>
    /// The plausibility band exists because retention is the mandatory input to the
    /// Battery Health Score. An implausible ratio that is merely clamped becomes a
    /// confident verdict: clamped low it rates a new pack Poor, clamped high it
    /// rates a worn one Excellent. Graded Suspect it is kept for diagnosis and
    /// excluded from every computation, which is what
    /// <see cref="Measurement{T}.IsUsable"/> already expresses.
    /// </para>
    /// </remarks>
    public static Measurement<double> CalculateRetentionPercent(
        Measurement<int> fullChargeMWh,
        Measurement<int> designMWh)
    {
        if (designMWh.Value is int design && design <= 0)
        {
            return Measurement<double>.Unavailable(MeasurementSource.Derived);
        }

        Measurement<double> retention = Measurement.Combine(
            fullChargeMWh,
            designMWh,
            (full, designCapacity) => full / (double)designCapacity * 100.0,
            DataQuality.Calculated,
            MeasurementSource.Derived);

        return retention.Value is double percent && !IsPlausibleRetention(percent)
            ? Measurement<double>.Suspect(percent, MeasurementSource.Derived)
            : retention;
    }

    /// <summary>Whether a retention percentage is within the physically plausible band.</summary>
    public static bool IsPlausibleRetention(double percent) =>
        double.IsFinite(percent)
        && percent >= MinPlausibleRetentionPercent
        && percent <= MaxPlausibleRetentionPercent;

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

    /// <summary>
    /// Builds the signed rate (positive = charging, negative = discharging) from
    /// the separate unsigned charge/discharge rates <c>BatteryStatus</c> reports.
    /// </summary>
    /// <remarks>
    /// The rate fields carry quirk Q3's sentinels — <c>0x80000000</c>
    /// (BATTERY_UNKNOWN_RATE) is common for a moment after the charger is plugged
    /// in or pulled. Cast and negated it becomes <see cref="int.MinValue"/>, which
    /// is not a rate and cannot be <see cref="Math.Abs(int)"/>'d, so a sentinel
    /// (or anything past <see cref="int.MaxValue"/>) is Unavailable, never a number.
    /// </remarks>
    public static Measurement<int> SignedRateFromChargeDischarge(
        bool? charging, bool? discharging, uint? chargeRate, uint? dischargeRate, MeasurementSource source)
    {
        static bool Usable(uint? rate) => rate is uint r && !BatterySentinels.IsSentinel(r) && r <= int.MaxValue;

        return (charging, discharging) switch
        {
            (true, _) => Usable(chargeRate) ? Measurement<int>.Measured((int)chargeRate!.Value, source) : Measurement<int>.Unavailable(source),
            (_, true) => Usable(dischargeRate) ? Measurement<int>.Measured(-(int)dischargeRate!.Value, source) : Measurement<int>.Unavailable(source),
            (false, false) => Measurement<int>.Measured(0, source),
            _ => Measurement<int>.Unavailable(source),
        };
    }
}
