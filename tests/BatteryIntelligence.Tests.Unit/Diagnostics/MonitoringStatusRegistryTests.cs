using BatteryIntelligence.Core.Diagnostics;

namespace BatteryIntelligence.Tests.Unit.Diagnostics;

/// <summary>
/// The Healthy → Degraded surface for the Diagnostics page (R-098;
/// docs/monitoring-dataflow.md section 8).
/// </summary>
public sealed class MonitoringStatusRegistryTests
{
    [Fact]
    public void Snapshot_BeforeAnyReport_IsStartingForEveryComponent()
    {
        var registry = new MonitoringStatusRegistry();

        IReadOnlyList<MonitoringStatus> snapshot = registry.Snapshot();

        Assert.Equal(Enum.GetValues<MonitoringComponent>().Length, snapshot.Count);
        Assert.All(snapshot, s => Assert.Equal(MonitoringHealth.Starting, s.Health));
    }

    [Fact]
    public void ReportSuccess_MakesTheComponentHealthy_AndClearsTheError()
    {
        var registry = new MonitoringStatusRegistry();

        registry.ReportFailure(MonitoringComponent.Power, "boom");
        registry.ReportSuccess(MonitoringComponent.Power);

        MonitoringStatus status = Status(registry, MonitoringComponent.Power);
        Assert.Equal(MonitoringHealth.Healthy, status.Health);
        Assert.Null(status.LastError);
        Assert.Equal(0, status.ConsecutiveFailures);
        Assert.NotNull(status.LastSuccessUtc);
    }

    [Fact]
    public void ReportFailure_BelowThreshold_RecordsTheErrorButIsNotDegraded()
    {
        var registry = new MonitoringStatusRegistry();

        registry.ReportFailure(MonitoringComponent.Battery, "one");
        registry.ReportFailure(MonitoringComponent.Battery, "two");

        MonitoringStatus status = Status(registry, MonitoringComponent.Battery);
        Assert.NotEqual(MonitoringHealth.Degraded, status.Health);
        Assert.Equal("two", status.LastError);
        Assert.Equal(2, status.ConsecutiveFailures);
    }

    [Fact]
    public void ReportFailure_AtThreshold_BecomesDegraded()
    {
        var registry = new MonitoringStatusRegistry();

        for (int i = 0; i < MonitoringStatusRegistry.DegradedThreshold; i++)
        {
            registry.ReportFailure(MonitoringComponent.Analytics, $"fail {i}");
        }

        Assert.Equal(MonitoringHealth.Degraded, Status(registry, MonitoringComponent.Analytics).Health);
    }

    [Fact]
    public void ReportSuccess_AfterDegraded_RecoversToHealthy()
    {
        var registry = new MonitoringStatusRegistry();

        for (int i = 0; i < 5; i++)
        {
            registry.ReportFailure(MonitoringComponent.Alerts, "down");
        }

        registry.ReportSuccess(MonitoringComponent.Alerts);

        Assert.Equal(MonitoringHealth.Healthy, Status(registry, MonitoringComponent.Alerts).Health);
    }

    [Fact]
    public void Changed_FiresOnTransition_NotOnARepeatSuccess()
    {
        var registry = new MonitoringStatusRegistry();
        int fired = 0;
        registry.Changed += (_, _) => fired++;

        registry.ReportSuccess(MonitoringComponent.Sessions); // Starting -> Healthy: 1
        registry.ReportSuccess(MonitoringComponent.Sessions); // no rendered change
        registry.ReportSuccess(MonitoringComponent.Sessions); // no rendered change

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Changed_FiresWhenHealthCrossesToDegradedAndBack()
    {
        var registry = new MonitoringStatusRegistry();
        int fired = 0;
        registry.Changed += (_, _) => fired++;

        registry.ReportFailure(MonitoringComponent.Database, "x"); // Starting -> Healthy (rendered): 1
        registry.ReportFailure(MonitoringComponent.Database, "y"); // error text change: 2
        registry.ReportFailure(MonitoringComponent.Database, "z"); // -> Degraded: 3
        registry.ReportSuccess(MonitoringComponent.Database);      // -> Healthy: 4

        Assert.Equal(4, fired);
    }

    private static MonitoringStatus Status(MonitoringStatusRegistry registry, MonitoringComponent component) =>
        registry.Snapshot().Single(s => s.Component == component);
}
