namespace BatteryIntelligence.Windows;

/// <summary>
/// Marks the Windows module. Win32 and WinRT interop, power events, tray and startup integration.
/// </summary>
/// <remarks>
/// Implementation lands in Phase 2. See docs/roadmap.md.
/// </remarks>
internal static class ModuleMarker
{
    /// <summary>Module name, used in diagnostics output.</summary>
    public const string Name = "Windows";
}
