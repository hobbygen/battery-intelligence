using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;
using BatteryIntelligence.Windows;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Battery.Sources;

/// <summary>
/// S2 — <c>GetSystemPowerStatus</c>. Always present, whole-system only (no
/// per-device breakdown), and the sole source of AC line status
/// (docs/api-strategy.md section 2).
/// </summary>
/// <remarks>
/// On a machine with more than one battery this reports one synthetic entry at
/// index 0. That is a correct reflection of the API's own scope — Windows itself
/// does not expose per-battery AC status through this call — and the composite
/// provider only draws AC status and last-resort percentage from it, never
/// per-device capacity or identity.
/// </remarks>
public sealed class SystemPowerStatusSource : IRawBatterySource
{
    private readonly ILogger<SystemPowerStatusSource> _logger;

    public SystemPowerStatusSource(ILogger<SystemPowerStatusSource> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public MeasurementSource SourceId => MeasurementSource.SystemPowerStatus;

    Task<IReadOnlyList<RawBatteryEntry>> IRawBatterySource.ReadAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        SystemPowerStatusReading? status = SystemPowerStatusReader.Read();
        if (status is null)
        {
            _logger.LogWarning("GetSystemPowerStatus failed.");
            return Task.FromResult<IReadOnlyList<RawBatteryEntry>>([]);
        }

        if (status.NoBattery)
        {
            return Task.FromResult<IReadOnlyList<RawBatteryEntry>>([]);
        }

        Measurement<bool> acOnline = status.AcLineOnline is bool ac
            ? Measurement<bool>.Measured(ac, SourceId)
            : Measurement<bool>.Unavailable(SourceId);

        Measurement<double> percentage = status.BatteryPercent is int p
            ? Measurement<double>.Measured(p, SourceId)
            : Measurement<double>.Unavailable(SourceId);

        BatteryState? state = status switch
        {
            { Charging: true } => BatteryState.Charging,
            { AcLineOnline: true, BatteryPercent: 100 } => BatteryState.Full,
            { AcLineOnline: true } => BatteryState.Idle,
            { AcLineOnline: false } => BatteryState.Discharging,
            _ => null,
        };

        RawBatteryDevice device = new(
            CorrelationIndex: 0,
            DeviceName: null,
            Manufacturer: null,
            SerialNumber: null,
            Chemistry: null,
            DesignCapacityMWh: Measurement<int>.Unavailable(SourceId),
            DesignVoltageMv: Measurement<int>.Unavailable(SourceId),
            ReportsInMilliamps: false);

        RawBatteryReading reading = new(
            Percentage: percentage,
            State: state is BatteryState s ? Measurement<BatteryState>.Measured(s, SourceId) : Measurement<BatteryState>.Unavailable(SourceId),
            AcOnline: acOnline,
            RemainingCapacityMWh: Measurement<int>.Unavailable(SourceId),
            FullChargeCapacityMWh: Measurement<int>.Unavailable(SourceId),
            VoltageMv: Measurement<int>.Unavailable(SourceId),
            PowerMw: Measurement<int>.Unavailable(SourceId),
            CycleCount: Measurement<int>.Unavailable(SourceId),
            TemperatureCelsius: Measurement<double>.Unavailable(SourceId));

        return Task.FromResult<IReadOnlyList<RawBatteryEntry>>([new RawBatteryEntry(device, reading)]);
    }
}
