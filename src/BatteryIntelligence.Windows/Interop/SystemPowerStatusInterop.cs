using System.Runtime.InteropServices;

namespace BatteryIntelligence.Windows.Interop;

/// <summary>
/// <c>GetSystemPowerStatus</c> (S2, docs/api-strategy.md section 2) — always
/// present, used as the last-resort source and for AC line status.
/// </summary>
internal static partial class SystemPowerStatusInterop
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    /// <summary>
    /// Raw <c>SYSTEM_POWER_STATUS</c>. Every field can carry the byte sentinel
    /// <c>255</c> ("unknown") or, for the two lifetime fields, the 32-bit sentinel
    /// <c>0xFFFFFFFF</c> (quirk Q3) — callers must check via
    /// <see cref="BatteryIntelligence.Core.Battery.BatterySentinels"/> before trusting them.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    internal const byte AcLineStatusOffline = 0;
    internal const byte AcLineStatusOnline = 1;
    internal const byte AcLineStatusUnknown = 255;

    internal const byte BatteryFlagHigh = 1;
    internal const byte BatteryFlagLow = 2;
    internal const byte BatteryFlagCritical = 4;
    internal const byte BatteryFlagCharging = 8;
    internal const byte BatteryFlagNoBattery = 128;
    internal const byte BatteryFlagUnknown = 255;
}
