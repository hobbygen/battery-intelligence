using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// The alert orchestrator over a scripted battery stream (docs/testing.md
/// section 4). Traceability: R-086, R-087. Phase 9 exit criteria: no duplicate
/// alerts, and a notification failure degrades to the in-app centre.
/// </summary>
public sealed class AlertMonitoringServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DrainingThroughTheThreshold_FiresOneLowBatteryAlert_PersistedAndPresented()
    {
        Harness h = await Harness.CreateAsync();
        h.Settings.Current.Alerts.LowBatteryPercent = 20;

        h.Battery.PushState(Start, BatteryState.Discharging, 30, ac: false);
        await h.Service.RefreshAsync();
        h.Battery.PushState(Start.AddMinutes(1), BatteryState.Discharging, 18, ac: false);
        await h.Service.RefreshAsync();

        Assert.Single(h.Store.Rows);
        Assert.Equal(AlertType.LowBattery, h.Store.Rows[0].Type);
        Assert.Equal(1, h.Service.UnacknowledgedCount);
        Assert.Single(h.Service.RecentAlerts);
        Assert.Equal(1, h.Presenter.ShowCount);

        // Still below for a while — no second alert.
        for (int m = 2; m < 10; m++)
        {
            h.Battery.PushState(Start.AddMinutes(m), BatteryState.Discharging, 15, ac: false);
            await h.Service.RefreshAsync();
        }

        Assert.Single(h.Store.Rows);

        await h.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ANotificationFailure_DegradesToInApp_WithoutError()
    {
        Harness h = await Harness.CreateAsync();
        h.Presenter.ThrowOnShow = true;
        h.Settings.Current.Alerts.LowBatteryPercent = 20;

        h.Battery.PushState(Start, BatteryState.Discharging, 30, ac: false);
        await h.Service.RefreshAsync();
        h.Battery.PushState(Start.AddMinutes(1), BatteryState.Discharging, 15, ac: false);
        await h.Service.RefreshAsync();

        // The toast threw, but the alert is still persisted and counted.
        Assert.Single(h.Store.Rows);
        Assert.Equal(1, h.Service.UnacknowledgedCount);
        Assert.Null(h.Service.LastError);

        await h.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RePluggingAndReDraining_ReFires_ButAnOscillationDoesNot()
    {
        Harness h = await Harness.CreateAsync();
        h.Settings.Current.Alerts.LowBatteryPercent = 20;
        h.Settings.Current.Alerts.CooldownMinutes = 1;

        h.Battery.PushState(Start, BatteryState.Discharging, 30, ac: false);
        await h.Service.RefreshAsync();
        h.Battery.PushState(Start.AddMinutes(1), BatteryState.Discharging, 18, ac: false);
        await h.Service.RefreshAsync();
        Assert.Single(h.Store.Rows);

        // Charge back to 60, then drain to 18 again an hour later → re-fires.
        h.Battery.PushState(Start.AddMinutes(30), BatteryState.Charging, 60, ac: true);
        await h.Service.RefreshAsync();
        h.Battery.PushState(Start.AddMinutes(90), BatteryState.Discharging, 18, ac: false);
        await h.Service.RefreshAsync();
        Assert.Equal(2, h.Store.Rows.Count);

        // Now oscillate around the line — no third alert.
        foreach ((int min, double pct) in new[] { (95, 21.0), (100, 19.0), (105, 22.0), (110, 18.0) })
        {
            h.Battery.PushState(Start.AddMinutes(min), BatteryState.Discharging, pct, ac: false);
            await h.Service.RefreshAsync();
        }

        Assert.Equal(2, h.Store.Rows.Count);

        await h.Service.StopAsync(CancellationToken.None);
    }

    private sealed class Harness
    {
        public required AlertMonitoringService Service { get; init; }
        public required AnalyticsFakeBattery Battery { get; init; }
        public required FakeAlertStore Store { get; init; }
        public required FakePresenter Presenter { get; init; }
        public required AnalyticsFakeSettings Settings { get; init; }

        public static async Task<Harness> CreateAsync()
        {
            AnalyticsFakeBattery battery = new();
            FakeAlertStore store = new();
            FakePresenter presenter = new();
            AnalyticsFakeSettings settings = new();

            AlertMonitoringService service = new(
                battery,
                new FakeAnalyticsService(),
                new FakeProcessMonitoring(),
                new FakeRuntimeEstimationService(),
                store,
                presenter,
                settings,
                NullLogger<AlertMonitoringService>.Instance);

            await service.StartAsync(CancellationToken.None);
            return new Harness { Service = service, Battery = battery, Store = store, Presenter = presenter, Settings = settings };
        }
    }
}
