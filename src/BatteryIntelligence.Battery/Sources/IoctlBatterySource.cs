using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Windows;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Battery.Sources;

/// <summary>
/// S4 — <c>IOCTL_BATTERY_QUERY_INFORMATION</c>/<c>_STATUS</c> via the battery
/// device interface. The richest source: cycle count, temperature, manufacture
/// date and unique ID, used to enrich or repair WinRT/WMI results
/// (docs/api-strategy.md section 2).
/// </summary>
public sealed class IoctlBatterySource : IRawBatterySource
{
    private readonly ILogger<IoctlBatterySource> _logger;

    public IoctlBatterySource(ILogger<IoctlBatterySource> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public MeasurementSource SourceId => MeasurementSource.BatteryIoctl;

    Task<IReadOnlyList<RawBatteryEntry>> IRawBatterySource.ReadAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        List<RawBatteryEntry> entries = [];

        IReadOnlyList<string> paths;
        try
        {
            paths = BatteryDeviceEnumerator.EnumerateDevicePaths();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Battery device enumeration (SetupDi) failed.");
            return Task.FromResult<IReadOnlyList<RawBatteryEntry>>([]);
        }

        for (int i = 0; i < paths.Count; i++)
        {
            try
            {
                RawBatteryEntry? entry = ReadOne(paths[i], i);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                // A stale tag or a device that vanished between enumeration and
                // open must not take down the other batteries' readings.
                _logger.LogWarning(ex, "IOCTL battery read failed for device path {Path}.", paths[i]);
            }
        }

        return Task.FromResult<IReadOnlyList<RawBatteryEntry>>(entries);
    }

    private RawBatteryEntry? ReadOne(string devicePath, int index)
    {
        using BatteryIoctlDevice? device = BatteryIoctlDevice.TryOpen(devicePath);
        if (device is null)
        {
            // A battery slot with no battery inserted reports no tag. That is a
            // legitimate "nothing here", not a failure.
            return null;
        }

        BatteryStaticInfo? staticInfo = device.QueryStaticInfo();
        BatteryLiveStatus? liveStatus = device.QueryLiveStatus();

        bool reportsInMilliamps = staticInfo?.Capabilities.HasFlag(BatteryCapabilityFlags.CapacityRelative) == true;

        Measurement<int> design = staticInfo is not null && !BatterySentinels.IsSentinel(staticInfo.DesignedCapacity)
            ? Measurement<int>.Measured(unchecked((int)staticInfo.DesignedCapacity), SourceId)
            : Measurement<int>.Unavailable(SourceId);

        Measurement<int> full = staticInfo is not null && !BatterySentinels.IsSentinel(staticInfo.FullChargedCapacity)
            ? Measurement<int>.Measured(unchecked((int)staticInfo.FullChargedCapacity), SourceId)
            : Measurement<int>.Unavailable(SourceId);

        Measurement<int> cycles = staticInfo is not null
            ? BatteryCalculations.ApplyCycleCountZeroQuirk(
                !BatterySentinels.IsSentinel(staticInfo.CycleCount)
                    ? Measurement<int>.Measured(unchecked((int)staticInfo.CycleCount), SourceId)
                    : Measurement<int>.Unavailable(SourceId))
            : Measurement<int>.Unavailable(SourceId);

        RawBatteryDevice rawDevice = new(
            CorrelationIndex: index,
            DeviceName: device.QueryDeviceName(),
            Manufacturer: device.QueryManufactureName(),
            SerialNumber: device.QuerySerialNumber(),
            Chemistry: string.IsNullOrEmpty(staticInfo?.Chemistry) ? null : staticInfo.Chemistry,
            DesignCapacityMWh: design,
            DesignVoltageMv: Measurement<int>.Unavailable(SourceId), // BATTERY_INFORMATION carries no design voltage field
            ReportsInMilliamps: reportsInMilliamps,
            UniqueId: device.QueryUniqueId());

        Measurement<int> remaining = Measurement<int>.Unavailable(SourceId);
        Measurement<int> voltage = Measurement<int>.Unavailable(SourceId);
        Measurement<int> power = Measurement<int>.Unavailable(SourceId);
        Measurement<bool> acOnline = Measurement<bool>.Unavailable(SourceId);
        Measurement<BatteryState> state = Measurement<BatteryState>.Unavailable(SourceId);

        if (liveStatus is not null)
        {
            remaining = !BatterySentinels.IsSentinel(liveStatus.CapacityRemaining)
                ? Measurement<int>.Measured(unchecked((int)liveStatus.CapacityRemaining), SourceId)
                : Measurement<int>.Unavailable(SourceId);

            voltage = !BatterySentinels.IsSentinel(liveStatus.Voltage)
                ? Measurement<int>.Measured(unchecked((int)liveStatus.Voltage), SourceId)
                : Measurement<int>.Unavailable(SourceId);

            power = !BatterySentinels.IsSentinel(liveStatus.Rate)
                ? Measurement<int>.Measured(liveStatus.Rate, SourceId)
                : Measurement<int>.Unavailable(SourceId);

            acOnline = Measurement<bool>.Measured(
                liveStatus.PowerState.HasFlag(BatteryPowerStateFlags.PowerOnLine), SourceId);

            BatteryState resolved = liveStatus.PowerState switch
            {
                _ when liveStatus.PowerState.HasFlag(BatteryPowerStateFlags.Charging) => BatteryState.Charging,
                _ when liveStatus.PowerState.HasFlag(BatteryPowerStateFlags.Discharging) => BatteryState.Discharging,
                _ when liveStatus.PowerState.HasFlag(BatteryPowerStateFlags.PowerOnLine) => BatteryState.Idle,
                _ => BatteryState.Unknown,
            };

            state = resolved == BatteryState.Unknown
                ? Measurement<BatteryState>.Unavailable(SourceId)
                : Measurement<BatteryState>.Measured(resolved, SourceId);
        }

        Measurement<double> temperature = device.QueryTemperatureDeciKelvin() is uint deciKelvin
            && !BatterySentinels.IsSentinel(deciKelvin)
                ? Measurement<double>.Measured(deciKelvin / 10.0 - 273.15, SourceId)
                : Measurement<double>.Unavailable(SourceId);

        Measurement<double> percentage = remaining.Value is int r && full.Value is int f && f > 0
            ? Measurement<double>.Measured(Math.Clamp(r / (double)f * 100.0, 0.0, 100.0), SourceId)
            : Measurement<double>.Unavailable(SourceId);

        RawBatteryReading reading = new(
            Percentage: percentage,
            State: state,
            AcOnline: acOnline,
            RemainingCapacityMWh: remaining,
            FullChargeCapacityMWh: full,
            VoltageMv: voltage,
            PowerMw: power,
            CycleCount: cycles,
            TemperatureCelsius: temperature);

        return new RawBatteryEntry(rawDevice, reading);
    }
}
