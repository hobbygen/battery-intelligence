using BatteryIntelligence.Core.Enums;
using BatteryIntelligence.Core.Models;

namespace BatteryIntelligence.Core.Interfaces;

/// <summary>
/// Persistence the session engine needs: resolving a device id, finding an open
/// session to adopt at startup, and recording session/system events.
/// </summary>
/// <remarks>
/// Declared in Core and implemented in Data, exactly like
/// <see cref="IBatterySampleWriteQueue"/> — this is what lets the Sessions
/// project depend only on Core for persistence, never on Data directly, keeping
/// Battery/Data/Sessions as independent siblings wired together only by the App
/// composition root (docs/architecture.md section 2).
/// </remarks>
public interface ISessionStore
{
    Task<long> GetOrCreateDeviceIdAsync(BatteryDevice device, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>The most recent sample for this device, or <see langword="null"/> if none has ever been recorded.</summary>
    Task<LastBatterySample?> GetLastSampleAsync(long batteryDeviceId, CancellationToken cancellationToken = default);

    /// <summary>The session with <c>EndUtc IS NULL</c> for this device, or <see langword="null"/> if none.</summary>
    Task<BatterySessionInfo?> GetOpenSessionAsync(long batteryDeviceId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new open session and returns its database id.</summary>
    Task<long> InsertOpenSessionAsync(BatterySessionInfo session, long batteryDeviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the live accumulators (screen/sleep seconds, interruptions,
    /// latest percentage) of a still-open session, so a crash loses at most one
    /// update interval of running totals rather than the whole session.
    /// </summary>
    Task UpdateOpenSessionAsync(long sessionId, BatterySessionInfo state, CancellationToken cancellationToken = default);

    /// <summary>Writes final <c>EndUtc</c>/<c>EndReason</c>/<c>ClosedCleanly</c> and totals for a session that just closed.</summary>
    Task CloseSessionAsync(long sessionId, BatterySessionInfo closed, CancellationToken cancellationToken = default);

    Task RecordSessionEventAsync(
        long sessionId, SessionEventKind kind, DateTimeOffset timestampUtc, double? percentage, string? detail,
        bool inferred, CancellationToken cancellationToken = default);

    Task RecordSystemEventAsync(
        SystemEventKind kind, DateTimeOffset timestampUtc, bool inferred, string? detail,
        CancellationToken cancellationToken = default);

    /// <summary>Most recent sessions across every device, newest first.</summary>
    Task<IReadOnlyList<BatterySessionInfo>> GetRecentSessionsAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Builds the merged timeline over a time range (specification section 12).</summary>
    Task<IReadOnlyList<TimelineSegment>> GetTimelineAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
