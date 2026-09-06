namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// The charge state of a battery device.
/// </summary>
/// <remarks>
/// Mirrors the WinRT <c>Windows.System.Power.BatteryStatus</c> vocabulary exactly
/// (<c>NotPresent</c>, <c>Discharging</c>, <c>Idle</c>, <c>Charging</c>), with
/// <see cref="Full"/> added as a derived state: idle, on AC, at 100%. Numeric
/// values are persisted and must never be renumbered (see docs/database.md).
/// </remarks>
public enum BatteryState
{
    /// <summary>State could not be determined.</summary>
    Unknown = 0,

    /// <summary>Battery is receiving charge.</summary>
    Charging = 1,

    /// <summary>Battery is supplying power to the system.</summary>
    Discharging = 2,

    /// <summary>Neither charging nor discharging (e.g. on AC, below full, charging withheld).</summary>
    Idle = 3,

    /// <summary>On AC and at full charge. Derived, not a native status.</summary>
    Full = 4,

    /// <summary>No battery device is present (desktop or removed).</summary>
    NotPresent = 5,
}
