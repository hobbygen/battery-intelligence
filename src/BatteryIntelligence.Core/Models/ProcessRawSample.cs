namespace BatteryIntelligence.Core.Models;

/// <summary>
/// One point-in-time observation of a single OS process, as produced by
/// <see cref="Interfaces.IProcessEnumerator"/> (docs/api-strategy.md section 2,
/// "Process telemetry (S8)"; docs/monitoring-dataflow.md section 5).
/// </summary>
/// <remarks>
/// Deliberately raw: cumulative processor time, not a percentage. CPU% is a
/// <em>delta</em> between two of these divided by elapsed wall time and core
/// count (<see cref="Processes.ProcessCpuCalculator"/>) — never a busy-sampled
/// figure. <see cref="ExecutablePath"/> and any friendlier name are resolved once
/// and cached by the enumerator, so a repeat observation of the same process
/// costs no file I/O.
/// </remarks>
/// <param name="ProcessId">The OS process id. Reused by the OS over time, so it is only unique paired with <paramref name="StartTimeUtc"/>.</param>
/// <param name="StartTimeUtc">When the process started, or <see langword="null"/> when the OS would not report it (access denied).</param>
/// <param name="ProcessName">The process image name without extension, e.g. "chrome".</param>
/// <param name="ExecutablePath">Full path to the executable, or <see langword="null"/> when it could not be read.</param>
/// <param name="ProcessorTime">Cumulative kernel + user CPU time since the process started.</param>
/// <param name="WorkingSetBytes">Current working set (physical memory) in bytes.</param>
/// <param name="IsForeground">Whether this process owns the foreground window at the moment of the sample.</param>
public sealed record ProcessRawSample(
    int ProcessId,
    DateTimeOffset? StartTimeUtc,
    string ProcessName,
    string? ExecutablePath,
    TimeSpan ProcessorTime,
    long WorkingSetBytes,
    bool IsForeground)
{
    /// <summary>A stable identity for a process across samples: pid plus start time.</summary>
    public (int Pid, long StartTicks) Key => (ProcessId, StartTimeUtc?.UtcTicks ?? 0);
}
