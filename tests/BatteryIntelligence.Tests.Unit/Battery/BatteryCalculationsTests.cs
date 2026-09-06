using BatteryIntelligence.Core.Battery;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Battery;

/// <summary>
/// Covers docs/testing.md section 3: retention maths, mA/mW normalisation
/// (quirk Q2), and the cycle-count-zero quirk (Q5) — all derived values the
/// specification requires to be graded correctly rather than presented as raw
/// measurements.
/// </summary>
public sealed class BatteryCalculationsTests
{
    [Fact]
    public void CalculateRetentionPercent_MatchesReferenceMachine()
    {
        // docs/capability-matrix.md section 1: 38,008 / 95,008 = 40.0%.
        Measurement<int> full = Measurement<int>.Measured(38_008, MeasurementSource.WinRtBattery);
        Measurement<int> design = Measurement<int>.Measured(95_008, MeasurementSource.WinRtBattery);

        Measurement<double> retention = BatteryCalculations.CalculateRetentionPercent(full, design);

        Assert.True(retention.HasValue);
        Assert.Equal(DataQuality.Calculated, retention.Quality);
        Assert.Equal(40.0, retention.Value!.Value, 1);
    }

    [Fact]
    public void CalculateRetentionPercent_DesignCapacityUnavailable_YieldsUnavailable_NotZero()
    {
        // Specification section 9: never invent a health percentage when the
        // inputs do not exist.
        Measurement<int> full = Measurement<int>.Measured(38_008, MeasurementSource.WinRtBattery);
        Measurement<int> design = Measurement<int>.Unavailable();

        Measurement<double> retention = BatteryCalculations.CalculateRetentionPercent(full, design);

        Assert.False(retention.HasValue);
    }

    [Fact]
    public void CalculateRetentionPercent_ZeroDesignCapacity_YieldsUnavailable()
    {
        Measurement<int> full = Measurement<int>.Measured(1000, MeasurementSource.Wmi);
        Measurement<int> design = Measurement<int>.Measured(0, MeasurementSource.Wmi);

        Assert.False(BatteryCalculations.CalculateRetentionPercent(full, design).HasValue);
    }

    [Fact]
    public void CalculateCurrentMa_IsAlwaysCalculated_NeverMeasured()
    {
        // docs/capability-matrix.md: C11 must never be promoted to Measured even
        // though both inputs are Measured.
        Measurement<int> power = Measurement<int>.Measured(8_442, MeasurementSource.WinRtBattery);
        Measurement<int> voltage = Measurement<int>.Measured(11_693, MeasurementSource.Wmi);

        Measurement<double> current = BatteryCalculations.CalculateCurrentMa(power, voltage);

        Assert.True(current.HasValue);
        Assert.Equal(DataQuality.Calculated, current.Quality);
        Assert.Equal(722.0, current.Value!.Value, 0);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(30_001)]
    public void CalculateCurrentMa_ImplausibleVoltage_YieldsUnavailable_NotInfinite(int voltageMv)
    {
        Measurement<int> power = Measurement<int>.Measured(1000, MeasurementSource.Wmi);
        Measurement<int> voltage = Measurement<int>.Measured(voltageMv, MeasurementSource.Wmi);

        Assert.False(BatteryCalculations.CalculateCurrentMa(power, voltage).HasValue);
    }

    [Fact]
    public void CalculateCurrentMa_VoltageUnavailable_YieldsUnavailable()
    {
        Measurement<int> power = Measurement<int>.Measured(1000, MeasurementSource.Wmi);
        Measurement<int> voltage = Measurement<int>.Unavailable();

        Assert.False(BatteryCalculations.CalculateCurrentMa(power, voltage).HasValue);
    }

    [Fact]
    public void NormalizeMilliampsToMilliwatts_PassesThroughWhenNotMilliampReporting()
    {
        Measurement<int> raw = Measurement<int>.Measured(6_332, MeasurementSource.WinRtBattery);
        Measurement<int> voltage = Measurement<int>.Measured(11_791, MeasurementSource.Wmi);

        Measurement<int> result = BatteryCalculations.NormalizeMilliampsToMilliwatts(raw, reportsInMilliamps: false, voltage);

        Assert.Equal(raw, result);
    }

    [Fact]
    public void NormalizeMilliampsToMilliwatts_ConvertsAndDowngradesToCalculated()
    {
        // Quirk Q2: 540 mA at 11,400 mV -> ~6,156 mW.
        Measurement<int> rawMilliamps = Measurement<int>.Measured(540, MeasurementSource.Wmi);
        Measurement<int> voltage = Measurement<int>.Measured(11_400, MeasurementSource.Wmi);

        Measurement<int> result = BatteryCalculations.NormalizeMilliampsToMilliwatts(rawMilliamps, reportsInMilliamps: true, voltage);

        Assert.True(result.HasValue);
        Assert.Equal(DataQuality.Calculated, result.Quality);
        Assert.Equal(6_156, result.Value!.Value);
    }

    [Fact]
    public void NormalizeMilliampsToMilliwatts_MilliampReportingWithoutVoltage_YieldsUnavailable_NotAWrongGuess()
    {
        Measurement<int> rawMilliamps = Measurement<int>.Measured(540, MeasurementSource.Wmi);
        Measurement<int> voltage = Measurement<int>.Unavailable();

        Assert.False(BatteryCalculations.NormalizeMilliampsToMilliwatts(rawMilliamps, reportsInMilliamps: true, voltage).HasValue);
    }

    [Fact]
    public void ApplyCycleCountZeroQuirk_ZeroBecomesUnavailable()
    {
        // Quirk Q5: a firmware-reported zero on a worn battery means "not
        // reported", not "brand new" (docs/capability-matrix.md section 4).
        Measurement<int> zero = Measurement<int>.Measured(0, MeasurementSource.Wmi);

        Assert.False(BatteryCalculations.ApplyCycleCountZeroQuirk(zero).HasValue);
    }

    [Fact]
    public void ApplyCycleCountZeroQuirk_NonZeroPassesThrough()
    {
        Measurement<int> value = Measurement<int>.Measured(340, MeasurementSource.BatteryIoctl);

        Assert.Equal(value, BatteryCalculations.ApplyCycleCountZeroQuirk(value));
    }

    [Fact]
    public void ApplyCycleCountZeroQuirk_UnavailableStaysUnavailable()
    {
        Assert.False(BatteryCalculations.ApplyCycleCountZeroQuirk(Measurement<int>.Unavailable()).HasValue);
    }
}
