namespace BatteryIntelligence.Core.Enums;

/// <summary>
/// Which direction a <c>BatterySession</c> row represents. Numeric values match
/// the <c>SessionType</c> column comment in docs/database.md ("1 charging,
/// 2 discharging") and must never be renumbered.
/// </summary>
public enum SessionType
{
    Charging = 1,
    Discharging = 2,
}
