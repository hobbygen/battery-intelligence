#if SIMULATION
namespace BatteryIntelligence.Battery.Simulation;

/// <summary>
/// Scripted hardware scenarios for <see cref="SimulatedBatteryProvider"/>
/// (specification section 61; docs/testing.md section 4).
/// </summary>
public enum BatterySimulationScenario
{
    /// <summary>100 -> 90 -> 80 -> 50 -> 20 -> 10%.</summary>
    NormalDischarge,

    /// <summary>20 -> 30 -> 40 -> 100%, tapering rate near full.</summary>
    NormalCharge,

    /// <summary>Two independent battery devices.</summary>
    MultipleBatteries,

    /// <summary>Every field present except temperature — the reference machine's real configuration.</summary>
    NoTemperatureSensor,

    /// <summary>Firmware reports cycle count 0 on a worn battery — must be treated as Unavailable (quirk Q5).</summary>
    NoCycleCount,

    /// <summary><c>BATTERY_CAPACITY_RELATIVE</c> set: rate/capacity reported in mA/mAh, verifying normalisation (quirk Q2).</summary>
    MilliampReporting,

    /// <summary>The provider succeeds for several reads, then fails from that point on.</summary>
    SensorDropoutMidSession,

    /// <summary>Every read fails.</summary>
    ApiFailure,

    /// <summary>
    /// A temperature sensor IS present and the battery heats up while charging —
    /// 32 → 48 °C — crossing the 45 °C warning threshold and holding above it, so
    /// the thermal band breakdown and threshold-event detection can be exercised
    /// (Phase 6; the reference machine's own hardware exposes no sensor).
    /// </summary>
    RisingTemperatureWhileCharging,
}
#endif
