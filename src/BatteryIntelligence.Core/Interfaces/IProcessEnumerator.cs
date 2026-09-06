using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// The operating-system seam for process telemetry (S8): one call returns a raw
/// snapshot of every visible process (docs/api-strategy.md section 2).
/// </summary>
/// <remarks>
/// Declared in Core, implemented in the ProcessMonitoring project — the same
/// decoupling as <see cref="IBatteryProvider"/>. Implementations must do no file
/// I/O on the repeat path: executable paths and friendly names are resolved once
/// per process and cached by <c>(pid, startTime)</c>
/// (docs/monitoring-dataflow.md section 5, item 3).
/// </remarks>
public interface IProcessEnumerator
{
    /// <summary>Logical processor count, for core-normalising CPU deltas.</summary>
    int CoreCount { get; }

    /// <summary>A raw snapshot of every process readable without elevation.</summary>
    IReadOnlyList<ProcessRawSample> Enumerate();
}
