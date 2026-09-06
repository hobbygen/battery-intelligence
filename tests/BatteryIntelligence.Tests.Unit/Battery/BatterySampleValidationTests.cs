using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Battery;

/// <summary>
/// The Validator stage's plausibility checks (docs/monitoring-dataflow.md
/// section 4). Every check re-grades the offending field Suspect rather than
/// dropping it, and a fully plausible reading must pass through unchanged.
/// </summary>
public sealed class BatterySampleValidationTests
{
    [Fact]
    public void ApplyPlausibilityChecks_PlausibleReading_IsUnchanged()
    {
        BatteryInfo info = MakeInfo(percentage: 84.0, voltageMv: 11_693, temperatureC: 32.0, powerMw: 8_442, remaining: 30_000, full: 38_008);

        BatteryInfo result = BatterySampleValidation.ApplyPlausibilityChecks(info);

        Assert.Equal(info, result);
        Assert.Equal(DataQuality.Measured, result.Percentage.Quality);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(30_001)]
    public void ApplyPlausibilityChecks_ImplausibleVoltage_IsGradedSuspect(int voltageMv)
    {
        BatteryInfo info = MakeInfo(84.0, voltageMv, 32.0, 8_442, 30_000, 38_008);

        BatteryInfo result = BatterySampleValidation.ApplyPlausibilityChecks(info);

        Assert.Equal(DataQuality.Suspect, result.VoltageMv.Quality);
        Assert.False(result.VoltageMv.IsUsable);
        Assert.True(result.VoltageMv.HasValue); // stored for diagnosis, not dropped
    }

    [Theory]
    [InlineData(-41.0)]
    [InlineData(81.0)]
    public void ApplyPlausibilityChecks_ImplausibleTemperature_IsGradedSuspect(double temperatureC)
    {
        BatteryInfo info = MakeInfo(84.0, 11_693, temperatureC, 8_442, 30_000, 38_008);

        BatteryInfo result = BatterySampleValidation.ApplyPlausibilityChecks(info);

        Assert.Equal(DataQuality.Suspect, result.TemperatureCelsius.Quality);
    }

    [Fact]
    public void ApplyPlausibilityChecks_ExcessiveRate_IsGradedSuspect()
    {
        BatteryInfo info = MakeInfo(84.0, 11_693, 32.0, powerMw: 400_000, 30_000, 38_008);

        BatteryInfo result = BatterySampleValidation.ApplyPlausibilityChecks(info);

        Assert.Equal(DataQuality.Suspect, result.PowerMw.Quality);
    }

    [Fact]
    public void ApplyPlausibilityChecks_RemainingFarAboveFull_IsGradedSuspect()
    {
        // 1.05x tolerance exceeded: remaining cannot legitimately be 20% above full.
        BatteryInfo info = MakeInfo(84.0, 11_693, 32.0, 8_442, remaining: 45_000, full: 38_008);

        BatteryInfo result = BatterySampleValidation.ApplyPlausibilityChecks(info);

        Assert.Equal(DataQuality.Suspect, result.RemainingCapacityMWh.Quality);
    }

    [Fact]
    public void ApplyPlausibilityChecks_RemainingSlightlyAboveFull_IsTolerated()
    {
        // Firmware occasionally reports slightly over full near end-of-charge.
        BatteryInfo info = MakeInfo(100.0, 11_693, 32.0, 500, remaining: 38_500, full: 38_008);

        BatteryInfo result = BatterySampleValidation.ApplyPlausibilityChecks(info);

        Assert.Equal(DataQuality.Measured, result.RemainingCapacityMWh.Quality);
    }

    [Theory]
    [InlineData(-5.0)]
    [InlineData(105.0)]
    public void ApplyPlausibilityChecks_OutOfRangePercentage_IsClampedAndGradedSuspect(double percentage)
    {
        BatteryInfo info = MakeInfo(percentage, 11_693, 32.0, 8_442, 30_000, 38_008);

        BatteryInfo result = BatterySampleValidation.ApplyPlausibilityChecks(info);

        Assert.Equal(DataQuality.Suspect, result.Percentage.Quality);
        Assert.InRange(result.Percentage.Value!.Value, 0.0, 100.0);
    }

    [Fact]
    public void ApplyPlausibilityChecks_UnavailableFields_StayUnavailable_NeverBecomeSuspect()
    {
        BatteryInfo info = BatteryInfo.Empty("battery0", DateTimeOffset.UtcNow);

        BatteryInfo result = BatterySampleValidation.ApplyPlausibilityChecks(info);

        Assert.False(result.VoltageMv.HasValue);
        Assert.False(result.TemperatureCelsius.HasValue);
        Assert.NotEqual(DataQuality.Suspect, result.VoltageMv.Quality);
    }

    private static BatteryInfo MakeInfo(
        double percentage, int voltageMv, double temperatureC, int powerMw, int remaining, int full) => new()
    {
        BatteryId = "battery0",
        TimestampUtc = DateTimeOffset.UtcNow,
        Percentage = Measurement<double>.Measured(percentage, MeasurementSource.WinRtBattery),
        State = Measurement<BatteryState>.Measured(BatteryState.Discharging, MeasurementSource.WinRtBattery),
        AcOnline = Measurement<bool>.Measured(false, MeasurementSource.SystemPowerStatus),
        RemainingCapacityMWh = Measurement<int>.Measured(remaining, MeasurementSource.WinRtBattery),
        FullChargeCapacityMWh = Measurement<int>.Measured(full, MeasurementSource.WinRtBattery),
        DesignCapacityMWh = Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery),
        RetentionPercent = Measurement<double>.Calculated(40.0),
        VoltageMv = Measurement<int>.Measured(voltageMv, MeasurementSource.Wmi),
        PowerMw = Measurement<int>.Measured(powerMw, MeasurementSource.WinRtBattery),
        CurrentMa = Measurement<double>.Calculated(722.0),
        CycleCount = Measurement<int>.Unavailable(),
        TemperatureCelsius = Measurement<double>.Measured(temperatureC, MeasurementSource.BatteryIoctl),
    };
}
