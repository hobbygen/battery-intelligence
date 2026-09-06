using BatteryIntelligence.Battery.Sources;
using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using Microsoft.Extensions.Logging;

namespace BatteryIntelligence.Battery;

/// <summary>
/// The production <see cref="IBatteryProvider"/>: merges S1 (WinRT), S3 (WMI) and
/// S4 (IOCTL) per-device readings field-by-field per the priority chains in
/// docs/capability-matrix.md section 3, and draws AC line status and a
/// last-resort fallback from S2 (<c>GetSystemPowerStatus</c>).
/// </summary>
/// <remarks>
/// This is the one place the per-field "first available source wins" rule is
/// applied. Every other layer only ever sees the merged, graded result.
/// </remarks>
public sealed class CompositeBatteryProvider : IBatteryProvider
{
    private readonly WinRtBatterySource _winRt;
    private readonly WmiBatterySource _wmi;
    private readonly IoctlBatterySource _ioctl;
    private readonly SystemPowerStatusSource _systemPowerStatus;
    private readonly ILogger<CompositeBatteryProvider> _logger;

    public CompositeBatteryProvider(
        WinRtBatterySource winRt,
        WmiBatterySource wmi,
        IoctlBatterySource ioctl,
        SystemPowerStatusSource systemPowerStatus,
        ILogger<CompositeBatteryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(winRt);
        ArgumentNullException.ThrowIfNull(wmi);
        ArgumentNullException.ThrowIfNull(ioctl);
        ArgumentNullException.ThrowIfNull(systemPowerStatus);
        ArgumentNullException.ThrowIfNull(logger);

        _winRt = winRt;
        _wmi = wmi;
        _ioctl = ioctl;
        _systemPowerStatus = systemPowerStatus;
        _logger = logger;
    }

    public string Name => "Composite (WinRT + WMI + IOCTL + SystemPowerStatus)";

    public async Task<IReadOnlyList<BatterySnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<RawBatteryEntry>> winRtTask = ReadSafely(_winRt, cancellationToken);
        Task<IReadOnlyList<RawBatteryEntry>> wmiTask = ReadSafely(_wmi, cancellationToken);
        Task<IReadOnlyList<RawBatteryEntry>> ioctlTask = ReadSafely(_ioctl, cancellationToken);
        Task<IReadOnlyList<RawBatteryEntry>> statusTask = ReadSafely(_systemPowerStatus, cancellationToken);

        await Task.WhenAll(winRtTask, wmiTask, ioctlTask, statusTask).ConfigureAwait(false);

        IReadOnlyList<RawBatteryEntry> winRt = winRtTask.Result;
        IReadOnlyList<RawBatteryEntry> wmi = wmiTask.Result;
        IReadOnlyList<RawBatteryEntry> ioctl = ioctlTask.Result;
        IReadOnlyList<RawBatteryEntry> status = statusTask.Result;

        int deviceCount = Math.Max(winRt.Count, Math.Max(wmi.Count, ioctl.Count));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (deviceCount == 0)
        {
            if (status.Count == 0)
            {
                // No source sees a battery at all — desktop mode, or a genuine
                // read failure across every source. Either way, "no battery" is
                // the honest answer (specification section 25).
                return [];
            }

            // Every richer source came up empty, but GetSystemPowerStatus still
            // reports a battery. This is the API's own last-resort role.
            return [BuildSnapshot(0, null, null, null, status[0], now)];
        }

        List<BatterySnapshot> snapshots = new(deviceCount);
        for (int i = 0; i < deviceCount; i++)
        {
            RawBatteryEntry? s1 = winRt.FirstOrDefault(e => e.Device.CorrelationIndex == i);
            RawBatteryEntry? s3 = wmi.FirstOrDefault(e => e.Device.CorrelationIndex == i);
            RawBatteryEntry? s4 = ioctl.FirstOrDefault(e => e.Device.CorrelationIndex == i);
            RawBatteryEntry? s2 = i == 0 ? status.FirstOrDefault() : null;

            snapshots.Add(BuildSnapshot(i, s1, s3, s4, s2, now));
        }

        return snapshots;
    }

    private BatterySnapshot BuildSnapshot(
        int index,
        RawBatteryEntry? s1,
        RawBatteryEntry? s3,
        RawBatteryEntry? s4,
        RawBatteryEntry? s2,
        DateTimeOffset now)
    {
        // C07: design capacity — S1 -> S4 -> S3.
        Measurement<int> design = FirstAvailable(
            s1?.Device.DesignCapacityMWh, s4?.Device.DesignCapacityMWh, s3?.Device.DesignCapacityMWh);

        bool reportsInMilliamps = s4?.Device.ReportsInMilliamps ?? s3?.Device.ReportsInMilliamps ?? false;

        string? chemistry = s4?.Device.Chemistry ?? s3?.Device.Chemistry;
        string? manufacturer = s3?.Device.Manufacturer ?? s4?.Device.Manufacturer;
        string? deviceName = s3?.Device.DeviceName ?? s4?.Device.DeviceName;
        string? serial = s3?.Device.SerialNumber ?? s4?.Device.SerialNumber;
        string? uniqueId = s4?.Device.UniqueId ?? s3?.Device.UniqueId;

        string hardwareId = uniqueId
            ?? (serial is not null && deviceName is not null ? $"{serial}:{deviceName}" : null)
            ?? $"battery{index}";

        BatteryDevice device = new(
            HardwareId: hardwareId,
            DeviceName: deviceName,
            Manufacturer: manufacturer,
            SerialNumber: serial,
            Chemistry: chemistry,
            DesignCapacityMWh: design,
            DesignVoltageMv: Measurement<int>.Unavailable(),
            ReportsInMilliamps: reportsInMilliamps);

        // C09: voltage — S3 -> S4. Not exposed by S1.
        Measurement<int> voltage = FirstAvailable(s3?.Reading.VoltageMv, s4?.Reading.VoltageMv);

        // C05/C06/C10: capacity and rate — S1 -> S3 -> S4, then Q2-normalised
        // using whichever voltage is available (design voltage is not exposed by
        // any current source, so live voltage stands in — see docs/estimation-strategy.md
        // section 2 and quirk Q2 in docs/capability-matrix.md section 4).
        Measurement<int> remaining = BatteryCalculations.NormalizeMilliampsToMilliwatts(
            FirstAvailable(s1?.Reading.RemainingCapacityMWh, s3?.Reading.RemainingCapacityMWh, s4?.Reading.RemainingCapacityMWh),
            reportsInMilliamps,
            voltage);

        Measurement<int> full = BatteryCalculations.NormalizeMilliampsToMilliwatts(
            FirstAvailable(s1?.Reading.FullChargeCapacityMWh, s3?.Reading.FullChargeCapacityMWh, s4?.Reading.FullChargeCapacityMWh),
            reportsInMilliamps,
            voltage);

        Measurement<int> designNormalized = BatteryCalculations.NormalizeMilliampsToMilliwatts(design, reportsInMilliamps, voltage);

        Measurement<int> power = BatteryCalculations.NormalizeMilliampsToMilliwatts(
            FirstAvailable(s1?.Reading.PowerMw, s3?.Reading.PowerMw, s4?.Reading.PowerMw),
            reportsInMilliamps,
            voltage);

        // C02: percentage — S1 (remaining/full) -> S2.
        Measurement<double> percentage = FirstAvailable(
            s1?.Reading.Percentage, s3?.Reading.Percentage, s4?.Reading.Percentage, s2?.Reading.Percentage);

        // C03: state — S1 -> S3 -> S2.
        Measurement<BatteryState> state = FirstAvailable(
            s1?.Reading.State, s3?.Reading.State, s4?.Reading.State, s2?.Reading.State);

        // C04: AC line connected — S2 is authoritative; S3/S4 per-device flags fall back.
        Measurement<bool> acOnline = FirstAvailable(s2?.Reading.AcOnline, s3?.Reading.AcOnline, s4?.Reading.AcOnline);

        // C12: cycle count — S4 -> S3. Quirk Q5 already applied by each source.
        Measurement<int> cycles = FirstAvailable(s4?.Reading.CycleCount, s3?.Reading.CycleCount);

        // C13: temperature — S4 -> S3.
        Measurement<double> temperature = FirstAvailable(s4?.Reading.TemperatureCelsius, s3?.Reading.TemperatureCelsius);

        // C08: retention — Calculated from C06/C07. C11: current — Calculated from C10/C09.
        Measurement<double> retention = BatteryCalculations.CalculateRetentionPercent(full, designNormalized);
        Measurement<double> current = BatteryCalculations.CalculateCurrentMa(power, voltage);

        BatteryInfo info = new()
        {
            BatteryId = hardwareId,
            TimestampUtc = now,
            Percentage = percentage,
            State = state,
            AcOnline = acOnline,
            RemainingCapacityMWh = remaining,
            FullChargeCapacityMWh = full,
            DesignCapacityMWh = designNormalized,
            RetentionPercent = retention,
            VoltageMv = voltage,
            PowerMw = power,
            CurrentMa = current,
            CycleCount = cycles,
            TemperatureCelsius = temperature,
        };

        return new BatterySnapshot(device, info);
    }

    private async Task<IReadOnlyList<RawBatteryEntry>> ReadSafely(IRawBatterySource source, CancellationToken cancellationToken)
    {
        try
        {
            return await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Every source already guards its own body; this is the final
            // backstop so a defect in one source can never take down the others
            // (specification section 44).
            _logger.LogError(ex, "Battery source {Source} threw unexpectedly.", source.SourceId);
            return [];
        }
    }

    private static Measurement<T> FirstAvailable<T>(params ReadOnlySpan<Measurement<T>?> candidates)
        where T : struct
    {
        foreach (Measurement<T>? candidate in candidates)
        {
            if (candidate is { HasValue: true } value)
            {
                return value;
            }
        }

        return Measurement<T>.Unavailable();
    }
}
