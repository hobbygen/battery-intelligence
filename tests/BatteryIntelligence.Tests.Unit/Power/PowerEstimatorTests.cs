using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Power;
using BatteryIntelligence.Core.Primitives;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Power;

/// <summary>
/// The energy-rate fallback ladder from docs/estimation-strategy.md section 2:
/// Measured → Calculated (V×I) → Estimated (ΔmWh/Δt) → Unavailable. A grade may
/// only ever degrade down the ladder, never improve.
/// </summary>
public sealed class PowerEstimatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Estimate_MeasuredPowerPresent_ReturnsItUnchanged()
    {
        Measurement<int> measured = Measurement<int>.Measured(-6_332, MeasurementSource.WinRtBattery);

        Measurement<int> result = PowerEstimator.Estimate(
            measured, Measurement<int>.Measured(11_700, MeasurementSource.Wmi),
            Measurement<double>.Unavailable(), [], Now);

        Assert.Equal(DataQuality.Measured, result.Quality);
        Assert.Equal(-6_332, result.Value);
    }

    [Fact]
    public void Estimate_NoMeasuredPower_ButVoltageAndSeparatelyMeasuredCurrent_IsCalculated()
    {
        Measurement<int> result = PowerEstimator.Estimate(
            Measurement<int>.Unavailable(),
            Measurement<int>.Measured(12_000, MeasurementSource.Wmi),
            Measurement<double>.Measured(-500, MeasurementSource.BatteryIoctl),
            [],
            Now);

        Assert.Equal(DataQuality.Calculated, result.Quality);
        // 12 V x -0.5 A = -6 W = -6000 mW
        Assert.Equal(-6_000, result.Value);
    }

    [Fact]
    public void Estimate_DerivedCurrentIsNotAcceptedForRung2()
    {
        // A current that is itself Calculated must not drive V×I — that would be circular.
        Measurement<int> result = PowerEstimator.Estimate(
            Measurement<int>.Unavailable(),
            Measurement<int>.Measured(12_000, MeasurementSource.Wmi),
            Measurement<double>.Calculated(-500),
            [],
            Now);

        Assert.Equal(DataQuality.Unknown, result.Quality);
        Assert.False(result.HasValue);
    }

    [Fact]
    public void Estimate_FallsBackToCapacitySlope_OverAtLeastAMinute_IsEstimated()
    {
        // Discharged 100 mWh over 2 minutes ⇒ -3000 mW.
        List<TimePoint> history =
        [
            new(Now.AddMinutes(-2), 30_000),
            new(Now.AddMinutes(-1), 29_950),
            new(Now, 29_900),
        ];

        Measurement<int> result = PowerEstimator.Estimate(
            Measurement<int>.Unavailable(), Measurement<int>.Unavailable(), Measurement<double>.Unavailable(),
            history, Now);

        Assert.Equal(DataQuality.Estimated, result.Quality);
        Assert.Equal(-3_000, result.Value);
        Assert.Equal(MeasurementSource.Model, result.Source);
    }

    [Fact]
    public void Estimate_CapacityHistoryShorterThanAMinute_YieldsUnavailable_NotABadGuess()
    {
        List<TimePoint> history =
        [
            new(Now.AddSeconds(-30), 30_000),
            new(Now, 29_990),
        ];

        Measurement<int> result = PowerEstimator.Estimate(
            Measurement<int>.Unavailable(), Measurement<int>.Unavailable(), Measurement<double>.Unavailable(),
            history, Now);

        Assert.False(result.HasValue);
    }

    [Fact]
    public void Estimate_NothingAvailable_IsUnavailable()
    {
        Measurement<int> result = PowerEstimator.Estimate(
            Measurement<int>.Unavailable(), Measurement<int>.Unavailable(), Measurement<double>.Unavailable(),
            [], Now);

        Assert.False(result.HasValue);
        Assert.Equal(DataQuality.Unknown, result.Quality);
    }

    [Fact]
    public void Estimate_SuspectMeasuredPower_IsNotTreatedAsRung1()
    {
        Measurement<int> result = PowerEstimator.Estimate(
            Measurement<int>.Suspect(999_999, MeasurementSource.WinRtBattery),
            Measurement<int>.Unavailable(),
            Measurement<double>.Unavailable(),
            [],
            Now);

        Assert.NotEqual(DataQuality.Measured, result.Quality);
        Assert.False(result.HasValue);
    }

    [Fact]
    public void Estimate_StaleCapacityTail_IsRejected()
    {
        // Newest point is well older than the estimate window ⇒ no estimate.
        List<TimePoint> history =
        [
            new(Now.AddMinutes(-10), 30_000),
            new(Now.AddMinutes(-8), 29_900),
        ];

        Measurement<int> result = PowerEstimator.Estimate(
            Measurement<int>.Unavailable(), Measurement<int>.Unavailable(), Measurement<double>.Unavailable(),
            history, Now);

        Assert.False(result.HasValue);
    }
}
