namespace BatteryIntelligence.Core.Diagnostics;

/// <summary>
/// Collects the per-subsystem health the Diagnostics page shows (specification
/// section 26). Every hosted monitoring orchestrator reports the outcome of each
/// tick here; the Diagnostics view model reads <see cref="Snapshot"/> and listens
/// for <see cref="Changed"/>.
/// </summary>
/// <remarks>
/// A singleton. Thread-safe — orchestrators report from timer threads and the
/// thread pool. It aggregates only: no retry or backoff logic lives here
/// (docs/monitoring-dataflow.md section 8).
/// </remarks>
public interface IMonitoringStatusRegistry
{
    /// <summary>Records that <paramref name="component"/>'s last tick succeeded.</summary>
    void ReportSuccess(MonitoringComponent component);

    /// <summary>Records that <paramref name="component"/>'s last tick failed with <paramref name="error"/>.</summary>
    void ReportFailure(MonitoringComponent component, string error);

    /// <summary>The current status of every component, in <see cref="MonitoringComponent"/> order.</summary>
    IReadOnlyList<MonitoringStatus> Snapshot();

    /// <summary>Raised when a component's rendered state changes — not on every repeat success.</summary>
    event EventHandler? Changed;
}
