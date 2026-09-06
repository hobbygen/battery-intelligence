namespace BatteryIntelligence.ProcessMonitoring;

/// <summary>
/// Marks the ProcessMonitoring module. Process sampling, grouping and estimated energy attribution.
/// </summary>
/// <remarks>
/// Implementation lands in Phase 7. See docs/roadmap.md.
/// </remarks>
internal static class ModuleMarker
{
    /// <summary>Module name, used in diagnostics output.</summary>
    public const string Name = "ProcessMonitoring";
}
