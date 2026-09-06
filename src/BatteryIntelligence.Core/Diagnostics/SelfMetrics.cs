namespace BatteryIntelligence.Core.Diagnostics;

/// <summary>
/// A snapshot of the application's own resource use, for the Diagnostics
/// "footprint" section (docs/monitoring-dataflow.md section 1 — the app's
/// consumption must be negligible relative to what it measures).
/// </summary>
/// <param name="ProcessCpuPercent">CPU use since the previous capture, as a percentage of one logical processor, or <see langword="null"/> on the first capture.</param>
/// <param name="WorkingSetBytes">Physical memory currently in the working set.</param>
/// <param name="PrivateBytes">Private (non-shared) committed memory.</param>
/// <param name="GcHeapBytes">Managed heap size.</param>
/// <param name="ThreadCount">OS threads in the process.</param>
/// <param name="PendingWrites">Rows queued across every write queue, not yet flushed.</param>
/// <param name="LastFlushUtc">When any write queue last flushed, or <see langword="null"/>.</param>
public sealed record SelfMetricsSnapshot(
    double? ProcessCpuPercent,
    long WorkingSetBytes,
    long PrivateBytes,
    long GcHeapBytes,
    int ThreadCount,
    int PendingWrites,
    DateTimeOffset? LastFlushUtc);

/// <summary>Captures <see cref="SelfMetricsSnapshot"/>s. Implemented in the App (it needs the live process handle).</summary>
public interface ISelfMetrics
{
    /// <summary>Captures the current footprint. CPU % is measured against the previous call.</summary>
    SelfMetricsSnapshot Capture();
}
