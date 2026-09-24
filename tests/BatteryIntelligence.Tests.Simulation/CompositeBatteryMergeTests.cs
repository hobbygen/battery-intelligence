using BatteryIntelligence.Battery;
using BatteryIntelligence.Battery.Sources;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// The per-field merge in <see cref="CompositeBatteryProvider"/>, exercised against
/// crafted source readings (docs/capability-matrix.md section 3).
/// </summary>
/// <remarks>
/// The milliamp/milliwatt flag is stated per source: WinRT always reports
/// milliwatt-hours, while WMI and IOCTL report milliamp-hours whenever the pack sets
/// <c>CapacityRelative</c>. Capacity retention divides one of these by another, so a
/// merge that borrows one source's flag for another source's number reports a figure
/// out by roughly the pack voltage — which is what made the health score rate a new
/// pack Poor and a worn one Excellent.
/// </remarks>
public sealed class CompositeBatteryMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private const int DesignVoltageMv = 11_400;

    [Fact]
    public void MilliampSourceAlongsideMilliwattSource_DoesNotRescaleTheMilliwattReadings()
    {
        // S1 reports milliwatt-hours and says so. S4 sits beside it reporting
        // milliamp-hours. The merge prefers S1 for both halves of retention, so S4's
        // flag must not touch them: 91,200 / 95,000 is 96%.
        RawBatteryEntry winRt = WinRtEntry(fullMWh: 91_200, designMWh: 95_000);
        RawBatteryEntry ioctl = MilliampEntry(fullMAh: 8_000, designMAh: 8_333, MeasurementSource.BatteryIoctl);

        BatterySnapshot snapshot = CompositeBatteryProvider.BuildSnapshot(0, winRt, null, ioctl, null, Now);

        Assert.True(snapshot.Info.RetentionPercent.IsUsable);
        Assert.Equal(96.0, snapshot.Info.RetentionPercent.Value!.Value, 1);
        Assert.Equal(91_200, snapshot.Info.FullChargeCapacityMWh.Value);
        Assert.Equal(95_000, snapshot.Info.DesignCapacityMWh.Value);
    }

    [Fact]
    public void AMilliampOnlySource_ConvertsBothHalves_SoRetentionIsUnaffected()
    {
        // 8,000 / 8,333 mAh is the same 96% whether or not the conversion happens;
        // the point is that both halves are converted, and the capacities surface in
        // milliwatt-hours as everything above this layer assumes.
        RawBatteryEntry ioctl = MilliampEntry(fullMAh: 8_000, designMAh: 8_333, MeasurementSource.BatteryIoctl);

        BatterySnapshot snapshot = CompositeBatteryProvider.BuildSnapshot(0, null, null, ioctl, null, Now);

        Assert.True(snapshot.Info.RetentionPercent.IsUsable);
        Assert.Equal(96.0, snapshot.Info.RetentionPercent.Value!.Value, 1);
        Assert.Equal(8_000 * DesignVoltageMv / 1_000, snapshot.Info.FullChargeCapacityMWh.Value);
        Assert.Equal(8_333 * DesignVoltageMv / 1_000, snapshot.Info.DesignCapacityMWh.Value);
    }

    [Fact]
    public void WhenOneSourceHasBothHalves_ItSuppliesThePair_RatherThanTheHigherPriorityDesign()
    {
        // S1 knows the design capacity but not the full-charge capacity, and S3
        // reports in milliamp-hours. Taking design from S1 and full from S3 would
        // divide milliamp-hours by milliwatt-hours. S3 has both, so S3 supplies both.
        RawBatteryEntry winRtDesignOnly = new(
            new RawBatteryDevice(0, null, null, null, null,
                Measurement<int>.Measured(95_000, MeasurementSource.WinRtBattery),
                Measurement<int>.Unavailable(MeasurementSource.WinRtBattery),
                ReportsInMilliamps: false),
            RawBatteryReading.Empty);

        RawBatteryEntry wmi = MilliampEntry(fullMAh: 8_000, designMAh: 8_333, MeasurementSource.Wmi);

        BatterySnapshot snapshot = CompositeBatteryProvider.BuildSnapshot(0, winRtDesignOnly, wmi, null, null, Now);

        Assert.True(snapshot.Info.RetentionPercent.IsUsable);
        Assert.Equal(96.0, snapshot.Info.RetentionPercent.Value!.Value, 1);
    }

    [Fact]
    public void WhenNoSourceHasBothHalves_RetentionIsGradedEstimated_NotCalculated()
    {
        // The two halves genuinely have to come from different firmware views. That
        // is still worth reporting, but it is no longer exact arithmetic over one
        // source's numbers, and the grade has to say so.
        RawBatteryEntry designOnly = new(
            new RawBatteryDevice(0, null, null, null, null,
                Measurement<int>.Measured(95_000, MeasurementSource.WinRtBattery),
                Measurement<int>.Unavailable(MeasurementSource.WinRtBattery),
                ReportsInMilliamps: false),
            RawBatteryReading.Empty);

        RawBatteryEntry fullOnly = new(
            new RawBatteryDevice(0, null, null, null, null,
                Measurement<int>.Unavailable(MeasurementSource.Wmi),
                Measurement<int>.Unavailable(MeasurementSource.Wmi),
                ReportsInMilliamps: false),
            RawBatteryReading.Empty with
            {
                FullChargeCapacityMWh = Measurement<int>.Measured(91_200, MeasurementSource.Wmi),
                VoltageMv = Measurement<int>.Measured(DesignVoltageMv, MeasurementSource.Wmi),
            });

        BatterySnapshot snapshot = CompositeBatteryProvider.BuildSnapshot(0, designOnly, fullOnly, null, null, Now);

        Assert.Equal(96.0, snapshot.Info.RetentionPercent.Value!.Value, 1);
        Assert.Equal(DataQuality.Estimated, snapshot.Info.RetentionPercent.Quality);
    }

    private static RawBatteryEntry WinRtEntry(int fullMWh, int designMWh) =>
        new(
            new RawBatteryDevice(0, null, null, null, null,
                Measurement<int>.Measured(designMWh, MeasurementSource.WinRtBattery),
                Measurement<int>.Unavailable(MeasurementSource.WinRtBattery),
                ReportsInMilliamps: false),
            RawBatteryReading.Empty with
            {
                FullChargeCapacityMWh = Measurement<int>.Measured(fullMWh, MeasurementSource.WinRtBattery),
            });

    private static RawBatteryEntry MilliampEntry(int fullMAh, int designMAh, MeasurementSource source) =>
        new(
            new RawBatteryDevice(0, null, null, null, "LiP",
                Measurement<int>.Measured(designMAh, source),
                Measurement<int>.Unavailable(source),
                ReportsInMilliamps: true),
            RawBatteryReading.Empty with
            {
                FullChargeCapacityMWh = Measurement<int>.Measured(fullMAh, source),
                VoltageMv = Measurement<int>.Measured(DesignVoltageMv, source),
            });
}
