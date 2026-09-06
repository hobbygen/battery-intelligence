using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Battery;

/// <summary>
/// Multi-battery aggregate correctness (specification section 25;
/// docs/testing.md section 4 "Multiple batteries" scenario).
/// </summary>
public sealed class BatteryAggregationTests
{
    [Fact]
    public void Aggregate_NoSnapshots_YieldsNull()
    {
        Assert.Null(BatteryAggregation.Aggregate([], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Aggregate_SingleBattery_IsThatBatterysReading_RetaggedAsAggregate()
    {
        BatterySnapshot snapshot = MakeSnapshot("battery0", percentage: 80, remaining: 3000, full: 3800, design: 9500, powerMw: -1000);

        BatteryInfo? aggregate = BatteryAggregation.Aggregate([snapshot], DateTimeOffset.UtcNow);

        Assert.NotNull(aggregate);
        Assert.Equal(BatteryDevice.AggregateHardwareId, aggregate!.BatteryId);
        Assert.Equal(80, aggregate.Percentage.Value!.Value, 0);
    }

    [Fact]
    public void Aggregate_TwoBatteries_SumsCapacityAndPower()
    {
        BatterySnapshot a = MakeSnapshot("battery0", percentage: 80, remaining: 3000, full: 3800, design: 9500, powerMw: -1000);
        BatterySnapshot b = MakeSnapshot("battery1", percentage: 60, remaining: 1800, full: 3000, design: 8000, powerMw: -500);

        BatteryInfo? aggregate = BatteryAggregation.Aggregate([a, b], DateTimeOffset.UtcNow);

        Assert.NotNull(aggregate);
        Assert.Equal(4800, aggregate!.RemainingCapacityMWh.Value);
        Assert.Equal(6800, aggregate.FullChargeCapacityMWh.Value);
        Assert.Equal(17500, aggregate.DesignCapacityMWh.Value);
        Assert.Equal(-1500, aggregate.PowerMw.Value);

        // Aggregate percentage must come from summed capacities, not an average
        // of the two batteries' independent percentages (4800/6800 = 70.6%, not
        // the naive average of 80% and 60%, which would be 70% by coincidence
        // here but wrong in general).
        Assert.Equal(4800.0 / 6800.0 * 100.0, aggregate.Percentage.Value!.Value, 1);
    }

    [Fact]
    public void Aggregate_OneDeviceMissingCapacity_YieldsUnavailableSum_NotAPartialSum()
    {
        BatterySnapshot a = MakeSnapshot("battery0", percentage: 80, remaining: 3000, full: 3800, design: 9500, powerMw: -1000);
        BatterySnapshot b = a with
        {
            Info = a.Info with { RemainingCapacityMWh = Measurement<int>.Unavailable() },
            Device = a.Device with { HardwareId = "battery1" },
        };

        BatteryInfo? aggregate = BatteryAggregation.Aggregate([a, b], DateTimeOffset.UtcNow);

        Assert.NotNull(aggregate);
        Assert.False(aggregate!.RemainingCapacityMWh.HasValue);
    }

    [Fact]
    public void Aggregate_AnyBatteryCharging_DominatesTheSummaryState()
    {
        BatterySnapshot charging = MakeSnapshot("battery0", 50, 1000, 2000, 4000, 500, BatteryState.Charging);
        BatterySnapshot discharging = MakeSnapshot("battery1", 50, 1000, 2000, 4000, -500, BatteryState.Discharging);

        BatteryInfo? aggregate = BatteryAggregation.Aggregate([charging, discharging], DateTimeOffset.UtcNow);

        Assert.Equal(BatteryState.Charging, aggregate!.State.Value);
    }

    [Fact]
    public void Aggregate_BothFull_SummaryStateIsFull()
    {
        BatterySnapshot a = MakeSnapshot("battery0", 100, 4000, 4000, 4000, 0, BatteryState.Full);
        BatterySnapshot b = MakeSnapshot("battery1", 100, 4000, 4000, 4000, 0, BatteryState.Full);

        BatteryInfo? aggregate = BatteryAggregation.Aggregate([a, b], DateTimeOffset.UtcNow);

        Assert.Equal(BatteryState.Full, aggregate!.State.Value);
    }

    private static BatterySnapshot MakeSnapshot(
        string id,
        double percentage,
        int remaining,
        int full,
        int design,
        int powerMw,
        BatteryState state = BatteryState.Discharging)
    {
        BatteryDevice device = new(id, "Test Battery", "Test Mfr", "SN1", "LiP",
            Measurement<int>.Measured(design, MeasurementSource.WinRtBattery),
            Measurement<int>.Unavailable(), ReportsInMilliamps: false);

        BatteryInfo info = new()
        {
            BatteryId = id,
            TimestampUtc = DateTimeOffset.UtcNow,
            Percentage = Measurement<double>.Measured(percentage, MeasurementSource.WinRtBattery),
            State = Measurement<BatteryState>.Measured(state, MeasurementSource.WinRtBattery),
            AcOnline = Measurement<bool>.Measured(state == BatteryState.Charging || state == BatteryState.Full, MeasurementSource.SystemPowerStatus),
            RemainingCapacityMWh = Measurement<int>.Measured(remaining, MeasurementSource.WinRtBattery),
            FullChargeCapacityMWh = Measurement<int>.Measured(full, MeasurementSource.WinRtBattery),
            DesignCapacityMWh = Measurement<int>.Measured(design, MeasurementSource.WinRtBattery),
            RetentionPercent = BatteryCalculations.CalculateRetentionPercent(
                Measurement<int>.Measured(full, MeasurementSource.WinRtBattery),
                Measurement<int>.Measured(design, MeasurementSource.WinRtBattery)),
            VoltageMv = Measurement<int>.Measured(11_800, MeasurementSource.Wmi),
            PowerMw = Measurement<int>.Measured(powerMw, MeasurementSource.WinRtBattery),
            CurrentMa = Measurement<double>.Unavailable(),
            CycleCount = Measurement<int>.Unavailable(),
            TemperatureCelsius = Measurement<double>.Unavailable(),
        };

        return new BatterySnapshot(device, info);
    }
}
