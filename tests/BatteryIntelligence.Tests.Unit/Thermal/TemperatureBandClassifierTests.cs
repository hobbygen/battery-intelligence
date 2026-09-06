using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Thermal;

namespace BatteryIntelligence.Tests.Unit.Thermal;

/// <summary>
/// The fixed-boundary band classifier and the threshold classifier
/// (docs/ui-navigation.md section 2). Boundaries are 30 / 40 / 45 °C for bands;
/// the severity classifier moves with the configured alert threshold.
/// </summary>
public sealed class TemperatureBandClassifierTests
{
    [Theory]
    [InlineData(18.0, TemperatureBand.Cool)]
    [InlineData(29.99, TemperatureBand.Cool)]
    [InlineData(30.0, TemperatureBand.Normal)]
    [InlineData(39.9, TemperatureBand.Normal)]
    [InlineData(40.0, TemperatureBand.Warm)]
    [InlineData(44.9, TemperatureBand.Warm)]
    [InlineData(45.0, TemperatureBand.Hot)]
    [InlineData(60.0, TemperatureBand.Hot)]
    public void Classify_PutsAReadingInTheRightBand(double celsius, TemperatureBand expected) =>
        Assert.Equal(expected, TemperatureBandClassifier.Classify(celsius));

    [Fact]
    public void FromWarnCelsius_PutsCriticalSevenDegreesAbove()
    {
        TemperatureThresholds thresholds = TemperatureThresholds.FromWarnCelsius(45.0);

        Assert.Equal(45.0, thresholds.WarnCelsius);
        Assert.Equal(52.0, thresholds.CriticalCelsius);
    }

    [Theory]
    [InlineData(30.0, TemperatureSeverity.Normal)]
    [InlineData(44.9, TemperatureSeverity.Normal)]
    [InlineData(45.0, TemperatureSeverity.Warning)]
    [InlineData(51.9, TemperatureSeverity.Warning)]
    [InlineData(52.0, TemperatureSeverity.Critical)]
    public void Classify_SeverityFollowsTheConfiguredThreshold(double celsius, TemperatureSeverity expected)
    {
        TemperatureThresholds thresholds = TemperatureThresholds.FromWarnCelsius(45.0);

        Assert.Equal(expected, thresholds.Classify(celsius));
    }

    [Fact]
    public void Classify_SeverityShifts_WhenTheThresholdChanges()
    {
        // The same 42 °C reading is Normal at a 45 °C threshold but a Warning at 40.
        Assert.Equal(TemperatureSeverity.Normal, TemperatureThresholds.FromWarnCelsius(45.0).Classify(42.0));
        Assert.Equal(TemperatureSeverity.Warning, TemperatureThresholds.FromWarnCelsius(40.0).Classify(42.0));
    }
}
