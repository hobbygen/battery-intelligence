namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// Identifies one row of the battery capability matrix
/// (docs/capability-matrix.md section 3, C01-C14).
/// </summary>
/// <remarks>
/// Numeric values correspond to the "C" numbers in the matrix and must never be
/// renumbered; later phases add their own ranges (process, session, etc.) for
/// their own detectors rather than extending this one.
/// </remarks>
public enum CapabilityId
{
    /// <summary>C01 — battery present / count.</summary>
    BatteryPresent = 1,

    /// <summary>C02 — charge percentage.</summary>
    ChargePercentage = 2,

    /// <summary>C03 — charging / discharging / idle / full.</summary>
    ChargeState = 3,

    /// <summary>C04 — AC line connected.</summary>
    AcLineConnected = 4,

    /// <summary>C05 — remaining capacity (mWh).</summary>
    RemainingCapacity = 5,

    /// <summary>C06 — full-charge capacity (mWh).</summary>
    FullChargeCapacity = 6,

    /// <summary>C07 — design capacity (mWh).</summary>
    DesignCapacity = 7,

    /// <summary>C08 — capacity retention / wear %. Always Calculated.</summary>
    CapacityRetention = 8,

    /// <summary>C09 — voltage (mV).</summary>
    Voltage = 9,

    /// <summary>C10 — energy rate / power (mW), signed.</summary>
    PowerRate = 10,

    /// <summary>C11 — current (mA). Always Calculated, never Measured.</summary>
    Current = 11,

    /// <summary>C12 — cycle count.</summary>
    CycleCount = 12,

    /// <summary>C13 — battery temperature.</summary>
    Temperature = 13,

    /// <summary>C14 — manufacturer / model / serial / chemistry.</summary>
    Identity = 14,
}
