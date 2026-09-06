using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Core.Models;

/// <summary>
/// The identity of a physical battery device, independent of any point-in-time
/// reading.
/// </summary>
/// <remarks>
/// <see cref="HardwareId"/>, not an enumeration index, is what survives a reboot
/// and distinguishes a replaced battery from the original (docs/database.md
/// section "Devices"; specification section 25).
/// </remarks>
/// <param name="HardwareId">
/// A stable identifier for this physical device (ACPI unique ID when available,
/// otherwise a fallback derived from serial/model). Never empty.
/// </param>
/// <param name="DeviceName">Manufacturer's device name, when reported.</param>
/// <param name="Manufacturer">Battery manufacturer, when reported.</param>
/// <param name="SerialNumber">Serial number, when reported.</param>
/// <param name="Chemistry">Battery chemistry (e.g. "LiP"), when reported.</param>
/// <param name="DesignCapacityMWh">Capacity when new, in milliwatt-hours.</param>
/// <param name="DesignVoltageMv">Nominal design voltage, in millivolts.</param>
/// <param name="ReportsInMilliamps">
/// Whether this device's firmware reports rate/capacity in mA/mAh rather than
/// mW/mWh (the <c>BATTERY_CAPACITY_RELATIVE</c> capability bit, quirk Q2). Recorded
/// once per device and reused rather than re-detected every sample.
/// </param>
public sealed record BatteryDevice(
    string HardwareId,
    string? DeviceName,
    string? Manufacturer,
    string? SerialNumber,
    string? Chemistry,
    Measurement<int> DesignCapacityMWh,
    Measurement<int> DesignVoltageMv,
    bool ReportsInMilliamps)
{
    /// <summary>
    /// The identity used for the synthesised system-wide aggregate across every
    /// present battery (specification section 25).
    /// </summary>
    public const string AggregateHardwareId = "AGGREGATE";

    /// <summary>Whether this record represents the multi-battery aggregate rather than a physical device.</summary>
    public bool IsAggregate => HardwareId == AggregateHardwareId;
}
