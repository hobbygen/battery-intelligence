using System.Management;
using System.Runtime.Versioning;
using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Battery.Sources;

/// <summary>
/// S3 — WMI <c>root\wmi</c> battery classes: voltage, cycle count, manufacturer,
/// chemistry, serial, temperature (docs/api-strategy.md section 2).
/// </summary>
/// <remarks>
/// Each class is queried independently and its own failure caught separately
/// (quirk Q1, docs/capability-matrix.md section 4): on this project's reference
/// machine, <c>BatteryStaticData</c> fails on the modern CIM path while its
/// siblings succeed, so one combined query would silently lose all of them.
/// Instances are correlated across classes by <c>InstanceName</c> — the actual,
/// verified key these classes share — not by position.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WmiBatterySource : IRawBatterySource
{
    private const string Namespace = @"root\wmi";
    private readonly ILogger<WmiBatterySource> _logger;

    public WmiBatterySource(ILogger<WmiBatterySource> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public MeasurementSource SourceId => MeasurementSource.Wmi;

    Task<IReadOnlyList<RawBatteryEntry>> IRawBatterySource.ReadAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        Dictionary<string, ManagementBaseObject> staticData = QueryClass("BatteryStaticData", "InstanceName");
        Dictionary<string, ManagementBaseObject> status = QueryClass("BatteryStatus", "InstanceName");
        Dictionary<string, ManagementBaseObject> fullCharge = QueryClass("BatteryFullChargedCapacity", "InstanceName");
        Dictionary<string, ManagementBaseObject> cycleCount = QueryClass("BatteryCycleCount", "InstanceName");
        Dictionary<string, ManagementBaseObject> temperature = QueryClass("BatteryTemperature", "InstanceName");

        // The union of instance names across every class: a class failing
        // (quirk Q1) must not hide instances known only through its siblings.
        HashSet<string> instanceNames = [.. staticData.Keys, .. status.Keys, .. fullCharge.Keys, .. cycleCount.Keys];

        List<RawBatteryEntry> entries = new(instanceNames.Count);
        int index = 0;
        foreach (string instanceName in instanceNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            entries.Add(BuildEntry(
                index++,
                staticData.GetValueOrDefault(instanceName),
                status.GetValueOrDefault(instanceName),
                fullCharge.GetValueOrDefault(instanceName),
                cycleCount.GetValueOrDefault(instanceName),
                temperature.GetValueOrDefault(instanceName)));
        }

        return Task.FromResult<IReadOnlyList<RawBatteryEntry>>(entries);
    }

    private Dictionary<string, ManagementBaseObject> QueryClass(string className, string keyProperty)
    {
        Dictionary<string, ManagementBaseObject> result = [];

        try
        {
            using ManagementObjectSearcher searcher = new(Namespace, $"SELECT * FROM {className}");
            foreach (ManagementBaseObject instance in searcher.Get())
            {
                if (instance.Properties[keyProperty]?.Value is string key)
                {
                    result[key] = instance;
                }
            }
        }
        catch (Exception ex)
        {
            // Quirk Q1: this class alone may fail on the modern CIM path while its
            // siblings succeed. Caught here, per class, so the failure is isolated.
            _logger.LogDebug(ex, "WMI class {Class} unavailable in {Namespace}.", className, Namespace);
        }

        return result;
    }

    private RawBatteryEntry BuildEntry(
        int index,
        ManagementBaseObject? staticData,
        ManagementBaseObject? status,
        ManagementBaseObject? fullCharge,
        ManagementBaseObject? cycleCount,
        ManagementBaseObject? temperature)
    {
        uint? capabilities = ReadUInt32(staticData, "Capabilities");
        bool reportsInMilliamps = capabilities is uint caps && (caps & 0x40000000) != 0;

        RawBatteryDevice device = new(
            CorrelationIndex: index,
            DeviceName: ReadString(staticData, "DeviceName"),
            Manufacturer: ReadString(staticData, "ManufactureName"),
            SerialNumber: ReadString(staticData, "SerialNumber"),
            Chemistry: DecodeChemistry(ReadUInt32(staticData, "Chemistry")),
            DesignCapacityMWh: ToMeasurement(ReadUInt32(staticData, "DesignedCapacity")),
            DesignVoltageMv: Measurement<int>.Unavailable(SourceId), // not exposed by BatteryStaticData
            ReportsInMilliamps: reportsInMilliamps,
            UniqueId: ReadString(staticData, "UniqueID"));

        Measurement<int> remaining = ToMeasurement(ReadUInt32(status, "RemainingCapacity"));
        Measurement<int> voltage = ToMeasurement(ReadUInt32(status, "Voltage"));
        Measurement<int> full = ToMeasurement(ReadUInt32(fullCharge, "FullChargedCapacity"));

        bool? charging = ReadBool(status, "Charging");
        bool? discharging = ReadBool(status, "Discharging");
        bool? acOnline = ReadBool(status, "PowerOnline");
        uint? chargeRate = ReadUInt32(status, "ChargeRate");
        uint? dischargeRate = ReadUInt32(status, "DischargeRate");

        Measurement<int> power = (charging, discharging) switch
        {
            (true, _) when chargeRate is uint cr => Measurement<int>.Measured((int)cr, SourceId),
            (_, true) when dischargeRate is uint dr => Measurement<int>.Measured(-(int)dr, SourceId),
            (false, false) => Measurement<int>.Measured(0, SourceId),
            _ => Measurement<int>.Unavailable(SourceId),
        };

        BatteryState? state = (charging, discharging) switch
        {
            (true, _) => BatteryState.Charging,
            (_, true) => BatteryState.Discharging,
            (false, false) when acOnline == true => BatteryState.Idle,
            (false, false) when acOnline == false => BatteryState.Discharging,
            _ => null,
        };

        Measurement<int> rawCycleCount = ToMeasurement(ReadUInt32(cycleCount, "CycleCount"));
        Measurement<int> cycles = BatteryCalculations.ApplyCycleCountZeroQuirk(rawCycleCount);

        Measurement<double> temperatureC = ReadUInt32(temperature, "Temperature") is uint deciKelvin
            ? Measurement<double>.Measured(deciKelvin / 10.0 - 273.15, SourceId)
            : Measurement<double>.Unavailable(SourceId);

        Measurement<double> percentage = remaining.Value is int r && full.Value is int f && f > 0
            ? Measurement<double>.Measured(Math.Clamp(r / (double)f * 100.0, 0.0, 100.0), SourceId)
            : Measurement<double>.Unavailable(SourceId);

        RawBatteryReading reading = new(
            Percentage: percentage,
            State: state is BatteryState s ? Measurement<BatteryState>.Measured(s, SourceId) : Measurement<BatteryState>.Unavailable(SourceId),
            AcOnline: acOnline is bool ac ? Measurement<bool>.Measured(ac, SourceId) : Measurement<bool>.Unavailable(SourceId),
            RemainingCapacityMWh: remaining,
            FullChargeCapacityMWh: full,
            VoltageMv: voltage,
            PowerMw: power,
            CycleCount: cycles,
            TemperatureCelsius: temperatureC);

        return new RawBatteryEntry(device, reading);
    }

    private Measurement<int> ToMeasurement(uint? value) =>
        value is uint v && !BatterySentinels.IsSentinel(v)
            ? Measurement<int>.Measured(unchecked((int)v), SourceId)
            : Measurement<int>.Unavailable(SourceId);

    private static string? DecodeChemistry(uint? packed)
    {
        if (packed is not uint value || value == 0)
        {
            return null;
        }

        // Packed ASCII tag, little-endian byte order (quirk Q4), e.g. 0x50694C ->
        // bytes 4C,69,50,00 -> "LiP". Verified against this project's reference
        // machine (docs/capability-matrix.md section 1).
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);

        Span<char> chars = stackalloc char[4];
        int length = 0;
        foreach (byte b in bytes)
        {
            if (b == 0)
            {
                break;
            }

            chars[length++] = (char)b;
        }

        return length == 0 ? null : new string(chars[..length]);
    }

    private static uint? ReadUInt32(ManagementBaseObject? instance, string property)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            object? value = instance.Properties[property]?.Value;
            return value switch
            {
                uint u => u,
                int i => unchecked((uint)i),
                ushort us => us,
                _ => null,
            };
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static bool? ReadBool(ManagementBaseObject? instance, string property)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            return instance.Properties[property]?.Value as bool?;
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static string? ReadString(ManagementBaseObject? instance, string property)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            string? value = instance.Properties[property]?.Value as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (ManagementException)
        {
            return null;
        }
    }
}
