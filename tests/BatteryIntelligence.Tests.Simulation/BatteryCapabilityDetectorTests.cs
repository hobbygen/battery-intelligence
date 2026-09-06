using BatteryIntelligence.Battery;
using BatteryIntelligence.Battery.Simulation;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryIntelligence.Tests.Simulation;

/// <summary>
/// <see cref="BatteryCapabilityDetector"/> driven by scripted providers instead
/// of real hardware — specification section 26's mandatory Diagnostics rows must
/// render correctly for both the present-sensor and absent-sensor cases
/// (docs/testing.md's central problem: real hardware cannot exercise both).
/// </summary>
public sealed class BatteryCapabilityDetectorTests
{
    [Fact]
    public async Task NoTemperatureSensor_ReportsTemperatureUnavailable_ButEverythingElseAvailable()
    {
        BatteryCapabilityDetector detector = new(
            new SimulatedBatteryProvider(BatterySimulationScenario.NoTemperatureSensor),
            NullLogger<BatteryCapabilityDetector>.Instance);

        CapabilitySnapshot snapshot = await detector.DetectAsync();

        CapabilityRow? temperature = snapshot.Find(CapabilityId.Temperature);
        Assert.NotNull(temperature);
        Assert.False(temperature!.Available);

        CapabilityRow? percentage = snapshot.Find(CapabilityId.ChargePercentage);
        Assert.True(percentage!.Available);
    }

    [Fact]
    public async Task NoCycleCount_ReportsCycleCountUnavailable()
    {
        BatteryCapabilityDetector detector = new(
            new SimulatedBatteryProvider(BatterySimulationScenario.NoCycleCount),
            NullLogger<BatteryCapabilityDetector>.Instance);

        CapabilitySnapshot snapshot = await detector.DetectAsync();

        Assert.False(snapshot.Find(CapabilityId.CycleCount)!.Available);
    }

    [Fact]
    public async Task NoBattery_ReportsOnlyThePresenceRow_AsUnavailable()
    {
        BatteryCapabilityDetector detector = new(
            new EmptyBatteryProvider(),
            NullLogger<BatteryCapabilityDetector>.Instance);

        CapabilitySnapshot snapshot = await detector.DetectAsync();

        CapabilityRow row = Assert.Single(snapshot.Rows);
        Assert.Equal(CapabilityId.BatteryPresent, row.Id);
        Assert.False(row.Available);
    }

    [Fact]
    public async Task ProviderThrows_DetectionDoesNotThrow_ReportsNoBatteryInstead()
    {
        // Specification section 44: one failing subsystem must not take down the
        // application — here, the Diagnostics page itself.
        BatteryCapabilityDetector detector = new(
            new SimulatedBatteryProvider(BatterySimulationScenario.ApiFailure),
            NullLogger<BatteryCapabilityDetector>.Instance);

        CapabilitySnapshot snapshot = await detector.DetectAsync();

        Assert.False(snapshot.Find(CapabilityId.BatteryPresent)!.Available);
    }

    private sealed class EmptyBatteryProvider : IBatteryProvider
    {
        public string Name => "Empty";

        public Task<IReadOnlyList<BatterySnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BatterySnapshot>>([]);
    }
}
