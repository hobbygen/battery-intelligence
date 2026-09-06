using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Thermal;

namespace BatteryIntelligence.Tests.Unit.Thermal;

/// <summary>
/// The dwell-gated threshold-event detector: an event is confirmed only after the
/// reading holds at or above the warning threshold for the dwell time, so a brief
/// spike never creates one (docs/ui-navigation.md — "recorded when the reading
/// stays above a threshold for more than 60 seconds").
/// </summary>
public sealed class ThresholdEventDetectorTests
{
    private const double Warn = 45.0;

    [Fact]
    public void Observe_BriefSpikeBelowTheDwell_DoesNotCreateAnEvent()
    {
        ThresholdEventDetector detector = new(TimeSpan.FromSeconds(60));
        DateTimeOffset t = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(detector.Observe(t, 46.0, Warn, PowerDirection.Charging));
        Assert.Null(detector.Observe(t.AddSeconds(20), 47.0, Warn, PowerDirection.Charging)); // still under 60 s
        ThresholdEvent? closed = detector.Observe(t.AddSeconds(40), 40.0, Warn, PowerDirection.Charging);

        Assert.Null(closed);
        Assert.Null(detector.OpenEvent);
    }

    [Fact]
    public void Observe_SustainedAboveThreshold_ConfirmsAndThenClosesAnEvent()
    {
        ThresholdEventDetector detector = new(TimeSpan.FromSeconds(60));
        DateTimeOffset start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        detector.Observe(start, 46.0, Warn, PowerDirection.Charging);
        detector.Observe(start.AddSeconds(30), 48.0, Warn, PowerDirection.Charging);
        Assert.Null(detector.OpenEvent); // dwell not yet met

        detector.Observe(start.AddSeconds(65), 47.0, Warn, PowerDirection.Charging);
        Assert.NotNull(detector.OpenEvent);
        Assert.Equal(start, detector.OpenEvent!.StartUtc);
        Assert.Equal(48.0, detector.OpenEvent.PeakCelsius);

        ThresholdEvent? closed = detector.Observe(start.AddSeconds(120), 42.0, Warn, PowerDirection.Charging);

        Assert.NotNull(closed);
        Assert.Equal(start, closed!.StartUtc);
        Assert.Equal(start.AddSeconds(120), closed.EndUtc);
        Assert.Equal(48.0, closed.PeakCelsius);
        Assert.False(closed.IsOpen);
        Assert.Null(detector.OpenEvent);
    }

    [Fact]
    public void Observe_DropAndReRise_StartsAFreshDwellRatherThanReusingTheOldOne()
    {
        ThresholdEventDetector detector = new(TimeSpan.FromSeconds(60));
        DateTimeOffset t = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        detector.Observe(t, 46.0, Warn, PowerDirection.Discharging);
        detector.Observe(t.AddSeconds(30), 40.0, Warn, PowerDirection.Discharging); // dropped before confirm
        detector.Observe(t.AddSeconds(40), 46.0, Warn, PowerDirection.Discharging); // rises again
        Assert.Null(detector.OpenEvent);                                            // fresh dwell, not yet 60 s

        detector.Observe(t.AddSeconds(105), 46.0, Warn, PowerDirection.Discharging); // 65 s into the second rise
        Assert.NotNull(detector.OpenEvent);
        Assert.Equal(t.AddSeconds(40), detector.OpenEvent!.StartUtc);
    }
}
