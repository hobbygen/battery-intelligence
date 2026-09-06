using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;
using Microsoft.Extensions.Logging;
using Windows.Devices.Enumeration;
using Windows.Devices.Power;
using Windows.System.Power;
using WinRtBattery = Windows.Devices.Power.Battery;

namespace BatteryIntelligence.Battery.Sources;

/// <summary>
/// S1 — <c>Windows.Devices.Power.Battery</c>. The cleanest API; gives clean
/// per-battery reports in mWh/mW, but never voltage, current, temperature or
/// cycle count (docs/api-strategy.md section 2).
/// </summary>
public sealed class WinRtBatterySource : IRawBatterySource
{
    private readonly ILogger<WinRtBatterySource> _logger;

    public WinRtBatterySource(ILogger<WinRtBatterySource> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public MeasurementSource SourceId => MeasurementSource.WinRtBattery;

    async Task<IReadOnlyList<RawBatteryEntry>> IRawBatterySource.ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            DeviceInformationCollection devices = await DeviceInformation
                .FindAllAsync(WinRtBattery.GetDeviceSelector())
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            List<RawBatteryEntry> entries = new(devices.Count);

            for (int i = 0; i < devices.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    WinRtBattery battery = await WinRtBattery.FromIdAsync(devices[i].Id).AsTask(cancellationToken).ConfigureAwait(false);
                    entries.Add(ReadOne(battery, i));
                }
                catch (Exception ex)
                {
                    // One malfunctioning battery device must not hide the others.
                    _logger.LogWarning(ex, "WinRT battery report failed for device index {Index}.", i);
                }
            }

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WinRT battery enumeration failed.");
            return [];
        }
    }

    private RawBatteryEntry ReadOne(WinRtBattery battery, int index)
    {
        BatteryReport report = battery.GetReport();

        Measurement<int> design = ToMeasurement(report.DesignCapacityInMilliwattHours);
        Measurement<int> full = ToMeasurement(report.FullChargeCapacityInMilliwattHours);
        Measurement<int> remaining = ToMeasurement(report.RemainingCapacityInMilliwattHours);
        Measurement<int> rate = ToMeasurement(report.ChargeRateInMilliwatts);

        Measurement<double> percentage = remaining.Value is int r && full.Value is int f && f > 0
            ? Measurement<double>.Measured(Math.Clamp(r / (double)f * 100.0, 0.0, 100.0), SourceId)
            : Measurement<double>.Unavailable(SourceId);

        BatteryState state = report.Status switch
        {
            BatteryStatus.Charging => BatteryState.Charging,
            BatteryStatus.Discharging => BatteryState.Discharging,
            BatteryStatus.Idle => percentage.Value is >= 100.0 ? BatteryState.Full : BatteryState.Idle,
            BatteryStatus.NotPresent => BatteryState.NotPresent,
            _ => BatteryState.Unknown,
        };

        RawBatteryDevice device = new(
            CorrelationIndex: index,
            DeviceName: null,
            Manufacturer: null,
            SerialNumber: null,
            Chemistry: null,
            DesignCapacityMWh: design,
            DesignVoltageMv: Measurement<int>.Unavailable(SourceId),
            ReportsInMilliamps: false);

        RawBatteryReading reading = new(
            Percentage: percentage,
            State: state == BatteryState.Unknown
                ? Measurement<BatteryState>.Unavailable(SourceId)
                : Measurement<BatteryState>.Measured(state, SourceId),
            AcOnline: Measurement<bool>.Unavailable(SourceId),
            RemainingCapacityMWh: remaining,
            FullChargeCapacityMWh: full,
            VoltageMv: Measurement<int>.Unavailable(SourceId),
            PowerMw: rate,
            CycleCount: Measurement<int>.Unavailable(SourceId),
            TemperatureCelsius: Measurement<double>.Unavailable(SourceId));

        return new RawBatteryEntry(device, reading);
    }

    private Measurement<int> ToMeasurement(int? value) =>
        value is int v ? Measurement<int>.Measured(v, SourceId) : Measurement<int>.Unavailable(SourceId);
}
