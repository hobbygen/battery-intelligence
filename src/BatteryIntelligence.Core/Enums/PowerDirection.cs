namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// The direction of energy flow for a power reading, persisted as
/// <c>PowerSample.Direction</c> (docs/database.md section 4).
/// </summary>
/// <remarks>
/// Numeric values are written to the database and must never be renumbered.
/// This is a coarser classification than <see cref="BatteryState"/>: it only
/// answers "is energy going in, coming out, or neither", which is what the Power
/// page's charts and the <c>PowerSample</c> row need.
/// </remarks>
public enum PowerDirection
{
    /// <summary>Direction could not be determined (no usable state or rate).</summary>
    Unknown = 0,

    /// <summary>Energy flowing into the battery (positive rate).</summary>
    Charging = 1,

    /// <summary>Energy flowing out of the battery (negative rate).</summary>
    Discharging = 2,

    /// <summary>No meaningful net flow — on AC, fully charged, or rate near zero.</summary>
    Idle = 3,
}
