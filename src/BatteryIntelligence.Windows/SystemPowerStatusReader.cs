using BatteryIntelligence.Windows.Interop;

namespace BatteryIntelligence.Windows;

/// <summary>A single <c>GetSystemPowerStatus</c> reading (S2), sentinel-checked.</summary>
/// <param name="AcLineOnline">
/// <see langword="null"/> when the AC line status is reported unknown (255).
/// </param>
/// <param name="BatteryPercent">
/// 0-100, or <see langword="null"/> when reported unknown (255) or no battery is present.
/// </param>
/// <param name="Charging">Whether the charging bit is set in <c>BatteryFlag</c>.</param>
/// <param name="NoBattery">Whether Windows reports no battery present (desktop).</param>
public sealed record SystemPowerStatusReading(
    bool? AcLineOnline,
    int? BatteryPercent,
    bool Charging,
    bool NoBattery);

/// <summary>
/// Public wrapper over <c>GetSystemPowerStatus</c> (S2). Always succeeds on a
/// real Windows system; the return is <see langword="null"/> only if the API call
/// itself fails, which practice shows essentially never happens.
/// </summary>
public static class SystemPowerStatusReader
{
    public static SystemPowerStatusReading? Read()
    {
        if (!SystemPowerStatusInterop.GetSystemPowerStatus(out SystemPowerStatusInterop.SYSTEM_POWER_STATUS status))
        {
            return null;
        }

        bool? acOnline = status.ACLineStatus switch
        {
            SystemPowerStatusInterop.AcLineStatusOnline => true,
            SystemPowerStatusInterop.AcLineStatusOffline => false,
            _ => null,
        };

        int? percent = status.BatteryLifePercent == SystemPowerStatusInterop.AcLineStatusUnknown
            ? null
            : Math.Clamp((int)status.BatteryLifePercent, 0, 100);

        bool charging = (status.BatteryFlag & SystemPowerStatusInterop.BatteryFlagCharging) != 0;
        bool noBattery = status.BatteryFlag == SystemPowerStatusInterop.BatteryFlagNoBattery
            || (status.BatteryFlag & SystemPowerStatusInterop.BatteryFlagNoBattery) != 0;

        return new SystemPowerStatusReading(acOnline, percent, charging, noBattery);
    }
}
