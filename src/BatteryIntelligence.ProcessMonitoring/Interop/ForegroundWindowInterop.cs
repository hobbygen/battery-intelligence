using System.Runtime.InteropServices;

namespace BatteryIntelligence.ProcessMonitoring.Interop;

/// <summary>
/// The one native call the process sampler needs: which process owns the
/// foreground window (docs/api-strategy.md section 2, "Process telemetry (S8)").
/// </summary>
/// <remarks>
/// <c>LibraryImport</c> source generation per docs/api-strategy.md section 4. No
/// elevation required — <c>GetForegroundWindow</c> is available to every process.
/// </remarks>
internal static partial class ForegroundWindowInterop
{
    /// <summary>The process id owning the foreground window, or 0 if it cannot be determined.</summary>
    public static uint ForegroundProcessId()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0)
        {
            return 0;
        }

        _ = GetWindowThreadProcessId(hwnd, out uint processId);
        return processId;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
