using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Primitives;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Primitives;

/// <summary>
/// Tests for the measured / calculated / estimated / unavailable taxonomy.
/// </summary>
/// <remarks>
/// These are the executable form of the specification's core engineering
/// principle (section 3). If a calculated or estimated value can present itself
/// as measured, the application's central promise is broken, and only a test
/// catches that. Traceability: R-001, R-002, R-003.
/// </remarks>
public sealed class MeasurementTests
{
    [Fact]
    public void Unavailable_HasNoValue()
    {
        Measurement<int> measurement = Measurement<int>.Unavailable();

        Assert.False(measurement.HasValue);
        Assert.Null(measurement.Value);
        Assert.False(measurement.IsUsable);
    }

    [Fact]
    public void Unavailable_IsNotZero()
    {
        // The whole point of the type: an absent reading must not be
        // indistinguishable from a genuine zero.
        Measurement<int> absent = Measurement<int>.Unavailable();
        Measurement<int> genuineZero = Measurement<int>.Measured(0, MeasurementSource.Wmi);

        Assert.NotEqual(absent, genuineZero);
        Assert.False(absent.HasValue);
        Assert.True(genuineZero.HasValue);
    }

    [Theory]
    [InlineData(DataQuality.Measured, false)]
    [InlineData(DataQuality.Calculated, true)]
    [InlineData(DataQuality.Estimated, true)]
    [InlineData(DataQuality.Suspect, true)]
    public void RequiresBadge_OnlyMeasuredIsPresentedPlainly(DataQuality quality, bool expected)
    {
        Measurement<int> measurement = quality switch
        {
            DataQuality.Measured => Measurement<int>.Measured(5, MeasurementSource.WinRtBattery),
            DataQuality.Calculated => Measurement<int>.Calculated(5),
            DataQuality.Estimated => Measurement<int>.Estimated(5),
            _ => Measurement<int>.Suspect(5, MeasurementSource.Wmi),
        };

        Assert.Equal(expected, measurement.RequiresBadge);
    }

    [Fact]
    public void Suspect_IsNotUsableInComputation()
    {
        Measurement<int> suspect = Measurement<int>.Suspect(9999, MeasurementSource.Wmi);

        Assert.True(suspect.HasValue);
        Assert.False(suspect.IsUsable);
    }

    [Fact]
    public void Combine_OfTwoMeasured_YieldsCalculated_NotMeasured()
    {
        // Electric current is power divided by voltage. Both inputs are measured,
        // but the result was never read from hardware, so it must be calculated.
        Measurement<double> powerMilliwatts = Measurement<double>.Measured(6332, MeasurementSource.Wmi);
        Measurement<double> voltageMillivolts = Measurement<double>.Measured(11791, MeasurementSource.Wmi);

        Measurement<double> currentMilliamps = Measurement.Combine(
            powerMilliwatts,
            voltageMillivolts,
            (p, v) => p / v * 1000.0);

        Assert.True(currentMilliamps.HasValue);
        Assert.Equal(DataQuality.Calculated, currentMilliamps.Quality);
        Assert.NotEqual(DataQuality.Measured, currentMilliamps.Quality);
        Assert.Equal(537.0, currentMilliamps.Value!.Value, 0);
    }

    [Fact]
    public void Combine_WithUnavailableInput_YieldsUnavailable()
    {
        // Voltage is not exposed on some hardware. Current must then be
        // unavailable rather than assuming a nominal voltage.
        Measurement<double> power = Measurement<double>.Measured(6332, MeasurementSource.Wmi);
        Measurement<double> voltage = Measurement<double>.Unavailable();

        Measurement<double> current = Measurement.Combine(power, voltage, (p, v) => p / v);

        Assert.False(current.HasValue);
    }

    [Fact]
    public void Combine_WithEstimatedInput_DegradesToEstimated()
    {
        Measurement<double> measured = Measurement<double>.Measured(100, MeasurementSource.WinRtBattery);
        Measurement<double> estimated = Measurement<double>.Estimated(2);

        Measurement<double> result = Measurement.Combine(measured, estimated, (a, b) => a * b);

        Assert.Equal(DataQuality.Estimated, result.Quality);
    }

    [Fact]
    public void Map_PreservesGradeAndKeepsUnavailableUnavailable()
    {
        Measurement<int> estimated = Measurement<int>.Estimated(10);
        Assert.Equal(DataQuality.Estimated, estimated.Map(v => v * 2).Quality);

        Measurement<int> absent = Measurement<int>.Unavailable();
        Assert.False(absent.Map(v => v * 2).HasValue);
    }

    [Fact]
    public void DegradedBy_NeverImprovesAGrade()
    {
        Measurement<int> estimated = Measurement<int>.Estimated(42);

        Measurement<int> attemptedPromotion = estimated.DegradedBy(DataQuality.Measured);

        Assert.Equal(DataQuality.Estimated, attemptedPromotion.Quality);
    }
}

/// <summary>Tests for grade combination ordering.</summary>
public sealed class DataQualityTests
{
    [Theory]
    [InlineData(DataQuality.Measured, DataQuality.Calculated, DataQuality.Calculated)]
    [InlineData(DataQuality.Measured, DataQuality.Estimated, DataQuality.Estimated)]
    [InlineData(DataQuality.Calculated, DataQuality.Estimated, DataQuality.Estimated)]
    [InlineData(DataQuality.Estimated, DataQuality.Suspect, DataQuality.Suspect)]
    [InlineData(DataQuality.Measured, DataQuality.Measured, DataQuality.Measured)]
    public void Worst_ReturnsTheMoreSevereGrade(DataQuality a, DataQuality b, DataQuality expected)
    {
        Assert.Equal(expected, a.Worst(b));
        Assert.Equal(expected, b.Worst(a));
    }

    [Fact]
    public void Worst_TreatsUnknownAsWorseThanEstimated()
    {
        // Unknown is stored as 0 for database compatibility but is nearly the
        // worst grade in practice, so numeric ordering must not be relied upon.
        Assert.Equal(DataQuality.Unknown, DataQuality.Estimated.Worst(DataQuality.Unknown));
    }

    [Fact]
    public void Worst_OfEmptySequence_IsUnknown()
    {
        Assert.Equal(DataQuality.Unknown, DataQualityExtensions.Worst([]));
    }

    [Fact]
    public void Worst_OfSequence_ReturnsMostSevere()
    {
        DataQuality[] grades =
        [
            DataQuality.Measured,
            DataQuality.Calculated,
            DataQuality.Estimated,
        ];

        Assert.Equal(DataQuality.Estimated, DataQualityExtensions.Worst(grades));
    }

    [Fact]
    public void OnlyMeasured_IsPresentedPlainly()
    {
        Assert.True(DataQuality.Measured.IsPresentedPlainly());
        Assert.False(DataQuality.Calculated.IsPresentedPlainly());
        Assert.False(DataQuality.Estimated.IsPresentedPlainly());
        Assert.False(DataQuality.Suspect.IsPresentedPlainly());
    }

    [Fact]
    public void SuspectAndUnknown_AreNotTrustworthy()
    {
        Assert.False(DataQuality.Suspect.IsTrustworthy());
        Assert.False(DataQuality.Unknown.IsTrustworthy());
        Assert.True(DataQuality.Measured.IsTrustworthy());
        Assert.True(DataQuality.Calculated.IsTrustworthy());
        Assert.True(DataQuality.Estimated.IsTrustworthy());
    }
}
