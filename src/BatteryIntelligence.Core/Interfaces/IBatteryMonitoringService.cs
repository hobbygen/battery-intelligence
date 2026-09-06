using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// The application-level orchestrator sitting between the UI and
/// <see cref="IBatteryProvider"/>: <c>BatteryViewModel → IBatteryMonitoringService
/// → IBatteryProvider → (composite provider)</c>, per specification section 73.
/// </summary>
/// <remarks>
/// Holds the last known state so pages can bind to it immediately on navigation
/// without waiting for the next sample, and raises <see cref="Updated"/> so bound
/// ViewModels refresh without polling. It is a singleton hosted service
/// (docs/architecture.md section 4): one polling/event pipeline feeding every
/// consumer, not one per page.
/// </remarks>
public interface IBatteryMonitoringService
{
    /// <summary>The most recent snapshot for every present battery device, oldest call first.</summary>
    IReadOnlyList<BatterySnapshot> CurrentSnapshots { get; }

    /// <summary>
    /// The system-wide aggregate across every present battery, or
    /// <see langword="null"/> before the first successful read.
    /// </summary>
    BatteryInfo? Aggregate { get; }

    /// <summary>The most recent capability snapshot, or <see langword="null"/> before detection has run.</summary>
    CapabilitySnapshot? Capabilities { get; }

    /// <summary>
    /// The most recent monitoring failure, or <see langword="null"/> when the last
    /// read succeeded. Cleared on the next successful read.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Raised on the thread pool after <see cref="CurrentSnapshots"/>,
    /// <see cref="Aggregate"/> or <see cref="Capabilities"/> change. Consumers must
    /// marshal to the UI thread themselves.
    /// </summary>
    event EventHandler? Updated;

    /// <summary>
    /// Forces an immediate read, outside the regular polling interval — used after
    /// a Windows power-event notification and by "Refresh" affordances.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
