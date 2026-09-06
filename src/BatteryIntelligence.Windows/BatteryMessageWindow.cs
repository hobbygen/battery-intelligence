using System.Runtime.InteropServices;
using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Windows.Interop;

namespace BatteryIntelligence.Windows;

/// <summary>What changed, as reported by a power setting notification.</summary>
public enum PowerNotificationKind
{
    /// <summary>The AC/battery power source changed (<c>GUID_ACDC_POWER_SOURCE</c>).</summary>
    AcDcSource,

    /// <summary>The battery percentage changed (<c>GUID_BATTERY_PERCENTAGE_REMAINING</c>).</summary>
    BatteryPercentage,

    /// <summary>A device configuration change occurred (<c>WM_DEVICECHANGE</c>/<c>DBT_DEVNODES_CHANGED</c>) — battery arrival/removal is one cause.</summary>
    DeviceChange,
}

/// <summary>
/// A hidden message-only window that exists solely to receive
/// <c>WM_POWERBROADCAST</c> and <c>WM_DEVICECHANGE</c> (S5, docs/api-strategy.md
/// section 2), and re-raises them as ordinary .NET events.
/// </summary>
/// <remarks>
/// <para>
/// WinUI 3 gives no access to a window procedure, so this creates its own
/// <c>HWND_MESSAGE</c> window. It must be constructed on a thread that pumps a
/// Win32 message loop — the application's UI thread, which WinUI already pumps —
/// or its messages are never delivered.
/// </para>
/// <para>
/// One process hosts one instance (singleton lifetime, docs/architecture.md
/// section 4); the static callback below assumes that and is not safe to use with
/// more than one live instance.
/// </para>
/// </remarks>
public sealed class BatteryMessageWindow : IDisposable
{
    private const string ClassName = "BatteryIntelligence.MessageWindow";

    private static BatteryMessageWindow? _current;
    private readonly nint _hwnd;
    private readonly nint _acDcHandle;
    private readonly nint _percentageHandle;
    private readonly nint _screenStateHandle;
    private readonly bool _wtsRegistered;
    private bool _disposed;

    public BatteryMessageWindow()
    {
        if (_current is not null)
        {
            throw new InvalidOperationException("Only one BatteryMessageWindow may exist per process.");
        }

        _current = this;

        unsafe
        {
            nint instance = MessageWindowInterop.GetModuleHandleW(null);

            MessageWindowInterop.WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)Marshal.SizeOf<MessageWindowInterop.WNDCLASSEXW>(),
                lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = instance,
                lpszClassName = ClassName,
            };

            ushort atom = MessageWindowInterop.RegisterClassExW(windowClass);
            if (atom == 0)
            {
                int registerError = Marshal.GetLastPInvokeError();
                // ERROR_CLASS_ALREADY_EXISTS (1410) is tolerated — a previous
                // instance in this same process (e.g. a hot-reload cycle in
                // development) may not have unregistered it yet; the class
                // definition is identical, so reuse is safe.
                if (registerError != 1410)
                {
                    throw new InvalidOperationException($"RegisterClassExW failed (error {registerError}).");
                }
            }

            _hwnd = MessageWindowInterop.CreateWindowExW(
                0, ClassName, ClassName, 0, 0, 0, 0, 0, MessageWindowInterop.HwndMessage, 0, instance, 0);

            if (_hwnd == 0)
            {
                throw new InvalidOperationException(
                    $"CreateWindowExW failed (error {Marshal.GetLastPInvokeError()}).");
            }
        }

        _acDcHandle = MessageWindowInterop.RegisterPowerSettingNotification(
            _hwnd, MessageWindowInterop.GuidAcDcPowerSource, MessageWindowInterop.DeviceNotifyWindowHandle);

        _percentageHandle = MessageWindowInterop.RegisterPowerSettingNotification(
            _hwnd, MessageWindowInterop.GuidBatteryPercentageRemaining, MessageWindowInterop.DeviceNotifyWindowHandle);

        // S5 — screen power state (docs/session-engine.md section 4).
        _screenStateHandle = MessageWindowInterop.RegisterPowerSettingNotification(
            _hwnd, MessageWindowInterop.GuidConsoleDisplayState, MessageWindowInterop.DeviceNotifyWindowHandle);

        // S6 — lock/unlock (docs/session-engine.md section 4). Failure here is
        // non-fatal: lock tracking is degraded, not the whole application.
        _wtsRegistered = MessageWindowInterop.WTSRegisterSessionNotification(
            _hwnd, MessageWindowInterop.NotifyForThisSession);
    }

    /// <summary>
    /// Raised on the UI thread (the message loop that pumps this window) whenever
    /// a registered power or device-change notification fires.
    /// </summary>
    public event EventHandler<PowerNotificationKind>? NotificationReceived;

    /// <summary>S7 — the system is suspending (<c>PBT_APMSUSPEND</c>). Raised on the UI thread.</summary>
    public event EventHandler? Suspended;

    /// <summary>S7 — the system resumed (<c>PBT_APMRESUMEAUTOMATIC</c>/<c>PBT_APMRESUMESUSPEND</c>). Raised on the UI thread.</summary>
    public event EventHandler? Resumed;

    /// <summary>S5 — the display power state changed. Raised on the UI thread.</summary>
    public event EventHandler<ScreenState>? ScreenStateChanged;

    /// <summary>S6 — the session was locked or unlocked. Raised on the UI thread.</summary>
    public event EventHandler<LockState>? LockStateChanged;

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        BatteryMessageWindow? window = _current;

        try
        {
            switch (msg)
            {
                case MessageWindowInterop.WmPowerBroadcast when (uint)wParam == MessageWindowInterop.PbtPowerSettingChange
                    && lParam != 0:
                    {
                        Guid settingGuid = Marshal.PtrToStructure<Guid>(lParam);
                        if (settingGuid == MessageWindowInterop.GuidAcDcPowerSource)
                        {
                            window?.RaiseNotification(PowerNotificationKind.AcDcSource);
                        }
                        else if (settingGuid == MessageWindowInterop.GuidBatteryPercentageRemaining)
                        {
                            window?.RaiseNotification(PowerNotificationKind.BatteryPercentage);
                        }
                        else if (settingGuid == MessageWindowInterop.GuidConsoleDisplayState)
                        {
                            // POWERBROADCAST_SETTING: Guid (16) + DataLength (4) + Data (4, a DWORD here).
                            int value = Marshal.ReadInt32(lParam, 20);
                            ScreenState state = value switch
                            {
                                0 => ScreenState.Off,
                                1 => ScreenState.On,
                                2 => ScreenState.Dimmed,
                                _ => ScreenState.Unknown,
                            };
                            window?.RaiseScreenStateChanged(state);
                        }

                        return 0;
                    }

                case MessageWindowInterop.WmPowerBroadcast when (uint)wParam == MessageWindowInterop.PbtApmSuspend:
                    window?.RaiseSuspended();
                    return 0;

                case MessageWindowInterop.WmPowerBroadcast when (uint)wParam is MessageWindowInterop.PbtApmResumeAutomatic
                    or MessageWindowInterop.PbtApmResumeSuspend:
                    window?.RaiseResumed();
                    return 0;

                case MessageWindowInterop.WmWtsSessionChange:
                    if ((uint)wParam == MessageWindowInterop.WtsSessionLock)
                    {
                        window?.RaiseLockStateChanged(LockState.Locked);
                    }
                    else if ((uint)wParam == MessageWindowInterop.WtsSessionUnlock)
                    {
                        window?.RaiseLockStateChanged(LockState.Unlocked);
                    }

                    return 0;

                case MessageWindowInterop.WmDeviceChange when (uint)wParam == MessageWindowInterop.DbtDevNodesChanged:
                    window?.RaiseNotification(PowerNotificationKind.DeviceChange);
                    return 0;

                default:
                    return MessageWindowInterop.DefWindowProcW(hwnd, msg, wParam, lParam);
            }
        }
        catch
        {
            // A window procedure must never let an exception cross back into
            // native code (specification section 44) — a fault here would take
            // down the whole message loop, not just battery monitoring.
            return 0;
        }
    }

    private void RaiseNotification(PowerNotificationKind kind) =>
        NotificationReceived?.Invoke(this, kind);

    private void RaiseSuspended() => Suspended?.Invoke(this, EventArgs.Empty);

    private void RaiseResumed() => Resumed?.Invoke(this, EventArgs.Empty);

    private void RaiseScreenStateChanged(ScreenState state) => ScreenStateChanged?.Invoke(this, state);

    private void RaiseLockStateChanged(LockState state) => LockStateChanged?.Invoke(this, state);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_acDcHandle != 0)
        {
            MessageWindowInterop.UnregisterPowerSettingNotification(_acDcHandle);
        }

        if (_percentageHandle != 0)
        {
            MessageWindowInterop.UnregisterPowerSettingNotification(_percentageHandle);
        }

        if (_screenStateHandle != 0)
        {
            MessageWindowInterop.UnregisterPowerSettingNotification(_screenStateHandle);
        }

        if (_wtsRegistered)
        {
            MessageWindowInterop.WTSUnRegisterSessionNotification(_hwnd);
        }

        if (_hwnd != 0)
        {
            MessageWindowInterop.DestroyWindow(_hwnd);
        }

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }
}
