using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// The application-level session tracker: <c>SessionsViewModel →
/// ISessionMonitoringService → (session state machine + repositories)</c>,
/// mirroring <see cref="IBatteryMonitoringService"/>'s role for battery readings
/// (specification section 73).
/// </summary>
public interface ISessionMonitoringService
{
    /// <summary>The currently open session, or <see langword="null"/> when idle (no session in progress).</summary>
    BatterySessionInfo? CurrentSession { get; }

    /// <summary>Current display power state, tracked from S5 (docs/session-engine.md section 4).</summary>
    ScreenState CurrentScreenState { get; }

    /// <summary>Current session lock state, tracked from S6.</summary>
    LockState CurrentLockState { get; }

    /// <summary>Raised on the thread pool whenever <see cref="CurrentSession"/> changes.</summary>
    event EventHandler? Updated;

    /// <summary>
    /// The database id of the open session for a given battery, or
    /// <see langword="null"/> if none — what the write queue stamps onto each
    /// persisted <c>BatterySample</c> row so retention's open-session guard has
    /// something to check (docs/database.md section 6).
    /// </summary>
    long? GetOpenSessionId(string batteryId);

    /// <summary>Most recent sessions, newest first.</summary>
    Task<IReadOnlyList<BatterySessionInfo>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the merged timeline (specification section 12) over a time range:
    /// session boundaries, session/system events, and screen-state changes, in
    /// chronological order.
    /// </summary>
    Task<IReadOnlyList<TimelineSegment>> GetTimelineAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
