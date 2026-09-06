using BatteryIntelligence.Battery.Simulation;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// Scripted hardware scenarios (specification section 61; docs/testing.md
/// section 4) exercised through the production <see cref="Core.Interfaces.IBatteryProvider"/>
/// contract.
/// </summary>
public sealed class SimulatedBatteryProviderTests
{
    [Fact]
    public async Task NormalDischarge_FollowsTheScriptedCurve()
    {
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.NormalDischarge);
        double[] expected = [100.0, 90.0, 80.0, 50.0, 20.0, 10.0];

        foreach (double expectedPercent in expected)
        {
            IReadOnlyList<BatterySnapshot> snapshots = await provider.GetSnapshotsAsync();

            Assert.Single(snapshots);
            Assert.Equal(expectedPercent, snapshots[0].Info.Percentage.Value!.Value, 0);
            Assert.Equal(BatteryState.Discharging, snapshots[0].Info.State.Value);
        }
    }

    [Fact]
    public async Task NormalCharge_ReachesFull_WithTaperedRate()
    {
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.NormalCharge);

        IReadOnlyList<BatterySnapshot> first = await provider.GetSnapshotsAsync();
        Assert.Equal(BatteryState.Charging, first[0].Info.State.Value);
        Assert.True(first[0].Info.PowerMw.Value > 0);

        // Advance to the final scripted step (index 3 = 100%).
        await provider.GetSnapshotsAsync();
        await provider.GetSnapshotsAsync();
        IReadOnlyList<BatterySnapshot> last = await provider.GetSnapshotsAsync();

        Assert.Equal(100.0, last[0].Info.Percentage.Value!.Value, 0);
        Assert.Equal(BatteryState.Full, last[0].Info.State.Value);
    }

    [Fact]
    public async Task MultipleBatteries_ReturnsTwoIndependentDevices()
    {
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.MultipleBatteries);

        IReadOnlyList<BatterySnapshot> snapshots = await provider.GetSnapshotsAsync();

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(2, snapshots.Select(s => s.Device.HardwareId).Distinct().Count());
    }

    [Fact]
    public async Task NoTemperatureSensor_TemperatureIsUnavailable_NotZero()
    {
        // The reference machine's real configuration (docs/capability-matrix.md).
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.NoTemperatureSensor);

        IReadOnlyList<BatterySnapshot> snapshots = await provider.GetSnapshotsAsync();

        Assert.False(snapshots[0].Info.TemperatureCelsius.HasValue);
    }

    [Fact]
    public async Task NoCycleCount_FirmwareZero_IsTreatedAsUnavailable()
    {
        // Quirk Q5: zero from firmware on a battery at real wear is "not
        // reported", not "brand new".
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.NoCycleCount);

        IReadOnlyList<BatterySnapshot> snapshots = await provider.GetSnapshotsAsync();

        Assert.False(snapshots[0].Info.CycleCount.HasValue);
    }

    [Fact]
    public async Task MilliampReporting_DeviceFlagIsSet()
    {
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.MilliampReporting);

        IReadOnlyList<BatterySnapshot> snapshots = await provider.GetSnapshotsAsync();

        Assert.True(snapshots[0].Device.ReportsInMilliamps);
    }

    [Fact]
    public async Task SensorDropoutMidSession_SucceedsThenFails()
    {
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.SensorDropoutMidSession);

        await provider.GetSnapshotsAsync();
        await provider.GetSnapshotsAsync();
        await provider.GetSnapshotsAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSnapshotsAsync());
    }

    [Fact]
    public async Task ApiFailure_EveryReadThrows()
    {
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.ApiFailure);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSnapshotsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSnapshotsAsync());
    }

    [Fact]
    public async Task EveryMeasuredValue_CarriesTheSimulationSource()
    {
        // A simulated reading must never be mistaken for a real one downstream
        // (docs/architecture.md section 10).
        SimulatedBatteryProvider provider = new(BatterySimulationScenario.NormalDischarge);

        IReadOnlyList<BatterySnapshot> snapshots = await provider.GetSnapshotsAsync();

        Assert.Equal(MeasurementSource.Simulation, snapshots[0].Info.Percentage.Source);
        Assert.Equal(MeasurementSource.Simulation, snapshots[0].Info.State.Source);
    }
}
