using System.Runtime.InteropServices;

namespace BatteryIntelligence.Windows.Interop;

/// <summary>
/// P/Invoke surface for a message-only window (<c>HWND_MESSAGE</c>) and power
/// setting notifications (S5, docs/api-strategy.md sections 2-3).
/// </summary>
/// <remarks>
/// WinUI 3 does not surface a window procedure directly, so
/// <see cref="BatteryIntelligence.Windows.BatteryMessageWindow"/> creates a
/// dedicated hidden window purely to receive <c>WM_POWERBROADCAST</c> and
/// <c>WM_DEVICECHANGE</c>. A native function pointer (not a delegate) backs the
/// window procedure, matching the project's AOT-friendly interop convention.
/// </remarks>
internal static unsafe partial class MessageWindowInterop
{
    internal const nint HwndMessage = -3;
    internal const uint WmPowerBroadcast = 0x0218;
    internal const uint WmDeviceChange = 0x0219;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmWtsSessionChange = 0x02B1;
    internal const uint PbtPowerSettingChange = 0x8013;
    internal const uint DbtDevNodesChanged = 0x0007;
    internal const uint DeviceNotifyWindowHandle = 0;

    // S7 — suspend/resume (docs/api-strategy.md section 2).
    internal const uint PbtApmSuspend = 0x0004;
    internal const uint PbtApmResumeSuspend = 0x0007;
    internal const uint PbtApmResumeAutomatic = 0x0012;

    // S6 — WTS session lock/unlock (docs/api-strategy.md section 2).
    internal const uint WtsSessionLock = 0x7;
    internal const uint WtsSessionUnlock = 0x8;
    internal const uint NotifyForThisSession = 0;

    internal static readonly Guid GuidAcDcPowerSource = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");
    internal static readonly Guid GuidBatteryPercentageRemaining = new("a7ad8041-b45a-4cae-87a3-eecbb468a9e1");

    /// <summary>S5 — screen power state. <c>Data</c> is a DWORD: 0 off, 1 on, 2 dimmed.</summary>
    internal static readonly Guid GuidConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POWERBROADCAST_SETTING_HEADER
    {
        public Guid PowerSetting;
        public uint DataLength;

        // Data[1] follows immediately in memory; read via pointer arithmetic.
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandleW(string? moduleName);

    // Classic DllImport, not LibraryImport: WNDCLASSEXW mixes a fixed layout with
    // string fields, which the source-generated marshaller does not support
    // (SYSLIB1051). This is the one documented exception to the project's
    // LibraryImport convention (docs/api-strategy.md section 4).
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassExW(in WNDCLASSEXW wndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint RegisterPowerSettingNotification(nint recipient, in Guid powerSettingGuid, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterPowerSettingNotification(nint handle);

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSRegisterSessionNotification(nint hWnd, uint flags);

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSUnRegisterSessionNotification(nint hWnd);
}
