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

    /// <summary>
    /// Merges one battery device's view across the four sources. Internal and static
    /// rather than private so the merge — in particular the per-source unit handling
    /// that capacity retention depends on — can be exercised against crafted source
    /// readings without a battery present.
    /// </summary>
    internal static BatterySnapshot BuildSnapshot(
        int index,
        RawBatteryEntry? s1,
        RawBatteryEntry? s3,
        RawBatteryEntry? s4,
        RawBatteryEntry? s2,
        DateTimeOffset now)
    {
        string? chemistry = s4?.Device.Chemistry ?? s3?.Device.Chemistry;
        string? manufacturer = s3?.Device.Manufacturer ?? s4?.Device.Manufacturer;
        string? deviceName = s3?.Device.DeviceName ?? s4?.Device.DeviceName;
        string? serial = s3?.Device.SerialNumber ?? s4?.Device.SerialNumber;
        string? uniqueId = s4?.Device.UniqueId ?? s3?.Device.UniqueId;

        string hardwareId = uniqueId
            ?? (serial is not null && deviceName is not null ? $"{serial}:{deviceName}" : null)
            ?? $"battery{index}";

        // C09: voltage — S3 -> S4. Not exposed by S1. Resolved before any capacity,
        // because the milliamp normalisation below needs it.
        Measurement<int> voltage = FirstAvailable(s3?.Reading.VoltageMv, s4?.Reading.VoltageMv);

        // C05/C06/C07/C10: capacity and rate — Q2-normalised with the unit flag of
        // the source that produced each value (docs/capability-matrix.md section 4).
        //
        // The flag is per-source: WinRT always reports milliwatt-hours, while WMI and
        // IOCTL report milliamp-hours whenever the pack sets CapacityRelative. Applying
        // one source's flag to another source's number rescaled some capacities by the
        // pack voltage and not others, which is what made capacity retention — and the
        // health score built on top of it — read as a fraction of, or a multiple of,
        // the true figure.
        Measurement<int> remaining = FirstAvailable<int>(
            NormalizeReading(s1, r => r.RemainingCapacityMWh, voltage),
            NormalizeReading(s3, r => r.RemainingCapacityMWh, voltage),
            NormalizeReading(s4, r => r.RemainingCapacityMWh, voltage));

        // C06/C07: full-charge and design capacity are the two halves of capacity
        // retention, so they are taken from one source wherever a source reports both.
        // Pairing them across sources would divide one firmware's idea of the pack by
        // another's; only when no single source has both is the cross-source pair used,
        // and the retention derived from it is graded Estimated rather than Calculated.
        (Measurement<int> full, Measurement<int> design, bool pairedFromOneSource) =
            ResolveCapacityPair(s1, s4, s3, voltage);

        Measurement<int> power = FirstAvailable<int>(
            NormalizeReading(s1, r => r.PowerMw, voltage),
            NormalizeReading(s3, r => r.PowerMw, voltage),
            NormalizeReading(s4, r => r.PowerMw, voltage));

        BatteryDevice device = new(
            HardwareId: hardwareId,
            DeviceName: deviceName,
            Manufacturer: manufacturer,
            SerialNumber: serial,
            Chemistry: chemistry,
            DesignCapacityMWh: design,
            DesignVoltageMv: Measurement<int>.Unavailable(),
            ReportsInMilliamps: s4?.Device.ReportsInMilliamps ?? s3?.Device.ReportsInMilliamps ?? false);

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
        Measurement<double> retention = BatteryCalculations.CalculateRetentionPercent(full, design);
        if (!pairedFromOneSource && retention.Value is double crossSource)
        {
            retention = Measurement<double>.Estimated(crossSource, MeasurementSource.Derived);
        }

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
            DesignCapacityMWh = design,
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

    /// <summary>
    /// Reads one field from one source's live reading and converts it from
    /// milliamps to milliwatts when <em>that source</em> says the pack reports in
    /// milliamps (quirk Q2). Design voltage is exposed by no current source, so
    /// live voltage stands in (docs/estimation-strategy.md section 2).
    /// </summary>
    private static Measurement<int> NormalizeReading(
        RawBatteryEntry? entry,
        Func<RawBatteryReading, Measurement<int>> field,
        Measurement<int> voltageMv) =>
        entry is null
            ? Measurement<int>.Unavailable()
            : BatteryCalculations.NormalizeMilliampsToMilliwatts(
                field(entry.Reading), entry.Device.ReportsInMilliamps, voltageMv);

    /// <summary>
    /// Picks the full-charge/design capacity pair that capacity retention is
    /// computed from, preferring a single source that reports both.
    /// </summary>
    /// <remarks>
    /// Sources are tried in the C06/C07 priority order S1 -> S4 -> S3. The third
    /// element of the result is <see langword="false"/> when no source offered both
    /// and the two halves had to be taken from different firmware views, which the
    /// caller uses to downgrade the retention grade.
    /// </remarks>
    private static (Measurement<int> Full, Measurement<int> Design, bool PairedFromOneSource) ResolveCapacityPair(
        RawBatteryEntry? s1,
        RawBatteryEntry? s4,
        RawBatteryEntry? s3,
        Measurement<int> voltageMv)
    {
        foreach (RawBatteryEntry? entry in (ReadOnlySpan<RawBatteryEntry?>)[s1, s4, s3])
        {
            if (entry is null)
            {
                continue;
            }

            Measurement<int> full = NormalizeReading(entry, r => r.FullChargeCapacityMWh, voltageMv);
            Measurement<int> design = BatteryCalculations.NormalizeMilliampsToMilliwatts(
                entry.Device.DesignCapacityMWh, entry.Device.ReportsInMilliamps, voltageMv);

            if (full.HasValue && design.HasValue)
            {
                return (full, design, true);
            }
        }

        // No single source has both halves. Fall back to the per-field chains.
        Measurement<int> fallbackFull = FirstAvailable<int>(
            NormalizeReading(s1, r => r.FullChargeCapacityMWh, voltageMv),
            NormalizeReading(s3, r => r.FullChargeCapacityMWh, voltageMv),
            NormalizeReading(s4, r => r.FullChargeCapacityMWh, voltageMv));

        Measurement<int> fallbackDesign = FirstAvailable<int>(
            NormalizeDesign(s1, voltageMv),
            NormalizeDesign(s4, voltageMv),
            NormalizeDesign(s3, voltageMv));

        return (fallbackFull, fallbackDesign, false);
    }

    private static Measurement<int> NormalizeDesign(RawBatteryEntry? entry, Measurement<int> voltageMv) =>
        entry is null
            ? Measurement<int>.Unavailable()
            : BatteryCalculations.NormalizeMilliampsToMilliwatts(
                entry.Device.DesignCapacityMWh, entry.Device.ReportsInMilliamps, voltageMv);

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
