#if SIMULATION
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Interfaces;
using BatteryIntelligence.Core.Models;
using BatteryIntelligence.Core.Primitives;

namespace BatteryIntelligence.Battery.Simulation;

/// <summary>
/// Implements the production <see cref="IBatteryProvider"/> contract over a
/// scripted scenario instead of real hardware (specification section 61).
/// </summary>
/// <remarks>
/// Compiled only when <c>SIMULATION</c> is defined (Debug configuration), so it
/// is physically absent from Release binaries (docs/architecture.md section 10).
/// Each call to <see cref="GetSnapshotsAsync"/> advances one scripted step; the
/// last step repeats once the script is exhausted, so a test can keep reading
/// after the scenario "settles".
/// </remarks>
public sealed class SimulatedBatteryProvider : IBatteryProvider
{
    private readonly BatterySimulationScenario _scenario;
    private readonly IReadOnlyList<Func<DateTimeOffset, IReadOnlyList<BatterySnapshot>>> _steps;
    private int _step;

    public SimulatedBatteryProvider(BatterySimulationScenario scenario)
    {
        _scenario = scenario;
        _steps = BuildSteps(scenario);
    }

    public string Name => $"Simulated ({_scenario})";

    /// <summary>How many scripted steps remain before the scenario repeats its last frame.</summary>
    public int RemainingSteps => Math.Max(0, _steps.Count - _step);

    public Task<IReadOnlyList<BatterySnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_scenario == BatterySimulationScenario.ApiFailure)
        {
            throw new InvalidOperationException("Simulated API failure: every battery source is unreachable.");
        }

        if (_scenario == BatterySimulationScenario.SensorDropoutMidSession && _step >= 3)
        {
            throw new InvalidOperationException("Simulated sensor dropout.");
        }

        int index = Math.Min(_step, _steps.Count - 1);
        _step++;

        return Task.FromResult(_steps[index](DateTimeOffset.UtcNow));
    }

    private static IReadOnlyList<Func<DateTimeOffset, IReadOnlyList<BatterySnapshot>>> BuildSteps(
        BatterySimulationScenario scenario) => scenario switch
    {
        BatterySimulationScenario.NormalDischarge =>
        [.. new[] { 100.0, 90.0, 80.0, 50.0, 20.0, 10.0 }
            .Select<double, Func<DateTimeOffset, IReadOnlyList<BatterySnapshot>>>(pct =>
                now => [DischargingFrame(pct, now)])],

        BatterySimulationScenario.NormalCharge =>
        [.. new[] { 20.0, 30.0, 40.0, 100.0 }
            .Select<double, Func<DateTimeOffset, IReadOnlyList<BatterySnapshot>>>(pct =>
                now => [ChargingFrame(pct, now, taper: pct >= 90)])],

        BatterySimulationScenario.MultipleBatteries =>
        [now => [DischargingFrame(80.0, now, id: "sim-battery-0"), ChargingFrame(60.0, now, taper: false, id: "sim-battery-1")]],

        BatterySimulationScenario.NoTemperatureSensor =>
        [now => [DischargingFrame(55.0, now)]],

        BatterySimulationScenario.NoCycleCount =>
        [now => [DischargingFrame(55.0, now, cycleCountZero: true)]],

        BatterySimulationScenario.MilliampReporting =>
        [now => [MilliampFrame(55.0, now)]],

        BatterySimulationScenario.RisingTemperatureWhileCharging =>
        [.. new[] { (60.0, 32.0), (65.0, 35.0), (70.0, 39.0), (75.0, 43.0), (80.0, 46.0), (85.0, 47.5), (90.0, 48.0), (94.0, 47.0) }
            .Select<(double Pct, double Temp), Func<DateTimeOffset, IReadOnlyList<BatterySnapshot>>>(step =>
                now => [ChargingFrame(step.Pct, now, taper: step.Pct >= 90, temperatureCelsius: step.Temp)])],

        BatterySimulationScenario.SensorDropoutMidSession =>
        [.. Enumerable.Range(0, 3)
            .Select<int, Func<DateTimeOffset, IReadOnlyList<BatterySnapshot>>>(i =>
                now => [DischargingFrame(90.0 - (i * 5), now)])],

        _ => [now => [DischargingFrame(50.0, now)]],
    };

    private static BatterySnapshot DischargingFrame(
        double percentage,
        DateTimeOffset now,
        string id = "sim-battery-0",
        bool cycleCountZero = false)
    {
        const int design = 95_008;
        int full = 38_008;
        int remaining = (int)(full * percentage / 100.0);
        int rate = -6_332;
        int voltage = 11_791;

        BatteryDevice device = new(
            id, "Simulated Battery", "Simulated Manufacturer", "SIM-0001", "LiP",
            Measurement<int>.Measured(design, MeasurementSource.Simulation),
            Measurement<int>.Measured(voltage, MeasurementSource.Simulation),
            ReportsInMilliamps: false);

        BatteryInfo info = BuildInfo(
            id, now, percentage, BatteryState.Discharging, acOnline: false,
            remaining, full, design, voltage, rate,
            cycleCount: cycleCountZero ? 0 : 340,
            temperatureCelsius: null);

        return new BatterySnapshot(device, info);
    }

    private static BatterySnapshot ChargingFrame(
        double percentage, DateTimeOffset now, bool taper, string id = "sim-battery-0", double temperatureCelsius = 32.0)
    {
        const int design = 95_008;
        int full = 38_008;
        int remaining = (int)(full * percentage / 100.0);
        int rate = taper ? 1_200 : 9_500;
        int voltage = 12_400;

        BatteryDevice device = new(
            id, "Simulated Battery", "Simulated Manufacturer", "SIM-0001", "LiP",
            Measurement<int>.Measured(design, MeasurementSource.Simulation),
            Measurement<int>.Measured(voltage, MeasurementSource.Simulation),
            ReportsInMilliamps: false);

        BatteryState state = percentage >= 100.0 ? BatteryState.Full : BatteryState.Charging;

        BatteryInfo info = BuildInfo(
            id, now, percentage, state, acOnline: true,
            remaining, full, design, voltage, percentage >= 100.0 ? 0 : rate,
            cycleCount: 340, temperatureCelsius: temperatureCelsius);

        return new BatterySnapshot(device, info);
    }

    private static BatterySnapshot MilliampFrame(double percentage, DateTimeOffset now)
    {
        const string id = "sim-battery-milliamp";
        const int designMah = 8_100;
        int fullMah = 3_250;
        int remainingMah = (int)(fullMah * percentage / 100.0);
        int rateMa = -540;
        int voltage = 11_400;

        BatteryDevice device = new(
            id, "Simulated mA Battery", "Simulated Manufacturer", "SIM-0002", "LiP",
            Measurement<int>.Measured(designMah, MeasurementSource.Simulation),
            Measurement<int>.Measured(voltage, MeasurementSource.Simulation),
            ReportsInMilliamps: true);

        BatteryInfo info = BuildInfo(
            id, now, percentage, BatteryState.Discharging, acOnline: false,
            remainingMah, fullMah, designMah, voltage, rateMa,
            cycleCount: 12, temperatureCelsius: 29.0);

        return new BatterySnapshot(device, info);
    }

    private static BatteryInfo BuildInfo(
        string id,
        DateTimeOffset now,
        double percentage,
        BatteryState state,
        bool acOnline,
        int remaining,
        int full,
        int design,
        int voltage,
        int rate,
        int cycleCount,
        double? temperatureCelsius)
    {
        Measurement<int> cycles = BatteryIntelligence.Core.Battery.BatteryCalculations.ApplyCycleCountZeroQuirk(
            Measurement<int>.Measured(cycleCount, MeasurementSource.Simulation));

        return new BatteryInfo
        {
            BatteryId = id,
            TimestampUtc = now,
            Percentage = Measurement<double>.Measured(percentage, MeasurementSource.Simulation),
            State = Measurement<BatteryState>.Measured(state, MeasurementSource.Simulation),
            AcOnline = Measurement<bool>.Measured(acOnline, MeasurementSource.Simulation),
            RemainingCapacityMWh = Measurement<int>.Measured(remaining, MeasurementSource.Simulation),
            FullChargeCapacityMWh = Measurement<int>.Measured(full, MeasurementSource.Simulation),
            DesignCapacityMWh = Measurement<int>.Measured(design, MeasurementSource.Simulation),
            RetentionPercent = Measurement<double>.Calculated(full / (double)design * 100.0),
            VoltageMv = Measurement<int>.Measured(voltage, MeasurementSource.Simulation),
            PowerMw = Measurement<int>.Measured(rate, MeasurementSource.Simulation),
            CurrentMa = Measurement<double>.Calculated(rate / (double)voltage * 1000.0),
            CycleCount = cycles,
            TemperatureCelsius = temperatureCelsius is double t
                ? Measurement<double>.Measured(t, MeasurementSource.Simulation)
                : Measurement<double>.Unavailable(),
        };
    }
}
#endif
