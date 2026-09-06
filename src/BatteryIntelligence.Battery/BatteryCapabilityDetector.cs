using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Battery;

/// <summary>
/// Probes what this machine can actually report by issuing one real read through
/// the composite provider and inspecting the grade of every field, per
/// docs/capability-matrix.md section 3 and specification section 26.
/// </summary>
public sealed class BatteryCapabilityDetector : IBatteryCapabilityDetector
{
    private readonly IBatteryProvider _provider;
    private readonly ILogger<BatteryCapabilityDetector> _logger;

    public BatteryCapabilityDetector(IBatteryProvider provider, ILogger<BatteryCapabilityDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(logger);

        _provider = provider;
        _logger = logger;
    }

    public async Task<CapabilitySnapshot> DetectAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BatterySnapshot> snapshots;
        try
        {
            snapshots = await _provider.GetSnapshotsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Capability detection could not read any battery source.");
            snapshots = [];
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (snapshots.Count == 0)
        {
            return new CapabilitySnapshot(now, [
                new CapabilityRow(
                    CapabilityId.BatteryPresent,
                    "Battery present / count",
                    Available: false,
                    Grade: null,
                    Source: MeasurementSource.Unknown,
                    Detail: "No battery device was found by any source. This is expected on a desktop; on a laptop it indicates every battery source failed."),
            ]);
        }

        BatteryInfo primary = snapshots[0].Info;
        List<CapabilityRow> rows =
        [
            new CapabilityRow(
                CapabilityId.BatteryPresent,
                "Battery present / count",
                Available: true,
                Grade: DataQuality.Measured,
                Source: MeasurementSource.WinRtBattery,
                Detail: $"{snapshots.Count} battery device(s) detected."),

            Row(CapabilityId.ChargePercentage, "Charge percentage", primary.Percentage),
            Row(CapabilityId.ChargeState, "Charging / discharging / idle / full", primary.State),
            Row(CapabilityId.AcLineConnected, "AC line connected", primary.AcOnline),
            Row(CapabilityId.RemainingCapacity, "Remaining capacity (mWh)", primary.RemainingCapacityMWh),
            Row(CapabilityId.FullChargeCapacity, "Full-charge capacity (mWh)", primary.FullChargeCapacityMWh),
            Row(CapabilityId.DesignCapacity, "Design capacity (mWh)", primary.DesignCapacityMWh),
            Row(CapabilityId.CapacityRetention, "Capacity retention / wear %", primary.RetentionPercent,
                unavailableDetail: "Requires both design and full-charge capacity; at least one is unavailable."),
            Row(CapabilityId.Voltage, "Voltage (mV)", primary.VoltageMv,
                unavailableDetail: "Not exposed by WinRT; WMI/IOCTL voltage classes did not report a value."),
            Row(CapabilityId.PowerRate, "Energy rate / power (mW)", primary.PowerMw),
            Row(CapabilityId.Current, "Electric current (mA)", primary.CurrentMa,
                unavailableDetail: "Calculated from power and voltage; one of those is unavailable."),
            Row(CapabilityId.CycleCount, "Cycle count", primary.CycleCount,
                unavailableDetail: "Not reported by firmware (a reported zero is treated as \"not reported\", not \"brand new\")."),
            Row(CapabilityId.Temperature, "Battery temperature", primary.TemperatureCelsius,
                unavailableDetail: "Sensor not exposed by this device."),

            new CapabilityRow(
                CapabilityId.Identity,
                "Manufacturer / model / serial / chemistry",
                Available: snapshots[0].Device.Manufacturer is not null
                    || snapshots[0].Device.DeviceName is not null
                    || snapshots[0].Device.Chemistry is not null,
                Grade: DataQuality.Measured,
                Source: MeasurementSource.Wmi,
                Detail: DescribeIdentity(snapshots[0].Device)),
        ];

        return new CapabilitySnapshot(now, rows);
    }

    private static CapabilityRow Row<T>(
        CapabilityId id,
        string name,
        Measurement<T> measurement,
        string? unavailableDetail = null)
        where T : struct
    {
        if (!measurement.HasValue)
        {
            return new CapabilityRow(id, name, Available: false, Grade: null, Source: measurement.Source,
                Detail: unavailableDetail ?? "Not reported by any available source.");
        }

        return new CapabilityRow(
            id,
            name,
            Available: true,
            Grade: measurement.Quality,
            Source: measurement.Source,
            Detail: DescribeSource(measurement.Source));
    }

    private static string DescribeSource(MeasurementSource source) => source switch
    {
        MeasurementSource.WinRtBattery => "Windows.Devices.Power.Battery",
        MeasurementSource.SystemPowerStatus => "GetSystemPowerStatus",
        MeasurementSource.Wmi => @"WMI root\wmi",
        MeasurementSource.BatteryIoctl => "IOCTL_BATTERY_QUERY_*",
        MeasurementSource.Derived => "Calculated from other measurements",
        MeasurementSource.Simulation => "Simulated hardware",
        _ => "Unknown",
    };

    private static string DescribeIdentity(BatteryDevice device)
    {
        List<string> parts = [];
        if (device.Manufacturer is not null)
        {
            parts.Add(device.Manufacturer);
        }

        if (device.DeviceName is not null)
        {
            parts.Add(device.DeviceName);
        }

        if (device.Chemistry is not null)
        {
            parts.Add(device.Chemistry);
        }

        return parts.Count == 0 ? "Not reported by any available source." : string.Join(" / ", parts);
    }
}
