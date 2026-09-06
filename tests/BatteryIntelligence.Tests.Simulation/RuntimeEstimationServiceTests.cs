using BatteryIntelligence.Analytics;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// The live runtime estimator over a scripted discharge stream
/// (docs/estimation-strategy.md section 3). Traceability: R-080.
/// </summary>
public sealed class RuntimeEstimationServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ASteadyDischarge_ProducesAShrinkingAtCurrentUsageFigure()
    {
        AnalyticsFakeBattery battery = new();
        AnalyticsFakeSessions sessions = new() { CurrentScreenState = ScreenState.On };
        RuntimeEstimationService service = new(battery, sessions, new AnalyticsFakeSettings(), NullLogger<RuntimeEstimationService>.Instance);
        await service.StartAsync(CancellationToken.None);

        int remaining = 20_000;
        RuntimeEstimate? afterTenMinutes = null;
        for (int minute = 0; minute <= 20; minute++)
        {
            battery.Push(AnalyticsFakeBattery.Discharging(Start.AddMinutes(minute), powerMw: 10_000, remainingMwh: remaining));
            remaining -= 167; // ~10 W for a minute
            if (minute == 10)
            {
                afterTenMinutes = service.Current;
            }
        }

        Assert.NotNull(afterTenMinutes);
        Assert.True(afterTenMinutes!.IsAvailable);
        Assert.True(service.Current.IsAvailable);
        Assert.True(service.Current.AtCurrentUsage < afterTenMinutes.AtCurrentUsage);
        Assert.Equal(EstimateConfidence.High, service.Current.Confidence);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ScreenOff_RuntimeStaysUnavailableUntilScreenOffSamplesArrive()
    {
        AnalyticsFakeBattery battery = new();
        AnalyticsFakeSessions sessions = new() { CurrentScreenState = ScreenState.On };
        RuntimeEstimationService service = new(battery, sessions, new AnalyticsFakeSettings(), NullLogger<RuntimeEstimationService>.Instance);
        await service.StartAsync(CancellationToken.None);

        for (int minute = 0; minute < 12; minute++)
        {
            battery.Push(AnalyticsFakeBattery.Discharging(Start.AddMinutes(minute), 12_000, 20_000));
        }

        Assert.Null(service.Current.ScreenOff);

        sessions.CurrentScreenState = ScreenState.Off;
        for (int minute = 12; minute < 24; minute++)
        {
            battery.Push(AnalyticsFakeBattery.Discharging(Start.AddMinutes(minute), 5_000, 18_000));
        }

        Assert.NotNull(service.Current.ScreenOff);
        Assert.True(service.Current.ScreenOff > service.Current.ScreenOn); // lower draw → longer runtime

        await service.StopAsync(CancellationToken.None);
    }
}
